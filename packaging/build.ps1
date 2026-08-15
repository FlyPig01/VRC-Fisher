param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [string]$DotNet = "dotnet",
    [string]$InnoCompiler = ""
)

& (Join-Path $PSScriptRoot "build-installer.ps1") `
    -Version $Version `
    -Repository $Repository `
    -DotNet $DotNet `
    -InnoCompiler $InnoCompiler
exit $LASTEXITCODE
