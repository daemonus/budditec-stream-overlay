using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Budditec.Overlay.Core.Diagnostics;
using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Chat;
using Budditec.Overlay.Desktop.Platform;
using Budditec.Overlay.Desktop.Services;
using Budditec.Overlay.Desktop.Settings;
using Budditec.Overlay.Desktop.Widgets;

namespace Budditec.Overlay.Desktop;

public partial class App : Application
{
    private OverlayController? _controller;
    private EmoteCache? _emotes;
    private EditHotkey? _hotkey;
    private SettingsWindow? _settingsWindow;
    private readonly List<OverlayWindow> _widgets = [];
    private bool _editing;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => Shutdown();

            var controller = _controller = new OverlayController(new SettingsStore());
            var emotes = _emotes = new EmoteCache(controller.Http);
            _widgets.Add(new RecordingWindow(controller));
            _widgets.Add(new LiveWindow(controller));
            _widgets.Add(new MicrophonesWindow(controller));
            _widgets.Add(new ChatWindow(controller, emotes));
            ShowEnabledWidgets(controller.Settings);
            controller.SettingsChanged += ShowEnabledWidgets;

            CreateTrayIcon();
            _hotkey = new EditHotkey(ToggleEditing);
            DispatcherTimer.Run(() =>
            {
                if (!_editing) foreach (var widget in _widgets.Where(w => w.IsVisible)) OverlayInterop.ReassertTopmost(widget);
                return true;
            }, TimeSpan.FromSeconds(2));

            controller.Start();
            if (DevOptions.StatsPath is { } statsPath)
                _ = DevOptions.WriteStatsAsync(statsPath, () =>
                {
                    var (count, frames, bytes) = emotes.Stats;
                    return [("messages", controller.Simulation?.Sent ?? 0), ("cachedEmotes", count), ("cachedFrames", frames),
                        ("cachedEmoteMB", bytes / 1048576), ("liveEmoteMB", EmoteFrames.LiveBytes / 1048576)];
                }, CancellationToken.None);
            if (DevOptions.SimulatedChatRate is null && (!controller.Settings.HasAnyChatChannel() || desktop.Args?.Contains("--settings") == true))
                OpenSettings();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowEnabledWidgets(OverlaySettings settings)
    {
        foreach (var widget in _widgets)
        {
            var enabled = settings.IsWidgetEnabled(widget.WidgetId);
            if (enabled && !widget.IsVisible) widget.Show();
            else if (!enabled && widget.IsVisible) widget.Hide();
        }
    }

    private void ToggleEditing()
    {
        _editing = !_editing;
        foreach (var widget in _widgets)
        {
            widget.IsEditing = _editing;
            if (!_editing && widget.IsVisible) _controller!.SaveWidgetBounds(widget.WidgetId, widget.CurrentBounds);
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            open.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(new SettingsViewModel(_controller!), _emotes!);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void CreateTrayIcon()
    {
        var editItem = new NativeMenuItem($"Edit layout ({EditHotkey.Description})");
        editItem.Click += (_, _) => ToggleEditing();
        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettings();
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

        var tray = new TrayIcon
        {
            // macOS menu bar icons are monochrome "template" images that the OS tints to match the menu bar.
            Icon = LoadIcon(OperatingSystem.IsMacOS() ? "tray-template.png" : "icon.ico"),
            ToolTipText = "Budditec Overlay",
            Menu = new NativeMenu { Items = { editItem, settingsItem, new NativeMenuItemSeparator(), exitItem } },
        };
        MacOSProperties.SetIsTemplateIcon(tray, OperatingSystem.IsMacOS());
        tray.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(this, [tray]);
    }

    private static WindowIcon LoadIcon(string name) =>
        new(AssetLoader.Open(new Uri($"avares://Budditec.Overlay.Desktop/Assets/{name}")));

    private void Shutdown()
    {
        _hotkey?.Dispose();
        _controller?.Dispose();
    }
}
