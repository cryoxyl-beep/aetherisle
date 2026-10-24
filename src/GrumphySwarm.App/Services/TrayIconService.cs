using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GrumphySwarm.App.ViewModels;
using GrumphySwarm.Core.State;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;

namespace GrumphySwarm.App.Services;

/// <summary>
/// System-tray presence: context menu (expand, collapse, settings placeholder,
/// exit) and left-click toggle. The icon is rendered at runtime — no binary
/// asset needed, and it stays crisp on light and dark taskbars.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotchViewModel _viewModel;
    private readonly ILogger<TrayIconService> _logger;

    private TaskbarIcon? _trayIcon;

    /// <summary>Raised when the user picks Exit in the tray menu.</summary>
    public event Action? ExitRequested;

    public TrayIconService(NotchViewModel viewModel, ILogger<TrayIconService> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_trayIcon is not null) return;

        var menu = new ContextMenu();

        var expand = new MenuItem { Header = "Expand notch", FontWeight = FontWeights.Medium };
        expand.Click += (_, _) => _viewModel.RequestExpandFromTray();

        var collapse = new MenuItem { Header = "Collapse notch" };
        collapse.Click += (_, _) => _viewModel.RequestCollapseFromTray();

        var settings = new MenuItem
        {
            Header = "Settings…",
            IsEnabled = false,
            ToolTip = "Settings arrive in Milestone 2",
        };

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(expand);
        menu.Items.Add(collapse);
        menu.Items.Add(new Separator());
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Grumphy Swarm — notch",
            ContextMenu = menu,
            IconSource = CreateIconSource(),
        };
        _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;

        _logger.LogInformation("Tray icon initialized.");
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.State == NotchState.Collapsed)
        {
            _viewModel.RequestExpandFromTray();
        }
        else
        {
            _viewModel.RequestCollapseFromTray();
        }
    }

    /// <summary>Draws a 32x32 notch glyph: black pill, light outline, status dots.</summary>
    private static ImageSource CreateIconSource()
    {
        const int size = 32;
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            var pillFill = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C));
            var pillStroke = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)) { Opacity = 0.8 };
            var pen = new Pen(pillStroke, 1.4);

            context.DrawRoundedRectangle(pillFill, pen, new Rect(5, 9, 22, 14), 6, 6);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
                null,
                new Point(11.5, 16),
                2.6,
                2.6);
            context.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)) { Opacity = 0.85 },
                null,
                new Point(20.5, 16),
                2,
                2);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
