param([string]$Out = 'build/NewEra-MemorySelect.exe')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

Invoke-Csc -Target winexe -Out $Out -Main 'MemorySelectV3Form'
Write-Output 'Build OK'
