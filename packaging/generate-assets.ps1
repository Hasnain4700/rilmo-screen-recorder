[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetsPath = Join-Path $PSScriptRoot 'Assets'
[IO.Directory]::CreateDirectory($assetsPath) | Out-Null
$source = [Drawing.Bitmap]::new((Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\brand\rilmo-master.png'))
try {
    foreach ($asset in @(@{Name='StoreLogo';Size=50},@{Name='Square44x44Logo';Size=44},@{Name='Square150x150Logo';Size=150})) {
        foreach ($scale in @(1,2)) {
            $size = [int]$asset.Size * $scale
            $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.DrawImage($source,[Drawing.Rectangle]::new(0,0,$size,$size))
                $suffix = if ($scale -eq 1) { '' } else { '.scale-200' }
                $bitmap.Save((Join-Path $assetsPath ($asset.Name+$suffix+'.png')),[Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    }
} finally { $source.Dispose() }
