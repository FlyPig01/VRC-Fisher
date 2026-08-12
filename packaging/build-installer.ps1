param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [ValidateSet("all")]
    [string]$Variant = "all",

    [string]$DotNet = "dotnet",
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
$DesktopProject = Join-Path $ProjectRoot "app\src\VrcFisher.Desktop\VrcFisher.Desktop.csproj"
$BuildRoot = Join-Path $ProjectRoot "build\installer"
$ReleaseRoot = Join-Path $ProjectRoot "releases\app-v$Version"
$StageRoot = Join-Path $BuildRoot "stage"

if (-not (Get-Command $DotNet -ErrorAction SilentlyContinue)) {
    throw "dotnet executable was not found: $DotNet"
}

if (-not $InnoCompiler) {
    $Command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($Command) { $InnoCompiler = $Command.Source }
    else {
        foreach ($Candidate in @(
            (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
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
if ($Variants.Count -ne 2) {
    throw "A single Setup must contain both CPU-only and DirectML components."
}
if (Test-Path -LiteralPath $StageRoot) { Remove-Item -LiteralPath $StageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

$Metadata = @{
    application_version = $Version
    repository = $Repository
    model_release_prefix = "models-v"
    runtime_api = 1
} | ConvertTo-Json
Write-Utf8NoBom -Path (Join-Path $StageRoot "release.json") -Content $Metadata
Copy-Item -LiteralPath (Join-Path $ProjectRoot "USER_GUIDE.md") -Destination $StageRoot -Force

foreach ($CurrentVariant in $Variants) {
    $PublishRoot = Join-Path $StageRoot $CurrentVariant
    New-Item -ItemType Directory -Force -Path $PublishRoot | Out-Null
    $Provider = if ($CurrentVariant -eq "directml") { "DirectML" } else { "CPU" }

    & $DotNet restore $DesktopProject -p:VrcExecutionProvider=$Provider -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $CurrentVariant."
    }

    & $DotNet publish $DesktopProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $PublishRoot `
        -p:Platform=x64 `
        -p:VrcExecutionProvider=$Provider `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $CurrentVariant."
    }

    $BundledModels = @(Get-ChildItem -LiteralPath $PublishRoot -Recurse -File -Filter "*.onnx")
    if ($BundledModels.Count -ne 0) {
        throw "Publish output contains ONNX models; models must remain Release downloads."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $PublishRoot "VrcFisher.pri") -PathType Leaf)) {
        throw "Publish output is missing the compiled application language resources: VrcFisher.pri"
    }
    $Dependencies = Get-Content -LiteralPath (Join-Path $PublishRoot "VrcFisher.deps.json") -Raw
    $HasDirectMLPackage = $Dependencies.Contains('Microsoft.ML.OnnxRuntime.DirectML')
    if (($CurrentVariant -eq "directml") -ne $HasDirectMLPackage) {
        throw "Publish output contains the wrong ONNX Runtime package for $CurrentVariant."
    }
}

& $InnoCompiler `
    "/DAppVersion=$Version" `
    "/DSourceDir=$StageRoot" `
    "/DOutputDir=$ReleaseRoot" `
    "/DOutputBaseName=VRC-Fisher-Setup-x64" `
    (Join-Path $PSScriptRoot "installer.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$Installer = Join-Path $ReleaseRoot "VRC-Fisher-Setup-x64.exe"
if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
    throw "Installer output is missing: $Installer"
}
if ((Get-Item -LiteralPath $Installer).Length -ge 1GB) {
    throw "Installer is 1 GB or larger: $Installer"
}
$Hash = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$Hash  VRC-Fisher-Setup-x64.exe" | Set-Content -LiteralPath "$Installer.sha256" -Encoding ASCII
Write-Host ("Installer size: {0:N1} MB" -f ((Get-Item -LiteralPath $Installer).Length / 1MB))
