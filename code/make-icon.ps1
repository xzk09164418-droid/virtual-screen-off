$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
$sizes = @(16,20,24,32,48,64,128,256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size,$size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform(($size/64.0),($size/64.0))
    $g.Clear([Drawing.Color]::Transparent)
    $frame = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(35,105,210))
    $screen = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(14,35,64))
    $pen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(114,232,255),3.5)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.FillRectangle($frame,4,9,56,39)
    $g.FillRectangle($screen,8,13,48,29)
    $g.FillRectangle($frame,28,47,8,8)
    $g.FillRectangle($frame,19,55,26,4)
    $g.DrawArc($pen,23,19,18,18,-45,270)
    $g.DrawLine($pen,32,17,32,27)
    if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'screen-control.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    $memory.Dispose(); $pen.Dispose(); $frame.Dispose(); $screen.Dispose(); $g.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create((Join-Path $assets 'screen-control.ico'))
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension = if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
} finally { $writer.Dispose(); $stream.Dispose() }
