param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "MsiTestHelpers.ps1")

function Assert-Equal {
    param(
        [string]$Actual,
        [string]$Expected,
        [string]$Message
    )

    if ($Actual -cne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$resolvedMsiPath = [System.IO.Path]::GetFullPath($MsiPath)
Assert-True (Test-Path -LiteralPath $resolvedMsiPath -PathType Leaf) "MSI does not exist: $resolvedMsiPath"

$database = Open-MsiDatabase $resolvedMsiPath

Assert-Equal (Get-MsiProperty $database "ProductName") "Eiri Reimbursement Helper" "ProductName mismatch."
Assert-Equal (Get-MsiProperty $database "ProductVersion") $ExpectedVersion "ProductVersion mismatch."
Assert-Equal (Get-MsiProperty $database "Manufacturer") "StaX02" "Manufacturer mismatch."
Assert-True (-not [string]::IsNullOrWhiteSpace((Get-MsiProperty $database "UpgradeCode"))) "UpgradeCode is missing."
Assert-Equal (Get-MsiProperty $database "ARPPRODUCTICON") "ProductIcon" "Add/Remove Programs icon is not configured."

$icons = @(Get-MsiQueryRows $database "SELECT ``Name`` FROM ``Icon``" 1)
Assert-True ($icons.Count -gt 0 -and $icons[0].Values[0] -eq "ProductIcon") "ProductIcon is not embedded in the MSI."

$shortcuts = @(Get-MsiQueryRows $database "SELECT ``Name``, ``Target``, ``Icon_`` FROM ``Shortcut``" 3)
$applicationShortcut = $shortcuts | Where-Object {
    $_.Values[0] -like "*Eiri Reimbursement Helper*" -and
    $_.Values[1] -eq "[INSTALLFOLDER]Eiri.Reimbursement.Desktop.exe" -and
    $_.Values[2] -eq "ProductIcon"
}
Assert-True ($null -ne $applicationShortcut) "The icon-backed Start menu shortcut is missing."

$fileNames = @(Get-MsiQueryRows $database "SELECT ``FileName`` FROM ``File``" 1) |
    ForEach-Object { $_.Values[0].Split('|')[-1] }
Assert-True ($fileNames -contains "Eiri.Reimbursement.Desktop.exe") "The desktop executable is missing."
Assert-True ($fileNames -contains "eiri-document-worker.exe") "The bundled document worker is missing."

$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$expectedPayload = Get-ChildItem -LiteralPath $resolvedPublishDirectory -File -Recurse |
    Where-Object { $_.Extension -cne ".pdb" } |
    ForEach-Object { "$($_.Name)|$($_.Length)" } |
    Sort-Object
$actualPayload = @(Get-MsiQueryRows $database "SELECT ``FileName``, ``FileSize`` FROM ``File``" 2) |
    ForEach-Object { "$($_.Values[0].Split('|')[-1])|$($_.Values[1])" } |
    Sort-Object
$payloadDifference = @(Compare-Object $expectedPayload $actualPayload)
Assert-True ($payloadDifference.Count -eq 0) "The MSI payload does not exactly match the published file set."

$removeFolders = @(Get-MsiQueryRows $database "SELECT ``FileName``, ``InstallMode`` FROM ``RemoveFile``" 2)
$startMenuCleanup = @($removeFolders | Where-Object {
    [string]::IsNullOrEmpty($_.Values[0]) -and $_.Values[1] -eq "2"
})
Assert-True ($startMenuCleanup.Count -gt 0) "Start menu cleanup on uninstall is missing."

$media = @(Get-MsiQueryRows $database "SELECT ``Cabinet`` FROM ``Media``" 1)
Assert-True ($media.Count -gt 0 -and $media[0].Values[0].StartsWith('#')) "The MSI does not embed its payload cabinet."

$upgradeRows = @(Get-MsiQueryRows $database "SELECT ``UpgradeCode``, ``VersionMax``, ``ActionProperty`` FROM ``Upgrade``" 3)
$majorUpgrade = @($upgradeRows | Where-Object {
    $_.Values[0] -eq (Get-MsiProperty $database "UpgradeCode") -and
    $_.Values[1] -eq $ExpectedVersion -and
    $_.Values[2] -eq "WIX_UPGRADE_DETECTED"
})
Assert-True ($majorUpgrade.Count -gt 0) "Major upgrade detection for earlier versions is missing."

Write-Output "MSI contract verified: $resolvedMsiPath"
