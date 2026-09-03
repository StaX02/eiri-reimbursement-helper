param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath,

    [string]$UpgradeMsiPath,

    [switch]$Elevated
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "MsiTestHelpers.ps1")
$resolvedMsiPath = [System.IO.Path]::GetFullPath($MsiPath)
if (-not (Test-Path -LiteralPath $resolvedMsiPath -PathType Leaf)) {
    throw "MSI does not exist: $resolvedMsiPath"
}
$resolvedUpgradeMsiPath = $null
if (-not [string]::IsNullOrWhiteSpace($UpgradeMsiPath)) {
    $resolvedUpgradeMsiPath = [System.IO.Path]::GetFullPath($UpgradeMsiPath)
    if (-not (Test-Path -LiteralPath $resolvedUpgradeMsiPath -PathType Leaf)) {
        throw "Upgrade MSI does not exist: $resolvedUpgradeMsiPath"
    }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator -and -not $Elevated) {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-MsiPath", "`"$resolvedMsiPath`"",
        "-Elevated"
    )
    if ($null -ne $resolvedUpgradeMsiPath) {
        $arguments += @("-UpgradeMsiPath", "`"$resolvedUpgradeMsiPath`"")
    }
    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList $arguments `
        -Verb RunAs `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Elevated MSI lifecycle test failed with exit code $($process.ExitCode)."
    }
    Write-Output "MSI install/uninstall lifecycle verified in an elevated process."
    exit 0
}

$testId = [Guid]::NewGuid().ToString("N")
$installDirectory = Join-Path $env:TEMP "eiri-msi-smoke-$testId"
$installLog = Join-Path $env:TEMP "eiri-msi-install-$testId.log"
$uninstallLog = Join-Path $env:TEMP "eiri-msi-uninstall-$testId.log"
$upgradeLog = Join-Path $env:TEMP "eiri-msi-upgrade-$testId.log"
$shortcutPath = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Eiri Reimbursement Helper\Eiri Reimbursement Helper.lnk"
$installed = $false
$installedMsiPath = $resolvedMsiPath

function Invoke-MsiExec {
    param(
        [string[]]$Arguments,
        [string]$Operation
    )

    $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $Arguments -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) {
        throw "$Operation failed with Windows Installer exit code $($process.ExitCode)."
    }
}

try {
    Invoke-MsiExec @(
        "/i",
        "`"$resolvedMsiPath`"",
        "/qn",
        "/norestart",
        "INSTALLFOLDER=`"$installDirectory`"",
        "/l*v",
        "`"$installLog`""
    ) "MSI installation"
    $installed = $true

    $desktopExecutable = Join-Path $installDirectory "Eiri.Reimbursement.Desktop.exe"
    $documentWorker = Join-Path $installDirectory "document-worker\eiri-document-worker.exe"
    if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
        throw "Installed desktop executable is missing: $desktopExecutable"
    }
    if (-not (Test-Path -LiteralPath $documentWorker -PathType Leaf)) {
        throw "Installed document worker is missing: $documentWorker"
    }
    if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
        throw "Installed Start menu shortcut is missing: $shortcutPath"
    }

    Invoke-MsiExec @(
        "/i",
        "`"$resolvedMsiPath`"",
        "/qn",
        "/norestart",
        "REINSTALL=ALL",
        "REINSTALLMODE=vomus"
    ) "MSI maintenance reinstall"

    if ($null -ne $resolvedUpgradeMsiPath) {
        $baseProductCode = Get-MsiPropertyFromPackage $resolvedMsiPath "ProductCode"
        $upgradeProductCode = Get-MsiPropertyFromPackage $resolvedUpgradeMsiPath "ProductCode"
        Invoke-MsiExec @(
            "/i",
            "`"$resolvedUpgradeMsiPath`"",
            "/qn",
            "/norestart",
            "INSTALLFOLDER=`"$installDirectory`"",
            "/l*v",
            "`"$upgradeLog`""
        ) "MSI major upgrade"
        $installedMsiPath = $resolvedUpgradeMsiPath

        $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
        if ($windowsInstaller.ProductState($baseProductCode) -ne -1) {
            throw "The previous product remains registered after the major upgrade."
        }
        if ($windowsInstaller.ProductState($upgradeProductCode) -ne 5) {
            throw "The upgraded product is not registered as locally installed."
        }
    }

    Invoke-MsiExec @(
        "/x",
        "`"$installedMsiPath`"",
        "/qn",
        "/norestart",
        "/l*v",
        "`"$uninstallLog`""
    ) "MSI uninstallation"
    $installed = $false

    if (Test-Path -LiteralPath $installDirectory) {
        throw "Installation directory remains after uninstall: $installDirectory"
    }
    if (Test-Path -LiteralPath $shortcutPath) {
        throw "Start menu shortcut remains after uninstall: $shortcutPath"
    }

    Write-Output "MSI install/uninstall lifecycle verified."
}
finally {
    if ($installed) {
        $process = Start-Process -FilePath "msiexec.exe" -ArgumentList @(
            "/x",
            "`"$installedMsiPath`"",
            "/qn",
            "/norestart"
        ) -Wait -PassThru
        if ($process.ExitCode -notin @(0, 1605, 3010)) {
            Write-Warning "Cleanup uninstall exited with code $($process.ExitCode)."
        }
    }
}
