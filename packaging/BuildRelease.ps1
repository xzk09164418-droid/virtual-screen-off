$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
# Windows PowerShell 5.1 needs a BOM to recognize non-ASCII UTF-8 scripts.
foreach ($script in Get-ChildItem code -Filter '*.ps1') {
    $bytes = [System.IO.File]::ReadAllBytes($script.FullName)
    if (@($bytes | Where-Object { $_ -gt 127 }).Count -gt 0 -and
        -not ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)) {
        throw "Save $($script.Name) as UTF-8 with BOM for Windows PowerShell 5.1"
    }
}
$version = (Get-Content (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Invalid version' }
$name = 'virtual-screen-off'
$dist = Join-Path (Get-Location) 'dist'
$packageName = "$name-$version-windows-x64"
$package = Join-Path $dist $packageName
if (Test-Path -LiteralPath $package) { throw 'Package directory already exists; use a clean checkout' }
New-Item -ItemType Directory -Path $package -Force | Out-Null
$built = Join-Path $dist 'compiled'
& ./code/build.ps1 -OutputDirectory $built
$packageRelease = Join-Path $package 'release'
New-Item -ItemType Directory -Path $packageRelease -Force | Out-Null
foreach ($exe in @('VirtualScreenTest.exe','ScreenHotkey.exe','ScreenStatus.exe','IRPresence.exe','ToFStream.exe')) {
    Copy-Item -LiteralPath (Join-Path $built $exe) -Destination $packageRelease
}
Copy-Item -LiteralPath config.example.yaml -Destination (Join-Path $package 'config.yaml')
Copy-Item -LiteralPath config.example.yaml -Destination $package
$packageCode = Join-Path $package 'code'
New-Item -ItemType Directory -Path $packageCode -Force | Out-Null
Copy-Item -LiteralPath code/configure-display.ps1,code/DisplayNative.cs -Destination $packageCode
# Validate config using a temporary copy outside the package: the program writes a log.
$check = Join-Path $dist 'validation'
New-Item -ItemType Directory -Path (Join-Path $check 'release') -Force | Out-Null
Copy-Item -LiteralPath config.example.yaml -Destination (Join-Path $check 'config.yaml')
Copy-Item -LiteralPath (Join-Path $built 'ScreenHotkey.exe') -Destination (Join-Path $check 'release')
$process = Start-Process -FilePath (Join-Path $check 'release/ScreenHotkey.exe') -ArgumentList '--check-config' -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw 'Config validation failed' }
foreach ($file in @('README.md','LICENSE','ACKNOWLEDGEMENTS.md','PRIVACY.md','THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath $file -Destination $package
}
Copy-Item -LiteralPath packaging/QUICKSTART.md -Destination $package
$commit = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit' }
"Version: $version`nCommit: $commit`nPlatform: Windows x64`nUnsigned build from project source." | Set-Content -LiteralPath (Join-Path $package 'BUILD-INFO.txt') -Encoding UTF8
$forbidden = Get-ChildItem $package -Recurse -File | Where-Object {
    $_.Extension -in @('.log','.bak','.pdb') -or $_.Name -in @('test-target.txt','test-adapter.txt','curve-settings.json','curve-recovery.json')
}
if ($forbidden) { throw 'Unexpected private or generated files in package' }
$zip = Join-Path $dist ($packageName + '.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip
$digest = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$digest  $packageName.zip" | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Created $packageName.zip"
