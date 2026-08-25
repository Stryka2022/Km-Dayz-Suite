using System.Text.RegularExpressions;

namespace Dzl.Core.Remote;

/// <summary>Parses the stable total-player footer returned by BattlEye's <c>players</c> command.</summary>
public static partial class BattlEyePlayerParser
{
    [GeneratedRegex(@"\(\s*(\d+)\s+players?\s+in\s+total\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalPattern();

    public static int? ParseCount(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        var match = TotalPattern().Match(response);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }
}
