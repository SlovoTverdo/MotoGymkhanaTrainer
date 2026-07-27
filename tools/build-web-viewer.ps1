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
$outputIndex = Join-Path $distRoot "index.html"
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
    $quotedArguments = foreach ($argument in $Arguments) {
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
    throw "Track v4 source was not found: $sourceTrack"
}
if (@(Get-ChildItem -LiteralPath $webProjectRoot -Recurse -Filter "*.cs" -File).Count -ne 0) {
    throw "Web Viewer must not contain C# scripts."
}

$track = Get-Content -LiteralPath $sourceTrack -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $track -or $track.formatVersion -ne 4) {
    throw "Track source must be an Exported Track with formatVersion 4: $sourceTrack"
}

Copy-Item -LiteralPath $sourceTrack -Destination $embeddedTrack -Force

Invoke-Godot `
    -Arguments @("--headless", "--editor", "--path", $webProjectRoot, "--quit") `
    -Operation "Godot import"

Invoke-Godot `
    -Arguments @("--headless", "--path", $webProjectRoot, "--script", "res://tests/track_v4_parser_test.gd") `
    -Operation "Track v4 parser tests"

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

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $externalTrack)) | Out-Null
Copy-Item -LiteralPath $embeddedTrack -Destination $externalTrack -Force
if (-not (Test-Path -LiteralPath $externalTrack -PathType Leaf)) {
    throw "External Track copy was not created: $externalTrack"
}

Write-Output "Web Viewer export completed: $outputIndex"
Write-Output "Published Track: $externalTrack"
