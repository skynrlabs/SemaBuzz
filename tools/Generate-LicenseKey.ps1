#Requires -Version 5.1
<#
.SYNOPSIS
    Generates a SemaBuzz Pro license key.

.DESCRIPTION
    Keys are validated offline in the app using HMAC-SHA256.
    The HmacSecret here MUST match the one in SemaBuzzLicense.cs.

    Key format (raw 24 chars):  [8-char nonce][16-char HMAC signature]
    Displayed as: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX

.PARAMETER Count
    Number of keys to generate (default 1).

.EXAMPLE
    .\Generate-LicenseKey.ps1
    .\Generate-LicenseKey.ps1 -Count 5
#>
param(
    [int]$Count = 1
)

# â”€â”€ KEEP THIS IN SYNC WITH SemaBuzzLicense.cs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
$HmacSecret = "SB-PRO-2026-CHANGEME"
# â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

$HmacSecretBytes = [System.Text.Encoding]::UTF8.GetBytes($HmacSecret)
$HexChars = '0123456789ABCDEF'.ToCharArray()

function New-LicenseKey {
    # 4 random bytes â†’ 8 uppercase hex chars (nonce)
    $nonceBytes = [byte[]]::new(4)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($nonceBytes)
    $nonce = ($nonceBytes | ForEach-Object { $_.ToString('X2') }) -join ''

    # HMAC-SHA256(secret, nonce) â†’ first 8 bytes â†’ 16 hex chars
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $HmacSecretBytes
    $hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($nonce))
    $sig  = ($hash[0..7] | ForEach-Object { $_.ToString('X2') }) -join ''

    $raw = $nonce + $sig  # 24 chars

    # Format as XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
    $formatted = ($raw -replace '(.{4})(?=.)', '$1-')
    return $formatted
}

for ($i = 0; $i -lt $Count; $i++) {
    New-LicenseKey
}
