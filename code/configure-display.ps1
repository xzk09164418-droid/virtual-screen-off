param([int]$Index = -1)
$ErrorActionPreference = 'Stop'
# Compile the native declarations in memory; querying does not change topology.
Add-Type -Path (Join-Path $PSScriptRoot 'DisplayNative.cs')
$config = [DisplayNative]::Query($false)
$paths = @($config.Paths)
for ($i = 0; $i -lt $paths.Count; $i++) {
    $name = [DisplayNative]::Name($paths[$i])
    [PSCustomObject]@{ Index = $i; Name = $name.Name; Internal = [DisplayNative]::Internal($paths[$i]) }
}
if ($Index -eq -1) { return }
if ($Index -lt 0 -or $Index -ge $paths.Count) { throw 'Index is outside the current display list.' }
$selected = $paths[$Index]
if ([DisplayNative]::Internal($selected)) { throw 'The internal display cannot be selected as the virtual display.' }
$release = Join-Path (Split-Path $PSScriptRoot -Parent) 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $release 'test-target.txt'), [DisplayNative]::Name($selected).DevicePath, $utf8)
[IO.File]::WriteAllText((Join-Path $release 'test-adapter.txt'), [DisplayNative]::AdapterPath($selected), $utf8)
Write-Output 'Saved local display identity. No display state was changed.'
