<?php
// eBay OAuth relay — place this at ingmining.com/ebay-callback/index.php
// When eBay redirects here with ?code=..., this forwards it to the local app.
$code  = $_GET['code']  ?? '';
$state = $_GET['state'] ?? '';

if ($code !== '') {
    $target = 'http://localhost:9330/api/ebay/callback'
            . '?code='  . urlencode($code)
            . '&state=' . urlencode($state);
    header('Location: ' . $target, true, 302);
    exit;
}
?>
<!DOCTYPE html>
<html><body>
<p>Waiting for eBay login...</p>
</body></html>
