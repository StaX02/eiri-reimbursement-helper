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
Start-Transcript -Path (Join-Path $PSScriptRoot '..\..\artifacts\msi-lifecycle.log') -Append
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$upgradeCode = Get-MsiPropertyFromPackage $resolvedMsiPath "UpgradeCode"
if (@($windowsInstaller.RelatedProducts($upgradeCode)).Count -gt 0) {
    throw "An Eiri installation already exists. Run lifecycle tests in a clean Windows environment."
}
$dataRegistry = 'HKCU:\Software\StaX02\Eiri Reimbursement Helper'
$previousData = (Get-ItemProperty -LiteralPath $dataRegistry -Name DataDirectory -ErrorAction SilentlyContinue).DataDirectory
$installDirectory = Join-Path $env:TEMP "eiri-msi-smoke-$testId"
$testDataParent = Join-Path $env:TEMP "eiri-msi-data-$testId"
$dataDirectory = Join-Path $testDataParent 'EiriReimbursementHelper'
$desktopShortcutPath = Join-Path $env:PUBLIC 'Desktop\Eiri Reimbursement Helper.lnk'
$installLog = Join-Path $env:TEMP "eiri-msi-install-$testId.log"
$uninstallLog = Join-Path $env:TEMP "eiri-msi-uninstall-$testId.log"
$upgradeLog = Join-Path $env:TEMP "eiri-msi-upgrade-$testId.log"
$shortcutPath = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Eiri Reimbursement Helper\Eiri Reimbursement Helper.lnk"
$installed = $false
$installedMsiPath = $resolvedMsiPath
if ((Test-Path -LiteralPath $shortcutPath) -or (Test-Path -LiteralPath $desktopShortcutPath)) {
    throw 'An existing Eiri shortcut would be overwritten. Use a clean Windows test environment.'
}

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
        "DATADIRECTORY=`"$dataDirectory`"",
        "ADDDESKTOPSHORTCUT=1",
        "/l*v",
        "`"$installLog`""
    ) "MSI installation"
    $installed = $true

    if ((Get-ItemProperty -LiteralPath $dataRegistry).DataDirectory.TrimEnd('\') -ne $dataDirectory) {
        throw 'The selected data directory was not persisted.'
    }
    if (-not (Test-Path -LiteralPath $desktopShortcutPath)) { throw 'Desktop shortcut is missing.' }
    $null = New-Item -ItemType Directory -Path (Join-Path $dataDirectory 'originals') -Force
    Set-Content -LiteralPath (Join-Path $dataDirectory 'library.db') -Value 'lifecycle database sentinel'
    Set-Content -LiteralPath (Join-Path $dataDirectory 'originals\invoice.pdf') -Value 'lifecycle invoice sentinel'
    Set-Content -LiteralPath (Join-Path $testDataParent 'keep.txt') -Value 'outside library'

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
        if (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'originals\invoice.pdf'))) { throw 'Upgrade removed user data.' }
        if (-not (Test-Path -LiteralPath $desktopExecutable)) { throw 'Upgrade changed the installation directory.' }
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
    if (Test-Path -LiteralPath $desktopShortcutPath) { throw 'Desktop shortcut remains after uninstall.' }
    if (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'originals\invoice.pdf'))) { throw 'Default uninstall deleted user data.' }

    $newDataDirectory = Join-Path $testDataParent 'Relocated\EiriReimbursementHelper'
    Invoke-MsiExec @('/i', "`"$installedMsiPath`"", '/qn', '/norestart', "INSTALLFOLDER=`"$installDirectory`"", "DATADIRECTORY=`"$newDataDirectory`"", 'ADDDESKTOPSHORTCUT=0') 'Reinstall with a newly selected data location'
    $installed = $true
    if ((Get-ItemProperty -LiteralPath $dataRegistry).DataDirectory.TrimEnd('\') -ne $newDataDirectory) { throw 'Reinstall ignored the new data location.' }
    $null = New-Item -ItemType Directory -Path (Join-Path $newDataDirectory 'originals') -Force
    Copy-Item -LiteralPath (Join-Path $dataDirectory 'library.db') -Destination $newDataDirectory
    Copy-Item -LiteralPath (Join-Path $dataDirectory 'originals\invoice.pdf') -Destination (Join-Path $newDataDirectory 'originals')
    if (Test-Path -LiteralPath $desktopShortcutPath) { throw 'Desktop shortcut opt-out was ignored.' }
    Invoke-MsiExec @('/x', "`"$installedMsiPath`"", '/qn', '/norestart', 'DELETEAPPDATA=1', '/l*v', "`"$uninstallLog.delete.log`"") 'Uninstall and delete data'
    $installed = $false
    if (Test-Path -LiteralPath $newDataDirectory) { throw 'Explicit cleanup left application data behind.' }
    if (-not (Test-Path -LiteralPath (Join-Path $dataDirectory 'library.db'))) { throw 'Cleanup removed a previously retained library.' }
    if (-not (Test-Path -LiteralPath (Join-Path $testDataParent 'keep.txt'))) { throw 'Cleanup removed a file outside the library.' }

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
    if ($null -ne $previousData) {
        $null = New-Item -Path $dataRegistry -Force
        Set-ItemProperty -LiteralPath $dataRegistry -Name DataDirectory -Value $previousData
    }
    else {
        Remove-ItemProperty -LiteralPath $dataRegistry -Name DataDirectory -ErrorAction SilentlyContinue
    }
}
