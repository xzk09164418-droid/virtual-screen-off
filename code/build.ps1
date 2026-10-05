param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'release'))
$ErrorActionPreference = 'Stop'
$release = $OutputDirectory
New-Item -ItemType Directory -Path $release -Force | Out-Null
$sources = @('DisplayNative.cs','WindowStates.cs','SessionRunner.cs','RecoveryGuard.cs','ScreenTestApp.cs','ScreenSchedule.cs','ScreenConfig.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /main:ScreenTestApp ("/win32icon:" + (Join-Path $PSScriptRoot '..\assets\screen-control.ico')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ("/out:" + (Join-Path $release 'VirtualScreenTest.exe')) $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败：$LASTEXITCODE" }
& $compiler /nologo /target:winexe /platform:x64 /main:ScreenHotkey ("/win32icon:" + (Join-Path $PSScriptRoot '..\assets\screen-control.ico')) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ("/out:" + (Join-Path $release 'ScreenHotkey.exe')) (Join-Path $PSScriptRoot 'ScreenHotkey.cs') (Join-Path $PSScriptRoot 'NightScreenGuard.cs') (Join-Path $PSScriptRoot 'PresencePolicy.cs') (Join-Path $PSScriptRoot 'IrPresenceMonitor.cs') (Join-Path $PSScriptRoot 'SensorCoordinator.cs') (Join-Path $PSScriptRoot 'ToFControlPolicy.cs') (Join-Path $PSScriptRoot 'BatteryDisplayPower.cs') (Join-Path $PSScriptRoot 'DisplayNative.cs') (Join-Path $PSScriptRoot 'ScreenConfig.cs')
if ($LASTEXITCODE -ne 0) { throw "热键程序编译失败：$LASTEXITCODE" }
& $compiler /nologo /target:exe /platform:x64 /main:ScreenStatus /reference:System.Web.Extensions.dll ("/out:" + (Join-Path $release 'ScreenStatus.exe')) (Join-Path $PSScriptRoot 'DisplayNative.cs') (Join-Path $PSScriptRoot 'ScreenStatus.cs')
if ($LASTEXITCODE -ne 0) { throw "状态接口编译失败：$LASTEXITCODE" }

$refs=@('Windows.Foundation','Windows.Media','Windows.Devices','Windows.Graphics','Windows.Storage') | ForEach-Object { '/r:C:\Windows\System32\WinMetadata\'+$_+'.winmd' }
$refs+=@('System.Runtime','System.Runtime.InteropServices.WindowsRuntime','System.Threading.Tasks','System.Collections') | ForEach-Object { Get-ChildItem ("C:\Windows\Microsoft.NET\assembly\GAC_MSIL\"+$_) -Recurse -Filter '*.dll' | ForEach-Object { '/r:'+ $_.FullName } }
& $compiler /nologo /target:exe /platform:x64 /r:System.Web.Extensions.dll $refs ("/out:"+ (Join-Path $release 'IRPresence.exe')) (Join-Path $PSScriptRoot 'IRPresence.cs') (Join-Path $PSScriptRoot 'ToFGate.cs') (Join-Path $PSScriptRoot 'ToFConfirmation.cs')
if($LASTEXITCODE -ne 0){throw 'IR helper compilation failed'}


& $compiler /nologo /target:exe /platform:x64 /r:System.Web.Extensions.dll $refs ("/out:"+ (Join-Path $release 'ToFStream.exe')) (Join-Path $PSScriptRoot 'ToFStream.cs')
if($LASTEXITCODE -ne 0){throw 'ToF stream compilation failed'}
