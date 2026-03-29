# ProxyMaster - Publish Script

$ErrorActionPreference = 'Stop'

$ProjectDir  = $PSScriptRoot
$ProjectFile = "$ProjectDir\ProxyMaster.csproj"
$PublishDir  = "$ProjectDir\publish\ProxyMaster"
$ZipOut      = "$ProjectDir\publish\ProxyMaster-1.0.0-win-x64.zip"

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  ProxyMaster - Publish'                 -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''

# ---- 1. Clean ----
Write-Host '[1/3] Cleaning output...' -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

# ---- 2. Publish ----
Write-Host '[2/3] Publishing (self-contained, single-file)...' -ForegroundColor Yellow

dotnet publish `"$ProjectFile`" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o `"$PublishDir`"

if ($LASTEXITCODE -ne 0) {
    Write-Host '[ERROR] Publish failed.' -ForegroundColor Red
    exit 1
}

Copy-Item "$ProjectDir\WinDivert\WinDivert.dll"   "$PublishDir\WinDivert.dll"   -Force
Copy-Item "$ProjectDir\WinDivert\WinDivert64.sys" "$PublishDir\WinDivert64.sys" -Force

# ---- 3. ZIP ----
Write-Host '[3/3] Creating ZIP archive...' -ForegroundColor Yellow
if (Test-Path $ZipOut) { Remove-Item $ZipOut -Force }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipOut

Write-Host ''
Write-Host 'Done!' -ForegroundColor Green
Write-Host ''
Write-Host "  Folder : $PublishDir"  -ForegroundColor Cyan
Write-Host "  ZIP    : $ZipOut"      -ForegroundColor Cyan
Write-Host ''
Write-Host '  Files:' -ForegroundColor Gray
Get-ChildItem $PublishDir | ForEach-Object {
    $size = if ($_.Length -gt 1MB) { '{0:F1} MB' -f ($_.Length / 1MB) }
            elseif ($_.Length -gt 1KB) { '{0:F0} KB' -f ($_.Length / 1KB) }
            else { "$($_.Length) B" }
    Write-Host ("  {0,-30} {1,8}" -f $_.Name, $size) -ForegroundColor White
}
Write-Host ''
Write-Host '  IMPORTANT: Run ProxyMaster.exe as Administrator' -ForegroundColor Yellow
Write-Host ''