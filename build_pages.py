import json, sys, time, urllib.request, ssl, paramiko

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# Build Reviews page JSON
reviews_slides = [
    {"name": "Chrisbuch_10", "title": "Antminer S19 110THs – eBay Verified",
     "content": "As described, up and running with autotune at 110-115Th/s. Thanks. Seller answers questions for operations like great customer support!",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
    {"name": "Peter Kvande", "title": "Goldshell 4-Box 1200W – eBay Verified",
     "content": "The seller was very authentic toward the buyer and showed generous concern regarding the transactions. I will continue to support this seller for the professionalism and excellent service provided.",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
    {"name": "Barry", "title": "Antminer S19 110THs – eBay Verified",
     "content": "Great seller! Great communication A+++ Item arrived fast and safe to the UK! Very Very Good Seller!!!",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
    {"name": "Adrian Eaves", "title": "New Antminer S19 – eBay Verified",
     "content": "Excellent seller, Item as described, Item work perfect. Very good communication, and custom letter too. Very appreciated.",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
    {"name": "Walter Rowe", "title": "Antminer S21 234TH/s – eBay Verified",
     "content": "Item is as described, well packaged, shipped promptly, and arrived sooner than anticipated without any issues, thank you so much! I recommended this seller !!!",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
    {"name": "Davebill", "title": "Antminer S19 95Th/s – eBay Verified",
     "content": "Seller was patient with me as a new user of bidding features and my delayed payment. Fast shipping. Excellent physical condition on arrival. Highly recommend!",
     "image": {"url": "https://ingbtc.live-website.com/wp-content/uploads/2024/06/ebay-icon-150x150-1.png", "id": ""}, "rating": "5"},
]

DARK_BG = "#0a0e14"
GREEN_BG = "#122D31"

def make_container(cid, children, settings=None):
    s = {"flex_direction": "column"}
    if settings:
        s.update(settings)
    return {"id": cid, "elType": "container", "settings": s, "elements": children, "isInner": False}

def heading(hid, title, size="h2", color="#ffffff", font_size=32, weight="700", align="center"):
    return {"id": hid, "elType": "widget", "widgetType": "heading",
            "settings": {"title": title, "header_size": size, "align": align,
                         "title_color": color, "typography_typography": "custom",
                         "typography_font_size": {"unit": "px", "size": font_size},
                         "typography_font_weight": weight}, "elements": []}

def texteditor(tid, html, align="center"):
    return {"id": tid, "elType": "widget", "widgetType": "text-editor",
            "settings": {"editor": html, "align": align}, "elements": []}

def button_widget(bid, text, url, color="#ffc300", align="center"):
    return {"id": bid, "elType": "widget", "widgetType": "button",
            "settings": {"text": text, "link": {"url": url, "is_external": "on"},
                         "align": align, "background_color": color,
                         "border_radius": {"unit": "px", "top": "8", "right": "8", "bottom": "8", "left": "8"},
                         "text_padding": {"unit": "px", "top": "14", "right": "32", "bottom": "14", "left": "32"}},
            "elements": []}

reviews_json = [
    make_container("rev0001", [
        heading("rev0002", "ING Mining&#8482; – Verified Customer Reviews", "h1", "#ffffff", 44, "800"),
        texteditor("rev0003", "<p style='color:#aaa;font-size:18px;max-width:720px;margin:0 auto;'>Real feedback from verified eBay buyers. 5-star rated ASIC miner seller trusted by miners worldwide since 2019.</p>"),
        make_container("rev0004", [
            {"id": "rev0005", "elType": "widget", "widgetType": "html",
             "settings": {"html": "<div style='display:flex;gap:8px;justify-content:center;flex-wrap:wrap;margin-top:20px'><span style='background:#ffc300;color:#000;padding:6px 16px;border-radius:20px;font-weight:700;font-size:14px'>★★★★★ eBay Top Rated Seller</span><span style='background:#1a1a2e;border:1px solid #ffc300;color:#ffc300;padding:6px 16px;border-radius:20px;font-weight:700;font-size:14px'>10,000+ Transactions</span><span style='background:#1a1a2e;border:1px solid #22c55e;color:#22c55e;padding:6px 16px;border-radius:20px;font-weight:700;font-size:14px'>Verified ASIC Seller</span></div>"},
             "elements": []}
        ], {"flex_direction": "column", "content_width": "full"}),
    ], {"background_background": "classic", "background_color": GREEN_BG,
        "flex_justify_content": "center", "padding": {"unit": "px", "top": "80", "right": "40", "bottom": "60", "left": "40", "isLinked": False}}),

    make_container("rev1001", [
        heading("rev1002", "What Our Customers Are Saying", "h2", "#ffc300", 32, "700"),
        {"id": "rev1003", "elType": "widget", "widgetType": "testimonial-carousel",
         "settings": {"slides": reviews_slides, "slides_to_show": {"unit": "px", "size": 3},
                      "navigation": "arrows", "autoplay": "yes", "autoplay_speed": 5000},
         "elements": []},
        button_widget("rev1004", "See All Reviews on eBay →", "https://www.ebay.com/str/ingmining?_tab=feedback"),
    ], {"background_background": "classic", "background_color": DARK_BG,
        "padding": {"unit": "px", "top": "60", "right": "40", "bottom": "60", "left": "40", "isLinked": False}}),
]

def simple_page(pid_str, title, desc, btn_text, btn_url):
    return [
        make_container(pid_str + "a", [
            heading(pid_str + "b", title, "h1", "#ffffff", 40, "800"),
            texteditor(pid_str + "c", f"<p style='color:#aaa;font-size:18px;max-width:720px;margin:0 auto;'>{desc}</p>"),
            button_widget(pid_str + "d", btn_text, btn_url),
        ], {"background_background": "classic", "background_color": GREEN_BG,
            "flex_justify_content": "center",
            "padding": {"unit": "px", "top": "80", "right": "40", "bottom": "80", "left": "40", "isLinked": False}})
    ]

pages = {
    1607: simple_page("c07", "ASIC Mining Consulting",
                      "Expert guidance from experienced ASIC miners. Infrastructure planning, fleet optimization, profitability analysis, and firmware tuning for serious mining operations.",
                      "Contact Us for Consulting", "https://ingmining.com/contact-us"),
    1608: reviews_json,
    1609: simple_page("u09", "Used ASIC Miners for Sale",
                      "Quality refurbished Bitcoin and cryptocurrency ASIC miners. All machines tested, tuned, and ready to mine. Bitmain Antminer S19, S21, and more.",
                      "Shop All Miners", "https://ingmining.com/products/"),
    1610: simple_page("n10", "New ASIC Miners for Sale",
                      "Brand new ASIC miners in stock — Bitmain Antminer S19 XP, S21, IceRiver KAS series and more. Ships fast from the USA.",
                      "Shop All Miners", "https://ingmining.com/products/"),
    1611: simple_page("p11", "ASIC Miner Parts & Accessories",
                      "Hash boards, control boards, power supplies, fans, cables, and MicroSD cards for Bitmain Antminer and other ASIC miners. All parts tested before shipping.",
                      "Browse Products", "https://ingmining.com/products/"),
    1612: simple_page("s12", "ING Mining Support Center",
                      "Need help with your ASIC miner? Our expert team provides setup guides, firmware support, troubleshooting, and after-sale support for all machines purchased from ING Mining.",
                      "Contact Support", "https://ingmining.com/contact-us"),
    1613: simple_page("v13", "Privacy Policy – ING Mining LLC",
                      "ING Mining LLC respects your privacy. We collect only the information needed to process your order. We do not sell your personal data to third parties. For questions contact us at support@ingmining.com",
                      "Contact Us", "https://ingmining.com/contact-us"),
}

print("Connecting to server...")
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect('access993476667.webspace-data.io', 22, 'u115220565', '012484Birth@@31', timeout=30)
sftp = ssh.open_sftp()

# Upload each page JSON
for pid, pjson in pages.items():
    json_str = json.dumps(pjson, ensure_ascii=False)
    fname = f'clickandbuilds/INGBTC/wp-content/uploads/pg_{pid}.json'
    with sftp.open(fname, 'w') as jf:
        jf.write(json_str.encode('utf-8'))
print(f"Uploaded {len(pages)} JSON files")

# Build PHP to apply them
php_lines = ['<?php']
php_lines.append('ini_set("max_execution_time", 120);')
php_lines.append('define("ABSPATH", "/homepages/20/d993476667/htdocs/clickandbuilds/INGBTC/");')
php_lines.append('require_once(ABSPATH . "wp-load.php");')
php_lines.append('$uploads = ABSPATH . "wp-content/uploads/";')
php_lines.append('echo "WP loaded\\n";')

for pid in pages.keys():
    php_lines.append(f'$j = file_get_contents($uploads . "pg_{pid}.json");')
    php_lines.append(f'$r1 = $GLOBALS["wpdb"]->update("wp_postmeta", array("meta_value"=>$j), array("post_id"=>{pid}, "meta_key"=>"_elementor_data"));')
    php_lines.append(f'if ($r1 === false || $r1 === 0) {{')
    php_lines.append(f'  $exists = $GLOBALS["wpdb"]->get_var("SELECT meta_id FROM wp_postmeta WHERE post_id={pid} AND meta_key=\'_elementor_data\'");')
    php_lines.append(f'  if (!$exists) $GLOBALS["wpdb"]->insert("wp_postmeta", array("post_id"=>{pid}, "meta_key"=>"_elementor_data", "meta_value"=>$j));')
    php_lines.append(f'  $GLOBALS["wpdb"]->update("wp_postmeta", array("meta_value"=>"elementor"), array("post_id"=>{pid}, "meta_key"=>"_elementor_edit_mode"));')
    php_lines.append(f'}}')
    php_lines.append(f'delete_post_meta({pid}, "_elementor_element_cache");')
    php_lines.append(f'delete_post_meta({pid}, "_elementor_css");')
    php_lines.append(f'foreach (glob(ABSPATH . "wp-content/uploads/elementor/css/post-{pid}*.css") as $f) unlink($f);')
    php_lines.append(f'unlink($uploads . "pg_{pid}.json");')
    php_lines.append(f'echo "Done {pid}\\n";')

php_lines.append('unlink(__FILE__);')
php_lines.append('echo "ALL DONE\\n";')

php_content = '\n'.join(php_lines)
with sftp.open('clickandbuilds/INGBTC/update_pages.php', 'w') as f:
    f.write(php_content.encode('utf-8'))
print("Uploaded update_pages.php")

sftp.close()
ssh.close()

# Execute via HTTP
time.sleep(1)
ctx = ssl.create_default_context()
ctx.check_hostname = False
ctx.verify_mode = ssl.CERT_NONE
req = urllib.request.Request('https://ingmining.com/update_pages.php', headers={'User-Agent': 'Mozilla/5.0'})
with urllib.request.urlopen(req, context=ctx, timeout=90) as resp:
    result = resp.read().decode('utf-8', errors='replace')
print("Result:", result[:2000])
