using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PcMonitorOverlay.Uninstaller;

internal static class Program
{
    private const string AppName = "PC Monitor Overlay";
    private const string AppExeName = "PcMonitorOverlay.exe";
    private const string UninstallerExeName = "PcMonitorOverlayUninstaller.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        Forms.Application.EnableVisualStyles();

        var options = UninstallOptions.Parse(args);

        try
        {
            var installDirectory = GetInstallDirectory(options.InstallDirectory);
            var installedApp = Path.Combine(installDirectory, AppExeName);
            var installedUninstaller = Path.Combine(installDirectory, UninstallerExeName);

            if (!options.Quiet && !ConfirmUninstall())
            {
                return 2;
            }

            StopInstalledApp(installedApp);
            RemoveShortcuts(installedApp, installedUninstaller);

            if (options.RemoveSettings)
            {
                RemoveSettingsDirectory();
            }

            if (Directory.Exists(installDirectory))
            {
                AssertSafeInstallDirectory(installDirectory, installedApp, installedUninstaller);

                if (IsCurrentProcessInside(installDirectory))
                {
                    ScheduleInstallDirectoryRemoval(installDirectory);
                }
                else
                {
                    Directory.Delete(installDirectory, recursive: true);
                }
            }

            ShowMessage(
                $"{AppName} was uninstalled successfully.",
                Forms.MessageBoxIcon.Information,
                options);

            return 0;
        }
        catch (Exception ex)
        {
            ShowMessage(
                $"Uninstall failed.\n\n{ex.Message}",
                Forms.MessageBoxIcon.Error,
                options,
                force: true);

            return 1;
        }
    }

    private static string GetInstallDirectory(string? installDirectory) =>
        string.IsNullOrWhiteSpace(installDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "PcMonitorOverlay")
            : Path.GetFullPath(installDirectory);

    private static bool ConfirmUninstall()
    {
        var result = Forms.MessageBox.Show(
            $"Uninstall {AppName}?",
            AppName,
            Forms.MessageBoxButtons.YesNo,
            Forms.MessageBoxIcon.Question,
            Forms.MessageBoxDefaultButton.Button2);

        return result == Forms.DialogResult.Yes;
    }

    private static void StopInstalledApp(string installedApp)
    {
        var installedPath = Path.GetFullPath(installedApp);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExeName)))
        {
            try
            {
                if (process.MainModule?.FileName is string path &&
                    string.Equals(Path.GetFullPath(path), installedPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void RemoveShortcuts(string installedApp, string installedUninstaller)
    {
        var startMenuDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);

        RemoveShortcutIfTargetMatches(Path.Combine(startMenuDirectory, $"{AppName}.lnk"), installedApp);
        RemoveShortcutIfTargetMatches(Path.Combine(startMenuDirectory, $"Uninstall {AppName}.lnk"), installedUninstaller);
        RemoveShortcutIfTargetMatches(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppName}.lnk"), installedApp);
        RemoveShortcutIfTargetMatches(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk"), installedApp);
        RemoveShortcutIfTargetMatches(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{AppName}.lnk"), installedApp);

        if (Directory.Exists(startMenuDirectory) && !Directory.EnumerateFileSystemEntries(startMenuDirectory).Any())
        {
            Directory.Delete(startMenuDirectory);
        }
    }

    private static void RemoveShortcutIfTargetMatches(string shortcutPath, string targetPath)
    {
        if (!File.Exists(shortcutPath))
        {
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic dynamicShell = shell!;
            shortcut = dynamicShell.CreateShortcut(shortcutPath);
            dynamic dynamicShortcut = shortcut;
            string? shortcutTarget = dynamicShortcut.TargetPath;

            if (!string.IsNullOrWhiteSpace(shortcutTarget) &&
                string.Equals(Path.GetFullPath(shortcutTarget), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(shortcutPath);
            }
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void RemoveSettingsDirectory()
    {
        var settingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PcMonitorOverlay");

        if (!Directory.Exists(settingsDirectory))
        {
            return;
        }

        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).TrimEnd('\\');
        var fullSettingsDirectory = Path.GetFullPath(settingsDirectory).TrimEnd('\\');
        if (!fullSettingsDirectory.StartsWith($"{localAppData}\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove unexpected settings directory: {settingsDirectory}");
        }

        Directory.Delete(settingsDirectory, recursive: true);
    }

    private static void AssertSafeInstallDirectory(string installDirectory, string installedApp, string installedUninstaller)
    {
        var fullInstallDirectory = Path.GetFullPath(installDirectory).TrimEnd('\\');
        var root = Path.GetPathRoot(fullInstallDirectory)?.TrimEnd('\\');
        if (string.Equals(fullInstallDirectory, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove drive root: {installDirectory}");
        }

        foreach (var blocked in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (!string.IsNullOrWhiteSpace(blocked) &&
                string.Equals(fullInstallDirectory, Path.GetFullPath(blocked).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to remove broad directory: {installDirectory}");
            }
        }

        if (!File.Exists(installedApp) && !File.Exists(installedUninstaller))
        {
            throw new InvalidOperationException($"Refusing to remove install directory because installed app files were not found: {installDirectory}");
        }
    }

    private static bool IsCurrentProcessInside(string directory)
    {
        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentProcessPath))
        {
            return false;
        }

        var fullDirectory = Path.GetFullPath(directory).TrimEnd('\\');
        var fullProcessPath = Path.GetFullPath(currentProcessPath);
        return fullProcessPath.StartsWith($"{fullDirectory}\\", StringComparison.OrdinalIgnoreCase);
    }

    private static void ScheduleInstallDirectoryRemoval(string installDirectory)
    {
        var cleanupScript = Path.Combine(Path.GetTempPath(), $"PcMonitorOverlayCleanup-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(
            cleanupScript,
            """
            param([string]$Target)
            Start-Sleep -Milliseconds 900
            Remove-Item -LiteralPath $Target -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{cleanupScript}\" -Target \"{installDirectory}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static void ShowMessage(
        string message,
        Forms.MessageBoxIcon icon,
        UninstallOptions options,
        bool force = false)
    {
        if (!options.Quiet || force)
        {
            Forms.MessageBox.Show(message, AppName, Forms.MessageBoxButtons.OK, icon);
        }
    }

    private sealed record UninstallOptions(bool Quiet, bool RemoveSettings, string? InstallDirectory)
    {
        public static UninstallOptions Parse(string[] args)
        {
            var values = args
                .Select(arg => arg.TrimStart('-', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new UninstallOptions(
                values.Contains("quiet") || values.Contains("silent"),
                values.Contains("removesettings") || values.Contains("remove-settings"),
                GetValue(args, "installdir"));
        }

        private static string? GetValue(string[] args, string name)
        {
            var prefix = $"/{name}=";
            var dashPrefix = $"-{name}=";

            foreach (var arg in args)
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg[prefix.Length..].Trim('"');
                }

                if (arg.StartsWith(dashPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg[dashPrefix.Length..].Trim('"');
                }
            }

            return null;
        }
    }
}
