#requires -Version 5.1
<#
.SYNOPSIS
Creates (or resets) the SQL login the Docker-hosted G Energy API uses to reach GEnergyDb.

.DESCRIPTION
Run this yourself, once, in Windows PowerShell on the PC that hosts SQL Server. It:
  1. checks that SQL Server accepts SQL logins and listens on TCP 1433 (you enable both yourself),
  2. asks you to type a password (never shown, never written to the console or a log),
  3. creates/updates the login 'genergy_app' with access only to GEnergyDb
     (read, write and schema changes for reviewed migrations; no server-level rights),
  4. saves ConnectionStrings:ContainerConnection in this project's .NET user secrets.
No database is dropped or reset, and no existing data is changed.
#>
[CmdletBinding()]
param(
    [string]$Server = 'localhost',
    [string]$Database = 'GEnergyDb',
    [string]$Login = 'genergy_app'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Invoke-Sql([string]$Sql, [hashtable]$Parameters = @{}) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
    try {
        $connection.Open()
        $command = $connection.CreateCommand(); $command.CommandText = $Sql
        foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
        return $command.ExecuteScalar()
    } finally { $connection.Dispose() }
}

# Builds every statement in variables (EXEC cannot concatenate function calls) and quotes the
# password server-side, so it never appears in SQL text or logs.
$script:LoginSql = @"
DECLARE @quotedPassword nvarchar(max) = N'''' + REPLACE(@password, N'''', N'''''') + N'''';
DECLARE @sql nvarchar(max);
IF SUSER_ID(@login) IS NULL
    SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + @quotedPassword + N', CHECK_POLICY = ON, DEFAULT_DATABASE = ' + QUOTENAME(@db) + N';';
ELSE
    SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + @quotedPassword + N', CHECK_POLICY = ON; ALTER LOGIN ' + QUOTENAME(@login) + N' ENABLE;';
EXEC sys.sp_executesql @sql;
DECLARE @createUser nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
SET @sql = N'USE ' + QUOTENAME(@db) + N';
IF DATABASE_PRINCIPAL_ID(@login) IS NULL EXEC sys.sp_executesql @createUser;
IF IS_ROLEMEMBER(N''db_datareader'', @login) = 0 OR IS_ROLEMEMBER(N''db_datareader'', @login) IS NULL EXEC sp_addrolemember N''db_datareader'', @login;
IF IS_ROLEMEMBER(N''db_datawriter'', @login) = 0 OR IS_ROLEMEMBER(N''db_datawriter'', @login) IS NULL EXEC sp_addrolemember N''db_datawriter'', @login;
IF IS_ROLEMEMBER(N''db_ddladmin'', @login) = 0 OR IS_ROLEMEMBER(N''db_ddladmin'', @login) IS NULL EXEC sp_addrolemember N''db_ddladmin'', @login;';
EXEC sys.sp_executesql @sql, N'@login sysname, @createUser nvarchar(max)', @login = @login, @createUser = @createUser;
"@

