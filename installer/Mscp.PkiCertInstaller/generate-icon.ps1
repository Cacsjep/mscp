# Builds a multi-resolution app.ico for the PKI Cert Installer.
# Renders a Milestone-blue shield with a white "PKI" wordmark at 16,
# 32, 48, 64, 128, 256 px; embeds each as PNG inside a single .ico
# container so Windows uses the right size everywhere it shows up
# (taskbar, alt-tab, file explorer, jump list, large icon view).
#
#   pwsh installer/Mscp.PkiCertInstaller/generate-icon.ps1
#
# Re-run any time the design changes; the build picks the file up
# automatically through <ApplicationIcon> in the csproj.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here 'Assets\app.ico'

function New-PkiIconPng([int]$size) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # Shield silhouette - flat top, rounded shoulders, point at bottom.
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $w = [float]$size; $h = [float]$size
    $padX = $w * 0.08; $padY = $h * 0.06
    $left = $padX; $right = $w - $padX
    $top  = $padY; $shoulder = $padY + ($h * 0.18)
    $bottom = $h - $padY * 1.2

    $path.StartFigure()
    $path.AddArc([float]$left, [float]$top,
                 [float]($w * 0.18), [float]($h * 0.30), 180, 90)
    $path.AddLine([float]($left + $w * 0.09), [float]$top, [float]($right - $w * 0.09), [float]$top)
    $path.AddArc([float]($right - $w * 0.18), [float]$top,
                 [float]($w * 0.18), [float]($h * 0.30), 270, 90)
    $path.AddBezier(
        [float]$right, [float]$shoulder,
        [float]$right, [float]($shoulder + $h * 0.30),
        [float]($right - $w * 0.10), [float]($bottom - $h * 0.10),
        [float]($w * 0.5), [float]$bottom)
    $path.AddBezier(
        [float]($w * 0.5), [float]$bottom,
        [float]($left + $w * 0.10), [float]($bottom - $h * 0.10),
        [float]$left, [float]($shoulder + $h * 0.30),
        [float]$left, [float]$shoulder)
    $path.CloseFigure()

    $brushFill   = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0, 153, 218))
    $brushStroke = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 0, 116, 165), [float]([Math]::Max(1, $size * 0.02)))
    $g.FillPath($brushFill, $path)
    $g.DrawPath($brushStroke, $path)

    # White "PKI" wordmark, sized to fit the shield body. Skip on tiny
    # 16/24px renders where the letters would just be soup.
    if ($size -ge 32) {
        $fontSize = [Math]::Max(8, [int]($size * 0.32))
        $font = [System.Drawing.Font]::new('Segoe UI', [float]$fontSize, [System.Drawing.FontStyle]::Bold,
                    [System.Drawing.GraphicsUnit]::Pixel)
        $sf = [System.Drawing.StringFormat]::new()
        $sf.Alignment     = [System.Drawing.StringAlignment]::Center
        $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
        # Vertically nudge text up slightly so it sits in the body, not the point.
        $textRect = [System.Drawing.RectangleF]::new(0, -$h * 0.04, $w, $h)
        $g.DrawString('PKI', $font, [System.Drawing.Brushes]::White, $textRect, $sf)
        $font.Dispose()
        $sf.Dispose()
    }

    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $g.Dispose(); $bmp.Dispose(); $brushFill.Dispose(); $brushStroke.Dispose(); $path.Dispose()
    return ,$bytes
}

# Standard ICO sizes Windows actually uses across shell surfaces.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs  = @{}
foreach ($s in $sizes) { $pngs[$s] = New-PkiIconPng $s }

# Pack as ICO: 6-byte ICONDIR + 16-byte entry per image + concatenated PNGs.
$ico = [System.IO.MemoryStream]::new()
$bw  = [System.IO.BinaryWriter]::new($ico)
$bw.Write([UInt16]0)            # Reserved
$bw.Write([UInt16]1)            # Type 1 = icon
$bw.Write([UInt16]$sizes.Count) # Count

$dataOffset = 6 + (16 * $sizes.Count)
foreach ($s in $sizes) {
    $bytes = $pngs[$s]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = $w
    $bw.Write($w)               # Width  (0 means 256)
    $bw.Write($h)               # Height (0 means 256)
    $bw.Write([byte]0)          # Color count (0 if no palette)
    $bw.Write([byte]0)          # Reserved
    $bw.Write([UInt16]1)        # Planes
    $bw.Write([UInt16]32)       # Bit count
    $bw.Write([UInt32]$bytes.Length)  # Bytes in resource
    $bw.Write([UInt32]$dataOffset)    # Offset
    $dataOffset += $bytes.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }

[System.IO.File]::WriteAllBytes($out, $ico.ToArray())
Write-Host "Wrote $out ($($ico.Length) bytes, $($sizes.Count) sizes)" -ForegroundColor Green
