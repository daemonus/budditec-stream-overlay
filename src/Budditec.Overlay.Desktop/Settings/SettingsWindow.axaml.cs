using Avalonia.Controls;
using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Desktop.Chat;

namespace Budditec.Overlay.Desktop.Settings;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsViewModel vm, EmoteCache emotes) : this()
    {
        DataContext = vm;
        Closed += (_, _) => vm.Detach();

        // A live preview of the edited (unapplied) chat style. Never fades and keeps every sample.
        var preview = new ChatView(emotes, PreviewStyle(vm));
        ChatPreview.Child = preview;
        foreach (var message in SampleMessages()) preview.Handle(new ChatMessageReceived(message));
        vm.PropertyChanged += (_, _) => preview.ApplyStyle(PreviewStyle(vm));
    }

    private static Core.Settings.ChatStyle PreviewStyle(SettingsViewModel vm)
    {
        var style = vm.BuildChatStyle();
        style.FadeAfterSeconds = 0;
        style.MaxMessages = 10;
        return style;
    }

    private static IEnumerable<ChatMessage> SampleMessages()
    {
        var now = DateTimeOffset.UtcNow;
        yield return new ChatMessage("preview-1", ChatPlatform.Twitch, "budditec", "Budditec", "#FF7F50",
            [new TextFragment("hello from Twitch "), new EmoteFragment("Kappa", TwitchEmoteParser.NativeEmoteUrl("25"))], now);
        yield return new ChatMessage("preview-2", ChatPlatform.Kick, "kickviewer", "KickViewer", "#1E90FF",
            [new TextFragment("and one from Kick")], now);
        yield return new ChatMessage("preview-3", ChatPlatform.YouTube, "ytviewer", "YouTube Viewer", null,
            [new TextFragment("YouTube says hi, with a longer line to show how wrapping looks")], now);
        yield return new ChatMessage("preview-4", ChatPlatform.Twitch, "budditec", "Budditec", "#FF7F50",
            [new TextFragment("waves (a /me action)")], now, IsAction: true);
    }
}
