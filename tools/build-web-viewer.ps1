param(
[Parameter(Mandatory = $true)]
[string] $GodotPath,

```
[string] $TrackPath = "",

[string] $PresetName = "Web"
```

)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$webProjectRoot = Join-Path $repoRoot "web-viewer"
$webProjectFile = Join-Path $webProjectRoot "project.godot"
$distRoot = Join-Path $repoRoot "dist\web"
$embeddedTrackPath = Join-Path $webProjectRoot "tracks\default-track.json"
$externalTrackDirectory = Join-Path $distRoot "tracks"
$externalTrackPath = Join-Path $externalTrackDirectory "default-track.json"
$exportIndexPath = Join-Path $distRoot "index.html"

if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
throw "Godot executable was not found: $GodotPath"
}

if (-not (Test-Path -LiteralPath $webProjectFile -PathType Leaf)) {
throw "Web Viewer project was not found: $webProjectFile"
}

$godotFileName = [System.IO.Path]::GetFileName($GodotPath)

if ($godotFileName -match "(?i)mono|dotnet") {
Write-Warning (
"The selected Godot executable appears to be a .NET build. " +
"Use the standard Godot build for the GDScript Web Viewer."
)
}

if (-not [string]::IsNullOrWhiteSpace($TrackPath)) {
$resolvedTrackPath = [System.IO.Path]::GetFullPath(
(Join-Path (Get-Location) $TrackPath)
)

```
if (-not (Test-Path -LiteralPath $resolvedTrackPath -PathType Leaf)) {
    throw "Track JSON was not found: $resolvedTrackPath"
}

$embeddedTrackDirectory = Split-Path -Parent $embeddedTrackPath

New-Item `
    -ItemType Directory `
    -Force `
    -Path $embeddedTrackDirectory |
    Out-Null

Copy-Item `
    -LiteralPath $resolvedTrackPath `
    -Destination $embeddedTrackPath `
    -Force

Write-Host "Updated embedded fallback Track:"
Write-Host "  $embeddedTrackPath"
```

}

if (-not (Test-Path -LiteralPath $embeddedTrackPath -PathType Leaf)) {
throw (
"Fallback Track JSON is missing: $embeddedTrackPath`n" +
"Pass -TrackPath or add web-viewer/tracks/default-track.json."
)
}

if (Test-Path -LiteralPath $distRoot) {
Remove-Item `        -LiteralPath $distRoot`
-Recurse `
-Force
}

New-Item `    -ItemType Directory`
-Force `
-Path $distRoot |
Out-Null

Write-Host "Exporting Godot Web Viewer..."
Write-Host "Project: $webProjectRoot"
Write-Host "Preset:  $PresetName"
Write-Host "Output:  $exportIndexPath"

& $GodotPath `    --headless`
--path $webProjectRoot `    --export-release $PresetName`
$exportIndexPath

if ($LASTEXITCODE -ne 0) {
throw "Godot Web export failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $exportIndexPath -PathType Leaf)) {
throw "Godot reported success, but index.html was not created."
}

New-Item `    -ItemType Directory`
-Force `
-Path $externalTrackDirectory |
Out-Null

Copy-Item `    -LiteralPath $embeddedTrackPath`
-Destination $externalTrackPath `
-Force

# Prevent accidental Jekyll processing if the folder is ever deployed

# through a branch-based Pages configuration.

New-Item `    -ItemType File`
-Force `
-Path (Join-Path $distRoot ".nojekyll") |
Out-Null

Write-Host ""
Write-Host "Web Viewer export completed:"
Write-Host "  $distRoot"
Write-Host ""
Write-Host "External Track:"
Write-Host "  $externalTrackPath"
Write-Host ""
Write-Host "Local test:"
Write-Host "  cd `"$distRoot`""
Write-Host "  python -m http.server 8060"
Write-Host "  open http://localhost:8060/"

## FILE: .github/workflows/deploy-web-viewer.yml

name: Deploy Web Viewer

on:
push:
branches:
- main
paths:
- "dist/web/**"
- ".github/workflows/deploy-web-viewer.yml"

workflow_dispatch:

permissions:
contents: read
pages: write
id-token: write

concurrency:
group: github-pages
cancel-in-progress: true

jobs:
deploy:
name: Deploy GitHub Pages
runs-on: ubuntu-latest

```
environment:
  name: github-pages
  url: ${{ steps.deployment.outputs.page_url }}

steps:
  - name: Checkout repository
    uses: actions/checkout@v4

  - name: Verify Pages artifact
    shell: bash
    run: |
      set -euo pipefail

      if [ ! -f "dist/web/index.html" ]; then
        echo "::error::dist/web/index.html was not found."
        exit 1
      fi

      if [ ! -f "dist/web/tracks/default-track.json" ]; then
        echo "::error::dist/web/tracks/default-track.json was not found."
        exit 1
      fi

      echo "Pages artifact:"
      find dist/web -maxdepth 3 -type f -print

  - name: Configure GitHub Pages
    uses: actions/configure-pages@v5

  - name: Upload GitHub Pages artifact
    uses: actions/upload-pages-artifact@v4
    with:
      path: dist/web

  - name: Deploy GitHub Pages
    id: deployment
    uses: actions/deploy-pages@v4
```
