param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [ValidateSet("all", "cpu", "directml")]
    [string]$Variant = "all",

    [string]$Python = "python",
    [string]$InnoCompiler = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    $Encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $Encoding)
}

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$AppRoot = Join-Path $ProjectRoot "app"
$BuildRoot = Join-Path $ProjectRoot "build\installer"
$ReleaseRoot = Join-Path $ProjectRoot "releases\app-v$Version"

if (-not (Get-Command $Python -ErrorAction SilentlyContinue)) {
    throw "Python executable was not found: $Python"
}

if (-not $InnoCompiler) {
    $Command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($Command) {
        $InnoCompiler = $Command.Source
    }
    else {
        foreach ($Candidate in @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe"
        )) {
            if (Test-Path -LiteralPath $Candidate -PathType Leaf) {
                $InnoCompiler = $Candidate
                break
            }
        }
    }
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup 6 compiler not found. Install it or pass -InnoCompiler <ISCC.exe>."
}

$Variants = if ($Variant -eq "all") { @("cpu", "directml") } else { @($Variant) }
New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

foreach ($CurrentVariant in $Variants) {
    $VariantRoot = Join-Path $BuildRoot $CurrentVariant
    $BuildEnvironment = Join-Path $VariantRoot ".venv"
    $BuildPython = Join-Path $BuildEnvironment "Scripts\python.exe"
    $DistRoot = Join-Path $VariantRoot "dist"
    $WorkRoot = Join-Path $VariantRoot "work"
    $GeneratedRoot = Join-Path $VariantRoot "generated"
    $Requirements = Join-Path $AppRoot "requirements-$CurrentVariant.txt"
    $DisplayVariant = if ($CurrentVariant -eq "cpu") { "CPU" } else { "DirectML" }
    $OutputBaseName = "VRC-Fisher-Setup-$DisplayVariant-x64"
    $Installer = Join-Path $ReleaseRoot "$OutputBaseName.exe"

    if (-not (Test-Path -LiteralPath $BuildPython -PathType Leaf)) {
        New-Item -ItemType Directory -Force -Path $VariantRoot | Out-Null
        & $Python -m venv $BuildEnvironment
        if ($LASTEXITCODE -ne 0) {
            throw "Could not create the isolated $CurrentVariant build environment."
        }
    }

    & $BuildPython -m pip install --disable-pip-version-check `
        -r $Requirements `
        -r (Join-Path $AppRoot "requirements-build.txt") `
        -e $AppRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install $CurrentVariant build dependencies."
    }

    & $BuildPython -m pytest -q (Join-Path $AppRoot "tests")
    if ($LASTEXITCODE -ne 0) {
        throw "Application tests failed in the $CurrentVariant environment."
    }

    New-Item -ItemType Directory -Force -Path $GeneratedRoot | Out-Null
    $ReleaseMetadata = Join-Path $GeneratedRoot "release.json"
    $Metadata = @{
        application_version = $Version
        distribution = $CurrentVariant
        repository = $Repository
        model_release_prefix = "models-v"
        runtime_api = 1
    } | ConvertTo-Json
    Write-Utf8NoBom -Path $ReleaseMetadata -Content $Metadata

    $env:VRC_FISHER_RELEASE_METADATA = $ReleaseMetadata
    try {
        & $BuildPython -m PyInstaller `
            --noconfirm `
            --clean `
            --distpath $DistRoot `
            --workpath $WorkRoot `
            (Join-Path $PSScriptRoot "vrc_fisher.spec")
        if ($LASTEXITCODE -ne 0) {
            throw "PyInstaller failed for the $CurrentVariant variant."
        }
    }
    finally {
        Remove-Item Env:VRC_FISHER_RELEASE_METADATA -ErrorAction SilentlyContinue
    }

    $SourceDir = Join-Path $DistRoot "vrc-fisher"
    New-Item -ItemType Directory -Force -Path (Join-Path $SourceDir "config") | Out-Null
    Copy-Item -LiteralPath $ReleaseMetadata -Destination (Join-Path $SourceDir "release.json") -Force
    Copy-Item -LiteralPath (Join-Path $ProjectRoot "USER_GUIDE.md") -Destination $SourceDir -Force
    Copy-Item -LiteralPath (Join-Path $AppRoot "config\default.toml") `
        -Destination (Join-Path $SourceDir "config\default.toml") `
        -Force

    $BundledModels = @(Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Filter "*.onnx")
    if ($BundledModels.Count -ne 0) {
        throw "Application installer staging contains ONNX models; build aborted."
    }

    & $InnoCompiler `
        "/DAppVersion=$Version" `
        "/DAppVariant=$DisplayVariant" `
        "/DOutputBaseName=$OutputBaseName" `
        "/DSourceDir=$SourceDir" `
        "/DOutputDir=$ReleaseRoot" `
        (Join-Path $PSScriptRoot "installer.iss")
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for the $CurrentVariant variant."
    }

    if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
        throw "Installer output is missing: $Installer"
    }
    if ((Get-Item -LiteralPath $Installer).Length -ge 1GB) {
        throw "Installer is 1 GB or larger: $Installer"
    }
    $Hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$Hash  $OutputBaseName.exe" | Set-Content `
        -LiteralPath "$Installer.sha256" `
        -Encoding ASCII

    Write-Host "Installer: $Installer"
    Write-Host ("Installer size: {0:N1} MB" -f ((Get-Item -LiteralPath $Installer).Length / 1MB))
}
