param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "dist-portable"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\PcMonitorOverlay\PcMonitorOverlay.csproj"
$installerProject = Join-Path $repoRoot "src\PcMonitorOverlay.Installer\PcMonitorOverlay.Installer.csproj"
$uninstallerProject = Join-Path $repoRoot "src\PcMonitorOverlay.Uninstaller\PcMonitorOverlay.Uninstaller.csproj"
$distributionReadme = Join-Path $repoRoot "distribution\README.txt"
$outputPath = Join-Path $repoRoot $Output
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

function Clear-DistributionDirectory {
    param([string]$Path)

    $resolvedRepo = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd("\")
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd("\")

    if ($fullPath -eq $resolvedRepo -or -not $fullPath.StartsWith("$resolvedRepo\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean output path outside repository: $fullPath"
    }

    if (Test-Path $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
}

function Publish-SingleFile {
    param([string]$Project)

    & $dotnet publish $Project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=embedded `
        -p:DebugSymbols=false `
        -o $outputPath

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed: $Project"
    }
}

Clear-DistributionDirectory -Path $outputPath

Publish-SingleFile -Project $appProject
Publish-SingleFile -Project $installerProject
Publish-SingleFile -Project $uninstallerProject

if (Test-Path $distributionReadme) {
    Copy-Item -LiteralPath $distributionReadme -Destination (Join-Path $outputPath "README.txt") -Force
}

Get-ChildItem -LiteralPath $outputPath | Sort-Object Name | Select-Object Name, Length
