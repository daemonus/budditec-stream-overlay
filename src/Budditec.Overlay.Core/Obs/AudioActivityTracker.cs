namespace Budditec.Overlay.Core.Obs;

/// <summary>
/// Turns audio levels into an on/off "active" state with a hold time, so speech doesn't flicker.
/// </summary>
public sealed class AudioActivityTracker
{
    private readonly Dictionary<string, DateTime> _lastAboveThreshold = new();

    public double ThresholdDb { get; set; } = -45;
    public TimeSpan Hold { get; set; } = TimeSpan.FromMilliseconds(250);

    public void Update(IReadOnlyDictionary<string, AudioLevel> levels, DateTime now)
    {
        foreach (var (name, level) in levels)
            if (level.PeakDb >= ThresholdDb)
                _lastAboveThreshold[name] = now;
    }

    public bool IsActive(string name, DateTime now) =>
        _lastAboveThreshold.TryGetValue(name, out var last) && now - last <= Hold;
}
