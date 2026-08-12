param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Locator,

    [Parameter(Mandatory = $true)]
    [string]$Minigame
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$LocatorPath = (Resolve-Path -LiteralPath $Locator).Path
$MinigamePath = (Resolve-Path -LiteralPath $Minigame).Path
$ReleaseRoot = Join-Path $ProjectRoot "releases\models-v$Version"

if (Test-Path -LiteralPath $ReleaseRoot) {
    throw "Model release directory already exists: $ReleaseRoot"
}
$TotalSize = (Get-Item -LiteralPath $LocatorPath).Length + (Get-Item -LiteralPath $MinigamePath).Length
if ($TotalSize -ge 1GB) {
    throw "The two ONNX models are 1 GB or larger; release aborted."
}

New-Item -ItemType Directory -Path $ReleaseRoot | Out-Null
$Models = @(
    @{ filename = "locator.onnx"; path = $LocatorPath },
    @{ filename = "minigame.onnx"; path = $MinigamePath }
)
$ManifestModels = @()
foreach ($Model in $Models) {
    $Destination = Join-Path $ReleaseRoot $Model.filename
    Copy-Item -LiteralPath $Model.path -Destination $Destination
    $ManifestModels += @{
        filename = $Model.filename
        size = (Get-Item -LiteralPath $Destination).Length
        sha256 = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
@{
    schema_version = 1
    runtime_api = 1
    version = $Version
    models = $ManifestModels
} | ConvertTo-Json -Depth 4 | Set-Content `
    -LiteralPath (Join-Path $ReleaseRoot "model-manifest.json") `
    -Encoding UTF8

Write-Host "Model release assets: $ReleaseRoot"
Write-Host ("Combined model size: {0:N1} MB" -f ($TotalSize / 1MB))
