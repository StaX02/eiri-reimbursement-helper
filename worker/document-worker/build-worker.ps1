[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $PythonExecutable = "python"
)

$ErrorActionPreference = "Stop"

$workerRoot = $PSScriptRoot
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildRoot = Join-Path $workerRoot "obj"
$virtualEnvironment = Join-Path $buildRoot "bundle-venv"
$virtualEnvironmentPython = Join-Path $virtualEnvironment "Scripts\python.exe"
$distRoot = Join-Path $buildRoot "dist"
$workRoot = Join-Path $buildRoot "pyinstaller"
$specRoot = Join-Path $buildRoot "spec"
$workerEntryPoint = Join-Path $workerRoot "src\eiri_document_worker\__main__.py"

if (-not (Test-Path -LiteralPath $virtualEnvironmentPython -PathType Leaf)) {
    & $PythonExecutable -m venv $virtualEnvironment
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create the document worker build environment."
    }
}

& $virtualEnvironmentPython -m pip install --disable-pip-version-check --no-cache-dir "$workerRoot[bundle]"
if ($LASTEXITCODE -ne 0) {
    throw "Unable to install the document worker build dependencies."
}

& $virtualEnvironmentPython -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --name eiri-document-worker `
    --paths (Join-Path $workerRoot "src") `
    --collect-all rapidocr `
    --collect-all onnxruntime `
    --collect-binaries pypdfium2 `
    --distpath $distRoot `
    --workpath $workRoot `
    --specpath $specRoot `
    $workerEntryPoint
if ($LASTEXITCODE -ne 0) {
    throw "Unable to bundle the document worker."
}

$builtWorker = Join-Path $distRoot "eiri-document-worker"
if (-not (Test-Path -LiteralPath (Join-Path $builtWorker "eiri-document-worker.exe") -PathType Leaf)) {
    throw "The bundled document worker executable was not produced."
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Copy-Item -Path (Join-Path $builtWorker "*") -Destination $outputRoot -Recurse -Force
