#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop')][string]$Action = 'Start',
    [switch]$NoBrowser,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$taskHashAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $taskHash = ([BitConverter]::ToString($taskHashAlgorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskRoot.ToLowerInvariant())))).Replace('-', '').Substring(0, 16) }
finally { $taskHashAlgorithm.Dispose() }
$taskRuntime = Join-Path $env:LOCALAPPDATA "GEnergyLauncher\$taskHash"
New-Item -ItemType Directory -Path $taskRuntime -Force | Out-Null
$taskStatePath = Join-Path $taskRuntime 'processes.json'
$taskEntries = @()
$taskStarted = @()
$taskMutex = New-Object Threading.Mutex($false, "Local\GEnergyLauncher_$taskHash")
$taskHasLock = $false

function Show-Notice([string]$Message, [bool]$Failure = $false) {
    # A modal dialog must never hold the Start/Stop lock while waiting for OK.
    if ($script:taskHasLock) {
        $taskMutex.ReleaseMutex()
        $script:taskHasLock = $false
    }
    if (!$Quiet) {
        $taskDialog = New-Object -ComObject WScript.Shell
        $taskIcon = if ($Failure) { 16 } else { 64 }
        [void]$taskDialog.Popup($Message, 0, 'G Energy', $taskIcon)
    }
}
function Save-State {
    # This file contains process identities only, never credentials or connection strings.
    $taskTemporaryState = $taskStatePath + '.tmp'
    ConvertTo-Json -InputObject @($script:taskEntries) -Depth 4 | Set-Content -LiteralPath $taskTemporaryState -Encoding UTF8
    if (Test-Path -LiteralPath $taskStatePath) { [IO.File]::Replace($taskTemporaryState, $taskStatePath, ($taskStatePath + '.bak')) }
    else { [IO.File]::Move($taskTemporaryState, $taskStatePath) }
}
function Get-OwnedProcess($Entry) {
    if (!$Entry) { return $null }
    $taskProcess = Get-Process -Id $Entry.ProcessId -ErrorAction SilentlyContinue
    if (!$taskProcess) { return $null }
    try {
        if ($taskProcess.StartTime.ToUniversalTime().Ticks -ne [long]$Entry.StartTicks) { return $null }
        $taskDetails = Get-CimInstance Win32_Process -Filter "ProcessId=$($Entry.ProcessId)"
        if ($taskDetails.ExecutablePath -ine $Entry.Executable -or !$taskDetails.CommandLine.Contains([string]$Entry.Marker)) { return $null }
        return $taskProcess
    } catch { return $null }
}
function Find-Tool([string]$Name, [string[]]$Fallbacks) {
    $taskCommand = Get-Command $Name -ErrorAction SilentlyContinue
    if ($taskCommand) { return $taskCommand.Source }
    foreach ($taskCandidate in $Fallbacks) { if (Test-Path -LiteralPath $taskCandidate) { return $taskCandidate } }
    throw "$Name was not found. See README.md for prerequisites."
}
function Assert-FreePort([int]$Port) {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $Port is already used by a process this launcher did not start. Close its original terminal or app, then double-click Start G Energy again. No process was stopped."
    }
}
function Recover-ProjectProcess([string]$Name, [int]$Port, [string]$Executable, [string]$Marker) {
    # A missing/stale state file must not make an already running copy of this
    # exact project look like an unrelated port conflict. Never adopt by port alone.
    $taskListenerIds = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    if ($taskListenerIds.Count -ne 1) { return $null }
    try {
        $taskDetails = Get-CimInstance Win32_Process -Filter "ProcessId=$($taskListenerIds[0])"
        if ($taskDetails.ExecutablePath -ine $Executable) { return $null }
        $taskMarkerPattern = '(?i)(?:^|\s)"?' + [regex]::Escape($Marker) + '"?(?=\s|$)'
        if ($taskDetails.CommandLine -notmatch $taskMarkerPattern) { return $null }
        $taskOwner = Invoke-CimMethod -InputObject $taskDetails -MethodName GetOwnerSid
        if ($taskOwner.ReturnValue -ne 0 -or $taskOwner.Sid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { return $null }
        $taskProcess = Get-Process -Id $taskListenerIds[0]
        $taskEntry = [pscustomobject]@{ Name = $Name; ProcessId = $taskProcess.Id; StartTicks = [string]$taskProcess.StartTime.ToUniversalTime().Ticks; Executable = $taskDetails.ExecutablePath; Marker = $Marker }
        if (!(Get-OwnedProcess $taskEntry)) { return $null }
        $script:taskEntries += $taskEntry
        Save-State
        return $taskEntry
    } catch { return $null }
}
function Run-Hidden([string]$Executable, [string]$Arguments, [string]$Directory, [string]$Name) {
    $taskInfo = New-Object Diagnostics.ProcessStartInfo
    $taskInfo.FileName = $Executable
    $taskInfo.Arguments = $Arguments
    $taskInfo.WorkingDirectory = $Directory
    $taskInfo.UseShellExecute = $false
    $taskInfo.CreateNoWindow = $true
    $taskInfo.RedirectStandardOutput = $true
    $taskInfo.RedirectStandardError = $true
    $taskProcess = New-Object Diagnostics.Process
    $taskProcess.StartInfo = $taskInfo
    try {
        [void]$taskProcess.Start()
        $taskOutput = $taskProcess.StandardOutput.ReadToEndAsync()
        $taskErrors = $taskProcess.StandardError.ReadToEndAsync()
        if (!$taskProcess.WaitForExit(120000)) {
            if (!$taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
            throw "$Name took more than two minutes. Check your internet connection if dependencies need restoring. See logs in $taskRuntime"
        }
        [IO.File]::WriteAllText((Join-Path $taskRuntime "$Name.out.log"), $taskOutput.Result)
        [IO.File]::WriteAllText((Join-Path $taskRuntime "$Name.err.log"), $taskErrors.Result)
        if ($taskProcess.ExitCode -ne 0) { throw "$Name failed (exit $($taskProcess.ExitCode)). See logs in $taskRuntime" }
    } finally { $taskProcess.Dispose() }
}
function Start-ServiceProcess([string]$Executable, [string]$Arguments, [string]$Directory, [string]$Name, [string]$Marker) {
    $taskProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -WorkingDirectory $Directory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRuntime "$Name.out.log") -RedirectStandardError (Join-Path $taskRuntime "$Name.err.log")
    $taskEntry = [pscustomobject]@{ Name = $Name; ProcessId = $taskProcess.Id; StartTicks = [string]$taskProcess.StartTime.ToUniversalTime().Ticks; Executable = $Executable; Marker = $Marker }
    $script:taskEntries += $taskEntry
    $script:taskStarted += $taskEntry
    Save-State
    return $taskEntry
}
function Wait-Ready($Entry, [string]$Url) {
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if (!(Get-OwnedProcess $Entry)) { throw "$($Entry.Name) exited. See logs in $taskRuntime" }
        try {
            $taskResponse = Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 2
            if ($taskResponse.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 400
    } while ([DateTime]::UtcNow -lt $taskDeadline)
    throw "$($Entry.Name) did not become ready at $Url. See logs in $taskRuntime"
}

try {
    try { $taskHasLock = $taskMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $taskHasLock = $true }
    # Double-clicks during startup are harmless; do not stack modal error dialogs.
    if (!$taskHasLock) { Write-Output 'G Energy is already starting or stopping; duplicate request ignored.'; return }
    if (Test-Path -LiteralPath $taskStatePath) {
        # Windows PowerShell 5.1 emits a JSON array as one pipeline object.
        try {
            $taskSavedEntries = Get-Content -LiteralPath $taskStatePath -Raw | ConvertFrom-Json
            $taskEntries = @($taskSavedEntries | Where-Object { Get-OwnedProcess $_ })
        } catch { $taskEntries = @() }
    }
    if ($Action -eq 'Stop') {
        foreach ($taskEntry in @($taskEntries | Sort-Object Name -Descending)) {
            $taskProcess = Get-OwnedProcess $taskEntry
            if ($taskProcess) { Stop-Process -Id $taskProcess.Id -ErrorAction Stop; $taskProcess.WaitForExit(10000) | Out-Null }
        }
        $taskEntries = @()
        Save-State
        Show-Notice 'G Energy has stopped. SQL Server and all saved database records are kept.'
        Write-Output 'G Energy stopped.'
        return
    }

    $taskSqlService = Get-Service MSSQLSERVER -ErrorAction SilentlyContinue
    if (!$taskSqlService -or $taskSqlService.Status -ne 'Running') { throw 'SQL Server (MSSQLSERVER) is not running. Open Windows Services, start it, then try again. The database will not be reset.' }
    $taskDotnet = Find-Tool 'dotnet.exe' @((Join-Path (Split-Path $taskRoot -Parent) 'runtimes\projectflow-dotnet\dotnet.exe'), "$env:ProgramFiles\dotnet\dotnet.exe")
    $taskNode = Find-Tool 'node.exe' @("$env:ProgramFiles\nodejs\node.exe")
    $taskApiRoot = Join-Path $taskRoot 'backend\GEnergy.Api'
    $taskWebRoot = Join-Path $taskRoot 'frontend'
    $taskDll = Join-Path $taskApiRoot 'bin\Release\net10.0\GEnergy.Api.dll'
    $taskVite = Join-Path $taskWebRoot 'node_modules\vite\bin\vite.js'
    $taskApiEntry = @($taskEntries | Where-Object Name -eq 'api') | Select-Object -First 1
    $taskWebEntry = @($taskEntries | Where-Object Name -eq 'web') | Select-Object -First 1
    if (!$taskApiEntry) { $taskApiEntry = Recover-ProjectProcess 'api' 5080 $taskDotnet $taskDll }
    if (!$taskWebEntry) { $taskWebEntry = Recover-ProjectProcess 'web' 5173 $taskNode $taskVite }
    if (!$taskApiEntry) { Assert-FreePort 5080 }
    if (!$taskWebEntry) { Assert-FreePort 5173 }
    if (!(Test-Path -LiteralPath $taskVite)) {
        $taskNpm = Join-Path (Split-Path $taskNode -Parent) 'node_modules\npm\bin\npm-cli.js'
        if (!(Test-Path -LiteralPath $taskNpm)) { throw 'npm is missing. Install Node.js with npm and try again.' }
        Run-Hidden $taskNode ('"{0}" ci' -f $taskNpm) $taskWebRoot 'dependencies'
    }
    if (!$taskApiEntry) {
        $taskBuildArguments = 'build "{0}" --configuration Release' -f (Join-Path $taskApiRoot 'GEnergy.Api.csproj')
        # Installed projects can launch offline; restore only on first setup.
        if (Test-Path -LiteralPath (Join-Path $taskApiRoot 'obj\project.assets.json')) { $taskBuildArguments += ' --no-restore' }
        Run-Hidden $taskDotnet $taskBuildArguments $taskRoot 'build'
        $taskPreviousEnvironment = $env:ASPNETCORE_ENVIRONMENT
        try {
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            $taskApiEntry = Start-ServiceProcess $taskDotnet ('"{0}" --urls http://localhost:5080' -f $taskDll) $taskApiRoot 'api' $taskDll
        } finally { $env:ASPNETCORE_ENVIRONMENT = $taskPreviousEnvironment }
    }
    Wait-Ready $taskApiEntry 'http://localhost:5080/swagger/v1/swagger.json'
    if (!$taskWebEntry) {
        $taskWebEntry = Start-ServiceProcess $taskNode ('"{0}" --host localhost --port 5173 --strictPort' -f $taskVite) $taskWebRoot 'web' $taskVite
    }
    Wait-Ready $taskWebEntry 'http://localhost:5173/'
    if (!$NoBrowser) { Start-Process 'http://localhost:5173/login' }
    Write-Output "G Energy ready: http://localhost:5173 (API http://localhost:5080). Logs: $taskRuntime"
} catch {
    $taskFailure = $_.Exception.Message
    # Roll back only processes created by this failed launch; preserve previously running services.
    foreach ($taskEntry in $taskStarted) {
        $taskProcess = Get-OwnedProcess $taskEntry
        if ($taskProcess) { Stop-Process -Id $taskProcess.Id -ErrorAction SilentlyContinue }
    }
    if ($taskHasLock) {
        $taskEntries = @($taskEntries | Where-Object { Get-OwnedProcess $_ })
        Save-State
        Add-Content -LiteralPath (Join-Path $taskRuntime 'launcher-errors.log') -Value "$([DateTime]::Now.ToString('s')) $taskFailure"
    }
    Show-Notice ($taskFailure + "`r`n`r`nLogs: " + $taskRuntime) $true
    Write-Error $taskFailure -ErrorAction Continue
    exit 1
} finally {
    if ($taskHasLock) { $taskMutex.ReleaseMutex() }
    $taskMutex.Dispose()
}
