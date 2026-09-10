param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$assemblyRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\CustomActions\bin\$Configuration\net48"))
Add-Type -Path (Join-Path $assemblyRoot 'WixToolset.Dtf.WindowsInstaller.dll')
Add-Type -Path (Join-Path $assemblyRoot 'CustomActions.dll')
$evidenceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\artifacts\installer-options'))
$null = New-Item -ItemType Directory -Path $evidenceRoot -Force
$fixture = Join-Path $evidenceRoot ([Guid]::NewGuid().ToString('N'))
$data = Join-Path $fixture 'EiriReimbursementHelper'
$install = Join-Path $fixture 'Application'
[Eiri.Reimbursement.Installer.InstallerActions]::ValidateDirectories($install, $data)
foreach ($badPath in @('C:\', 'relative', $install, (Join-Path $install 'EiriReimbursementHelper'))) {
    $rejected = $false
    try { [Eiri.Reimbursement.Installer.InstallerActions]::ValidateDirectories($install, $badPath) }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Unsafe data path accepted: $badPath" }
}
$null = New-Item -ItemType Directory -Path (Join-Path $data 'originals') -Force
Set-Content -LiteralPath (Join-Path $data 'library.db') -Value 'database sentinel'
Set-Content -LiteralPath (Join-Path $data 'originals\invoice.pdf') -Value 'invoice sentinel'
Set-Content -LiteralPath (Join-Path $data 'unrelated.txt') -Value 'keep this file'
Set-Content -LiteralPath (Join-Path $fixture 'outside.txt') -Value 'keep this file too'
[Eiri.Reimbursement.Installer.InstallerActions]::DeleteLibraryData($data)
if (Test-Path -LiteralPath (Join-Path $data 'library.db')) { throw 'Database cleanup failed.' }
if (Test-Path -LiteralPath (Join-Path $data 'originals')) { throw 'Material cleanup failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $data 'unrelated.txt'))) { throw 'Unrelated file was removed.' }
if (-not (Test-Path -LiteralPath (Join-Path $fixture 'outside.txt'))) { throw 'Parent file was removed.' }

$outside = Join-Path $fixture 'OutsideLibrary'
$null = New-Item -ItemType Directory -Path $outside
Set-Content -LiteralPath (Join-Path $outside 'keep.txt') -Value 'junction target sentinel'
Set-Content -LiteralPath (Join-Path $data 'library.db') -Value 'preserve on unsafe cleanup'
$junction = Join-Path $data 'cache'
$null = New-Item -ItemType Junction -Path $junction -Target $outside
try {
    $rejected = $false
    try { [Eiri.Reimbursement.Installer.InstallerActions]::DeleteLibraryData($data) }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Cleanup followed a directory junction.' }
    if (-not (Test-Path -LiteralPath (Join-Path $data 'library.db'))) { throw 'Unsafe cleanup partially deleted the database.' }
    if (-not (Test-Path -LiteralPath (Join-Path $outside 'keep.txt'))) { throw 'Junction target was modified.' }
} finally { [IO.Directory]::Delete($junction) }

# Render the actual controls off-screen; this complements the MSI lifecycle test.
$forms = @{
    install = [Eiri.Reimbursement.Installer.InstallOptionsForm]::new($install, $data, $true, $false)
    upgrade = [Eiri.Reimbursement.Installer.InstallOptionsForm]::new($install, $data, $true, $true)
    uninstall = [Eiri.Reimbursement.Installer.RemoveOptionsForm]::new($data)
}
try {
    if ($forms.uninstall.DeleteData) { throw 'Uninstall must keep data by default.' }
    foreach ($entry in $forms.GetEnumerator()) {
        $form = $entry.Value
        $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $form.Location = [Drawing.Point]::new(-20000, -20000)
        $form.Show()
        $form.PerformLayout()
        $bitmap = [Drawing.Bitmap]::new($form.Width, $form.Height)
        try {
            $form.DrawToBitmap($bitmap, [Drawing.Rectangle]::new(0, 0, $form.Width, $form.Height))
            $bitmap.Save((Join-Path $evidenceRoot ($entry.Key + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    }
} finally { foreach ($form in $forms.Values) { $form.Dispose() } }
Write-Output 'Installer directory validation, cleanup boundaries, safe defaults and control rendering verified.'
