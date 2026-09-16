# يبني icon.ico لملف البرنامج - بطاقة داكنة متدرّجة بسهمين، نفس هوية الودجت بالضبط.
# مقاسات صغيرة (16/32) بتصميم مبسّط واضح، والكبيرة (48/256) بتفاصيل كاملة (تدرّج + ظل + لمعان).
# سكربت بناء لمرة واحدة (أو كلما تغيّرت الألوان الافتراضية) - مش جزء من البرنامج نفسه.
Add-Type -AssemblyName System.Drawing

function New-RoundRect([System.Drawing.RectangleF]$r, [double]$rad) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right-$d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right-$d, $r.Bottom-$d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom-$d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Arrow($gfx, $col, [double]$x, [double]$y, [double]$w, [double]$h, [bool]$up, [bool]$roundStem) {
    $b = New-Object System.Drawing.SolidBrush $col
    $headH = $h * 0.52
    $stemW = $w * 0.34
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($up) {
        $pts = @(
            (New-Object System.Drawing.PointF ($x+$w/2), $y),
            (New-Object System.Drawing.PointF ($x+$w), ($y+$headH)),
            (New-Object System.Drawing.PointF $x, ($y+$headH))
        )
        $p.AddPolygon($pts)
        $gfx.FillPath($b, $p)
        $stemRect = New-Object System.Drawing.RectangleF ($x+($w-$stemW)/2), ($y+$headH-1), $stemW, ($h-$headH+1)
    } else {
        $pts = @(
            (New-Object System.Drawing.PointF ($x+$w/2), ($y+$h)),
            (New-Object System.Drawing.PointF ($x+$w), ($y+$h-$headH)),
            (New-Object System.Drawing.PointF $x, ($y+$h-$headH))
        )
        $p.AddPolygon($pts)
        $gfx.FillPath($b, $p)
        $stemRect = New-Object System.Drawing.RectangleF ($x+($w-$stemW)/2), $y, $stemW, ($h-$headH+1)
    }
    if ($roundStem -and $stemRect.Height -gt 1) {
        $sp = New-RoundRect $stemRect ($stemW/2.2)
        $gfx.FillPath($b, $sp)
        $sp.Dispose()
    } else {
        $gfx.FillRectangle($b, $stemRect)
    }
    $b.Dispose(); $p.Dispose()
}

function New-IconFrame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $detailed = $size -ge 48
    $pad = [Math]::Max(1, [Math]::Round($size * ($(if ($detailed) { 0.09 } else { 0.05 }))))
    $rad = $size * 0.26

    if ($detailed) {
        # ظل ناعم خلف البطاقة
        $shadowSteps = 3
        for ($i = $shadowSteps; $i -ge 1; $i--) {
            $grow = $i * ($size * 0.012)
            $sr = New-Object System.Drawing.RectangleF ($pad-$grow), ($pad-$grow+$size*0.01), ($size-$pad*2+$grow*2), ($size-$pad*2+$grow*2)
            $sp = New-RoundRect $sr ($rad+$grow*0.5)
            $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(22,0,0,0))
            $g.FillPath($sb, $sp)
            $sp.Dispose(); $sb.Dispose()
        }
    }

    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - $pad*2), ($size - $pad*2)
    $path = New-RoundRect $rect $rad

    if ($detailed) {
        $top = [System.Drawing.Color]::FromArgb(255, 42, 42, 47)
        $bot = [System.Drawing.Color]::FromArgb(255, 26, 26, 30)
        $gb = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $top, $bot, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        $g.FillPath($gb, $path)
        $gb.Dispose()

        # حدّ ناعم + خط لمعان فوق
        $edgePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60,255,255,255)), ([Math]::Max(1,$size*0.006))
        $g.DrawPath($edgePen, $path)
        $edgePen.Dispose()
        $hl = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(45,255,255,255)), ([Math]::Max(1,$size*0.008))
        $g.DrawLine($hl, ($rect.X+$rad*0.5), ($rect.Y+1), ($rect.Right-$rad*0.5), ($rect.Y+1))
        $hl.Dispose()
    } else {
        $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 30, 34))
        $g.FillPath($bg, $path)
        $bg.Dispose()
    }

    $aw = $size * 0.21; $ah = $size * ($(if ($detailed) { 0.46 } else { 0.52 }))
    $gap = $size * 0.09
    $totalW = $aw * 2 + $gap
    $sx = ($size - $totalW) / 2
    $sy = ($size - $ah) / 2

    Draw-Arrow $g ([System.Drawing.Color]::FromArgb(255,76,141,246)) $sx $sy $aw $ah $true $detailed
    Draw-Arrow $g ([System.Drawing.Color]::FromArgb(255,53,199,90))  ($sx+$aw+$gap) $sy $aw $ah $false $detailed

    $g.Dispose(); $path.Dispose()
    return $bmp
}

$sizes = @(16,32,48,256)
$frames = @()
foreach ($s in $sizes) { $frames += ,(New-IconFrame $s) }

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)

$pngBytesList = @()
foreach ($f in $frames) {
    $pms = New-Object System.IO.MemoryStream
    $f.Save($pms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList += ,($pms.ToArray())
    $pms.Dispose()
}

$headerSize = 6 + 16 * $sizes.Count
$offset = $headerSize
for ($i=0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $wb = if ($s -ge 256) { 0 } else { $s }
    $bytes = $pngBytesList[$i]
    $bw.Write([byte]$wb); $bw.Write([byte]$wb)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $pngBytesList) { $bw.Write($bytes) }

$out = Join-Path $PSScriptRoot "icon.ico"
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

# نسخة PNG 256 حتى تنعاين بسهولة بدون فتح exe - قبل ما نحرّر البيتماب
$frames[3].Save((Join-Path $PSScriptRoot "icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
foreach ($f in $frames) { $f.Dispose() }
Write-Host "[ok] $out"
