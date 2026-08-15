param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

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

function Copy-RequiredFile {
    param(
        [string]$Source,
        [string]$Destination
    )
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required legal file was not found: $Source"
    }
    $DestinationDirectory = Split-Path -Parent $Destination
    if ($DestinationDirectory) {
        New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-PackageVersion {
    param(
        [string]$Project,
        [string]$PackageId
    )
    [xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
    $References = @($ProjectXml.SelectNodes("/Project/ItemGroup/PackageReference")) |
        Where-Object { $_.Include -eq $PackageId }
    $Versions = @($References | ForEach-Object { [string]$_.Version } | Select-Object -Unique)
    if ($Versions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($Versions[0])) {
        throw "Expected exactly one explicit version for $PackageId in $Project."
    }
    return $Versions[0]
}

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$DesktopProject = Join-Path $ProjectRoot "app\src\VrcFisher.Desktop\VrcFisher.Desktop.csproj"
$InfrastructureProject = Join-Path $ProjectRoot "app\src\VrcFisher.Infrastructure\VrcFisher.Infrastructure.csproj"
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

if (Test-Path -LiteralPath $StageRoot) { Remove-Item -LiteralPath $StageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageRoot | Out-Null
if (Test-Path -LiteralPath $ReleaseRoot) { Remove-Item -LiteralPath $ReleaseRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
$LegacyInstallerHash = Join-Path $ReleaseRoot "VRC-Fisher-Setup-x64.exe.sha256"
if (Test-Path -LiteralPath $LegacyInstallerHash) { Remove-Item -LiteralPath $LegacyInstallerHash -Force }

$Metadata = @{
    application_version = $Version
    repository = $Repository
    model_release_prefix = "models-v"
    runtime_api = 1
} | ConvertTo-Json
Write-Utf8NoBom -Path (Join-Path $StageRoot "release.json") -Content $Metadata
Copy-Item -LiteralPath (Join-Path $ProjectRoot "USER_GUIDE.md") -Destination $StageRoot -Force
Copy-RequiredFile `
    -Source (Join-Path $ProjectRoot "LICENSE") `
    -Destination (Join-Path $StageRoot "LICENSE")
Copy-RequiredFile `
    -Source (Join-Path $ProjectRoot "THIRD_PARTY_NOTICES.md") `
    -Destination (Join-Path $StageRoot "THIRD_PARTY_NOTICES.md")
Copy-RequiredFile `
    -Source (Join-Path $ProjectRoot "training\LICENSE") `
    -Destination (Join-Path $StageRoot "licenses\AGPL-3.0.txt")

$PublishRoot = Join-Path $StageRoot "program"
New-Item -ItemType Directory -Force -Path $PublishRoot | Out-Null

& $DotNet restore $DesktopProject -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed for the DirectML runtime."
}

& $DotNet publish $DesktopProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $PublishRoot `
    -p:Platform=x64 `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -p:InformationalVersion=$Version `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for the DirectML runtime."
}

# Symbols and the optional DirectML debug layer are development artifacts.
# Keep them out of the end-user installer while retaining the retail DirectML.dll.
$UnneededPublishArtifacts = @(Get-ChildItem -LiteralPath $PublishRoot -Recurse -File | Where-Object {
    $_.Extension -eq ".pdb" -or $_.Name -eq "DirectML.Debug.dll"
})
foreach ($Artifact in $UnneededPublishArtifacts) {
    Remove-Item -LiteralPath $Artifact.FullName -Force
}
if (Get-ChildItem -LiteralPath $PublishRoot -Recurse -File -Filter "*.pdb") {
    throw "Publish output still contains debugging symbols."
}
if (Test-Path -LiteralPath (Join-Path $PublishRoot "DirectML.Debug.dll") -PathType Leaf) {
    throw "Publish output still contains the optional DirectML debug layer."
}

$BundledModels = @(Get-ChildItem -LiteralPath $PublishRoot -Recurse -File -Filter "*.onnx")
if ($BundledModels.Count -ne 0) {
    throw "Publish output contains ONNX models; models must remain Release downloads."
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishRoot "VrcFisher.pri") -PathType Leaf)) {
    throw "Publish output is missing the compiled application language resources: VrcFisher.pri"
}
$Dependencies = Get-Content -LiteralPath (Join-Path $PublishRoot "VrcFisher.deps.json") -Raw
if (-not $Dependencies.Contains('Microsoft.ML.OnnxRuntime.DirectML')) {
    throw "Publish output is missing Microsoft.ML.OnnxRuntime.DirectML."
}

$NuGetPackages = if ($env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES
} else {
    Join-Path ([Environment]::GetFolderPath("UserProfile")) ".nuget\packages"
}
$LegalRoot = Join-Path $StageRoot "licenses\third-party"
$WindowsAppSdkVersion = Get-PackageVersion $DesktopProject "Microsoft.WindowsAppSDK"
$LoggingVersion = Get-PackageVersion $DesktopProject "Microsoft.Extensions.Logging"
$OnnxRuntimeDirectMlVersion = Get-PackageVersion $InfrastructureProject "Microsoft.ML.OnnxRuntime.DirectML"
$LegalFiles = @(
    @("microsoft.windowsappsdk\$WindowsAppSdkVersion\license.txt", "WindowsAppSDK-LICENSE.txt"),
    @("microsoft.windowsappsdk\$WindowsAppSdkVersion\NOTICE.txt", "WindowsAppSDK-NOTICE.txt"),
    @("microsoft.ml.onnxruntime.directml\$OnnxRuntimeDirectMlVersion\LICENSE", "ONNXRuntime.DirectML-LICENSE.txt"),
    @("microsoft.ml.onnxruntime.directml\$OnnxRuntimeDirectMlVersion\ThirdPartyNotices.txt", "ONNXRuntime.DirectML-NOTICES.txt"),
    @("microsoft.extensions.logging\$LoggingVersion\THIRD-PARTY-NOTICES.TXT", "Microsoft.Extensions.Logging-NOTICES.txt")
)
foreach ($LegalFile in $LegalFiles) {
    Copy-RequiredFile `
        -Source (Join-Path $NuGetPackages $LegalFile[0]) `
        -Destination (Join-Path $LegalRoot $LegalFile[1])
}

$DotNetRoot = Split-Path -Parent (Get-Command $DotNet -ErrorAction Stop).Source
Copy-RequiredFile `
    -Source (Join-Path $DotNetRoot "LICENSE.txt") `
    -Destination (Join-Path $LegalRoot ".NET-LICENSE.txt")
Copy-RequiredFile `
    -Source (Join-Path $DotNetRoot "ThirdPartyNotices.txt") `
    -Destination (Join-Path $LegalRoot ".NET-NOTICES.txt")
Copy-RequiredFile `
    -Source (Join-Path (Split-Path -Parent $InnoCompiler) "license.txt") `
    -Destination (Join-Path $LegalRoot "Inno-Setup-LICENSE.txt")

$RequiredStageLegalFiles = @(
    "LICENSE",
    "THIRD_PARTY_NOTICES.md",
    "licenses\AGPL-3.0.txt",
    "licenses\third-party\WindowsAppSDK-LICENSE.txt",
    "licenses\third-party\ONNXRuntime.DirectML-NOTICES.txt",
    "licenses\third-party\.NET-NOTICES.txt",
    "licenses\third-party\Inno-Setup-LICENSE.txt"
)
foreach ($RelativePath in $RequiredStageLegalFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $StageRoot $RelativePath) -PathType Leaf)) {
        throw "Installer stage is missing required legal file: $RelativePath"
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
Write-Host ("Installer size: {0:N1} MB" -f ((Get-Item -LiteralPath $Installer).Length / 1MB))
