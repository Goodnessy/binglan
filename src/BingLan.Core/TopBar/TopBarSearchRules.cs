namespace BingLan.Core.TopBar;

public enum TopBarSearchItemKind
{
    App,
    Folder,
    File
}

/// <summary>
/// One thing the top bar's search can open: a Start-menu program or a desktop launcher
/// (<see cref="TopBarSearchItemKind.App"/>), a desktop folder, or a desktop file. Pure
/// data — the app builds it from its own sources and the rules score it.
/// </summary>
public sealed record TopBarSearchCandidate(
    TopBarSearchItemKind Kind,
    string DisplayName,
    string FullFileName,
    string Path);

/// <summary>A parsed global hotkey: Win32 modifier flags plus a virtual-key code.</summary>
public sealed record TopBarHotkeyBinding(uint Modifiers, int VirtualKey);

/// <summary>
/// Matching and ranking for the top bar's search, plus the hotkey text format the
/// settings persist. Everything here is pure and thread-safe: the window snapshots
/// candidates, scores them off the UI thread and marshals the result back.
/// </summary>
public static class TopBarSearchRules
{
    /// <summary>Results beyond this many matches add scroll without adding signal.</summary>
    public const int MaxResults = 24;

    // Win32 RegisterHotKey modifier flags; the letters are what the settings file stores.
    public const uint ModifierAlt = 0x0001;
    public const uint ModifierControl = 0x0002;
    public const uint ModifierShift = 0x0004;
    public const uint ModifierWindows = 0x0008;

    private const int ScoreExact = 4;
    private static readonly char[] QuerySeparators = [' ', '　', '\t'];

    /// <summary>
    /// Scores candidates against the whitespace-separated query. Every token must hit
    /// (name, pinyin initials or full file name); the sum of token scores orders the
    /// result, apps lead files by a fixed boost, and equal scores fall back to kind
    /// then name so the list never reorders between identical searches.
    /// </summary>
    public static IReadOnlyList<TopBarSearchCandidate> Search(
        IEnumerable<TopBarSearchCandidate> candidates,
        string query)
    {
        var tokens = query.Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .ToArray();
        if (tokens.Length == 0)
        {
            return [];
        }

        var scored = new List<(int Score, TopBarSearchCandidate Candidate)>();
        foreach (var candidate in candidates)
        {
            var total = 0;
            foreach (var token in tokens)
            {
                var score = TokenScore(candidate, token);
                if (score == 0)
                {
                    total = 0;
                    break;
                }
                total += score;
            }
            if (total > 0)
            {
                // The boost sits between two match qualities: a name-prefix app still
                // leads a merely-contained file, while an exact file match stays ahead
                // of an app that only contains the query.
                if (candidate.Kind == TopBarSearchItemKind.App)
                {
                    total += 2;
                }
                scored.Add((total, candidate));
            }
        }

        return scored
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Candidate.Kind)
            .ThenBy(entry => entry.Candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(entry => entry.Candidate)
            .ToArray();
    }

    private static int TokenScore(TopBarSearchCandidate candidate, string token)
    {
        var name = candidate.DisplayName.ToLowerInvariant();
        // The full file name carries the extension, so its match always scores one
        // step below the same hit on the display name ("微信" over "微信.lnk").
        var fileName = candidate.FullFileName.ToLowerInvariant();
        var initials = PinyinInitials(candidate.DisplayName);
        return new[]
        {
            Quality(name, token, 0),
            Quality(initials, token, 0),
            Quality(fileName, token, -1)
        }.Max();
    }

    private static int Quality(string key, string token, int offset) => key.Length == 0
        ? 0
        : key.Equals(token, StringComparison.Ordinal)
            ? ScoreExact + offset
            : key.StartsWith(token, StringComparison.Ordinal)
                ? ScoreExact - 1 + offset
                : key.Contains(token, StringComparison.Ordinal)
                    ? ScoreExact - 2 + offset
                    : 0;

    /// <summary>
    /// The pinyin initial letters of the text's Chinese characters, with ASCII letters
    /// and digits kept as-is ("QQ音乐" -> "qqyy"). Everything else is skipped, so the
    /// initials read as one contiguous string that prefix and substring queries match.
    /// </summary>
    public static string PinyinInitials(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character >= PinyinInitialsTable.FirstCodePoint
                && character <= PinyinInitialsTable.LastCodePoint)
            {
                var letter = PinyinInitialsTable.Letters[
                    character - PinyinInitialsTable.FirstCodePoint];
                if (letter != '-')
                {
                    builder.Append(letter);
                }
            }
            else if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Parses the persisted hotkey text ("Win+Shift+F"). At least one modifier and
    /// exactly one plain key (letter, digit, F1–F24 or Space) is required — a bare key
    /// would swallow normal typing everywhere. Anything else reads as null.
    /// </summary>
    public static TopBarHotkeyBinding? ParseHotkey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        uint modifiers = 0;
        var hasKey = false;
        var virtualKey = 0;
        foreach (var rawToken in text.Split('+'))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierControl;
                    continue;
                case "alt":
                    modifiers |= ModifierAlt;
                    continue;
                case "shift":
                    modifiers |= ModifierShift;
                    continue;
                case "win" or "windows":
                    modifiers |= ModifierWindows;
                    continue;
            }

            if (hasKey || VirtualKeyFromKeyName(token) is not { } key)
            {
                return null;
            }
            hasKey = true;
            virtualKey = key;
        }

        return modifiers != 0 && hasKey ? new TopBarHotkeyBinding(modifiers, virtualKey) : null;
    }

    public static bool IsValidHotkey(string? text) => ParseHotkey(text) is not null;

    /// <summary>The canonical text form: modifiers in a fixed order, then the key.</summary>
    public static string FormatHotkey(TopBarHotkeyBinding binding)
    {
        var parts = new List<string>(5);
        if ((binding.Modifiers & ModifierControl) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((binding.Modifiers & ModifierAlt) != 0)
        {
            parts.Add("Alt");
        }
        if ((binding.Modifiers & ModifierShift) != 0)
        {
            parts.Add("Shift");
        }
        if ((binding.Modifiers & ModifierWindows) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(KeyNameFromVirtualKey(binding.VirtualKey) ?? "??");
        return string.Join("+", parts);
    }

    /// <summary>Letters, digits, F1–F24 and Space map straight to their virtual keys.</summary>
    public static int? VirtualKeyFromKeyName(string token) => token.ToUpperInvariant() switch
    {
        var letter when letter.Length == 1 && letter[0] is >= 'A' and <= 'Z' => letter[0],
        var digit when digit.Length == 1 && digit[0] is >= '0' and <= '9' => digit[0],
        "SPACE" or "SPACEBAR" => 0x20,
        var function when function.Length > 1
            && function[0] == 'F'
            && int.TryParse(function[1..], out var number)
            && number is >= 1 and <= 24 => 0x70 + number - 1,
        _ => null
    };

    private static string? KeyNameFromVirtualKey(int virtualKey) => virtualKey switch
    {
        0x20 => "Space",
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x70 + 1),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        _ => null
    };
}
