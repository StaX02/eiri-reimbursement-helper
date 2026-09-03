param(
    [string]$Version,
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$desktopProject = Join-Path $repositoryRoot "src\Eiri.Reimbursement.Desktop\Eiri.Reimbursement.Desktop.csproj"
$versionNode = Select-Xml -Path $desktopProject -XPath "/Project/PropertyGroup/Version" |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $versionNode.Node.InnerText
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version is missing. Add <Version> to the desktop project or pass -Version."
}
$publishDirectory = Join-Path $repositoryRoot "artifacts\release\v$Version\$RuntimeIdentifier"
$installerProject = Join-Path $PSScriptRoot "Eiri.Reimbursement.Installer.wixproj"
$msiPath = Join-Path $repositoryRoot "artifacts\release\Eiri-Reimbursement-Helper-v$Version-$RuntimeIdentifier.msi"

if (-not $SkipPublish) {
    & dotnet publish $desktopProject `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        -p:NuGetAudit=false `
        -p:Version=$Version `
        -p:PublishDir="$publishDirectory\"
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop publish failed with exit code $LASTEXITCODE."
    }
}

$desktopExecutable = Join-Path $publishDirectory "Eiri.Reimbursement.Desktop.exe"
$documentWorker = Join-Path $publishDirectory "document-worker\eiri-document-worker.exe"
if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
    throw "Published desktop executable is missing: $desktopExecutable"
}
if (-not (Test-Path -LiteralPath $documentWorker -PathType Leaf)) {
    throw "Bundled document worker is missing: $documentWorker"
}

& dotnet build $installerProject `
    --configuration Release `
    -p:NuGetAudit=false `
    -p:ProductVersion=$Version `
    -p:PublishDirectory="$publishDirectory"
if ($LASTEXITCODE -ne 0) {
    throw "MSI build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw "MSI build completed without the expected artifact: $msiPath"
}

& (Join-Path $PSScriptRoot "tests\Verify-ApplicationIcon.ps1") `
    -ExecutablePath $desktopExecutable `
    -IconPath (Join-Path $repositoryRoot "icon.ico")
& (Join-Path $PSScriptRoot "tests\Verify-Msi.ps1") `
    -MsiPath $msiPath `
    -ExpectedVersion $Version `
    -PublishDirectory $publishDirectory

Write-Output $msiPath
