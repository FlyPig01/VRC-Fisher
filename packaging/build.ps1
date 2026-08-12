param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [ValidateSet("all")]
    [string]$Variant = "all",

    [string]$DotNet = "dotnet",
    [string]$InnoCompiler = ""
)

& (Join-Path $PSScriptRoot "build-installer.ps1") `
    -Version $Version `
    -Repository $Repository `
    -Variant $Variant `
    -DotNet $DotNet `
    -InnoCompiler $InnoCompiler
exit $LASTEXITCODE