Write-Host 'Checking SQL Server...' -ForegroundColor Cyan
# One-step server preparation: SQL logins (mixed mode) and a fixed TCP port 1433, so the
# Docker container can reach SQL Server through host.docker.internal. Asks before changing.
$instance = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue).MSSQLSERVER
if ($instance) {
    $serverKey = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instance\MSSQLServer"
    $tcpKey = "$serverKey\SuperSocketNetLib\Tcp"
    $mixed = (Get-ItemProperty $serverKey).LoginMode -eq 2
    $tcpOn = (Get-ItemProperty $tcpKey).Enabled -eq 1 -and (Get-ItemProperty "$tcpKey\IPAll").TcpPort -eq '1433'
    if (-not ($mixed -and $tcpOn)) {
        $admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        if (-not $admin) {
            # Reopen this script in an elevated window; Windows asks you to approve (UAC).
            Write-Host "`nSQL Server settings need Administrator rights. Click 'Yes' on the Windows prompt; the setup continues in a new window." -ForegroundColor Yellow
            try {
                Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Server', $Server, '-Database', $Database, '-Login', $Login) | Out-Null
            } catch {
                Write-Host 'The Administrator prompt was cancelled. Nothing was changed.' -ForegroundColor Yellow
            }
            exit 1
        }
        Write-Host "`nThis will change SQL Server ($instance):" -ForegroundColor Yellow
        if (-not $mixed) { Write-Host '  - Authentication: Windows only  ->  SQL Server and Windows Authentication' }
        if (-not $tcpOn) { Write-Host '  - TCP/IP: enable on fixed port 1433' }
        Write-Host '  - Restart the SQL Server (MSSQLSERVER) service (a few seconds; no data is changed)'
        if ((Read-Host 'Type Y to continue') -notin 'Y', 'y') { Write-Host 'Nothing was changed.'; exit 1 }
        if (-not $mixed) { Set-ItemProperty $serverKey -Name LoginMode -Value 2 -Type DWord }
        if (-not $tcpOn) {
            Set-ItemProperty $tcpKey -Name Enabled -Value 1 -Type DWord
            Set-ItemProperty "$tcpKey\IPAll" -Name TcpDynamicPorts -Value ''
            Set-ItemProperty "$tcpKey\IPAll" -Name TcpPort -Value '1433'
        }
        Write-Host 'Restarting SQL Server...' -ForegroundColor Cyan
        Restart-Service MSSQLSERVER -Force
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-Date) -lt $deadline -and -not (Get-NetTCPConnection -LocalPort 1433 -State Listen -ErrorAction SilentlyContinue)) { Start-Sleep -Seconds 2 }
        Write-Host 'SQL Server settings updated.' -ForegroundColor Green
    }
}
if ([int](Invoke-Sql "SELECT CONVERT(int, SERVERPROPERTY('IsIntegratedSecurityOnly'))") -eq 1) {
    Write-Host @'

SQL Server only accepts Windows logins. Turn on SQL logins first (you do this, once):
  1. Open SQL Server Management Studio and connect to localhost.
  2. Right-click the server > Properties > Security.
  3. Choose "SQL Server and Windows Authentication mode" > OK.
  4. Restart the "SQL Server (MSSQLSERVER)" service (Windows Services, or right-click the server > Restart).
Then run this script again.
'@ -ForegroundColor Yellow
    exit 1
}
if (-not (Get-NetTCPConnection -LocalPort 1433 -State Listen -ErrorAction SilentlyContinue)) {
    Write-Host @'

SQL Server is not listening on TCP port 1433. Enable TCP/IP first (you do this, once):
  1. Open "SQL Server 2022 Configuration Manager" (or your version) from the Start menu.
  2. SQL Server Network Configuration > Protocols for MSSQLSERVER > TCP/IP > Enable.
  3. Double-click TCP/IP > IP Addresses > IPAll: clear "TCP Dynamic Ports", set "TCP Port" to 1433.
  4. Restart the "SQL Server (MSSQLSERVER)" service.
Then run this script again.
'@ -ForegroundColor Yellow
    exit 1
}
if (-not (Invoke-Sql 'SELECT DB_ID(@db)' @{ '@db' = $Database })) { throw "Database $Database was not found on $Server. It is never created by this script." }

Write-Host "Choose a password for the SQL login '$Login' (at least 12 characters, with upper, lower, digit and symbol)." -ForegroundColor Cyan
$first = Read-Host 'Password' -AsSecureString
$second = Read-Host 'Repeat password' -AsSecureString
$plain = [Net.NetworkCredential]::new('', $first).Password
if ($plain -ne [Net.NetworkCredential]::new('', $second).Password) { throw 'The passwords do not match. Nothing was changed.' }
if ($plain.Length -lt 12) { throw 'Use at least 12 characters. Nothing was changed.' }
try {
    # The password travels as a SQL parameter and is quoted server-side; it never appears in SQL text or logs.
    Invoke-Sql $script:LoginSql @{ '@login' = $Login; '@password' = $plain; '@db' = $Database } | Out-Null

    # Save the container connection next to the existing secrets without touching other keys.
    $project = [IO.File]::ReadAllText((Join-Path $root 'backend\GEnergy.Api\GEnergy.Api.csproj'))
    $id = [regex]::Match($project, '<UserSecretsId>\s*([^<\s]+)\s*</UserSecretsId>').Groups[1].Value
    $folder = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id"; $file = Join-Path $folder 'secrets.json'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $secrets = if (Test-Path -LiteralPath $file) { [IO.File]::ReadAllText($file) | ConvertFrom-Json } else { New-Object PSObject }
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Server'] = 'host.docker.internal,1433'; $builder['Database'] = $Database; $builder['User ID'] = $Login; $builder['Password'] = $plain
    $builder['Encrypt'] = $true; $builder['TrustServerCertificate'] = $true
    $secrets | Add-Member -NotePropertyName 'ConnectionStrings:ContainerConnection' -NotePropertyValue $builder.ConnectionString -Force
    $temporary = "$file.tmp"
    [IO.File]::WriteAllText($temporary, ($secrets | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $file) { [IO.File]::Replace($temporary, $file, "$file.bak") } else { [IO.File]::Move($temporary, $file) }
} finally {
    $plain = $null; $builder = $null
}
Write-Host "Done. Login '$Login' can use $Database only, and the Docker API connection is saved in user secrets." -ForegroundColor Green
Write-Host 'Next: double-click "Start G Energy.vbs".' -ForegroundColor Green
