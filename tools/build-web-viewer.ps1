[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $GodotPath,

    [string] $TrackPath = "exports/tracks/new-track-001.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$webProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "web-viewer"))
$distRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist\web"))
$distBoundary = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "dist")) + [System.IO.Path]::DirectorySeparatorChar
$godotExecutable = [System.IO.Path]::GetFullPath($GodotPath)
$sourceTrack = if ([System.IO.Path]::IsPathRooted($TrackPath)) {
    [System.IO.Path]::GetFullPath($TrackPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $TrackPath))
}
$embeddedTrack = Join-Path $webProjectRoot "tracks\default-track.json"
$godotLog = Join-Path $webProjectRoot ".web-viewer-tooling-cache\build-web-viewer.log"
$outputIndex = Join-Path $distRoot "index.html"
$outputPack = Join-Path $distRoot "index.pck"
$externalTrack = Join-Path $distRoot "tracks\default-track.json"

function Invoke-Godot {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [Parameter(Mandatory = $true)]
        [string] $Operation
    )

    # Standard Godot on Windows is a GUI-subsystem executable. PowerShell's call
    # operator can return before it exits, so Start-Process is required for a
    # trustworthy exit code in local and CI-like scripted builds.
    $argumentsWithLog = @("--log-file", $godotLog) + $Arguments
    $quotedArguments = foreach ($argument in $argumentsWithLog) {
        '"' + $argument.Replace('"', '\"') + '"'
    }
    $process = Start-Process `
        -FilePath $godotExecutable `
        -ArgumentList $quotedArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "$Operation failed with exit code $($process.ExitCode)."
    }
}

if (-not (Test-Path -LiteralPath $godotExecutable -PathType Leaf)) {
    throw "Godot executable was not found: $godotExecutable"
}
if ([System.IO.Path]::GetFileName($godotExecutable) -match "(?i)mono|\.net") {
    throw "Use the regular Godot executable for the GDScript Web Viewer, not a .NET/Mono build: $godotExecutable"
}
if (-not (Test-Path -LiteralPath (Join-Path $webProjectRoot "project.godot") -PathType Leaf)) {
    throw "Web Viewer project is missing: $webProjectRoot"
}
if (-not (Test-Path -LiteralPath $sourceTrack -PathType Leaf)) {
    throw "Track v5 source was not found: $sourceTrack"
}
if (@(Get-ChildItem -LiteralPath $webProjectRoot -Recurse -Filter "*.cs" -File).Count -ne 0) {
    throw "Web Viewer must not contain C# scripts."
}

$track = Get-Content -LiteralPath $sourceTrack -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $track -or $track.formatVersion -ne 5) {
    throw "Track source must be an Exported Track with formatVersion 5: $sourceTrack"
}

Copy-Item -LiteralPath $sourceTrack -Destination $embeddedTrack -Force

Invoke-Godot `
    -Arguments @("--headless", "--editor", "--path", $webProjectRoot, "--quit") `
    -Operation "Godot import"

Invoke-Godot `
    -Arguments @("--headless", "--path", $webProjectRoot, "--script", "res://tests/track_v4_parser_test.gd") `
    -Operation "Track v5 parser and marking Path tests"

if (-not $distRoot.StartsWith($distBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace output outside the repository dist directory: $distRoot"
}
if (Test-Path -LiteralPath $distRoot) {
    Remove-Item -LiteralPath $distRoot -Recurse -Force
}
[System.IO.Directory]::CreateDirectory($distRoot) | Out-Null

Invoke-Godot `
    -Arguments @("--headless", "--path", $webProjectRoot, "--export-release", "Web", $outputIndex) `
    -Operation "Godot Web release export"
if (-not (Test-Path -LiteralPath $outputIndex -PathType Leaf)) {
    throw "Godot reported success but index.html was not created: $outputIndex"
}
if (-not (Test-Path -LiteralPath $outputPack -PathType Leaf)) {
    throw "Godot reported success but index.pck was not created: $outputPack"
}

# GitHub Pages caches the PCK URL for several minutes. A content-addressed
# filename makes every runtime change immediately distinguishable from a stale
# mobile-browser cache while index.html remains the stable Pages entry point.
$packHash = (Get-FileHash -LiteralPath $outputPack -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 16)
$versionedPackName = "index.$packHash.pck"
$versionedPack = Join-Path $distRoot $versionedPackName
$packLength = (Get-Item -LiteralPath $outputPack).Length
$indexHtml = [System.IO.File]::ReadAllText($outputIndex, [System.Text.Encoding]::UTF8)
$configPattern = '(?m)^const GODOT_CONFIG = (?<json>\{[^\r\n]+\});$'
$configMatches = [regex]::Matches($indexHtml, $configPattern)
if ($configMatches.Count -ne 1) {
    throw "Godot index.html must contain exactly one GODOT_CONFIG object."
}
$configMatch = $configMatches[0]
$godotConfig = $configMatch.Groups["json"].Value | ConvertFrom-Json
$originalPackProperty = $godotConfig.fileSizes.PSObject.Properties["index.pck"]
if (
    $godotConfig.executable -ne "index" -or
    $null -eq $originalPackProperty -or
    [long] $originalPackProperty.Value -ne $packLength
) {
    throw "Godot index.html does not contain the expected executable/PCK configuration."
}
$godotConfig | Add-Member -NotePropertyName "mainPack" -NotePropertyValue $versionedPackName -Force
$godotConfig.fileSizes.PSObject.Properties.Remove("index.pck")
$godotConfig.fileSizes | Add-Member -NotePropertyName $versionedPackName -NotePropertyValue $packLength
$versionedConfigLine = "const GODOT_CONFIG = $($godotConfig | ConvertTo-Json -Compress -Depth 10);"
$indexHtml = `
    $indexHtml.Substring(0, $configMatch.Index) + `
    $versionedConfigLine + `
    $indexHtml.Substring($configMatch.Index + $configMatch.Length)
Move-Item -LiteralPath $outputPack -Destination $versionedPack
[System.IO.File]::WriteAllText(
    $outputIndex,
    $indexHtml,
    [System.Text.UTF8Encoding]::new($false)
)
$versionedPackMarker = '"' + $versionedPackName + '":' + $packLength
if (
    -not (Test-Path -LiteralPath $versionedPack -PathType Leaf) -or
    (Test-Path -LiteralPath $outputPack) -or
    -not $indexHtml.Contains('"mainPack":"' + $versionedPackName + '"') -or
    -not $indexHtml.Contains($versionedPackMarker)
) {
    throw "Versioned PCK post-processing validation failed."
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $externalTrack)) | Out-Null
Copy-Item -LiteralPath $embeddedTrack -Destination $externalTrack -Force
if (-not (Test-Path -LiteralPath $externalTrack -PathType Leaf)) {
    throw "External Track copy was not created: $externalTrack"
}

Write-Output "Web Viewer export completed: $outputIndex"
Write-Output "Versioned runtime pack: $versionedPack"
Write-Output "Published Track: $externalTrack"
