param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [ValidateSet("all", "cpu", "directml")]
    [string]$Variant = "all",

    [string]$Python = "python",
    [string]$InnoCompiler = ""
)

& (Join-Path $PSScriptRoot "build-installer.ps1") `
    -Version $Version `
    -Repository $Repository `
    -Variant $Variant `
    -Python $Python `
    -InnoCompiler $InnoCompiler
exit $LASTEXITCODE
