namespace ExactFrame.Core.Models;

/// <summary>The measured capture size last applied to an app in Resize window mode.</summary>
public sealed record RememberedAppSize(string ProcessName, int Width, int Height, WindowArea Area);
