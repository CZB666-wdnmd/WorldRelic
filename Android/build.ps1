param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe',
    [string]$OutputPath = '',
    [string]$NdkPath = '',
    [string]$SdkPath = '',
    [string]$JdkPath = ''
)
$ErrorActionPreference = 'Stop'
$taskProject = Join-Path $PSScriptRoot 'UnityProject'
if (!$OutputPath) { $OutputPath = Join-Path $PSScriptRoot 'builds/WorldRelic/WorldRelic.apk' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'APK already exists; choose a new -OutputPath.' }
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity editor not found; supply -UnityPath.' }
if (!$NdkPath) { $NdkPath = Join-Path (Split-Path -Parent $UnityPath) 'Data/PlaybackEngines/AndroidPlayer/NDK' }
$taskCompiler = Join-Path $NdkPath 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
if (!(Test-Path -LiteralPath $taskCompiler -PathType Leaf)) { throw 'Android NDK not found; install Unity Android Build Support or supply -NdkPath (r27c).' }
$taskDirectory = Split-Path -Parent $OutputPath
if ((Test-Path -LiteralPath $taskDirectory) -and (Get-ChildItem -LiteralPath $taskDirectory -Force | Select-Object -First 1)) {
    throw 'Output directory is not empty; choose a new -OutputPath directory.'
}
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$taskNativeDir = Join-Path $taskProject 'Assets/Plugins/Android/arm64-v8a'
New-Item -ItemType Directory -Path $taskNativeDir -Force | Out-Null
& $taskCompiler --target=aarch64-linux-android26 -shared -fPIC -O2 -Wall -Wextra -Werror '-Wl,-z,max-page-size=16384' '-Wl,-soname,libWorldRelicXRPreInit.so' (Join-Path $PSScriptRoot 'native/WorldRelicXRPreInit.c') -llog -ldl -o (Join-Path $taskNativeDir 'libWorldRelicXRPreInit.so')
if ($LASTEXITCODE -ne 0) { throw 'WorldRelic native device gate build failed.' }
$taskLog = Join-Path $taskDirectory 'build.log'
$taskPrevious = @{}
$taskOverrides = @{
    WORLDRELIC_ANDROID_OUTPUT = $OutputPath
    WORLDRELIC_ANDROID_NDK = [IO.Path]::GetFullPath($NdkPath)
    WORLDRELIC_ANDROID_SDK = $SdkPath
    WORLDRELIC_ANDROID_JDK = $JdkPath
}
try {
    foreach ($taskKey in $taskOverrides.Keys) {
        $taskPrevious[$taskKey] = [Environment]::GetEnvironmentVariable($taskKey, 'Process')
        [Environment]::SetEnvironmentVariable($taskKey, $taskOverrides[$taskKey], 'Process')
    }
    $taskProcess = Start-Process -FilePath $UnityPath -ArgumentList @('-batchmode','-nographics','-buildTarget','Android','-projectPath',('"'+$taskProject+'"'),'-executeMethod','LocalVrcw.Editor.WorldRelicAndroidBuild.Build','-logFile',('"'+$taskLog+'"')) -WindowStyle Hidden -PassThru
    $taskProcess.WaitForExit()
    if ($taskProcess.ExitCode -ne 0) { throw "Android build failed ($($taskProcess.ExitCode)); see $taskLog" }
    if (!(Test-Path -LiteralPath $OutputPath -PathType Leaf)) { throw "Unity produced no APK; see $taskLog" }
} finally {
    foreach ($taskKey in $taskPrevious.Keys) { [Environment]::SetEnvironmentVariable($taskKey, $taskPrevious[$taskKey], 'Process') }
}
Write-Output $OutputPath
