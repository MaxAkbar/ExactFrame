namespace ExactFrame.Core.Services;

/// <summary>Whether ExactFrame starts, in the notification area, when the user signs in to Windows.</summary>
public interface IStartupService
{
    /// <summary>True when this copy of ExactFrame is set to start and Windows hasn't turned that off.</summary>
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
