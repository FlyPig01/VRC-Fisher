param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Locator,

    [Parameter(Mandatory = $true)]
    [string]$Minigame,

    [Parameter(Mandatory = $true)]
    [string]$LocatorCheckpoint,

    [Parameter(Mandatory = $true)]
    [string]$MinigameCheckpoint,

    [Parameter(Mandatory = $true)]
    [string]$ModelCard,

    [switch]$AutomaticAllowed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LocatorPath = (Resolve-Path -LiteralPath $Locator).Path
$MinigamePath = (Resolve-Path -LiteralPath $Minigame).Path
$LocatorCheckpointPath = (Resolve-Path -LiteralPath $LocatorCheckpoint).Path
$MinigameCheckpointPath = (Resolve-Path -LiteralPath $MinigameCheckpoint).Path
$ModelCardPath = (Resolve-Path -LiteralPath $ModelCard).Path
$SourceRoot = Join-Path $ProjectRoot "models\v$Version"
$ReleaseRoot = Join-Path $ProjectRoot "releases\models-v$Version"
$ModelLicensePath = Join-Path $ProjectRoot "training\LICENSE"

foreach ($RequiredInput in @(
    @{ path = $LocatorPath; extension = ".onnx" },
    @{ path = $MinigamePath; extension = ".onnx" },
    @{ path = $LocatorCheckpointPath; extension = ".pt" },
    @{ path = $MinigameCheckpointPath; extension = ".pt" },
    @{ path = $ModelCardPath; extension = ".md" }
)) {
    if (-not (Test-Path -LiteralPath $RequiredInput.path -PathType Leaf)) {
        throw "Required model input is not a file: $($RequiredInput.path)"
    }
    if ([System.IO.Path]::GetExtension($RequiredInput.path) -ne $RequiredInput.extension) {
        throw "Model input must use $($RequiredInput.extension): $($RequiredInput.path)"
    }
}
if (-not (Test-Path -LiteralPath $ModelLicensePath -PathType Leaf)) {
    throw "Model license is missing: $ModelLicensePath"
}
$ModelCardContent = Get-Content -LiteralPath $ModelCardPath -Raw
if ([string]::IsNullOrWhiteSpace($ModelCardContent) -or $ModelCardContent.Contains("TBD")) {
    throw "Model card is empty or still contains TBD fields: $ModelCardPath"
}
if (-not $ModelCardContent.Contains("AGPL-3.0")) {
    throw "Model card must identify the upstream-designated model license as AGPL-3.0."
}

foreach ($OutputRoot in @($SourceRoot, $ReleaseRoot)) {
    if (Test-Path -LiteralPath $OutputRoot) {
        throw "Model output directory already exists: $OutputRoot"
    }
}
$MaximumGitBlobSize = 100MB
foreach ($ModelPath in @(
    $LocatorPath,
    $MinigamePath,
    $LocatorCheckpointPath,
    $MinigameCheckpointPath
)) {
    if ((Get-Item -LiteralPath $ModelPath).Length -ge $MaximumGitBlobSize) {
        throw "Model file is 100 MiB or larger and cannot be committed to ordinary GitHub Git: $ModelPath. Configure and document Git LFS before changing this limit."
    }
}
$RuntimeSize = (Get-Item -LiteralPath $LocatorPath).Length + (Get-Item -LiteralPath $MinigamePath).Length
if ($RuntimeSize -ge 1GB) {
    throw "The two ONNX models are 1 GB or larger; release aborted."
}
$LocatorHash = (Get-FileHash -LiteralPath $LocatorPath -Algorithm SHA256).Hash.ToLowerInvariant()
$MinigameHash = (Get-FileHash -LiteralPath $MinigamePath -Algorithm SHA256).Hash.ToLowerInvariant()
$LocatorCheckpointHash = (Get-FileHash -LiteralPath $LocatorCheckpointPath -Algorithm SHA256).Hash.ToLowerInvariant()
$MinigameCheckpointHash = (Get-FileHash -LiteralPath $MinigameCheckpointPath -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($RequiredModelCardValue in @(
    $Version,
    $LocatorHash,
    $MinigameHash,
    $LocatorCheckpointHash,
    $MinigameCheckpointHash,
    (Get-Item -LiteralPath $LocatorPath).Length.ToString(),
    (Get-Item -LiteralPath $MinigamePath).Length.ToString(),
    (Get-Item -LiteralPath $LocatorCheckpointPath).Length.ToString(),
    (Get-Item -LiteralPath $MinigameCheckpointPath).Length.ToString()
)) {
    if (-not $ModelCardContent.Contains($RequiredModelCardValue)) {
        throw "Model card does not contain required release value: $RequiredModelCardValue"
    }
}

New-Item -ItemType Directory -Path (Join-Path $SourceRoot "checkpoints") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $SourceRoot "runtime") -Force | Out-Null
Copy-Item -LiteralPath $LocatorCheckpointPath -Destination (Join-Path $SourceRoot "checkpoints\locator.pt")
Copy-Item -LiteralPath $MinigameCheckpointPath -Destination (Join-Path $SourceRoot "checkpoints\minigame.pt")
Copy-Item -LiteralPath $LocatorPath -Destination (Join-Path $SourceRoot "runtime\locator.onnx")
Copy-Item -LiteralPath $MinigamePath -Destination (Join-Path $SourceRoot "runtime\minigame.onnx")
Copy-Item -LiteralPath $ModelCardPath -Destination (Join-Path $SourceRoot "MODEL_CARD.md")
Copy-Item -LiteralPath $ModelLicensePath -Destination (Join-Path $SourceRoot "MODEL_LICENSE.txt")

