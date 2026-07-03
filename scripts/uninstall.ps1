param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\PcMonitorOverlay"),
    [switch]$RemoveSettings
)

$ErrorActionPreference = "Stop"

$appName = "PC Monitor Overlay"
$processName = "PcMonitorOverlay"
$exeName = "PcMonitorOverlay.exe"
$uninstallerExeName = "PcMonitorOverlayUninstaller.exe"
$installPath = [System.IO.Path]::GetFullPath($InstallDir)
$installedExe = Join-Path $installPath $exeName
$installedUninstaller = Join-Path $installPath $uninstallerExeName

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Remove-AppShortcut {
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

function Assert-SafeDeletePath {
    param(
        [string]$Path,
        [string]$ExpectedParent
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd("\")
    $fullParent = [System.IO.Path]::GetFullPath($ExpectedParent).TrimEnd("\")

    if ($fullPath -eq $fullParent) {
        throw "Refusing to delete parent directory itself: $fullPath"
    }

    if (-not $fullPath.StartsWith("$fullParent\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete outside expected parent: $fullPath"
    }
}

function Assert-SafeInstallDirectory {
    param(
        [string]$Path,
        [string]$ExpectedExe
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd("\")
    $root = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd("\")
    if ($fullPath -eq $root) {
        throw "Refusing to delete drive root: $fullPath"
    }

    foreach ($blocked in @($env:USERPROFILE, $env:LOCALAPPDATA, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($blocked -and ($fullPath -eq [System.IO.Path]::GetFullPath($blocked).TrimEnd("\"))) {
            throw "Refusing to delete broad system/user directory: $fullPath"
        }
    }

    if (-not (Test-Path $ExpectedExe)) {
        throw "Refusing to delete install directory because app exe was not found: $ExpectedExe"
    }
}

Write-Step "Stopping installed app"
$installedExeFullPath = [System.IO.Path]::GetFullPath($installedExe)
Get-Process $processName -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -eq $installedExeFullPath) } |
    Stop-Process -Force

Write-Step "Removing shortcuts"
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath("Programs")) $appName
Remove-AppShortcut -ShortcutPath (Join-Path $startMenuDirectory "$appName.lnk") -TargetPath $installedExe
Remove-AppShortcut -ShortcutPath (Join-Path $startMenuDirectory "Uninstall $appName.lnk") -TargetPath $installedUninstaller
Remove-AppShortcut -ShortcutPath (Join-Path ([Environment]::GetFolderPath("Programs")) "$appName.lnk") -TargetPath $installedExe
Remove-AppShortcut -ShortcutPath (Join-Path ([Environment]::GetFolderPath("Programs")) "Uninstall $appName.lnk") -TargetPath $installedUninstaller
Remove-AppShortcut -ShortcutPath (Join-Path ([Environment]::GetFolderPath("Desktop")) "$appName.lnk") -TargetPath $installedExe
Remove-AppShortcut -ShortcutPath (Join-Path ([Environment]::GetFolderPath("Startup")) "$appName.lnk") -TargetPath $installedExe

if ((Test-Path $startMenuDirectory -PathType Container) -and -not (Get-ChildItem -LiteralPath $startMenuDirectory -Force)) {
    Remove-Item -LiteralPath $startMenuDirectory -Force
}

if (Test-Path $installPath) {
    Assert-SafeInstallDirectory -Path $installPath -ExpectedExe $installedExe

    Write-Step "Removing installed files"
    Remove-Item -LiteralPath $installPath -Recurse -Force
}

if ($RemoveSettings) {
    $settingsPath = Join-Path $env:LOCALAPPDATA "PcMonitorOverlay"
    if (Test-Path $settingsPath) {
        Assert-SafeDeletePath -Path $settingsPath -ExpectedParent $env:LOCALAPPDATA

        Write-Step "Removing settings"
        Remove-Item -LiteralPath $settingsPath -Recurse -Force
    }
}

Write-Host ""
Write-Host "$appName was uninstalled successfully." -ForegroundColor Green
