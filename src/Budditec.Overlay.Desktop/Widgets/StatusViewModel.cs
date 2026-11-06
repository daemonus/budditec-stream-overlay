using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Budditec.Overlay.Core.Obs;
using Budditec.Overlay.Core.Settings;

namespace Budditec.Overlay.Desktop.Widgets;

public sealed partial class StatusViewModel : ObservableObject
{
    private readonly AudioActivityTracker _tracker = new();
    private AudioWidgetSettings _audio = new();
    private ObsStatus _status = ObsStatus.Disconnected("Connecting");

    [ObservableProperty]
    public partial bool ObsConnected { get; set; }

    [ObservableProperty]
    public partial string ObsDetail { get; set; } = "Connecting to OBS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsPaused), nameof(RecordLabel))]
    public partial RecordState Record { get; set; }

    [ObservableProperty]
    public partial bool Streaming { get; set; }

    public bool IsRecording => Record == RecordState.Recording;
    public bool IsPaused => Record == RecordState.Paused;
    public string RecordLabel => Record == RecordState.Paused ? "PAUSED" : "REC";

    public ObservableCollection<AudioSourceViewModel> Sources { get; } = [];

    public void UpdateStatus(ObsStatus status)
    {
        _status = status;
        ObsConnected = status.Connected;
        ObsDetail = status.Connected ? status.CurrentScene ?? "" : $"OBS: {status.Detail}";
        Record = status.Record;
        Streaming = status.Streaming;

        var visible = status.AudioSources
            .Where(s => _audio.SelectedSources.Count == 0 || _audio.SelectedSources.Contains(s.Name))
            .ToList();
        for (var i = Sources.Count - 1; i >= 0; i--)
            if (visible.All(v => v.Name != Sources[i].Name)) Sources.RemoveAt(i);
        for (var i = 0; i < visible.Count; i++)
        {
            var existing = Sources.FirstOrDefault(s => s.Name == visible[i].Name);
            if (existing is null) Sources.Insert(i, existing = new AudioSourceViewModel(visible[i].Name));
            else if (Sources.IndexOf(existing) != i) Sources.Move(Sources.IndexOf(existing), i);
            existing.Muted = visible[i].Muted;
        }
    }

    public void UpdateSettings(AudioWidgetSettings audio)
    {
        _audio = audio;
        _tracker.ThresholdDb = audio.SpeakingThresholdDb;
        _tracker.Hold = TimeSpan.FromMilliseconds(audio.HoldMilliseconds);
        UpdateStatus(_status);
    }

    public void UpdateLevels(IReadOnlyDictionary<string, AudioLevel> levels)
    {
        var now = DateTime.UtcNow;
        _tracker.Update(levels, now);
        foreach (var source in Sources)
        {
            source.Level = levels.TryGetValue(source.Name, out var level) ? Math.Clamp((level.PeakDb + 60) / 60, 0, 1) : 0;
            source.IsActive = !source.Muted && _tracker.IsActive(source.Name, now);
        }
    }
}

public sealed partial class AudioSourceViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Peak level mapped from -60..0 dB to 0..1.</summary>
    [ObservableProperty]
    public partial double Level { get; set; }
}
