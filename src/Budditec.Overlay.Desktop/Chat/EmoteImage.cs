using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Budditec.Overlay.Desktop.Chat;

/// <summary>
/// Draws a decoded emote at a fixed height; animated emotes share one clock. Holds a reference on its
/// frames while on screen so the cache can evict them without breaking visible messages.
/// </summary>
public sealed class EmoteImage : Control
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly DispatcherTimer Timer = new(TimeSpan.FromMilliseconds(20), DispatcherPriority.Render, (_, _) => Tick?.Invoke());
    private static event Action? Tick;

    private readonly EmoteFrames _frames;
    private bool _holdsRef;
    private int _frame;

    public EmoteImage(EmoteFrames frames, double height, string code)
    {
        _frames = frames;
        // Taken immediately so a cache trim between building and attaching can't free the frames.
        frames.AddRef();
        _holdsRef = true;
        Height = height;
        Width = height * frames.Size.Width / frames.Size.Height;
        ToolTip.SetTip(this, code);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public override void Render(DrawingContext context)
    {
        if (_holdsRef) _frames.Draw(context, _frame, new Rect(Bounds.Size));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_holdsRef && !_frames.IsDisposed)
        {
            _frames.AddRef();
            _holdsRef = true;
        }
        if (!_frames.IsAnimated) return;
        Tick += OnTick;
        Timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Tick -= OnTick;
        if (Tick is null) Timer.Stop();
        if (!_holdsRef) return;
        _holdsRef = false;
        _frames.Release();
    }

    private void OnTick()
    {
        var frame = _frames.IndexAt(Clock.ElapsedMilliseconds);
        if (frame == _frame) return;
        _frame = frame;
        InvalidateVisual();
    }
}
