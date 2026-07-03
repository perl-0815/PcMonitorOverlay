using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace PcMonitorOverlay.Installer;

internal static class Program
{
    private const string AppName = "PC Monitor Overlay";
    private const string AppExeName = "PcMonitorOverlay.exe";
    private const string UninstallerExeName = "PcMonitorOverlayUninstaller.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        Forms.Application.EnableVisualStyles();

        var options = InstallOptions.Parse(args);

        try
        {
            var sourceDirectory = AppContext.BaseDirectory;
            var sourceApp = Path.Combine(sourceDirectory, AppExeName);
            var sourceUninstaller = Path.Combine(sourceDirectory, UninstallerExeName);

            if (!File.Exists(sourceApp))
            {
                throw new FileNotFoundException($"{AppExeName} was not found next to the installer.", sourceApp);
            }

            var installDirectory = GetInstallDirectory(options.InstallDirectory);
            var installedApp = Path.Combine(installDirectory, AppExeName);
            var installedUninstaller = Path.Combine(installDirectory, UninstallerExeName);

            Directory.CreateDirectory(installDirectory);
            StopInstalledApp(installedApp);

            File.Copy(sourceApp, installedApp, overwrite: true);
            if (File.Exists(sourceUninstaller))
            {
                File.Copy(sourceUninstaller, installedUninstaller, overwrite: true);
            }

            CopyIfExists(Path.Combine(sourceDirectory, "README.txt"), Path.Combine(installDirectory, "README.txt"));

            if (!options.NoStartMenuShortcut)
            {
                var startMenuDirectory = GetStartMenuDirectory();
                CreateShortcut(
                    Path.Combine(startMenuDirectory, $"{AppName}.lnk"),
                    installedApp,
                    installDirectory,
                    AppName);

                if (File.Exists(installedUninstaller))
                {
                    CreateShortcut(
                        Path.Combine(startMenuDirectory, $"Uninstall {AppName}.lnk"),
                        installedUninstaller,
                        installDirectory,
                        $"Uninstall {AppName}");
                }
            }

            if (!options.NoDesktopShortcut)
            {
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk"),
                    installedApp,
                    installDirectory,
                    AppName);
            }

            var startupShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{AppName}.lnk");
            if (options.StartWithWindows)
            {
                CreateShortcut(startupShortcut, installedApp, installDirectory, AppName);
            }
            else
            {
                RemoveShortcutIfTargetMatches(startupShortcut, installedApp);
            }

            if (!options.NoLaunch)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installedApp,
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });
            }

            ShowMessage(
                $"{AppName} was installed successfully.\n\nInstall location:\n{installDirectory}",
                Forms.MessageBoxIcon.Information,
                options);

            return 0;
        }
        catch (Exception ex)
        {
            ShowMessage(
                $"Installation failed.\n\n{ex.Message}",
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

    private static string GetStartMenuDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            AppName);

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: true);
        }
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

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell is unavailable.");

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic dynamicShell = shell!;
            shortcut = dynamicShell.CreateShortcut(shortcutPath);
            dynamic dynamicShortcut = shortcut;
            dynamicShortcut.TargetPath = targetPath;
            dynamicShortcut.WorkingDirectory = workingDirectory;
            dynamicShortcut.Description = description;
            dynamicShortcut.IconLocation = targetPath;
            dynamicShortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
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
        InstallOptions options,
        bool force = false)
    {
        if (!options.Quiet || force)
        {
            Forms.MessageBox.Show(message, AppName, Forms.MessageBoxButtons.OK, icon);
        }
    }

    private sealed record InstallOptions(
        bool Quiet,
        bool NoLaunch,
        bool NoDesktopShortcut,
        bool NoStartMenuShortcut,
        bool StartWithWindows,
        string? InstallDirectory)
    {
        public static InstallOptions Parse(string[] args)
        {
            var values = args
                .Select(arg => arg.TrimStart('-', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return new InstallOptions(
                values.Contains("quiet") || values.Contains("silent"),
                values.Contains("nolaunch"),
                values.Contains("nodesktop"),
                values.Contains("nostartmenu"),
                values.Contains("startup") || values.Contains("startwithwindows"),
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
