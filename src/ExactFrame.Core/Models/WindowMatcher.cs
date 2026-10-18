namespace ExactFrame.Core.Models;

/// <summary>Literal, case-insensitive matching. Missing metadata never satisfies a requested condition.</summary>
public static class WindowMatcher
{
    public static WindowMatchCriteria? Normalize(WindowMatchCriteria? criteria)
    {
        if (criteria is null) return null;
        if (ValidationError(criteria) is not null) return criteria;
        var result = criteria with
        {
            ExecutablePath = Clean(criteria.ExecutablePath)?.Replace('/', '\\'),
            Title = string.IsNullOrWhiteSpace(criteria.Title) ? null : criteria.Title, ClassName = Clean(criteria.ClassName)
        };
        if (result.Title is null) result = result with { TitleMode = WindowTitleMatchMode.Contains };
        return result.ExecutablePath is null && result.Title is null && result.ClassName is null ? null : result;
    }

    public static string? ValidationError(WindowMatchCriteria? criteria)
    {
        if (criteria is null) return null;
        if (!Enum.IsDefined(criteria.TitleMode)) return "Choose Contains or Exact for the window title.";
        foreach (var value in new[] { criteria.ExecutablePath, criteria.Title, criteria.ClassName })
            if (value is { Length: > 2048 } || value?.Any(char.IsControl) == true)
                return "Matching fields must use 2048 characters or fewer and contain no control characters.";
        if (Clean(criteria.ExecutablePath) is { } path)
        {
            path = path.Replace('/', '\\');
            bool drive = path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
            bool unc = path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
                path[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 3;
            if ((!drive && !unc) || path.EndsWith('\\') || path[(drive ? 2 : 0)..].Contains(':') ||
                path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0 ||
                path.Split('\\').Any(part => part is "." or ".."))
                return "Enter the full executable path, such as C:\\Apps\\Editor\\Editor.exe. Wildcards and relative paths aren't supported.";
        }
        return null;
    }

    public static bool Matches(WindowInfo window, string? processName, WindowMatchCriteria? criteria)
    {
        if (string.IsNullOrWhiteSpace(processName) || ValidationError(criteria) is not null ||
            !string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase)) return false;
        criteria = Normalize(criteria);
        if (criteria is null) return true;
        return (criteria.ExecutablePath is null || string.Equals(criteria.ExecutablePath,
                    window.ExecutablePath?.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)) &&
            (criteria.ClassName is null || string.Equals(criteria.ClassName, window.ClassName, StringComparison.OrdinalIgnoreCase)) &&
            (criteria.Title is null || (criteria.TitleMode == WindowTitleMatchMode.Exact
                ? string.Equals(window.Title, criteria.Title, StringComparison.OrdinalIgnoreCase)
                : window.Title.Contains(criteria.Title, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool Equivalent(WindowMatchCriteria? left, WindowMatchCriteria? right)
    {
        left = Normalize(left); right = Normalize(right);
        return left == right || left is not null && right is not null && left.TitleMode == right.TitleMode &&
            string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Title, right.Title, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.ClassName, right.ClassName, StringComparison.OrdinalIgnoreCase);
    }

    public static int Specificity(WindowMatchCriteria? criteria) => criteria is null ? 0 :
        (Clean(criteria.ExecutablePath) is null ? 0 : 1) + (Clean(criteria.ClassName) is null ? 0 : 1) +
        (Clean(criteria.Title) is null ? 0 : criteria.TitleMode == WindowTitleMatchMode.Exact ? 2 : 1);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
