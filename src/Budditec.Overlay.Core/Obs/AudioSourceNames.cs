namespace Budditec.Overlay.Core.Obs;

public enum LevelBand
{
    Quiet,
    Green,
    Yellow,
    Red,
}

public static class AudioSourceNames
{
    /// <summary>
    /// Up to three characters: initials for several words ("Desktop Audio" is "DA", "Mic 2" is "M2"), otherwise
    /// the start of the name ("Discord" is "Dis").
    /// </summary>
    public static string Abbreviate(string name)
    {
        var words = Words(name);
        if (words.Count == 0) return name.Length <= 3 ? name : name[..3];
        if (words.Count == 1) return char.ToUpperInvariant(words[0][0]) + words[0][1..Math.Min(3, words[0].Length)];
        var initials = string.Concat(words.Select(w => w.All(char.IsDigit) ? w : char.ToUpperInvariant(w[0]).ToString()));
        return initials.Length <= 3 ? initials : initials[..3];
    }

    /// <summary>OBS's mixer colours: green up to -20 dB, yellow up to -9 dB, red above.</summary>
    public static LevelBand Band(double peakDb) => peakDb switch
    {
        >= -9 => LevelBand.Red,
        >= -20 => LevelBand.Yellow,
        _ => LevelBand.Green,
    };

    private static List<string> Words(string s)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= s.Length; i++)
        {
            var separator = i == s.Length || !char.IsLetterOrDigit(s[i]);
            if (!separator && start < 0) start = i;
            else if (separator && start >= 0)
            {
                words.Add(s[start..i]);
                start = -1;
            }
        }
        return words;
    }
}
