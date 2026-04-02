<#
.SYNOPSIS
    URSUS 빌드 및 인스톨러 패키징 스크립트

.DESCRIPTION
    1. dotnet restore + build (Release)
    2. dotnet publish URSUS.Setup → dist/URSUS_Setup.exe (self-contained single-file)
    3. 빌드 아티팩트 검증
    4. Inno Setup 컴파일 → dist/URSUS_<version>_Setup.exe (또는 ZIP 폴백)

.PARAMETER Configuration
    빌드 구성. 기본값: Release

.PARAMETER SkipBuild
    이미 빌드된 바이너리를 사용할 때 -SkipBuild 플래그 사용

.PARAMETER InnoSetupPath
    Inno Setup 컴파일러 경로. 기본: 표준 설치 경로 자동 탐색

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipBuild
    .\build.ps1 -Configuration Debug
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipBuild,

    [string]$InnoSetupPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ────────────────────────────────────────────────────────────────
$RepoRoot    = Split-Path -Parent $PSScriptRoot
$SolutionFile = Join-Path $RepoRoot "URSUS.sln"
$IssFile     = Join-Path $PSScriptRoot "URSUS.iss"
$DistDir     = Join-Path $RepoRoot "dist"
$BinDir      = Join-Path $RepoRoot "bin" $Configuration

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  URSUS Build & Package Pipeline"       -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Configuration : $Configuration"
Write-Host "  Repo Root     : $RepoRoot"
Write-Host "  Output Dir    : $DistDir"
Write-Host ""

# ── Step 1: Build ────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "[1/4] Building solution..." -ForegroundColor Yellow
    dotnet restore $SolutionFile
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    dotnet build $SolutionFile -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    Write-Host "  Build succeeded." -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping build (using existing binaries)." -ForegroundColor DarkYellow
}

# ── Step 2: Publish Setup.exe (self-contained single file) ─────────────
$SetupProject = Join-Path $RepoRoot "src" "URSUS.Setup" "URSUS.Setup.csproj"
$SetupPublishDir = Join-Path $DistDir "setup-publish"

if (-not $SkipBuild) {
    Write-Host "[2/4] Publishing Setup.exe (self-contained)..." -ForegroundColor Yellow

    dotnet publish $SetupProject -c $Configuration -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $SetupPublishDir

    if ($LASTEXITCODE -ne 0) { throw "Setup.exe publish failed" }

    # Copy Setup.exe to dist root for easy access
    $SetupExe = Join-Path $SetupPublishDir "URSUS.Setup.exe"
    if (Test-Path $SetupExe) {
        Copy-Item $SetupExe (Join-Path $DistDir "URSUS_Setup.exe") -Force
        Write-Host "  Setup.exe published → dist/URSUS_Setup.exe" -ForegroundColor Green
    } else {
        Write-Host "  Warning: Setup.exe not found after publish." -ForegroundColor DarkYellow
    }
} else {
    Write-Host "[2/4] Skipping Setup.exe publish (using existing binaries)." -ForegroundColor DarkYellow
}

# ── Step 3: Verify build output ─────────────────────────────────────────
Write-Host "[3/4] Verifying build artifacts..." -ForegroundColor Yellow

$RequiredFiles = @(
    "URSUS.GH.gha",
    "URSUS.dll",
    "Clipper2Lib.dll",
    "URSUS.GH.deps.json",
    "URSUS.GH.runtimeconfig.json"
)

$Missing = @()
foreach ($f in $RequiredFiles) {
    $fullPath = Join-Path $BinDir $f
    if (-not (Test-Path $fullPath)) {
        $Missing += $f
    }
}

if ($Missing.Count -gt 0) {
    Write-Host "  Missing files:" -ForegroundColor Red
    $Missing | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    throw "Build output incomplete. Run without -SkipBuild."
}

Write-Host "  All required artifacts present." -ForegroundColor Green

# ── Step 4: Inno Setup ──────────────────────────────────────────────────
Write-Host "[4/4] Creating installer..." -ForegroundColor Yellow

# Auto-detect Inno Setup compiler
if ([string]::IsNullOrEmpty($InnoSetupPath)) {
    $SearchPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    foreach ($p in $SearchPaths) {
        if (Test-Path $p) {
            $InnoSetupPath = $p
            break
        }
    }
}

# Ensure dist directory exists
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
}

if ([string]::IsNullOrEmpty($InnoSetupPath) -or -not (Test-Path $InnoSetupPath)) {
    Write-Host "  Inno Setup not found. Skipping installer creation." -ForegroundColor DarkYellow
    Write-Host "  Install Inno Setup 6 from: https://jrsoftware.org/isinfo.php" -ForegroundColor DarkYellow
    Write-Host ""
    Write-Host "  Alternatively, create a portable ZIP package:" -ForegroundColor DarkYellow

    # Fallback: create ZIP package
    $ZipName = "URSUS_portable.zip"
    $ZipPath = Join-Path $DistDir $ZipName
    $StagingDir = Join-Path $DistDir "_staging"

    if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
    New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

    # Copy core files
    foreach ($f in $RequiredFiles) {
        Copy-Item (Join-Path $BinDir $f) $StagingDir
    }
    # Copy additional runtime DLLs
    @("System.Drawing.Common.dll", "Microsoft.Win32.SystemEvents.dll") | ForEach-Object {
        $src = Join-Path $BinDir $_
        if (Test-Path $src) { Copy-Item $src $StagingDir }
    }
    # Copy mapping data
    $mappingFile = Join-Path $RepoRoot "bin" "adstrd_legald_mapping.json"
    if (Test-Path $mappingFile) { Copy-Item $mappingFile $StagingDir }

    Compress-Archive -Path "$StagingDir\*" -DestinationPath $ZipPath -Force
    Remove-Item -Recurse -Force $StagingDir

    Write-Host "  Created: $ZipPath" -ForegroundColor Green
} else {
    Write-Host "  Using Inno Setup: $InnoSetupPath"
    & $InnoSetupPath $IssFile
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
    Write-Host "  Installer created in $DistDir" -ForegroundColor Green
}

# ── Summary ──────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Build complete!"                      -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $DistDir) {
    Write-Host "  Output files:" -ForegroundColor White
    Get-ChildItem $DistDir -File | ForEach-Object {
        $size = "{0:N1} KB" -f ($_.Length / 1024)
        Write-Host "    $($_.Name)  ($size)"
    }
}
