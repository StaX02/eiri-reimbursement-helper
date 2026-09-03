param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$IconPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$resolvedExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
$resolvedIconPath = [System.IO.Path]::GetFullPath($IconPath)
if (-not (Test-Path -LiteralPath $resolvedExecutablePath -PathType Leaf)) {
    throw "Executable does not exist: $resolvedExecutablePath"
}
if (-not (Test-Path -LiteralPath $resolvedIconPath -PathType Leaf)) {
    throw "Icon does not exist: $resolvedIconPath"
}

$actualIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($resolvedExecutablePath)
$expectedIcon = [System.Drawing.Icon]::new($resolvedIconPath)
$actualBitmap = $actualIcon.ToBitmap()
$expectedBitmap = [System.Drawing.Bitmap]::new($actualBitmap.Width, $actualBitmap.Height)
$graphics = [System.Drawing.Graphics]::FromImage($expectedBitmap)
try {
    $graphics.DrawIcon(
        $expectedIcon,
        [System.Drawing.Rectangle]::new(0, 0, $expectedBitmap.Width, $expectedBitmap.Height))
    $difference = 0L
    $channelCount = $actualBitmap.Width * $actualBitmap.Height * 4
    for ($y = 0; $y -lt $actualBitmap.Height; $y++) {
        for ($x = 0; $x -lt $actualBitmap.Width; $x++) {
            $actualPixel = $actualBitmap.GetPixel($x, $y)
            $expectedPixel = $expectedBitmap.GetPixel($x, $y)
            $difference += [Math]::Abs($actualPixel.A - $expectedPixel.A)
            $difference += [Math]::Abs($actualPixel.R - $expectedPixel.R)
            $difference += [Math]::Abs($actualPixel.G - $expectedPixel.G)
            $difference += [Math]::Abs($actualPixel.B - $expectedPixel.B)
        }
    }

    $meanChannelDifference = $difference / $channelCount
    if ($meanChannelDifference -gt 30) {
        throw "Published executable does not visually match icon.ico. Mean channel difference: $meanChannelDifference."
    }
}
finally {
    $graphics.Dispose()
    $actualBitmap.Dispose()
    $expectedBitmap.Dispose()
    $actualIcon.Dispose()
    $expectedIcon.Dispose()
}

Write-Output "Application icon verified: $resolvedExecutablePath"
