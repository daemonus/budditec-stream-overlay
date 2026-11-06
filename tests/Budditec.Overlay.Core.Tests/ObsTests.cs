using System.Text.Json;
using Budditec.Overlay.Core.Obs;

namespace Budditec.Overlay.Core.Tests;

public class AudioSourceNamesTests
{
    [Theory]
    [InlineData("Desktop Audio", "DA")]
    [InlineData("Mic/Aux", "MA")]
    [InlineData("Mic 2", "M2")]
    [InlineData("Discord", "Dis")]
    [InlineData("go", "Go")]
    [InlineData("Elgato Wave:3 Microphone", "EW3")]
    [InlineData("--", "--")]
    public void Abbreviate(string name, string expected) => Assert.Equal(expected, AudioSourceNames.Abbreviate(name));

    [Theory]
    [InlineData(-40, LevelBand.Green)]
    [InlineData(-20, LevelBand.Yellow)]
    [InlineData(-9, LevelBand.Red)]
    [InlineData(0, LevelBand.Red)]
    public void BandsMatchObsMixer(double peakDb, LevelBand expected) => Assert.Equal(expected, AudioSourceNames.Band(peakDb));
}

public class ObsTests
{
    [Fact]
    public void AuthMatchesProtocolExample()
    {
        // Worked example from the obs-websocket v5 protocol docs.
        var auth = ObsWebSocketClient.ComputeAuth(
            "supersecretpassword",
            "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=",
            "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=");

        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=", auth);
    }

    [Fact]
    public void LevelsUseLoudestChannel()
    {
        using var doc = JsonDocument.Parse("""
            {"inputs":[
              {"inputName":"Mic","inputLevelsMul":[[0.1,0.5,0.5],[0.2,0.25,0.25]]},
              {"inputName":"Silent","inputLevelsMul":[[0,0,0]]}
            ]}
            """);

        var levels = ObsMonitor.ParseLevels(doc.RootElement);

        Assert.Equal(20 * Math.Log10(0.5), levels["Mic"].PeakDb, 6);
        Assert.Equal(20 * Math.Log10(0.2), levels["Mic"].MagnitudeDb, 6);
        Assert.Equal(double.NegativeInfinity, levels["Silent"].PeakDb);
    }

    [Fact]
    public void SilentInputsStillCountAsAudibleButOnesWithoutChannelsDont()
    {
        // A quiet mic still sends (silent) audio; a browser source not routing audio to OBS has no channels.
        using var doc = JsonDocument.Parse("""
            {"inputs":[
              {"inputName":"Mic","inputLevelsMul":[[0,0,0]]},
              {"inputName":"Twitch Alerts","inputLevelsMul":[]}
            ]}
            """);

        Assert.Equal(["Mic"], ObsMonitor.AudibleInputs(doc.RootElement));
    }

    [Fact]
    public void ActivityHoldsAfterLevelDrops()
    {
        var tracker = new AudioActivityTracker { ThresholdDb = -40, Hold = TimeSpan.FromMilliseconds(250) };
        var t0 = new DateTime(2026, 1, 1);

        tracker.Update(new Dictionary<string, AudioLevel> { ["Mic"] = new(-20, -30) }, t0);
        tracker.Update(new Dictionary<string, AudioLevel> { ["Mic"] = new(-60, -70) }, t0.AddMilliseconds(100));

        Assert.True(tracker.IsActive("Mic", t0.AddMilliseconds(200)));
        Assert.False(tracker.IsActive("Mic", t0.AddMilliseconds(300)));
        Assert.False(tracker.IsActive("Other", t0));
    }
}
