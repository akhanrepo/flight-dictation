# Builds an installer + update package into artifacts\releases, the local
# update feed that installed copies read (Flight:UpdateFeed in appsettings.json).
#   .\scripts\pack-local.ps1 -Version 0.1.1
# Then run artifacts\releases\FlightApp-win-Setup.exe to install, or restart an
# installed copy to pick the new version up (it applies on exit).
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'publish'

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\src\Flight.App\Flight.App.csproj" -c Release -r win-x64 --self-contained `
    -p:Platform=x64 -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw "publish failed" }

# vpk targets .NET 8; this machine may only have a newer runtime.
$env:DOTNET_ROLL_FORWARD = 'Major'
# Pack id must not be "Flight": uninstall deletes %LOCALAPPDATA%\<packId>,
# and %LOCALAPPDATA%\Flight holds the database and models.
vpk pack --packId FlightApp --packVersion $Version --packDir $publish `
    --mainExe Flight.App.exe --packTitle Flight --outputDir "$root\artifacts\releases"
if ($LASTEXITCODE) { throw "vpk pack failed" }
