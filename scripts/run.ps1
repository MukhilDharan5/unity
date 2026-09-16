param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts/PhoneCompanion/PhoneCompanion.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build first with scripts/build.ps1.' }
$launchArgs = @('--show')
if ($Demo) { $launchArgs += '--demo' }
Start-Process -FilePath $executable -ArgumentList $launchArgs -WindowStyle Hidden
