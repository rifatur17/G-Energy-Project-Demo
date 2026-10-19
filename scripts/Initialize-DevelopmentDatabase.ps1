#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Server = 'localhost',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,127}$')][string] $Database = 'GEnergyDb',
    [ValidateSet('InitialIdentity','Latest')][string] $Through = 'Latest',
    [string] $DotnetPath = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskApi = Join-Path $taskRoot 'backend/GEnergy.Api'
function Invoke-ProjectDotnet([string[]] $Arguments) {
    & $DotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE. Database was not reset or deleted." }
}
$taskConnectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$taskConnectionBuilder['Server'] = $Server
$taskConnectionBuilder['Database'] = $Database
$taskConnectionBuilder['Integrated Security'] = 'True'
$taskConnectionBuilder['Encrypt'] = 'True'
$taskConnectionBuilder['TrustServerCertificate'] = 'True'
$taskConnection = $taskConnectionBuilder.ConnectionString
if ($Through -eq 'InitialIdentity') {
    $taskSqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
    $taskExists = & $taskSqlcmd -S $Server -d master -E -C -b -l 10 -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$Database') IS NULL THEN 0 ELSE 1 END;"
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server preflight failed.' }
    if (($taskExists -join '').Trim() -eq '1') {
        $taskHistoryQuery = "SET NOCOUNT ON; IF OBJECT_ID('dbo.__EFMigrationsHistory') IS NOT NULL EXEC(N'SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId <> ''20261018185818_InitialIdentity'') THEN ''LATER'' ELSE ''SAFE'' END;'); ELSE SELECT 'SAFE';"
        $taskHistory = & $taskSqlcmd -S $Server -d $Database -E -C -b -l 10 -h -1 -W -Q $taskHistoryQuery
        if ($LASTEXITCODE -ne 0) { throw 'Could not verify migration history; refusing a target migration.' }
        if (($taskHistory -join '').Trim() -ne 'SAFE') { throw 'Later migrations already exist. Refusing a downgrade; use -Through Latest.' }
    }
}
[xml] $taskProject = Get-Content (Join-Path $taskApi 'GEnergy.Api.csproj') -Raw
$taskSecretsId = $taskProject.GetElementsByTagName('UserSecretsId')[0].InnerText
$taskSecretsFile = Join-Path $env:APPDATA "Microsoft/UserSecrets/$taskSecretsId/secrets.json"
$taskExistingSecrets = if (Test-Path -LiteralPath $taskSecretsFile) { Get-Content -LiteralPath $taskSecretsFile -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
$taskPayload = @{ 'ConnectionStrings:DefaultConnection' = $taskConnection }
if (-not $taskExistingSecrets.ContainsKey('Jwt:SigningKey')) {
    $taskPayload['Jwt:SigningKey'] = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
} elseif ([Text.Encoding]::UTF8.GetByteCount([string]$taskExistingSecrets['Jwt:SigningKey']) -lt 32) {
    throw 'Existing Jwt:SigningKey is too short. Configure a valid random signing key in user secrets first.'
}
$taskPayload | ConvertTo-Json -Compress | & $DotnetPath user-secrets set --project $taskApi
if ($LASTEXITCODE -ne 0) { throw 'Could not save local user secrets. Secret values were not printed.' }
$taskPreviousEnvironment = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT','Process')
$taskPreviousConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection','Process')
Push-Location $taskRoot
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__DefaultConnection = $taskConnection
    Invoke-ProjectDotnet -Arguments @('tool','restore')
    Invoke-ProjectDotnet -Arguments @('restore')
    Invoke-ProjectDotnet -Arguments @('ef','migrations','has-pending-model-changes','--project',$taskApi)
    $taskMigrationArguments = @('ef','database','update')
    if ($Through -eq 'InitialIdentity') { $taskMigrationArguments += 'InitialIdentity' }
    Invoke-ProjectDotnet -Arguments ($taskMigrationArguments + @('--project',$taskApi))
    Write-Host "Development database configured: $Server / $Database ($Through). Existing data was preserved."
} finally {
    Pop-Location
    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT',$taskPreviousEnvironment,'Process')
    [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection',$taskPreviousConnection,'Process')
    $taskPayload.Clear()
}
