using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Resources;
using System.Windows.Threading;
using PcMonitorOverlay.Controls;
using PcMonitorOverlay.Models;
using PcMonitorOverlay.Services;
using PcMonitorOverlay.Settings;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PcMonitorOverlay;

public partial class MainWindow : Window
{
    private static readonly WpfBrush ActiveTopmostBackground = CreateBrush(210, 25, 139, 119);
    private static readonly WpfBrush ActiveTopmostBorder = CreateBrush(255, 55, 214, 178);
    private static readonly WpfBrush ActiveLockBackground = CreateBrush(210, 178, 111, 24);
    private static readonly WpfBrush ActiveLockBorder = CreateBrush(255, 242, 184, 75);
    private static readonly WpfBrush InactiveBackground = CreateBrush(28, 255, 255, 255);
    private static readonly WpfBrush InactiveBorder = CreateBrush(48, 255, 255, 255);
    private static readonly WpfBrush InactiveForeground = CreateBrush(190, 216, 224, 232);
    private static readonly WpfBrush ActiveForeground = CreateBrush(255, 244, 247, 250);

    private readonly DispatcherTimer _timer;
    private readonly HardwareMonitorService _monitor;
    private readonly OverlaySettings _settings;
    private readonly CancellationTokenSource _refreshCancellation = new();
    private bool _settingsApplied;
    private bool _isClosing;
    private bool _refreshInProgress;
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _trayIcon;
    private Forms.ToolStripMenuItem? _topmostMenuItem;
    private Forms.ToolStripMenuItem? _lockMenuItem;

    public MainWindow()
    {
        InitializeComponent();

        _settings = OverlaySettings.Load();
        _monitor = new HardwareMonitorService();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += async (_, _) => await RefreshMetricsAsync();

        ApplySettings();
        _settingsApplied = true;
        CreateTrayIcon();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await RefreshMetricsAsync();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _isClosing = true;
        SaveSettings();
        _timer.Stop();
        _refreshCancellation.Cancel();
        _monitor.Dispose();
        _refreshCancellation.Dispose();
        _notifyIcon?.Dispose();
        _trayIcon?.Dispose();
    }

    private async Task RefreshMetricsAsync()
    {
        if (_isClosing || _refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;

        try
        {
            var token = _refreshCancellation.Token;
            var snapshot = await Task.Run(_monitor.ReadSnapshot, token);

            if (token.IsCancellationRequested || _isClosing)
            {
                return;
            }

            UpdateMetrics(snapshot);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            if (!_isClosing)
            {
                StatusText.Text = "Sensor error";
            }
        }
        finally
        {
            _refreshInProgress = false;
        }
    }

    private void UpdateMetrics(MonitorSnapshot snapshot)
    {
        UpdateMetric(snapshot.Cpu, CpuValueText, CpuDetailText, CpuGraph);
        UpdateMetric(snapshot.Memory, MemoryValueText, MemoryDetailText, MemoryGraph);
        UpdateMetric(snapshot.Gpu, GpuValueText, GpuDetailText, GpuGraph);
        UpdateMetric(snapshot.Vram, VramValueText, VramDetailText, VramGraph);

        StatusText.Text = snapshot.Timestamp.ToString("HH:mm:ss");
    }

    private static void UpdateMetric(
        MetricReading metric,
        System.Windows.Controls.TextBlock valueText,
        System.Windows.Controls.TextBlock detailText,
        UsageGraph graph)
    {
        valueText.Text = metric.Percent.HasValue ? $"{metric.Percent.Value:0}%" : "N/A";
        detailText.Text = metric.Detail;
        graph.AddValue(metric.Percent);
    }

    private void ApplySettings()
    {
        Left = _settings.Left;
        Top = _settings.Top;
        Width = Math.Max(_settings.Width, MinWidth);
        Height = Math.Max(_settings.Height, MinHeight);
        Opacity = _settings.Opacity;
        Topmost = _settings.Topmost;
        OpacitySlider.Value = _settings.Opacity;

        UpdateTopmostState();
        UpdateLockState();
    }

    private void SaveSettings()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Opacity = Opacity;
        _settings.Topmost = Topmost;
        _settings.Save();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        var showItem = new Forms.ToolStripMenuItem("Show overlay");
        showItem.Click += (_, _) => ShowOverlay();
        menu.Items.Add(showItem);

        _topmostMenuItem = new Forms.ToolStripMenuItem("Always on top")
        {
            Checked = Topmost,
            CheckOnClick = true
        };
        _topmostMenuItem.Click += (_, _) =>
        {
            Topmost = _topmostMenuItem.Checked;
            UpdateTopmostState();
            SaveSettings();
        };
        menu.Items.Add(_topmostMenuItem);

        _lockMenuItem = new Forms.ToolStripMenuItem("Lock movement")
        {
            Checked = _settings.MovementLocked,
            CheckOnClick = true
        };
        _lockMenuItem.Click += (_, _) =>
        {
            _settings.MovementLocked = _lockMenuItem.Checked;
            UpdateLockState();
            SaveSettings();
        };
        menu.Items.Add(_lockMenuItem);

        var resetItem = new Forms.ToolStripMenuItem("Reset position");
        resetItem.Click += (_, _) =>
        {
            Left = 80;
            Top = 80;
            Width = 520;
            Height = 330;
            SaveSettings();
            ShowOverlay();
        };
        menu.Items.Add(resetItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Close();
        menu.Items.Add(exitItem);

        _trayIcon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "PC Monitor Overlay",
            Icon = _trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowOverlay();
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var iconUri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
        StreamResourceInfo? iconResource = System.Windows.Application.GetResourceStream(iconUri);
        if (iconResource is null)
        {
            return new Drawing.Icon(Drawing.SystemIcons.Application, Drawing.SystemIcons.Application.Size);
        }

        using var stream = iconResource.Stream;
        return new Drawing.Icon(stream);
    }

    private void ShowOverlay()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.MovementLocked || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            SaveSettings();
        }
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is WpfButtonBase or System.Windows.Controls.Slider)
            {
                return true;
            }

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void TopmostButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdateTopmostState();
        SaveSettings();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.MovementLocked = !_settings.MovementLocked;
        UpdateLockState();
        SaveSettings();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Opacity = Math.Clamp(e.NewValue, 0.45, 1);

        if (_settingsApplied)
        {
            _settings.Opacity = Opacity;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateTopmostState()
    {
        TopmostButton.Content = Topmost ? "PIN" : "FREE";
        ApplyToggleButtonVisual(
            TopmostButton,
            Topmost,
            ActiveTopmostBackground,
            ActiveTopmostBorder);

        if (_topmostMenuItem is not null)
        {
            _topmostMenuItem.Checked = Topmost;
        }
    }

    private void UpdateLockState()
    {
        LockButton.Content = _settings.MovementLocked ? "LOCKED" : "LOCK";
        ApplyToggleButtonVisual(
            LockButton,
            _settings.MovementLocked,
            ActiveLockBackground,
            ActiveLockBorder);

        if (_lockMenuItem is not null)
        {
            _lockMenuItem.Checked = _settings.MovementLocked;
        }
    }

    private static void ApplyToggleButtonVisual(
        System.Windows.Controls.Button button,
        bool isActive,
        WpfBrush activeBackground,
        WpfBrush activeBorder)
    {
        button.Background = isActive ? activeBackground : InactiveBackground;
        button.BorderBrush = isActive ? activeBorder : InactiveBorder;
        button.Foreground = isActive ? ActiveForeground : InactiveForeground;
        button.Opacity = 1;
    }

    private static WpfBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new WpfSolidColorBrush(WpfColor.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
