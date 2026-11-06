namespace Budditec.Overlay.Core.Obs;

public enum RecordState
{
    Stopped,
    Recording,
    Paused,
}

public sealed record ObsStatus(
    bool Connected,
    string Detail,
    RecordState Record,
    bool Streaming,
    string? CurrentScene,
    IReadOnlyList<ObsAudioSource> AudioSources)
{
    public static ObsStatus Disconnected(string detail) => new(false, detail, RecordState.Stopped, false, null, []);
}

/// <summary>An audio-capable input that is live in the current program scene (or a global audio device).</summary>
public sealed record ObsAudioSource(string Name, bool Muted);

/// <summary>Loudest channel of an input, in dBFS (negative infinity for silence).</summary>
public readonly record struct AudioLevel(double PeakDb, double MagnitudeDb)
{
    public static double ToDb(double multiplier) => multiplier > 0 ? 20 * Math.Log10(multiplier) : double.NegativeInfinity;
}
