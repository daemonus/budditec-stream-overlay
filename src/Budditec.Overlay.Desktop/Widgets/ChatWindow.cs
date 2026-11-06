using Budditec.Overlay.Core.Settings;
using Budditec.Overlay.Desktop.Chat;
using Budditec.Overlay.Desktop.Services;

namespace Budditec.Overlay.Desktop.Widgets;

public sealed class ChatWindow : OverlayWindow
{
    public const string Id = OverlayWidgets.Chat;

    public ChatWindow(OverlayController controller, EmoteCache emotes)
        : base(Id, "Chat", new WidgetBounds(40, 300, 420, 520), controller.Settings.Widgets.GetValueOrDefault(Id))
    {
        var view = new ChatView(emotes, controller.Settings.ChatStyle);
        Body = view;
        controller.ChatEvent += view.Handle;
        controller.SettingsChanged += settings => view.ApplyStyle(settings.ChatStyle);
    }
}
