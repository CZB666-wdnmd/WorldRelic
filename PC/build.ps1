param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe',
    [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
$taskProject = Join-Path $PSScriptRoot 'UnityProject'
if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'builds/WorldRelic/WorldRelic.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Player already exists; choose a new -OutputPath.' }
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity editor not found; supply -UnityPath.' }
$taskDirectory = Split-Path -Parent $OutputPath
if ((Test-Path -LiteralPath $taskDirectory) -and (Get-ChildItem -LiteralPath $taskDirectory -Force | Select-Object -First 1)) {
    throw 'Output directory is not empty; choose a new -OutputPath directory.'
}
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskLog = Join-Path $taskDirectory 'build.log'
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList @('-batchmode','-nographics','-quit','-buildTarget','Win64','-projectPath',('"'+$taskProject+'"'),'-executeMethod','LocalVrcw.Editor.WorldRelicBuild.Build','--build-output',('"'+$OutputPath+'"'),'-logFile',('"'+$taskLog+'"')) -WindowStyle Hidden -PassThru
$taskProcess.WaitForExit()
if ($taskProcess.ExitCode -ne 0) { throw "Windows build failed ($($taskProcess.ExitCode)); see $taskLog" }
if (!(Test-Path -LiteralPath $OutputPath -PathType Leaf)) { throw "Unity produced no player; see $taskLog" }
Write-Output $OutputPath
