using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ExactFrame.CommandPalette;

[Guid("C34C60F2-D43F-4F0B-B0BC-78D28D95CB2C")]
public sealed partial class ExactFrameExtension(ManualResetEvent stopped) : IExtension, IDisposable
{
    private readonly ExactFrameProvider _provider = new();
    public object? GetProvider(ProviderType providerType) => providerType == ProviderType.Commands ? _provider : null;
    public void Dispose() => stopped.Set();
}

internal sealed partial class ExactFrameProvider : CommandProvider
{
    public ExactFrameProvider()
    {
        DisplayName = "ExactFrame";
        Icon = IconHelpers.FromRelativePath("Assets/StoreLogo.png");
    }

    public override ICommandItem[] TopLevelCommands() => [new CommandItem(new ExactFramePage())
    {
        Title = "ExactFrame", Subtitle = "Profiles, outlines and recording setup"
    }];
}
