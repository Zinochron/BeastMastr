using System.Globalization;
using System.Text.RegularExpressions;

namespace BeastMastr.Rules;

/// <summary>
/// Reads the chat lines of a board's end loot, as the recordings have them:
/// "3 bright remnants of resilience have been added to the loot list." then "You obtain 3 bright
/// remnants of resilience." The spoils of a room say "You obtain a fang of fire as loot." and are not
/// the rolled loot, so they are left out.
/// </summary>
public static partial class LootLog
{
    public readonly record struct Line(int Count, string Name);

    /// <summary>An item put on the loot list to be rolled for, or null.</summary>
    public static Line? Added(string text)
    {
        var match = AddedPattern().Match(Clean(text));
        return match.Success ? new Line(Count(match.Groups["n"].Value), match.Groups["name"].Value) : null;
    }

    /// <summary>An item obtained, other than a room's spoils ("… as loot."), or null.</summary>
    public static Line? Obtained(string text)
    {
        var clean = Clean(text);
        if (clean.EndsWith(" as loot.", System.StringComparison.OrdinalIgnoreCase))
            return null;

        var match = ObtainedPattern().Match(clean);
        return match.Success ? new Line(Count(match.Groups["n"].Value), match.Groups["name"].Value) : null;
    }

    /// <summary>"a"/"an" is one; "1,000" is a thousand.</summary>
    private static int Count(string text) =>
        int.TryParse(text.Replace(",", string.Empty).Replace(".", string.Empty), NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out var number)
            ? number
            : 1;

    /// <summary>The text as shown, without the game's private-use glyphs (item link and HQ marks).</summary>
    private static string Clean(string text)
    {
        var chars = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c < '' || c > '')
                chars.Append(c);
        }

        return chars.ToString().Trim();
    }

    [GeneratedRegex(@"^(?:(?<n>[\d,.]+)|an?)\s+(?<name>.+?)\s+(?:has|have) been added to the loot list\.$",
                    RegexOptions.IgnoreCase)]
    private static partial Regex AddedPattern();

    [GeneratedRegex(@"^You obtain (?:(?<n>[\d,.]+)|an?)\s+(?<name>.+?)\.$", RegexOptions.IgnoreCase)]
    private static partial Regex ObtainedPattern();
}
