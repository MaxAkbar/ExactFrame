using ExactFrame.Core.Geometry;
using ExactFrame.Core.Models;
using ExactFrame.Core.Settings;

namespace ExactFrame.Core.Tests;

public sealed record SavedNote(string Text, int Priority, string[] Tags);

public sealed class CSharpDbAppStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "exactframe-db-tests-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_folder, "exactframe.db");

    private string LegacyPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public async Task Settings_round_trip_through_the_database_across_store_instances()
    {
        var settings = new AppSettings
        {
            HideFromRecorders = false,
            Style = new OutlineStyle { Color = OutlineColor.Amber, ShowThirds = true },
            Profiles =
            [
                new FrameProfile
                {
                    Id = "recording", Name = "Recording", Width = 2560, Height = 1440,
                    Anchor = FrameAnchor.TopLeft,
                    NestedFrames = [new NestedFrame { Name = "Shorts", AspectWidth = 9, AspectHeight = 16 }]
                }
            ],
            ActiveProfileId = "recording",
            ToggleOutlineHotkey = HotkeyGesture.CtrlAlt(0x50),
            RememberedAppSizes = [new RememberedAppSize("devenv", 1600, 900, WindowArea.VisibleFrame)],
            LastFrame = new FrameProfile { Id = "last", Mode = FrameMode.Resize, TargetProcessName = "devenv" }
        };

        await using (var store = new CSharpDbAppStore(DatabasePath))
        {
            Assert.Equal(3, store.Load().Profiles.Count);
            store.Save(settings);
        }

        await using (var reopened = new CSharpDbAppStore(DatabasePath))
        {
            var loaded = reopened.Load();
            Assert.False(loaded.HideFromRecorders);
            Assert.Equal(settings.Style, loaded.Style);
            Assert.Equal(settings.Profiles, loaded.Profiles);
            Assert.Equal("recording", loaded.ActiveProfileId);
            Assert.Equal(settings.ToggleOutlineHotkey, loaded.ToggleOutlineHotkey);
            Assert.Equal(settings.RememberedAppSizes, loaded.RememberedAppSizes);
            Assert.Equal(settings.LastFrame, loaded.LastFrame);
        }

        Assert.True(File.Exists(DatabasePath));
    }

    [Fact]
    public async Task Legacy_json_is_imported_once_and_retained_as_a_backup()
    {
        var legacy = new AppSettings
        {
            HideFromRecorders = false,
            RememberedAppSizes = [new RememberedAppSize("notepad", 1200, 800, WindowArea.Client)]
        };
        new JsonSettingsStore(LegacyPath).Save(legacy);

        await using (var store = new CSharpDbAppStore(DatabasePath, LegacyPath))
        {
            var migrated = store.Load();
            Assert.False(migrated.HideFromRecorders);
            Assert.Equal(legacy.RememberedAppSizes, migrated.RememberedAppSizes);
        }

        new JsonSettingsStore(LegacyPath).Save(new AppSettings());
        await using (var reopened = new CSharpDbAppStore(DatabasePath, LegacyPath))
        {
            var loaded = reopened.Load();
            Assert.False(loaded.HideFromRecorders);
            Assert.Equal(legacy.RememberedAppSizes, loaded.RememberedAppSizes);
        }

        Assert.True(File.Exists(LegacyPath));
    }

    [Fact]
    public async Task Typed_collections_store_other_application_data_in_the_same_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var store = new CSharpDbAppStore(DatabasePath))
        {
            store.Load();
            await store.PutAsync("recording_notes", "first", new SavedNote("intro", 2, ["tutorial", "draft"]), cancellationToken);
            await store.PutAsync("recording_notes", "second", new SavedNote("outro", 1, ["tutorial"]), cancellationToken);
            Assert.True(await store.DeleteAsync<SavedNote>("recording_notes", "second", cancellationToken));
        }

        await using (var reopened = new CSharpDbAppStore(DatabasePath))
        {
            var note = await reopened.GetAsync<SavedNote>("recording_notes", "first", cancellationToken);
            Assert.NotNull(note);
            Assert.Equal("intro", note.Text);
            Assert.Equal(2, note.Priority);
            Assert.Equal(["tutorial", "draft"], note.Tags);
            Assert.Null(await reopened.GetAsync<SavedNote>("recording_notes", "second", cancellationToken));
            Assert.NotNull(reopened.Load());
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
