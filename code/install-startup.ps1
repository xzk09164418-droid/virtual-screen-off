$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'release\ScreenHotkey.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing executable: $exe" }
$process = Start-Process -FilePath $exe -ArgumentList '--apply-config' -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw '配置校验或自启设置失败，详见 release/screen-hotkey.log' }
Write-Output '已按 config.yaml 同步登录自启设置。'
