<?php
/**
 * Plugin Name: ING AutoLister License Manager
 * Description: License key management and Stripe payment webhooks for ING Listing Engine™
 * Version:     1.2.0
 * Author:      ING Mining LLC
 */

if ( ! defined( 'ABSPATH' ) ) exit;

// ── Database table on activation ─────────────────────────────────────────────
register_activation_hook( __FILE__, 'ing_create_tables' );
function ing_create_tables() {
    // Pre-seed webhook secret from Stripe setup
    if ( ! get_option( 'ing_stripe_webhook_secret' ) ) {
        update_option( 'ing_stripe_webhook_secret', 'whsec_5GSIR06fDdJcMNG1QBh0jc73ijXo6sCJ' );
    }

    global $wpdb;
    $t  = $wpdb->prefix . 'ing_licenses';
    $cs = $wpdb->get_charset_collate();
    $sql = "CREATE TABLE $t (
        id                   bigint(20)   NOT NULL AUTO_INCREMENT,
        license_key          varchar(64)  NOT NULL,
        tier                 varchar(16)  NOT NULL DEFAULT 'free',
        status               varchar(16)  NOT NULL DEFAULT 'active',
        email                varchar(255) NOT NULL DEFAULT '',
        stripe_session_id    varchar(255)          DEFAULT NULL,
        stripe_subscription_id varchar(255)        DEFAULT NULL,
        created_at           datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
        PRIMARY KEY  (id),
        UNIQUE KEY license_key (license_key),
        KEY email (email),
        KEY stripe_subscription_id (stripe_subscription_id)
    ) $cs;";
    require_once ABSPATH . 'wp-admin/includes/upgrade.php';
    dbDelta( $sql );
}

// ── REST routes ───────────────────────────────────────────────────────────────
add_action( 'rest_api_init', 'ing_register_routes' );
function ing_register_routes() {

    // License check — used by the desktop app on every activation
    register_rest_route( 'ing/v1', '/license/check', [
        'methods'             => 'POST',
        'callback'            => 'ing_license_check',
        'permission_callback' => '__return_true',
    ] );

    // Stripe webhook — called by Stripe after a successful payment
    register_rest_route( 'ing/v1', '/stripe/webhook', [
        'methods'             => 'POST',
        'callback'            => 'ing_stripe_webhook',
        'permission_callback' => '__return_true',
    ] );
}

// ── License check ─────────────────────────────────────────────────────────────
function ing_license_check( WP_REST_Request $req ) {
    $key     = strtoupper( trim( $req->get_param( 'key' ) ?? '' ) );
    $product = $req->get_param( 'product' ) ?? '';

    if ( empty( $key ) ) {
        return rest_response( false, 'unlicensed', 'No license key provided.' );
    }

    // Built-in beta / free keys
    $free_keys = [ 'ING-BETA-2025' ];
    if ( in_array( $key, $free_keys, true ) ) {
        return rest_response( true, 'free', 'Beta license active.' );
    }

    // Database lookup
    global $wpdb;
    $row = $wpdb->get_row(
        $wpdb->prepare(
            "SELECT tier, status FROM {$wpdb->prefix}ing_licenses WHERE license_key = %s LIMIT 1",
            $key
        )
    );

    if ( ! $row ) {
        return rest_response( false, 'unlicensed', 'Invalid license key.' );
    }

    if ( ( $row->status ?? 'active' ) !== 'active' ) {
        return rest_response( false, 'cancelled', 'Subscription cancelled. Renew at ingmining.com.' );
    }

    $label = $row->tier === 'pro' ? 'Pro' : 'Free';
    return rest_response( true, $row->tier, "$label license active." );
}

