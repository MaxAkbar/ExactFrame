namespace ExactFrame.Core.Models;

public enum WindowTitleMatchMode { Contains, Exact }

/// <summary>Optional AND conditions in addition to the application's process name.</summary>
public sealed record WindowMatchCriteria
{
    public string? ExecutablePath { get; init; }
    public string? Title { get; init; }
    public WindowTitleMatchMode TitleMode { get; init; }
    public string? ClassName { get; init; }
}
