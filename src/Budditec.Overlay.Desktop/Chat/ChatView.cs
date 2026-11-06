using System.Threading.Channels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Desktop.Chat;

/// <summary>
/// Native chat renderer: newest message at the bottom, older ones pushed up and clipped. Purely draws
/// ChatMessage models with a ChatStyle, so a web renderer could replace it without touching the rest.
/// </summary>
public sealed class ChatView : UserControl
{
    private static readonly TimeSpan EmoteWait = TimeSpan.FromSeconds(3);

    private readonly StackPanel _list = new() { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 2 };
    private readonly EmoteCache _emotes;
    private readonly Channel<ChatEvent> _queue = Channel.CreateUnbounded<ChatEvent>();
    private readonly DispatcherTimer _fadeTimer;
    private ChatStyle _style;

    public ChatView(EmoteCache emotes, ChatStyle style)
    {
        _emotes = emotes;
        _style = style;
        ClipToBounds = true;
        Content = _list;
        _fadeTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => FadeOld());
        ApplyStyle(style);
        Dispatcher.UIThread.Post(() => _ = ProcessQueueAsync());
    }

    /// <summary>Events are processed in order; a message waits briefly for its emotes so it lays out once.</summary>
    public void Handle(ChatEvent e) => _queue.Writer.TryWrite(e);

    public void ApplyStyle(ChatStyle style)
    {
        _style = style;
        _list.Effect = style.ShadowEnabled
            ? new DropShadowEffect
            {
                Color = WithOpacity(ParseOr(style.ShadowColor, Colors.Black), style.ShadowOpacity),
                BlurRadius = style.ShadowBlur,
                OffsetX = style.ShadowOffsetX,
                OffsetY = style.ShadowOffsetY,
            }
            : null;

        foreach (var child in _list.Children.OfType<TextBlock>().ToList())
            if (child.Tag is ChatMessage message)
                _list.Children[_list.Children.IndexOf(child)] = Build(message);
        Trim();

        if (style.FadeAfterSeconds > 0) _fadeTimer.Start();
        else _fadeTimer.Stop();
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var e in _queue.Reader.ReadAllAsync())
        {
            switch (e)
            {
                case ChatMessageReceived { Message: var message }:
                    await PreloadEmotesAsync(message);
                    _list.Children.Add(Build(message));
                    Trim();
                    break;
                case ChatMessageDeleted deleted:
                    RemoveWhere(m => m.Id == deleted.MessageId);
                    break;
                case ChatUserCleared cleared:
                    RemoveWhere(m => m.Platform == cleared.Platform && m.UserLogin == cleared.UserLogin);
                    break;
                case ChatCleared cleared:
                    RemoveWhere(m => m.Platform == cleared.Platform);
                    break;
            }
        }
    }

    private async Task PreloadEmotesAsync(ChatMessage message)
    {
        var height = EmotePixelHeight();
        var loads = message.Fragments.OfType<EmoteFragment>().Select(f => _emotes.GetAsync(f.Url, height)).ToList();
        if (loads.Count == 0) return;
        try
        {
            await Task.WhenAll(loads).WaitAsync(EmoteWait);
        }
        catch (TimeoutException)
        {
        }
    }

    private TextBlock Build(ChatMessage message)
    {
        var style = _style;
        var nameBrush = new SolidColorBrush(ParseOr(style.NameColorFor(message), Colors.White));
        var block = new TextBlock
        {
            Tag = message,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(style.FontFamily),
            FontSize = style.FontSize,
            FontWeight = (FontWeight)style.FontWeight,
            Foreground = new SolidColorBrush(ParseOr(style.TextColor, Colors.White)),
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(600) }],
        };

        var inlines = block.Inlines!;
        if (style.ShowPlatformIcon)
            inlines.Add(new InlineUIContainer(Icons.Platform(message.Platform, style.FontSize * 0.9)) { BaselineAlignment = BaselineAlignment.Center });
        inlines.Add(new Run(message.IsAction ? message.UserDisplayName + " " : message.UserDisplayName + ": ") { Foreground = nameBrush });

        var emoteHeight = style.FontSize * style.EmoteScale;
        var emotePixels = EmotePixelHeight();
        foreach (var fragment in message.Fragments)
        {
            switch (fragment)
            {
                case EmoteFragment emote when _emotes.Use(emote.Url, emotePixels) is { } frames:
                    inlines.Add(new InlineUIContainer(new EmoteImage(frames, emoteHeight, emote.Code)) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                case EmoteFragment emote:
                    inlines.Add(new Run(emote.Code));
                    break;
                case TextFragment text:
                    inlines.Add(message.IsAction ? new Run(text.Text) { Foreground = nameBrush, FontStyle = FontStyle.Italic } : new Run(text.Text));
                    break;
            }
        }
        _emotes.Trim();
        return block;
    }

    /// <summary>Emotes are decoded at the physical pixel height they're drawn at.</summary>
    private int EmotePixelHeight() =>
        (int)Math.Ceiling(_style.FontSize * _style.EmoteScale * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));

    private void FadeOld()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_style.FadeAfterSeconds);
        foreach (var block in _list.Children.OfType<TextBlock>().ToList())
        {
            if (block.Tag is not ChatMessage message || message.Timestamp > cutoff) continue;
            if (block.Opacity > 0) block.Opacity = 0;
            else _list.Children.Remove(block);
        }
    }

    private void Trim()
    {
        var excess = _list.Children.Count - Math.Max(1, _style.MaxMessages);
        if (excess > 0) _list.Children.RemoveRange(0, excess);
    }

    private void RemoveWhere(Func<ChatMessage, bool> predicate)
    {
        foreach (var block in _list.Children.OfType<TextBlock>().Where(b => b.Tag is ChatMessage m && predicate(m)).ToList())
            _list.Children.Remove(block);
    }

    private static Color ParseOr(string value, Color fallback) => Color.TryParse(value, out var color) ? color : fallback;

    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
}
