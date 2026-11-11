# Publishes a LumenLite release that running copies pick up by themselves.
#
#   ./release.ps1 1.4.1 "Ghi chú cho bản này"
#
# Sets <Version> in LumenLite.csproj, builds the single-file LumenLite.exe, and creates the release v<version> in the
# repository below with LumenLite.exe attached. Point $repo (and Repository in Services/Updater.cs) at your own fork.
# Each LumenLite checks that repository a minute after it starts and every six hours.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'TienDang-VFX/LumenLite-releases'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.4.1" }
$project = Join-Path $PSScriptRoot 'LumenLite.csproj'
$text = Get-Content $project -Raw
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($project, $text)

$out = Join-Path $PSScriptRoot 'dist\LumenLite-win-x64'
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$exe = Join-Path $out 'LumenLite.exe'
$built = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $built.StartsWith($Version)) { throw "Built $built, expected $Version" }

if (-not $Notes) { $Notes = (git log -1 --format=%s) }
$commit = (git rev-parse --short HEAD)
gh release create "v$Version" $exe -R $repo --title "LumenLite $Version" --notes "$Notes`n`nBuilt from $commit."
if ($LASTEXITCODE -ne 0) { throw "Release failed" }
Write-Host "Released LumenLite $Version to https://github.com/$repo/releases/tag/v$Version"
