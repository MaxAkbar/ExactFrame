using System.Globalization;
using System.Text;

namespace ExactFrame.Core.Integration;

public enum FrameAction { ShowOutline, HideOutline, ToggleOutline, ToggleClickThrough, CopyBounds, CaptureSnapStudio, OpenObs, OpenPowerToys }

/// <summary>The same commands are used by shortcuts, Workspaces and the optional palette companion.</summary>
public sealed record LaunchCommand(string? Profile, IReadOnlyList<FrameAction> Actions, bool Background, int DelayMilliseconds)
{
    public static LaunchCommand Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 32 || arguments.Sum(argument => argument.Length) > 8192 || arguments.Any(argument => argument.Any(char.IsControl)))
            throw new ArgumentException("The ExactFrame arguments are too long or contain unsupported characters.");
        string? profile = null;
        bool background = false;
        int delay = 0;
        var actions = new List<FrameAction>();
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i].ToLowerInvariant();
            switch (argument)
            {
                case "--background": background = true; break;
                case "--profile":
                    if (profile is not null || ++i >= arguments.Count || string.IsNullOrWhiteSpace(arguments[i]) ||
                        arguments[i].StartsWith("--", StringComparison.Ordinal) || arguments[i].Length > 256)
                        throw new ArgumentException("Use --profile followed by one saved profile name or ID.");
                    profile = arguments[i];
                    break;
                case "--delay-ms":
                    if (++i >= arguments.Count || !int.TryParse(arguments[i], NumberStyles.None, CultureInfo.InvariantCulture, out delay) || delay > 30000)
                        throw new ArgumentException("Use --delay-ms with a delay from 0 to 30000 milliseconds.");
                    break;
                case "--show-outline": actions.Add(FrameAction.ShowOutline); break;
                case "--hide-outline": actions.Add(FrameAction.HideOutline); break;
                case "--toggle-outline": actions.Add(FrameAction.ToggleOutline); break;
                case "--toggle-click-through": actions.Add(FrameAction.ToggleClickThrough); break;
                case "--copy-bounds": actions.Add(FrameAction.CopyBounds); break;
                case "--capture-snapstudio": actions.Add(FrameAction.CaptureSnapStudio); break;
                case "--obs": actions.Add(FrameAction.OpenObs); break;
                case "--powertoys": actions.Add(FrameAction.OpenPowerToys); break;
                default: throw new ArgumentException($"Unknown ExactFrame argument: {arguments[i]}");
            }
        }
        return new(profile, actions, background, delay);
    }

    /// <summary>Escapes one argument using Windows' command-line quoting rules, including imported profile IDs.</summary>
    public static string QuoteArgument(string value)
    {
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            text.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            text.Append(character);
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
