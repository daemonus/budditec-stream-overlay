using Budditec.Overlay.Core.Chat;
using Budditec.Overlay.Core.Chat.Twitch;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Core.Tests;

public class OverlaySettingsTests
{
    [Fact]
    public void DefaultsAreValid() => Assert.Null(new OverlaySettings().Validate());

    [Theory]
    [InlineData("#FFF")]
    [InlineData("red")]
    [InlineData("#GG0000")]
    public void RejectsColoursRenderersCantParse(string color)
    {
        var settings = new OverlaySettings();
        settings.ChatStyle.PlatformColors[ChatPlatform.Kick] = color;

        Assert.Contains("Kick colour", settings.Validate());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ws://127.0.0.1:4455", true)]
    [InlineData("wss://obs.local", true)]
    [InlineData("http://127.0.0.1:4455", false)]
    [InlineData("127.0.0.1:4455", false)]
    public void ObsUrlMustBeWebSocket(string url, bool valid)
    {
        var settings = new OverlaySettings { Obs = { Url = url } };

        Assert.Equal(valid, settings.Validate() is null);
    }

    [Fact]
    public void NormalizeTrimsChannels()
    {
        var settings = new OverlaySettings { Twitch = { Channel = "  budditec " }, Obs = { Url = " ws://x:1 " } };

        settings.Normalize();

        Assert.Equal(("budditec", "ws://x:1"), (settings.Twitch.Channel, settings.Obs.Url));
    }

    [Fact]
    public void AudioSourcesShowUnlessHidden()
    {
        var settings = new OverlaySettings { Audio = { HiddenSources = ["Desktop Audio", "Desktop Audio"] } };

        settings.Normalize();

        Assert.Equal(["Desktop Audio"], settings.Audio.HiddenSources);
        Assert.False(settings.Audio.IsSourceShown("Desktop Audio"));
        Assert.True(settings.Audio.IsSourceShown("Mic/Aux"));
    }


    [Fact]
    public void UpgradeSplitsTheOldStatusWidget()
    {
        var settings = new OverlaySettings
        {
            Widgets = { ["status"] = new WidgetBounds(10, 20, 320, 220) },
            DisabledWidgets = ["status", OverlayWidgets.Chat],
        };

        settings.Upgrade();

        Assert.Equal(new WidgetBounds(10, 20, 110, 36), settings.Widgets[OverlayWidgets.Recording]);
        Assert.Equal(new WidgetBounds(120, 20, 210, 36), settings.Widgets[OverlayWidgets.Live]);
        Assert.Equal(new WidgetBounds(10, 60, 320, 180), settings.Widgets[OverlayWidgets.Microphones]);
        Assert.False(settings.Widgets.ContainsKey("status"));
        Assert.Equal([OverlayWidgets.Chat, OverlayWidgets.Recording, OverlayWidgets.Live, OverlayWidgets.Microphones], settings.DisabledWidgets);
    }

    [Theory]
    [InlineData("Budditec", "budditec")]
    [InlineData("#budditec", "budditec")]
    [InlineData("@budditec", "budditec")]
    [InlineData("https://www.twitch.tv/Budditec", "budditec")]
    [InlineData("twitch.tv/budditec", "budditec")]
    [InlineData("www.twitch.tv/budditec/videos", "budditec")]
    public void TwitchChannelAcceptsNamesAndUrls(string input, string expected) =>
        Assert.Equal(expected, TwitchChatClient.ParseChannel(input));
}

public sealed class OverlayRuntimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "budditec-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _store;

    public OverlayRuntimeTests()
    {
        _store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        // No channels and no OBS URL, so nothing connects out.
        _store.Save(new OverlaySettings { Obs = { Url = "" } });
    }

    [Fact]
    public void ApplySavesAndRaisesChanged()
    {
        using var runtime = new OverlayRuntime(_store);
        OverlaySettings? changed = null;
        runtime.SettingsChanged += s => changed = s;
        var settings = SettingsStore.Clone(runtime.Settings);
        settings.DisabledWidgets = [OverlayWidgets.Chat];

        Assert.Null(runtime.Apply(settings));

        Assert.False(changed!.IsWidgetEnabled(OverlayWidgets.Chat));
        Assert.False(_store.Load().IsWidgetEnabled(OverlayWidgets.Chat));
    }

    [Fact]
    public void InvalidSettingsAreNotSaved()
    {
        using var runtime = new OverlayRuntime(_store);
        var settings = SettingsStore.Clone(runtime.Settings);
        settings.ChatStyle.TextColor = "white";

        Assert.NotNull(runtime.Apply(settings));

        Assert.Equal("#FFFFFF", _store.Load().ChatStyle.TextColor);
    }

    [Fact]
    public async Task ReloadsWhenAnotherProcessEditsTheFile()
    {
        using var runtime = new OverlayRuntime(_store);
        var changed = new TaskCompletionSource<OverlaySettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.SettingsChanged += s => changed.TrySetResult(s);
        runtime.Start();

        var edited = _store.Load();
        edited.ChatStyle.FontSize = 30;
        new SettingsStore(_store.Path).Save(edited);

        var reloaded = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(30, reloaded.ChatStyle.FontSize);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
