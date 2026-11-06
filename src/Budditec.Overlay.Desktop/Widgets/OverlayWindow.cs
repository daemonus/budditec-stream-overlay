using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Platform;

namespace Budditec.Overlay.Desktop.Widgets;

/// <summary>
/// A transparent, borderless, always-on-top widget window. Click-through normally; in edit mode it shows a
/// frame and can be dragged and resized.
/// </summary>
public class OverlayWindow : Window
{
    private readonly Border _frame;
    private bool _editing;

    public string WidgetId { get; }

    protected OverlayWindow(string widgetId, string title, WidgetBounds defaultBounds, WidgetBounds? savedBounds)
    {
        WidgetId = widgetId;
        Title = title;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var bounds = savedBounds ?? defaultBounds;
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;

        var grip = new Border
        {
            Width = 18,
            Height = 18,
            Background = new SolidColorBrush(Color.Parse("#CCBDA6FF")),
            CornerRadius = new CornerRadius(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.BottomRightCorner),
        };
        grip.PointerPressed += (_, e) =>
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        };

        _frame = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.Parse("#99101018")),
            BorderBrush = new SolidColorBrush(Color.Parse("#BDA6FF")),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new Panel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{title}  (drag to move, {EditHotkey.Description} to finish)",
                        Foreground = Brushes.White,
                        FontSize = 12,
                        Margin = new Thickness(6, 4),
                    },
                    grip,
                },
            },
        };
        _frame.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        Opened += (_, _) => OverlayInterop.SetClickThrough(this, clickThrough: true);
    }

    /// <summary>The widget's content; the edit frame is layered on top of it.</summary>
    protected Control Body
    {
        init => Content = new Panel { Children = { value, _frame } };
    }

    public bool IsEditing
    {
        get => _editing;
        set
        {
            _editing = value;
            _frame.IsVisible = value;
            OverlayInterop.SetClickThrough(this, clickThrough: !value);
        }
    }

    public WidgetBounds CurrentBounds => new(Position.X, Position.Y, (int)Math.Round(Bounds.Width), (int)Math.Round(Bounds.Height));
}
