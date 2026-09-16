param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OfflinePackageSource,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location -LiteralPath $projectRoot
try {
    $restoreArgs = @('restore', 'PhoneCompanion.slnx', '--nologo', '-m:1', '-nr:false', '-p:NuGetAudit=false')
    if ($OfflinePackageSource) { $restoreArgs += @('--source', $OfflinePackageSource) }
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build PhoneCompanion.slnx -c $Configuration --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & dotnet run --project tests/PhoneCompanion.Tests -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
        & dotnet run --project tests/PhoneCompanion.Windows.SmokeTests -c $Configuration --no-build -- artifacts/ui-smoke
        if ($LASTEXITCODE -ne 0) { throw 'Windows UI smoke checks failed.' }
    }
    & dotnet publish src/PhoneCompanion.Windows -c $Configuration --no-restore --no-build -o artifacts/PhoneCompanion --nologo -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host 'Ready: artifacts/PhoneCompanion/PhoneCompanion.exe'
}
finally { Pop-Location }
