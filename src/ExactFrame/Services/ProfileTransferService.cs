using System.Runtime.InteropServices;
using System.Text;
using ExactFrame.Core.Models;
using ExactFrame.Core.Profiles;
using ExactFrame.Core.Services;
using ExactFrame.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ExactFrame.Services;

/// <summary>Owned file pickers and a review dialog for portable profile packages.</summary>
internal sealed class ProfileTransferService : IProfileTransferService
{
    private nint _window;
    private Func<XamlRoot?>? _root;

    public void Attach(nint window, Func<XamlRoot?> root)
    {
        _window = window;
        _root = root;
    }

    public async Task<string?> ReadImportAsync(CancellationToken cancellation = default)
    {
        EnsureAttached();
        cancellation.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Import profiles"
        };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window);
        var selected = await picker.PickSingleFileAsync();
        cancellation.ThrowIfCancellationRequested();
        if (selected is null) return null;

        await using var input = new FileStream(selected.Path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length > ProfilePackageSerializer.MaxFileBytes)
            throw new InvalidOperationException("The profile package is too large. Choose a file smaller than 1 MiB.");
        // Read one extra byte so a file growing after the length check cannot evade the limit.
        var buffer = new byte[ProfilePackageSerializer.MaxFileBytes + 1];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await input.ReadAsync(buffer.AsMemory(count), cancellation);
            if (read == 0) break;
            count += read;
        }
        if (count > ProfilePackageSerializer.MaxFileBytes)
            throw new InvalidOperationException("The profile package is too large. Choose a file smaller than 1 MiB.");
        try
        {
            string json = new UTF8Encoding(false, true).GetString(buffer, 0, count);
            return json.StartsWith('\uFEFF') ? json[1..] : json;
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException("The profile package must be a UTF-8 JSON file.", ex);
        }
    }

    public async Task<bool> WriteExportAsync(string suggestedName, string json, CancellationToken cancellation = default)
    {
        EnsureAttached();
        cancellation.ThrowIfCancellationRequested();
        byte[] bytes = new UTF8Encoding(false).GetBytes(json);
        if (bytes.Length > ProfilePackageSerializer.MaxFileBytes)
            throw new InvalidOperationException("This profile package is larger than the 1 MiB limit.");
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SuggestedPackageName(suggestedName),
            DefaultFileExtension = ".json",
            CommitButtonText = "Export profiles"
        };
        picker.FileTypeChoices.Add("ExactFrame profile package", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window);
        var selected = await picker.PickSaveFileAsync();
        cancellation.ThrowIfCancellationRequested();
        if (selected is null) return false;

        string destination = selected.Path;
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("Choose a destination folder for the profile package.");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, cancellation);
                await output.FlushAsync(cancellation);
                output.Flush(flushToDisk: true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { /* Do not hide a write failure with a cleanup failure. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReviewImportAsync(ProfilePackage package,
        IReadOnlyList<DisplayInfo> displays, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(displays);
        cancellation.ThrowIfCancellationRequested();
        var content = new StackPanel { Spacing = 12, MinWidth = 380, MaxWidth = 540 };
        int count = package.Profiles.Count;
        content.Children.Add(Caption($"{count} {(count == 1 ? "profile" : "profiles")}: " +
            string.Join(", ", package.Profiles.Take(12).Select(entry => entry.Profile.Name)) +
            (count > 12 ? $", and {count - 12} more" : string.Empty)));
        content.Children.Add(Caption("Imports new copies. Existing profiles and shortcuts stay unchanged. " +
            "Imported profiles are added without being applied."));
        int appearanceCount = package.Profiles.Count(entry => entry.Profile.Appearance is not null);
        if (appearanceCount > 0)
            content.Children.Add(Caption($"{appearanceCount} {(appearanceCount == 1 ? "profile includes" : "profiles include")} " +
                "outline appearance, restored when that profile is applied."));
        int matchCount = package.Profiles.Count(entry => entry.Profile.TargetMatch is not null);
        if (matchCount > 0)
            content.Children.Add(Caption($"{matchCount} profiles include window matching conditions. Executable paths, titles and classes " +
                "are preserved; review them under Match this application before resizing on this PC."));
        content.Children.Add(new TextBlock { Text = "Choose a display for each saved display", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(Caption("Confirm the destination; display numbers can differ between PCs. Sizes remain in physical pixels. " +
            "Positions move to the chosen display and stay inside it when possible. Sizes are never reduced."));

        var choices = new Dictionary<string, ComboBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in package.Profiles.GroupBy(ProfilePackageSerializer.DisplayKey, StringComparer.OrdinalIgnoreCase))
        {
            var entry = source.First();
            var snapshot = entry.SourceDisplay;
            string description = snapshot is null ? source.Key == "$primary" ? "Saved primary display" : "Saved display"
                : $"Display {snapshot.Number} · {snapshot.Bounds.Width} × {snapshot.Bounds.Height} · {snapshot.ScalePercent}%";
            var destination = new ComboBox { Header = description, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(destination, $"Destination for {description}");
            foreach (var display in displays)
            {
                destination.Items.Add(new ComboBoxItem
                {
                    Content = $"{display.Title} · {display.Bounds.Width} × {display.Bounds.Height} · {display.ScalePercent}%" +
                        (display.IsPrimary ? " · Primary" : string.Empty),
                    Tag = display.DeviceName
                });
            }
            var matching = displays.FirstOrDefault(display => string.Equals(display.DeviceName, source.Key, StringComparison.OrdinalIgnoreCase));
            var selected = matching ?? displays.FirstOrDefault(display => display.IsPrimary) ?? displays.FirstOrDefault();
            if (selected is not null)
                destination.SelectedIndex = displays.ToList().FindIndex(display => display.DeviceName == selected.DeviceName);
            content.Children.Add(destination);
            if (snapshot is null)
                content.Children.Add(Caption("No display geometry was saved. Confirm a destination; custom positions will be centered."));
            else if (matching is null)
                content.Children.Add(Caption("The saved display is not connected. Confirm where these profiles should go."));
            choices.Add(source.Key, destination);
        }

        if (displays.Count == 0)
            content.Children.Add(Caption("No display is available. Connect a display before importing these profiles."));
        var dialog = new ContentDialog
        {
            Title = "Import profiles",
            Content = new ScrollViewer { Content = content, MaxHeight = 540, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Import copies",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = displays.Count > 0 && choices.Count > 0,
            XamlRoot = _root?.Invoke(),
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
        };
        if (dialog.XamlRoot is null) return null;
        AppThemeHelper.MatchOwner(dialog);
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        catch (COMException) { return null; }
        cancellation.ThrowIfCancellationRequested();
        if (result != ContentDialogResult.Primary) return null;
        return choices.ToDictionary(pair => pair.Key,
            pair => (string)((ComboBoxItem)pair.Value.SelectedItem).Tag, StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureAttached()
    {
        if (_window == 0) throw new InvalidOperationException("The ExactFrame window is not available for file selection.");
    }

    private static string SafeFileName(string suggestion)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new(suggestion.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        name = name.Trim().TrimEnd('.');
        if (name.Length > 100) name = name[..100];
        return name.Length == 0 ? "ExactFrame profiles" : name;
    }

    private static string SuggestedPackageName(string suggestion)
    {
        string name = SafeFileName(suggestion);
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        return name.EndsWith(".exactframe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exactframe";
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Format.ThemeBrush("MutedBrush")
    };
}
