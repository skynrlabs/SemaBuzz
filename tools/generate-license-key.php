<?php
/**
 * SemaBuzz Pro â€” License Key Generator
 *
 * Intended use: Lemon Squeezy webhook handler. After a successful order,
 * generate a key, email it to the customer, and log it.
 *
 * Key format (raw 24 uppercase hex chars):
 *   [8-char nonce][16-char HMAC-SHA256 signature]
 *   Displayed as: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
 *
 * KEEP HMAC_SECRET IN SYNC WITH SemaBuzzLicense.cs
 */

// â”€â”€ KEEP THIS IN SYNC WITH SemaBuzzLicense.cs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
define('HMAC_SECRET', 'SB-PRO-2026-CHANGEME');
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

// â”€â”€ Lemon Squeezy config â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
define('LS_WEBHOOK_SECRET', 'CHANGEME');   // Lemon Squeezy â†’ Settings â†’ Webhooks â†’ Signing secret
define('MAIL_FROM',         'noreply@semabuzz.com');
define('MAIL_FROM_NAME',    'SemaBuzz');
// â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

// â”€â”€ Key generation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/**
 * Generate a cryptographically random nonce (4 bytes â†’ 8 uppercase hex chars).
 */
function generate_nonce(): string
{
    return strtoupper(bin2hex(random_bytes(4)));
}

/**
 * Compute the 16-char HMAC-SHA256 signature for a given nonce.
 */
function compute_signature(string $nonce): string
{
    $hash = hash_hmac('sha256', $nonce, HMAC_SECRET, true); // raw binary
    return strtoupper(bin2hex(substr($hash, 0, 8)));        // first 8 bytes â†’ 16 hex chars
}

/**
 * Generate a single formatted license key.
 * Returns the key as XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
 */
function generate_license_key(): string
{
    $nonce = generate_nonce();
    $sig   = compute_signature($nonce);
    $raw   = $nonce . $sig; // 24 uppercase hex chars

    // Insert dashes every 4 chars
    return implode('-', str_split($raw, 4));
}

// â”€â”€ Lemon Squeezy webhook handler â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/**
 * Verify the Lemon Squeezy webhook signature.
 * LS sends a plain HMAC-SHA256 hex digest in the X-Signature header.
 * Throws RuntimeException if the signature is invalid.
 */
function verify_ls_signature(string $payload, string $sigHeader, string $secret): void
{
    if (empty($sigHeader)) {
        throw new RuntimeException('Missing X-Signature header.');
    }

    $expected = hash_hmac('sha256', $payload, $secret);

    if (!hash_equals($expected, strtolower($sigHeader))) {
        throw new RuntimeException('Lemon Squeezy webhook signature mismatch.');
    }
}

/**
 * Send the license key to the customer via PHP mail().
 * Replace with your preferred mailer (PHPMailer, Postmark, SendGrid, etc.)
 */
function send_license_email(string $toEmail, string $toName, string $formattedKey): bool
{
    $subject = 'Your SemaBuzz Pro License Key';

    $body = <<<TEXT
Hi {$toName},

Thank you for purchasing SemaBuzz Pro!

Your license key is:

    {$formattedKey}

To activate:
  1. Open SemaBuzz
  2. Go to Settings (gear icon)
  3. Scroll to the LICENSE section
  4. Paste your key and click ACTIVATE

Your key never expires and works offline â€” keep it safe.

Thanks,
The SemaBuzz Team
TEXT;

    $headers  = "From: " . MAIL_FROM_NAME . " <" . MAIL_FROM . ">\r\n";
    $headers .= "Content-Type: text/plain; charset=UTF-8\r\n";

    return mail($toEmail, $subject, $body, $headers);
}

// â”€â”€ Entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

// Allow CLI usage: php generate-license-key.php [count]
if (PHP_SAPI === 'cli') {
    $count = isset($argv[1]) ? max(1, (int) $argv[1]) : 1;
    for ($i = 0; $i < $count; $i++) {
        echo generate_license_key() . PHP_EOL;
    }
    exit(0);
}

// â”€â”€ Stripe webhook (HTTP POST only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
    http_response_code(405);
    exit('Method Not Allowed');
}

$payload   = file_get_contents('php://input');
$sigHeader = $_SERVER['HTTP_X_SIGNATURE'] ?? '';

try {
    verify_ls_signature($payload, $sigHeader, LS_WEBHOOK_SECRET);
} catch (RuntimeException $e) {
    http_response_code(400);
    exit('Webhook verification failed: ' . $e->getMessage());
}

$event = json_decode($payload, true);

if (json_last_error() !== JSON_ERROR_NONE || empty($event['meta']['event_name'])) {
    http_response_code(400);
    exit('Invalid JSON payload.');
}

// Only act on paid orders (one-time purchase)
// For subscriptions use: subscription_created
$eventName = $event['meta']['event_name'];
if ($eventName !== 'order_created') {
    http_response_code(200);
    exit('Ignored event type: ' . $eventName);
}

$attrs         = $event['data']['attributes'] ?? [];
$customerEmail = $attrs['user_email'] ?? '';
$customerName  = $attrs['user_name']  ?? 'Customer';

// Optional: only act on paid status (guard against free/refunded orders)
if (($attrs['status'] ?? '') !== 'paid') {
    http_response_code(200);
    exit('Order not paid, ignored.');
}

if (empty($customerEmail)) {
    http_response_code(400);
    exit('No customer email in event.');
}

$licenseKey = generate_license_key();

// Log the key (append to a local file; replace with DB insert as needed)
$logLine = implode("\t", [
    date('c'),
    $event['data']['id']  ?? 'unknown-order',
    $customerEmail,
    $licenseKey,
]) . PHP_EOL;

file_put_contents(__DIR__ . '/license-log.tsv', $logLine, FILE_APPEND | LOCK_EX);

// Email the key
$sent = send_license_email($customerEmail, $customerName, $licenseKey);

http_response_code(200);
echo json_encode(['ok' => true, 'sent' => $sent]);
