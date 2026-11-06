using System.Text;

namespace Budditec.Overlay.Core.Chat.Emotes;

public static class FragmentBuilder
{
    /// <summary>
    /// Splits plain text on spaces and swaps words that are third-party emotes for emote fragments.
    /// Adjacent text is merged so renderers get as few runs as possible.
    /// </summary>
    public static void AppendText(List<ChatFragment> fragments, string text, IEmoteLookup emotes)
    {
        if (text.Length == 0) return;

        var pending = new StringBuilder();
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf(' ', start);
            if (end < 0) end = text.Length;
            var word = text[start..end];

            if (word.Length > 0 && emotes.Find(word) is { } url)
            {
                Flush();
                fragments.Add(new EmoteFragment(word, url));
            }
            else
            {
                pending.Append(word);
            }

            if (end < text.Length) pending.Append(' ');
            start = end + 1;
        }
        Flush();

        void Flush()
        {
            if (pending.Length == 0) return;
            if (fragments.Count > 0 && fragments[^1] is TextFragment previous)
                fragments[^1] = new TextFragment(previous.Text + pending);
            else
                fragments.Add(new TextFragment(pending.ToString()));
            pending.Clear();
        }
    }
}
