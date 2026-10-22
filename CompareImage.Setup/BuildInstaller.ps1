<#
.SYNOPSIS
    CompareImage セットアップ・配布パッケージ自動生成スクリプト
.DESCRIPTION
    1. CompareImage プロジェクトを Release モードでビルド/発行 (dotnet publish)
    2. システムまたはローカルの Tesseract-OCR を収集して同梱
    3. ポータブル配布用 ZIP パッケージを生成
    4. Inno Setup がインストールされている場合は Setup.exe も自動生成
#>

[CmdletBinding()]
param (
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ProjectDir = Join-Path $SolutionDir "CompareImage"
$OutputDir = Join-Path $SolutionDir "Output"
$PublishDir = Join-Path $OutputDir "publish"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " CompareImage Setup Builder (with Tesseract-OCR)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. 出力先ディレクトリの準備
if (Test-Path $PublishDir) {
    Remove-Item -Recurse -Force $PublishDir
}
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# 2. dotnet publish の実行
Write-Host "[1/4] Publishing application (dotnet publish)..." -ForegroundColor Yellow
dotnet publish "$ProjectDir\CompareImage.csproj" -c $Configuration -o $PublishDir --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed."
    exit 1
}

# 3. Tesseract-OCR の同梱処理
Write-Host "[2/4] Bundling Tesseract-OCR..." -ForegroundColor Yellow
$TargetTesseractDir = Join-Path $PublishDir "Tesseract"
New-Item -ItemType Directory -Force -Path $TargetTesseractDir | Out-Null

$TesseractSource = $null
if (Test-Path "$SolutionDir\Tesseract\tesseract.exe") {
    $TesseractSource = "$SolutionDir\Tesseract"
} elseif (Test-Path "C:\Program Files\Tesseract-OCR\tesseract.exe") {
    $TesseractSource = "C:\Program Files\Tesseract-OCR"
}

if ($TesseractSource) {
    Write-Host "  -> Found Tesseract source: $TesseractSource" -ForegroundColor Green

    # 実行ファイルおよび DLL のコピー
    Get-ChildItem -Path $TesseractSource -File -Filter "*.exe" | Where-Object { $_.Name -ne "tesseract-uninstall.exe" } | Copy-Item -Destination $TargetTesseractDir -Force
    Get-ChildItem -Path $TesseractSource -File -Filter "*.dll" | Copy-Item -Destination $TargetTesseractDir -Force

    # tessdata のコピー (日本語、英語、OSD 等)
    $SourceTessdata = Join-Path $TesseractSource "tessdata"
    if (Test-Path $SourceTessdata) {
        $TargetTessdata = Join-Path $TargetTesseractDir "tessdata"
        New-Item -ItemType Directory -Force -Path $TargetTessdata | Out-Null
        Get-ChildItem -Path $SourceTessdata -File | Copy-Item -Destination $TargetTessdata -Force
        Write-Host "  -> Bundled tessdata language files." -ForegroundColor Green
    }
} else {
    Write-Warning "Tesseract-OCR was not found. Continuing without Tesseract."
}

# 4. 配布用 ZIP アーカイブ (ポータブル版) の作成
Write-Host "[3/4] Creating portable ZIP package..." -ForegroundColor Yellow
$ZipFileName = "CompareImage_Portable_v$Version.zip"
$ZipFilePath = Join-Path $OutputDir $ZipFileName
if (Test-Path $ZipFilePath) { Remove-Item -Force $ZipFilePath }
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipFilePath -CompressionLevel Optimal
Write-Host "  -> Created: $ZipFilePath" -ForegroundColor Green

# 5. Inno Setup によるインストーラー作成 (iscc がある場合)
Write-Host "[4/4] Checking for Inno Setup compiler..." -ForegroundColor Yellow
$IsccCandidates = @(
    "iscc.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)

$IsccPath = $null
foreach ($cand in $IsccCandidates) {
    $found = Get-Command $cand -ErrorAction SilentlyContinue
    if ($found) { $IsccPath = $found.Source; break }
    if (Test-Path $cand) { $IsccPath = $cand; break }
}

if ($IsccPath) {
    Write-Host "  -> Found Inno Setup compiler: $IsccPath" -ForegroundColor Green
    $IssFile = Join-Path $ScriptDir "CompareImageSetup.iss"
    & "$IsccPath" "/DMyAppVersion=$Version" "/DSourceOutputDir=$PublishDir" "$IssFile"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  -> Successfully generated Setup installer!" -ForegroundColor Green
    }
} else {
    Write-Host "  -> Inno Setup (ISCC.exe) not found. Skipped Setup.exe generation." -ForegroundColor DarkGray
    Write-Host "     (Install Inno Setup 6 to compile CompareImageSetup.iss into a single Setup.exe)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Package build finished successfully!" -ForegroundColor Green
Write-Host " Output Directory: $OutputDir" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan