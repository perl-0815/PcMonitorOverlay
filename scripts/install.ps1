param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\PcMonitorOverlay"),
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut,
    [switch]$StartWithWindows,
    [switch]$NoLaunch,
    [switch]$SkipDependencyInstall,
    [switch]$UseExistingBuild
)

$ErrorActionPreference = "Stop"

$appName = "PC Monitor Overlay"
$processName = "PcMonitorOverlay"
$exeName = "PcMonitorOverlay.exe"
$uninstallerExeName = "PcMonitorOverlayUninstaller.exe"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\PcMonitorOverlay\PcMonitorOverlay.csproj"
$publishDir = Join-Path $repoRoot "dist-portable"
$portableExe = Join-Path $publishDir $exeName
$portableUninstaller = Join-Path $publishDir $uninstallerExeName

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-DotNetCommand {
    $commands = @()

    $userDotNet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotNet) {
        $commands += $userDotNet
    }

    $programFilesDotNet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $programFilesDotNet) {
        $commands += $programFilesDotNet
    }

    $pathDotNet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($pathDotNet) {
        $commands += $pathDotNet.Source
    }

    foreach ($command in ($commands | Select-Object -Unique)) {
        try {
            & $command --version *> $null
            return $command
        }
        catch {
        }
    }

    return $null
}

function Test-DotNet8Sdk {
    param([string]$DotNet)

    if (-not $DotNet) {
        return $false
    }

    $sdks = & $DotNet --list-sdks 2>$null
    return [bool]($sdks | Where-Object { $_ -match "^8\." })
}

function Install-DotNet8Sdk {
    if ($SkipDependencyInstall) {
        throw ".NET 8 SDK is required to build from source. Re-run without -SkipDependencyInstall, or install it with: winget install --id Microsoft.DotNet.SDK.8 --source winget"
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "winget was not found. Install .NET 8 SDK manually, then run this script again: https://dotnet.microsoft.com/download/dotnet/8.0"
    }

    Write-Step "Installing .NET 8 SDK with winget"
    & $winget.Source install `
        --id Microsoft.DotNet.SDK.8 `
        --source winget `
        --silent `
        --accept-source-agreements `
        --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget could not install .NET 8 SDK. Install it manually, then run this script again."
    }
}

function Publish-PortableBuild {
    Write-Step "Creating portable build"
    & (Join-Path $PSScriptRoot "publish-portable.ps1")

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed."
    }
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $appName
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

function Remove-AppShortcutIfTargetMatches {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath
    )

    if (-not (Test-Path $ShortcutPath)) {
        return
    }

    $expectedTarget = [System.IO.Path]::GetFullPath($TargetPath)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    if ($shortcut.TargetPath -and ([System.IO.Path]::GetFullPath($shortcut.TargetPath) -eq $expectedTarget)) {
        Remove-Item -LiteralPath $ShortcutPath -Force
    }
}

function Stop-InstalledProcess {
    param([string]$InstalledExe)

    $fullPath = [System.IO.Path]::GetFullPath($InstalledExe)
    Get-Process $processName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -eq $fullPath) } |
        Stop-Process -Force
}

if ((Test-Path $project) -and -not $UseExistingBuild) {
    $dotnet = Get-DotNetCommand
    if (-not (Test-DotNet8Sdk $dotnet)) {
        Install-DotNet8Sdk
        $dotnet = Get-DotNetCommand
    }

    if (-not (Test-DotNet8Sdk $dotnet)) {
        throw ".NET 8 SDK is still unavailable. Open a new PowerShell window and run this script again."
    }

    Publish-PortableBuild
}
elseif (-not (Test-Path $portableExe)) {
    if (-not (Test-Path $project)) {
        throw "Portable exe was not found: $portableExe"
    }

    $dotnet = Get-DotNetCommand
    if (-not (Test-DotNet8Sdk $dotnet)) {
        Install-DotNet8Sdk
        $dotnet = Get-DotNetCommand
    }

    if (-not (Test-DotNet8Sdk $dotnet)) {
        throw ".NET 8 SDK is still unavailable. Open a new PowerShell window and run this script again."
    }

    Publish-PortableBuild
}
else {
    Write-Step "Using existing portable build"
}

if (-not (Test-Path $portableExe)) {
    throw "Portable exe was not created: $portableExe"
}

$installPath = [System.IO.Path]::GetFullPath($InstallDir)
$installedExe = Join-Path $installPath $exeName
$installedUninstaller = Join-Path $installPath $uninstallerExeName

Write-Step "Installing to $installPath"
New-Item -ItemType Directory -Force -Path $installPath | Out-Null
Stop-InstalledProcess -InstalledExe $installedExe
Copy-Item -LiteralPath $portableExe -Destination $installedExe -Force

if (Test-Path $portableUninstaller) {
    Copy-Item -LiteralPath $portableUninstaller -Destination $installedUninstaller -Force
}

Copy-Item -LiteralPath (Join-Path $publishDir "README.txt") -Destination (Join-Path $installPath "README.txt") -Force -ErrorAction SilentlyContinue

$portablePdb = Join-Path $publishDir "PcMonitorOverlay.pdb"
if (Test-Path $portablePdb) {
    Copy-Item -LiteralPath $portablePdb -Destination (Join-Path $installPath "PcMonitorOverlay.pdb") -Force
}

if (-not $NoStartMenuShortcut) {
    $programs = Join-Path ([Environment]::GetFolderPath("Programs")) $appName
    New-AppShortcut `
        -ShortcutPath (Join-Path $programs "$appName.lnk") `
        -TargetPath $installedExe `
        -WorkingDirectory $installPath

    if (Test-Path $installedUninstaller) {
        New-AppShortcut `
            -ShortcutPath (Join-Path $programs "Uninstall $appName.lnk") `
            -TargetPath $installedUninstaller `
            -WorkingDirectory $installPath
    }
}

if (-not $NoDesktopShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    New-AppShortcut `
        -ShortcutPath (Join-Path $desktop "$appName.lnk") `
        -TargetPath $installedExe `
        -WorkingDirectory $installPath
}

$startup = [Environment]::GetFolderPath("Startup")
$startupShortcut = Join-Path $startup "$appName.lnk"
if ($StartWithWindows) {
    New-AppShortcut `
        -ShortcutPath $startupShortcut `
        -TargetPath $installedExe `
        -WorkingDirectory $installPath
}
elseif (Test-Path $startupShortcut) {
    Remove-AppShortcutIfTargetMatches -ShortcutPath $startupShortcut -TargetPath $installedExe
}

Write-Host ""
Write-Host "$appName was installed successfully." -ForegroundColor Green
Write-Host "Installed exe: $installedExe"

if (-not $NoLaunch) {
    Start-Process -FilePath $installedExe -WorkingDirectory $installPath
}
