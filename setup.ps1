# ProxyMaster - Setup and Build Script
# Run as Administrator!

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  ProxyMaster - Setup and Build"         -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---- 1. Check .NET 8 SDK ----
Write-Host "[1/4] Checking .NET 8 SDK..." -ForegroundColor Yellow

$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
$sdkOutput = & $dotnetExe --list-sdks 2>&1
$hasSdk8   = ($sdkOutput | Out-String) -match "^8\."

if (-not $hasSdk8) {
    Write-Host "      .NET 8 SDK not found. Installing via winget..." -ForegroundColor Yellow

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        winget install Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements
    } else {
        Write-Host "      winget not found. Downloading installer..." -ForegroundColor Yellow
        $sdkUrl       = "https://download.visualstudio.microsoft.com/download/pr/57ffff70-1b2b-44fe-aa9b-209e30f79c6f/cc24e73a485140d3a32ce93f63acf4e2/dotnet-sdk-8.0.400-win-x64.exe"
        $sdkInstaller = "$env:TEMP\dotnet-sdk-8-x64.exe"
        Invoke-WebRequest -Uri $sdkUrl -OutFile $sdkInstaller -UseBasicParsing
        Start-Process -FilePath $sdkInstaller -ArgumentList "/install /quiet /norestart" -Wait
        Remove-Item $sdkInstaller -Force
    }

    # Refresh PATH
    $machinePath = [System.Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::Machine)
    $userPath    = [System.Environment]::GetEnvironmentVariable("PATH", [System.EnvironmentVariableTarget]::User)
    $env:PATH    = $machinePath + ";" + $userPath

    # Re-check after install
    $sdkOutput2 = & $dotnetExe --list-sdks 2>&1
    if (-not (($sdkOutput2 | Out-String) -match "^8\.")) {
        Write-Host "      SDK install may need a new terminal. Trying to build anyway..." -ForegroundColor Yellow
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetCmd) { $dotnetExe = $dotnetCmd.Source }
        else { $dotnetExe = "C:\Program Files\dotnet\dotnet.exe" }
    }

    Write-Host "      .NET 8 SDK installed." -ForegroundColor Green
} else {
    Write-Host "      .NET 8 SDK found." -ForegroundColor Green
}

# ---- 2. Download WinDivert ----
Write-Host ""
Write-Host "[2/4] Downloading WinDivert 2.2..." -ForegroundColor Yellow

$wdDir = "$PSScriptRoot\WinDivert"
New-Item -ItemType Directory -Force -Path $wdDir | Out-Null

$wdDll = "$wdDir\WinDivert.dll"
$wdSys = "$wdDir\WinDivert64.sys"

if (-not (Test-Path $wdDll) -or -not (Test-Path $wdSys)) {
    $wdUrl = "https://github.com/basil00/Divert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip"
    $wdZip = "$env:TEMP\WinDivert.zip"
    $wdTmp = "$env:TEMP\WinDivert_tmp"

    Write-Host "      Downloading WinDivert (~1 MB)..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $wdUrl -OutFile $wdZip -UseBasicParsing

    Expand-Archive -Path $wdZip -DestinationPath $wdTmp -Force

    # Find x64 binaries
    $dllSrc = Get-ChildItem $wdTmp -Recurse -Filter "WinDivert.dll"   | Where-Object { $_.DirectoryName -match "x64" } | Select-Object -First 1
    $sysSrc = Get-ChildItem $wdTmp -Recurse -Filter "WinDivert64.sys" | Select-Object -First 1

    if (-not $dllSrc) {
        $dllSrc = Get-ChildItem $wdTmp -Recurse -Filter "WinDivert.dll" | Select-Object -First 1
    }

    Copy-Item $dllSrc.FullName $wdDll -Force
    Copy-Item $sysSrc.FullName $wdSys -Force

    Remove-Item $wdZip -Force
    Remove-Item $wdTmp -Recurse -Force

    Write-Host "      WinDivert copied to: $wdDir" -ForegroundColor Green
} else {
    Write-Host "      WinDivert already present." -ForegroundColor Green
}

# ---- 3. Build project ----
Write-Host ""
Write-Host "[3/4] Building ProxyMaster..." -ForegroundColor Yellow

& $dotnetExe build "$PSScriptRoot\ProxyMaster.csproj" -c Release -r win-x64 --no-self-contained

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed." -ForegroundColor Red
    exit 1
}

# ---- 4. Done ----
Write-Host ""
Write-Host "[4/4] Done!" -ForegroundColor Green
$exePath = "$PSScriptRoot\bin\Release\net8.0-windows\win-x64\ProxyMaster.exe"
Write-Host ""
Write-Host "  Executable:" -ForegroundColor Cyan
Write-Host "  $exePath"    -ForegroundColor White
Write-Host ""
Write-Host "  IMPORTANT: Run ProxyMaster.exe as Administrator" -ForegroundColor Yellow
Write-Host "             (required for WinDivert kernel driver)" -ForegroundColor Yellow
Write-Host ""
