$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)

$tests = Get-ChildItem -LiteralPath 'tests' -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }
Invoke-Csc -Target exe -Out 'build/V3Checks.exe' -Main 'V3Checks' -ExtraSources $tests
Write-Output 'Build OK'

& .\build\V3Checks.exe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
