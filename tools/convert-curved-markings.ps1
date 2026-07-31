param(
    [Parameter(Mandatory = $true)]
    [string[]] $Path
)

$ErrorActionPreference = 'Stop'

function Convert-Marking {
    param([Parameter(Mandatory = $true)] $Marking)

    if ($null -ne $Marking.path) {
        return
    }
    $points = @($Marking.points)
    if ($points.Count -lt 2) {
        throw "Marking '$($Marking.id)' must contain at least two legacy points."
    }

    $segments = for ($index = 1; $index -lt $points.Count; $index++) {
        [ordered]@{
            type = 'line'
            end = $points[$index]
        }
    }
    $pathValue = [ordered]@{
        start = $points[0]
        segments = @($segments)
    }
    $Marking.PSObject.Properties.Remove('type')
    $Marking.PSObject.Properties.Remove('points')
    $Marking | Add-Member -NotePropertyName path -NotePropertyValue $pathValue
}

foreach ($candidate in $Path) {
    $resolved = Resolve-Path -LiteralPath $candidate
    foreach ($file in Get-Item -LiteralPath $resolved) {
        $document = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $targetVersion = $null
        if ($null -ne $document.exercise -and $document.formatVersion -in 1, 2, 3) {
            $targetVersion = 3
            foreach ($marking in @($document.markings)) {
                if ($null -eq $marking) { continue }
                if ($null -eq $marking.style) {
                    $marking | Add-Member -NotePropertyName style -NotePropertyValue 'solid'
                }
                if ($null -eq $marking.visibleInViewer) {
                    $marking | Add-Member -NotePropertyName visibleInViewer -NotePropertyValue $true
                }
                $legacyColors = @{ white = '#FFFFFF'; yellow = '#FFFF00'; blue = '#0000FF' }
                if ($legacyColors.ContainsKey([string]$marking.color)) {
                    $marking.color = $legacyColors[[string]$marking.color]
                }
            }
        }
        elseif ($null -ne $document.venueObjects -and $document.formatVersion -in 4, 5) {
            $targetVersion = 5
        }
        elseif ($null -ne $document.venue -and $null -ne $document.objects -and
                $null -eq $document.track -and $document.formatVersion -in 1, 2) {
            $targetVersion = 2
        }
        else {
            throw "Unsupported JSON contract in '$($file.FullName)'."
        }

        foreach ($marking in @($document.markings)) {
            if ($null -eq $marking) { continue }
            Convert-Marking -Marking $marking
            $canonicalColors = @{
                red = '#F21F14'; blue = '#1452FF'; yellow = '#FFD10D';
                green = '#1AD938'; orange = '#FF6B14'; white = '#FFFFFF'
            }
            if ($canonicalColors.ContainsKey([string]$marking.color)) {
                $marking.color = $canonicalColors[[string]$marking.color]
            }
            elseif ([string]$marking.color -match '^#[0-9a-fA-F]{6}$') {
                $marking.color = ([string]$marking.color).ToUpperInvariant()
            }
        }
        $document.formatVersion = $targetVersion
        $json = $document | ConvertTo-Json -Depth 100
        [System.IO.File]::WriteAllText($file.FullName, $json + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))
        Write-Output "CONVERTED v$targetVersion $($file.FullName)"
    }
}
