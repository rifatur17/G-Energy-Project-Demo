#requires -Version 5.1
<#
.SYNOPSIS
Runs the backend tests inside the official .NET SDK container.

.DESCRIPTION
Use this on PCs where Windows Smart App Control blocks locally built test DLLs. The repository
is mounted read-only and copied inside the container, so no build output is written back to
Windows. Frontend tests are JavaScript and still run on Windows with "npm test".
  .\scripts\Test-InDocker.ps1
  .\scripts\Test-InDocker.ps1 -Filter "FullyQualifiedName~Billing"
#>
[CmdletBinding()]
param([string]$Filter)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$docker = @((Get-Command docker.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:LOCALAPPDATA 'Programs\DockerDesktop\resources\bin\docker.exe'),
    (Join-Path $env:ProgramFiles 'Docker\Docker\resources\bin\docker.exe')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (!$docker) { throw 'Docker Desktop was not found.' }
$env:PATH = (Split-Path $docker -Parent) + ';' + $env:PATH
$filterArgument = if ($Filter) { "--filter '" + $Filter.Replace("'", "") + "'" } else { '' }
$script = "set -e; mkdir -p /work; cd /repo; cp -r global.json GEnergy.slnx dotnet-tools.json backend tests /work/; " +
    "find /work -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} +; " +
    "cd /work; dotnet test GEnergy.slnx --nologo $filterArgument"
& $docker run --rm -v "${root}:/repo:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash -c $script
exit $LASTEXITCODE
