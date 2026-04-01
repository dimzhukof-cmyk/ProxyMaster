Add-Type -AssemblyName System.Drawing

$pngs = @(
    'C:\Claude\ProxyMaster\Icon\PM_logo_16x16.png',
    'C:\Claude\ProxyMaster\Icon\PM_logo_32x32.png',
    'C:\Claude\ProxyMaster\Icon\PM_logo_48x48.png',
    'C:\Claude\ProxyMaster\Icon\PM_logo_256x256.png'
)

$images = $pngs | ForEach-Object { [System.Drawing.Bitmap]::new($_) }

$pngDataList = $images | ForEach-Object {
    $tmp = [System.IO.MemoryStream]::new()
    $_.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $tmp.ToArray()
}

$count = $images.Count
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

# ICONDIR header
$bw.Write([uint16]0)       # reserved
$bw.Write([uint16]1)       # type = ICO
$bw.Write([uint16]$count)  # count

# Directory entries
$offset = 6 + 16 * $count
for ($i = 0; $i -lt $count; $i++) {
    $img = $images[$i]
    $w = if ($img.Width  -ge 256) { 0 } else { $img.Width  }
    $h = if ($img.Height -ge 256) { 0 } else { $img.Height }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)     # color count
    $bw.Write([byte]0)     # reserved
    $bw.Write([uint16]1)   # planes
    $bw.Write([uint16]32)  # bit count
    $bw.Write([uint32]$pngDataList[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngDataList[$i].Length
}

# Image data
foreach ($data in $pngDataList) { $bw.Write($data) }

$bw.Flush()
[System.IO.File]::WriteAllBytes('C:\Claude\ProxyMaster\Icon\ProxyMaster.ico', $ms.ToArray())

$images | ForEach-Object { $_.Dispose() }
Write-Host "ICO created successfully"
