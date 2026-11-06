using System.Text;

namespace Budditec.Overlay.Core.Chat.Twitch;

/// <summary>
/// One IRCv3 line: "@tags :prefix COMMAND params :trailing".
/// </summary>
public sealed record IrcMessage(
    IReadOnlyDictionary<string, string> Tags,
    string Prefix,
    string Command,
    IReadOnlyList<string> Parameters)
{
    /// <summary>The last parameter (the message text for PRIVMSG).</summary>
    public string Trailing => Parameters.Count > 0 ? Parameters[^1] : "";

    public string Tag(string key) => Tags.TryGetValue(key, out var value) ? value : "";

    /// <summary>Nick part of the prefix ("nick!user@host").</summary>
    public string Nick => Prefix.Split('!', 2)[0];

    public static IrcMessage? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var span = line.AsSpan().TrimEnd("\r\n");

        var tags = new Dictionary<string, string>();
        if (span.StartsWith("@"))
        {
            var end = span.IndexOf(' ');
            if (end < 0) return null;
            foreach (var tag in span[1..end].ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = tag.IndexOf('=');
                if (eq < 0) tags[tag] = "";
                else tags[tag[..eq]] = UnescapeTagValue(tag[(eq + 1)..]);
            }
            span = span[(end + 1)..].TrimStart(' ');
        }

        var prefix = "";
        if (span.StartsWith(":"))
        {
            var end = span.IndexOf(' ');
            if (end < 0) return null;
            prefix = span[1..end].ToString();
            span = span[(end + 1)..].TrimStart(' ');
        }

        var commandEnd = span.IndexOf(' ');
        var command = (commandEnd < 0 ? span : span[..commandEnd]).ToString();
        if (command.Length == 0) return null;
        span = commandEnd < 0 ? [] : span[(commandEnd + 1)..];

        var parameters = new List<string>();
        while (!span.IsEmpty)
        {
            if (span[0] == ':')
            {
                parameters.Add(span[1..].ToString());
                break;
            }
            var end = span.IndexOf(' ');
            if (end < 0)
            {
                parameters.Add(span.ToString());
                break;
            }
            if (end > 0) parameters.Add(span[..end].ToString());
            span = span[(end + 1)..];
        }

        return new IrcMessage(tags, prefix, command, parameters);
    }

    private static string UnescapeTagValue(string value)
    {
        if (!value.Contains('\\')) return value;
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                if (value[i] != '\\') sb.Append(value[i]);
                continue;
            }
            sb.Append(value[++i] switch
            {
                's' => ' ',
                ':' => ';',
                'r' => '\r',
                'n' => '\n',
                var c => c,
            });
        }
        return sb.ToString();
    }
}
