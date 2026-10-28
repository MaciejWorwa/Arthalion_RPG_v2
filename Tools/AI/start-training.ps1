param(
    [string]$RunId = ('combat-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$Scenarios = '@starter',
    [int]$Environments = 1,
    [int]$Minutes = 60,
    [int]$Steps = 500000,
    [switch]$Resume,
    [switch]$SelfPlay,
    [switch]$Editor,
    [string]$Device = 'cpu'
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'setup.ps1')
$taskPython = Join-Path $PSScriptRoot '.venv/Scripts/python.exe'
$taskArgs = @((Join-Path $PSScriptRoot 'train.py'), '--run-id', $RunId, '--scenarios', $Scenarios,
    '--num-envs', $Environments, '--minutes', $Minutes, '--steps', $Steps, '--device', $Device)
if ($Resume) { $taskArgs += '--resume' }
if ($SelfPlay) { $taskArgs += '--self-play' }
if ($Editor) { $taskArgs += '--editor' }
& $taskPython @taskArgs
exit $LASTEXITCODE
