namespace Budditec.Overlay.Core.Chat.Emotes;

/// <summary>Looks up word-based (third-party) emotes such as 7TV, BTTV and FFZ.</summary>
public interface IEmoteLookup
{
    Uri? Find(string code);
}

public sealed class EmptyEmoteLookup : IEmoteLookup
{
    public static EmptyEmoteLookup Instance { get; } = new();

    public Uri? Find(string code) => null;
}
