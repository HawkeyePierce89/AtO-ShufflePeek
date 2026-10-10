param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'

$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'AtO-ShufflePeek.csproj'
$version = (dotnet msbuild $project -getProperty:Version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet msbuild failed with exit code $LASTEXITCODE"
    exit 1
}
if ([string]::IsNullOrEmpty($version)) {
    Write-Error "Could not read Version from $project"
    exit 1
}

$expected = "v$version"
if ($Tag -cne $expected) {
    Write-Error "Tag '$Tag' does not match project version '$version' (expected '$expected')"
    exit 1
}

Write-Output $version
if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
