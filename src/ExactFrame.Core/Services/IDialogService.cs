using ExactFrame.Core.Models;

namespace ExactFrame.Core.Services;

/// <summary>Prompts that need the user's input.</summary>
public interface IDialogService
{
    /// <summary>Asks for a profile name. Returns <c>null</c> when canceled.</summary>
    Task<string?> PromptProfileNameAsync(string suggestion);

    /// <summary>Records a new shortcut. Returns <c>null</c> when canceled.</summary>
    Task<HotkeyGesture?> RecordHotkeyAsync(string actionName, HotkeyGesture current);

    /// <summary>Asks the user to confirm a destructive action.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);
}
