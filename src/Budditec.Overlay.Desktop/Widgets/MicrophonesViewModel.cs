using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Desktop.Widgets;

public sealed partial class MicrophonesViewModel : ObservableObject
{
    private readonly AudioActivityTracker _tracker = new();
    private AudioWidgetSettings _audio = new();
    private ObsStatus _status = ObsStatus.Disconnected("Connecting");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFull))]
    public partial bool IsMinimal { get; set; }

    public bool IsFull => !IsMinimal;

    public ObservableCollection<AudioSourceViewModel> Sources { get; } = [];

    public void UpdateStatus(ObsStatus status)
    {
        _status = status;
        // Numbers count every source OBS lists, so hiding one doesn't renumber the rest.
        var visible = status.AudioSources
            .Select((s, i) => (Source: s, Number: i + 1))
            .Where(s => _audio.IsSourceShown(s.Source.Name))
            .ToList();
        for (var i = Sources.Count - 1; i >= 0; i--)
            if (visible.All(v => v.Source.Name != Sources[i].Name)) Sources.RemoveAt(i);
        for (var i = 0; i < visible.Count; i++)
        {
            var (source, number) = visible[i];
            var existing = Sources.FirstOrDefault(s => s.Name == source.Name);
            if (existing is null) Sources.Insert(i, existing = new AudioSourceViewModel(source.Name));
            else if (Sources.IndexOf(existing) != i) Sources.Move(Sources.IndexOf(existing), i);
            existing.Muted = source.Muted;
            existing.Label = _audio.MinimalLabel switch
            {
                MicrophoneLabel.Number => number.ToString(),
                MicrophoneLabel.ShortName => AudioSourceNames.Abbreviate(source.Name),
                _ => "",
            };
        }
    }

    public void UpdateSettings(AudioWidgetSettings audio)
    {
        _audio = audio;
        _tracker.ThresholdDb = audio.SpeakingThresholdDb;
        _tracker.Hold = TimeSpan.FromMilliseconds(audio.HoldMilliseconds);
        IsMinimal = audio.Display == MicrophoneDisplay.Minimal;
        UpdateStatus(_status);
    }

    public void UpdateLevels(IReadOnlyDictionary<string, AudioLevel> levels)
    {
        var now = DateTime.UtcNow;
        _tracker.Update(levels, now);
        foreach (var source in Sources)
        {
            var peak = levels.TryGetValue(source.Name, out var level) ? level.PeakDb : double.NegativeInfinity;
            source.Level = Math.Clamp((peak + 60) / 60, 0, 1);
            source.IsActive = !source.Muted && _tracker.IsActive(source.Name, now);
            source.UpdateBand(source.IsActive ? AudioSourceNames.Band(peak) : LevelBand.Quiet, now, _tracker.Hold);
        }
    }
}

public sealed partial class AudioSourceViewModel(string name) : ObservableObject
{
    private DateTime _bandUntil;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Peak level mapped from -60..0 dB to 0..1.</summary>
    [ObservableProperty]
    public partial double Level { get; set; }

    /// <summary>The number or short name under the minimal display's dot.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabel))]
    public partial string Label { get; set; } = "";

    public bool HasLabel => Label.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGreen), nameof(IsYellow), nameof(IsRed))]
    public partial LevelBand Band { get; set; }

    public bool IsGreen => Band == LevelBand.Green;
    public bool IsYellow => Band == LevelBand.Yellow;
    public bool IsRed => Band == LevelBand.Red;

    /// <summary>Louder colours stick for the hold time, so the dot doesn't flicker between peaks.</summary>
    public void UpdateBand(LevelBand band, DateTime now, TimeSpan hold)
    {
        if (band != LevelBand.Quiet && band < Band && now < _bandUntil) return;
        _bandUntil = now + hold;
        Band = band;
    }
}