// ── Stripe webhook ────────────────────────────────────────────────────────────
function ing_stripe_webhook( WP_REST_Request $req ) {
    $payload    = $req->get_body();
    $sig_header = $req->get_header( 'stripe-signature' );
    $secret     = get_option( 'ing_stripe_webhook_secret', '' );

    // Verify signature when a webhook secret is configured
    if ( ! empty( $secret ) ) {
        if ( ! ing_verify_stripe_sig( $payload, $sig_header, $secret ) ) {
            return new WP_REST_Response( [ 'error' => 'Invalid signature.' ], 400 );
        }
    }

    $event = json_decode( $payload, true );
    if ( json_last_error() !== JSON_ERROR_NONE ) {
        return new WP_REST_Response( [ 'error' => 'Invalid JSON.' ], 400 );
    }

    $type = $event['type'] ?? '';

    // New subscription — issue key and email it
    if ( $type === 'checkout.session.completed' ) {
        $session = $event['data']['object'];
        $email   = $session['customer_details']['email'] ?? '';
        $meta    = $session['metadata'] ?? [];
        $product = $meta['product'] ?? '';
        $sub_id  = $session['subscription'] ?? '';

        if ( $product === 'ING-eBay-AutoLister-Pro' && ! empty( $email ) ) {
            $key = ing_generate_key( 'PRO' );

            global $wpdb;
            $inserted = $wpdb->insert(
                $wpdb->prefix . 'ing_licenses',
                [
                    'license_key'            => $key,
                    'tier'                   => 'pro',
                    'status'                 => 'active',
                    'email'                  => sanitize_email( $email ),
                    'stripe_session_id'      => sanitize_text_field( $session['id'] ),
                    'stripe_subscription_id' => sanitize_text_field( $sub_id ),
                ]
            );

            if ( $inserted ) {
                ing_send_key_email( $email, $key, 'pro' );
            }
        }
    }

    // Subscription cancelled — deactivate the key
    if ( $type === 'customer.subscription.deleted' ) {
        $sub    = $event['data']['object'];
        $sub_id = $sub['id'] ?? '';

        if ( ! empty( $sub_id ) ) {
            global $wpdb;
            $wpdb->update(
                $wpdb->prefix . 'ing_licenses',
                [ 'status' => 'cancelled' ],
                [ 'stripe_subscription_id' => $sub_id ]
            );
        }
    }

    return new WP_REST_Response( [ 'received' => true ], 200 );
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function rest_response( bool $valid, string $tier, string $message ): array {
    return [ 'valid' => $valid, 'tier' => $tier, 'message' => $message ];
}

function ing_generate_key( string $tier_prefix ): string {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // no I/O/0/1 (ambiguous)
    $seg   = function () use ( $chars ) {
        $s = '';
        for ( $i = 0; $i < 4; $i++ ) {
            $s .= $chars[ random_int( 0, strlen( $chars ) - 1 ) ];
        }
        return $s;
    };
    return "ING-{$tier_prefix}-{$seg()}-{$seg()}";
}

function ing_send_key_email( string $to, string $key, string $tier ): void {
    $tier_label = strtoupper( $tier );
    $subject    = "Your ING Listing Engine™ {$tier_label} License Key";
    $body       = implode( "\r\n", [
        "Thank you for purchasing ING Listing Engine™ {$tier_label}!",
        '',
        'Your license key is:',
        '',
        "   {$key}",
        '',
        'To activate:',
        '  1. Open ING Listing Engine on your computer',
        '  2. Click the 🔑 License icon in the left sidebar',
        '  3. Paste your key into the License Key field',
        '  4. Click Activate',
        '',
        'Need help? Reply to this email.',
        '',
        '— ING Mining LLC',
        'https://ingmining.com',
    ] );
    wp_mail( $to, $subject, $body );
}

function ing_verify_stripe_sig( string $payload, string $sig_header, string $secret ): bool {
    $parts = [];
    foreach ( explode( ',', $sig_header ) as $part ) {
        $kv = explode( '=', $part, 2 );
        if ( count( $kv ) === 2 ) {
            $parts[ $kv[0] ][] = $kv[1];
        }
    }
    $timestamp = $parts['t'][0] ?? '';
    $sigs      = $parts['v1'] ?? [];
    if ( empty( $timestamp ) || empty( $sigs ) ) return false;

    $expected = hash_hmac( 'sha256', "{$timestamp}.{$payload}", $secret );
    foreach ( $sigs as $sig ) {
        if ( hash_equals( $expected, $sig ) ) return true;
    }
    return false;
}

// ── Simple admin page to save the webhook secret ─────────────────────────────
add_action( 'admin_menu', 'ing_admin_menu' );
function ing_admin_menu() {
    add_options_page(
        'ING License Manager',
        'ING Licenses',
        'manage_options',
        'ing-licenses',
        'ing_admin_page'
    );
}

add_action( 'admin_init', 'ing_admin_settings' );
function ing_admin_settings() {
    register_setting( 'ing_license_settings', 'ing_stripe_webhook_secret', 'sanitize_text_field' );
}

function ing_admin_page() {
    global $wpdb;
    $table   = $wpdb->prefix . 'ing_licenses';
    $licenses = $wpdb->get_results( "SELECT * FROM $table ORDER BY created_at DESC LIMIT 100" );
    ?>
    <div class="wrap">
        <h1>ING License Manager</h1>

        <h2>Stripe Webhook Secret</h2>
        <form method="post" action="options.php">
            <?php settings_fields( 'ing_license_settings' ); ?>
            <table class="form-table">
                <tr>
                    <th>Webhook Signing Secret</th>
                    <td>
                        <input type="text" name="ing_stripe_webhook_secret"
                               value="<?php echo esc_attr( get_option( 'ing_stripe_webhook_secret', '' ) ); ?>"
                               style="width:420px" placeholder="whsec_..." />
                        <p class="description">
                            From Stripe Dashboard → Developers → Webhooks → your endpoint → Signing secret.<br>
                            <strong>Webhook URL:</strong> <code><?php echo esc_url( rest_url( 'ing/v1/stripe/webhook' ) ); ?></code><br>
                            <strong>Events to subscribe:</strong> <code>checkout.session.completed</code>, <code>customer.subscription.deleted</code>
                        </p>
                    </td>
                </tr>
            </table>
            <?php submit_button(); ?>
        </form>

        <h2>Issued License Keys</h2>
        <table class="widefat striped">
            <thead><tr>
                <th>Key</th><th>Tier</th><th>Status</th><th>Email</th><th>Created</th>
            </tr></thead>
            <tbody>
            <?php if ( empty( $licenses ) ) : ?>
                <tr><td colspan="5">No keys issued yet.</td></tr>
            <?php else : ?>
                <?php foreach ( $licenses as $row ) : ?>
                <tr>
                    <td><code><?php echo esc_html( $row->license_key ); ?></code></td>
                    <td><?php echo esc_html( strtoupper( $row->tier ) ); ?></td>
                    <td><?php echo esc_html( ucfirst( $row->status ?? 'active' ) ); ?></td>
                    <td><?php echo esc_html( $row->email ); ?></td>
                    <td><?php echo esc_html( $row->created_at ); ?></td>
                </tr>
                <?php endforeach; ?>
            <?php endif; ?>
            </tbody>
        </table>
    </div>
    <?php
}