$SourceFiles = @(
    @{ filename = "checkpoints/locator.pt"; path = (Join-Path $SourceRoot "checkpoints\locator.pt"); kind = "checkpoint" },
    @{ filename = "checkpoints/minigame.pt"; path = (Join-Path $SourceRoot "checkpoints\minigame.pt"); kind = "checkpoint" },
    @{ filename = "runtime/locator.onnx"; path = (Join-Path $SourceRoot "runtime\locator.onnx"); kind = "runtime" },
    @{ filename = "runtime/minigame.onnx"; path = (Join-Path $SourceRoot "runtime\minigame.onnx"); kind = "runtime" },
    @{ filename = "MODEL_CARD.md"; path = (Join-Path $SourceRoot "MODEL_CARD.md"); kind = "documentation" },
    @{ filename = "MODEL_LICENSE.txt"; path = (Join-Path $SourceRoot "MODEL_LICENSE.txt"); kind = "license" }
)
$SourceManifestFiles = @()
foreach ($File in $SourceFiles) {
    $SourceManifestFiles += @{
        filename = $File.filename
        kind = $File.kind
        size = (Get-Item -LiteralPath $File.path).Length
        sha256 = (Get-FileHash -LiteralPath $File.path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
@{
    schema_version = 1
    version = $Version
    runtime_api = 1
    license = "AGPL-3.0"
    automatic_allowed = [bool]$AutomaticAllowed
    files = $SourceManifestFiles
} | ConvertTo-Json -Depth 4 | Set-Content `
    -LiteralPath (Join-Path $SourceRoot "source-manifest.json") `
    -Encoding UTF8

New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot "runtime\locator.onnx") -Destination (Join-Path $ReleaseRoot "locator.onnx")
Copy-Item -LiteralPath (Join-Path $SourceRoot "runtime\minigame.onnx") -Destination (Join-Path $ReleaseRoot "minigame.onnx")
Copy-Item -LiteralPath (Join-Path $SourceRoot "MODEL_CARD.md") -Destination (Join-Path $ReleaseRoot "MODEL_CARD.md")
Copy-Item -LiteralPath (Join-Path $SourceRoot "MODEL_LICENSE.txt") -Destination (Join-Path $ReleaseRoot "MODEL_LICENSE.txt")
$ManifestModels = @()
foreach ($FileName in @("locator.onnx", "minigame.onnx")) {
    $Path = Join-Path $ReleaseRoot $FileName
    $ManifestModels += @{
        filename = $FileName
        size = (Get-Item -LiteralPath $Path).Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$ManifestDocumentation = @()
foreach ($FileName in @("MODEL_CARD.md", "MODEL_LICENSE.txt")) {
    $Path = Join-Path $ReleaseRoot $FileName
    $ManifestDocumentation += @{
        filename = $FileName
        size = (Get-Item -LiteralPath $Path).Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
@{
    schema_version = 2
    runtime_api = 1
    version = $Version
    automatic_allowed = [bool]$AutomaticAllowed
    models = $ManifestModels
    documentation = $ManifestDocumentation
} | ConvertTo-Json -Depth 4 | Set-Content `
    -LiteralPath (Join-Path $ReleaseRoot "model-manifest.json") `
    -Encoding UTF8

foreach ($RequiredFile in @(
    "locator.onnx",
    "minigame.onnx",
    "model-manifest.json",
    "MODEL_CARD.md",
    "MODEL_LICENSE.txt"
)) {
    if (-not (Test-Path -LiteralPath (Join-Path $ReleaseRoot $RequiredFile) -PathType Leaf)) {
        throw "Model release is missing required file: $RequiredFile"
    }
}

$SourceManifest = Get-Content -LiteralPath (Join-Path $SourceRoot "source-manifest.json") -Raw | ConvertFrom-Json
foreach ($File in $SourceManifest.files) {
    $Path = Join-Path $SourceRoot ($File.filename -replace '/', '\')
    if ((Get-Item -LiteralPath $Path).Length -ne $File.size) {
        throw "Source manifest size mismatch: $($File.filename)"
    }
    $Hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($Hash -ne $File.sha256) {
        throw "Source manifest hash mismatch: $($File.filename)"
    }
}
$RuntimeSourceByName = @{
    "locator.onnx" = Join-Path $SourceRoot "runtime\locator.onnx"
    "minigame.onnx" = Join-Path $SourceRoot "runtime\minigame.onnx"
}
foreach ($FileName in $RuntimeSourceByName.Keys) {
    $SourceHash = (Get-FileHash -LiteralPath $RuntimeSourceByName[$FileName] -Algorithm SHA256).Hash
    $ReleaseHash = (Get-FileHash -LiteralPath (Join-Path $ReleaseRoot $FileName) -Algorithm SHA256).Hash
    if ($SourceHash -ne $ReleaseHash) {
        throw "Release model differs from committed source model: $FileName"
    }
}

Write-Host "Model release assets: $ReleaseRoot"
Write-Host "Model source assets (commit this directory): $SourceRoot"
Write-Host ("Combined runtime model size: {0:N1} MB" -f ($RuntimeSize / 1MB))
