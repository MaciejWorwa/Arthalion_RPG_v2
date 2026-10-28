param([switch]$Reinstall)
$ErrorActionPreference = 'Stop'
$taskAiRoot = $PSScriptRoot
$taskPython = Join-Path $taskAiRoot '.venv/Scripts/python.exe'
if (!(Test-Path -LiteralPath $taskPython)) {
    $taskUv = Join-Path $taskAiRoot '.bootstrap/bin/uv.exe'
    if (!(Test-Path -LiteralPath $taskUv)) {
        & python -m pip install --target (Join-Path $taskAiRoot '.bootstrap') uv
        if ($LASTEXITCODE -ne 0) { throw 'Could not install the local Python bootstrap.' }
    }
    # ML-Agents 1.1.0 requires Python 3.10.1 through 3.10.12.
    & $taskUv python install 3.10.12
    # Some uv releases report a minor-version-link error after successfully
    # extracting this historical Windows runtime. Validate the executable itself.
    $taskManagedRoot = if ($env:UV_PYTHON_INSTALL_DIR) { $env:UV_PYTHON_INSTALL_DIR } else { Join-Path $env:APPDATA 'uv/python' }
    $taskManagedPython = Join-Path $taskManagedRoot 'cpython-3.10.12-windows-x86_64-none/python.exe'
    if (!(Test-Path -LiteralPath $taskManagedPython)) { throw 'Python 3.10.12 was not installed. Install it and create Tools/AI/.venv manually.' }
    & $taskManagedPython -m venv (Join-Path $taskAiRoot '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the training environment.' }
    $Reinstall = $true
}
if ($Reinstall) {
    $taskRequirements = Join-Path $taskAiRoot 'requirements.lock.txt'
    if (!(Test-Path -LiteralPath $taskRequirements)) { $taskRequirements = Join-Path $taskAiRoot 'requirements.txt' }
    & $taskPython -m pip install -r $taskRequirements
    if ($LASTEXITCODE -ne 0) { throw 'Training dependency installation failed.' }
}
& $taskPython -c 'import torch, mlagents, onnxruntime; print("Python/trainer ready; torch=" + torch.__version__ + "; CUDA=" + str(torch.cuda.is_available()))'
if ($LASTEXITCODE -ne 0) { throw 'Run setup.ps1 -Reinstall to repair the training environment.' }
