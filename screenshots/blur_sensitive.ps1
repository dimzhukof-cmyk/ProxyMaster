Add-Type -AssemblyName System.Drawing

function Pixelate-Region {
    param(
        [System.Drawing.Bitmap]$bmp,
        [int]$x, [int]$y, [int]$w, [int]$h,
        [int]$blockSize = 12
    )
    for ($row = $y; $row -lt ($y + $h); $row += $blockSize) {
        for ($col = $x; $col -lt ($x + $w); $col += $blockSize) {
            # Sample center pixel of block
            $cx = [Math]::Min($col + $blockSize/2, $bmp.Width  - 1)
            $cy = [Math]::Min($row  + $blockSize/2, $bmp.Height - 1)
            $color = $bmp.GetPixel($cx, $cy)
            # Fill block with that color
            $bw = [Math]::Min($blockSize, $x + $w - $col)
            $bh = [Math]::Min($blockSize, $y + $h - $row)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $brush = [System.Drawing.SolidBrush]::new($color)
            $g.FillRectangle($brush, $col, $row, $bw, $bh)
            $g.Dispose()
            $brush.Dispose()
        }
    }
}

$files = @(
    'C:\Claude\ProxyMaster\screenshots\01_main.png',
    'C:\Claude\ProxyMaster\screenshots\02_active.png',
    'C:\Claude\ProxyMaster\screenshots\03_process_filter.png'
)

foreach ($file in $files) {
    $bmp = [System.Drawing.Bitmap]::new($file)

    $W = $bmp.Width
    $H = $bmp.Height
    Write-Host "$file  ${W}x${H}"

    # Масштабируем координаты под реальный размер скриншота
    # Базовая ширина окна ~806px, поля расположены в строке прокси-настроек
    $scale = $W / 806.0

    # Server address field (столбец Host)
    $hx = [int](144  * $scale)
    $hy = [int](155  * $scale)
    $hw = [int](118  * $scale)
    $hh = [int](30   * $scale)

    # Login field
    $lx = [int](362  * $scale)
    $ly = [int](155  * $scale)
    $lw = [int](163  * $scale)
    $lh = [int](30   * $scale)

    Pixelate-Region $bmp $hx $hy $hw $hh
    Pixelate-Region $bmp $lx $ly $lw $lh

    $tmp = $file + ".tmp.png"
    $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Move-Item -Force $tmp $file
    Write-Host "  Blurred: host[$hx,$hy,${hw}x${hh}]  login[$lx,$ly,${lw}x${lh}]"
}

Write-Host "Done"
