#requires -Version 5.1
<#
.SYNOPSIS
Saves your SSLCommerz sandbox store credentials so G Energy opens the real SSLCommerz checkout.

.DESCRIPTION
1. Register a free sandbox store at https://developer.sslcommerz.com/registration/ (you create the account).
2. SSLCommerz emails a Store ID and Store Password for the sandbox.
3. Run this script and type them. They are stored in this project's .NET user secrets
   (outside the repository), never shown on screen and never committed.
4. Stop and start G Energy again.
Use -Remove to go back to the built-in demo checkout.
#>
[CmdletBinding()]
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = [IO.File]::ReadAllText((Join-Path $root 'backend\GEnergy.Api\GEnergy.Api.csproj'))
$id = [regex]::Match($project, '<UserSecretsId>\s*([^<\s]+)\s*</UserSecretsId>').Groups[1].Value
$folder = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id"; $file = Join-Path $folder 'secrets.json'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$secrets = if (Test-Path -LiteralPath $file) { [IO.File]::ReadAllText($file) | ConvertFrom-Json } else { New-Object PSObject }

if ($Remove) {
    foreach ($key in 'SslCommerz:StoreId', 'SslCommerz:StorePassword', 'SslCommerz:Sandbox') { $secrets.PSObject.Properties.Remove($key) }
    Write-Host 'SSLCommerz credentials removed. G Energy will use the demo checkout after a restart.' -ForegroundColor Green
} else {
    $storeId = (Read-Host 'SSLCommerz sandbox Store ID').Trim()
    if ($storeId -notmatch '^[A-Za-z0-9._-]{3,64}$') { throw 'That does not look like a Store ID. Nothing was changed.' }
    $password = [Net.NetworkCredential]::new('', (Read-Host 'SSLCommerz sandbox Store Password' -AsSecureString)).Password.Trim()
    if ($password.Length -lt 4) { throw 'The Store Password is empty. Nothing was changed.' }
    $secrets | Add-Member -NotePropertyName 'SslCommerz:StoreId' -NotePropertyValue $storeId -Force
    $secrets | Add-Member -NotePropertyName 'SslCommerz:StorePassword' -NotePropertyValue $password -Force
    $secrets | Add-Member -NotePropertyName 'SslCommerz:Sandbox' -NotePropertyValue $true -Force
    $password = $null
    Write-Host 'Saved. Stop and start G Energy; "Pay with SSLCommerz" will open the real SSLCommerz sandbox page.' -ForegroundColor Green
}
$temporary = "$file.tmp"
[IO.File]::WriteAllText($temporary, ($secrets | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath $file) { [IO.File]::Replace($temporary, $file, "$file.bak") } else { [IO.File]::Move($temporary, $file) }
