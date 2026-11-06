# Optional PowerToys integration

ExactFrame works independently of PowerToys. PowerToys is **not required**, is never installed automatically, and is not checked during normal startup. Existing framing, profiles, shortcuts, OBS and SnapStudio behavior remain available without it.

Open **Profiles → PowerToys (optional)** to check the tools installed on this PC. Missing or older utilities disable only their own actions in that dialog. Tool errors stay in the dialog rather than replacing ExactFrame's recording status. Nothing starts when you show an outline or apply an ordinary profile.

## Command Palette

The companion is a **separate, optional development package**. It requires Windows 11, PowerToys Command Palette and an existing Windows development-package setup. The portable ExactFrame app has no Command Palette SDK dependency, package registration or additional runtime requirement. Build the companion from `ExactFrame.PowerToys.slnx`; normal app builds use `ExactFrame.slnx`.

1. Install PowerToys with Command Palette enabled. Download the companion development ZIP for your architecture from the [2.3.0 release](https://github.com/MaxAkbar/ExactFrame/releases/tag/v2.3.0), extract it to a stable folder, then run **Install-Companion.ps1** in PowerShell. The script checks prerequisites and registers the package; it never enables Developer Mode, changes certificate trust or installs PowerToys. Reload Command Palette afterward.
2. In ExactFrame, open **Profiles → PowerToys (optional)** and turn on **Share saved profiles**. Sharing is off by default.
3. Open PowerToys Command Palette and find **ExactFrame**. Search the list for a profile or action.
4. After changing profiles, choose **Refresh profiles** in the companion to reload the list.

Available actions apply a profile and show its outline; show, hide or toggle the current outline; toggle click-through; copy bounds; capture the main frame in SnapStudio; open the OBS preview dialog; and open ExactFrame or its PowerToys setup. OBS changes still require the existing preview/apply workflow. SnapStudio uses the existing application selection and capture validation.

Sharing publishes `%LOCALAPPDATA%\ExactFrame\command-palette.json` containing the ExactFrame executable path and each profile's ID, name, dimensions and mode. It does not publish screenshots, matching window titles, target executable paths, monitor identifiers or database contents. The companion never opens `exactframe.db` and performs no network requests.

Turning sharing off removes the catalog. Commands recheck the catalog when invoked, so previously displayed results cannot launch ExactFrame after a successful opt-out. If the catalog cannot be removed, the dialog reports that; remove the file or restore write access to complete revocation. Moving the portable app requires opening its new copy and refreshing sharing. Missing, invalid, unsupported or moved catalogs show setup guidance in the companion.

### Build the companion

```powershell
dotnet restore ExactFrame.PowerToys.slnx -p:Platform=x64 --locked-mode
dotnet test tests/ExactFrame.PowerToys.Tests/ExactFrame.PowerToys.Tests.csproj -c Release -p:Platform=x64 --no-restore
./scripts/Build-PowerToysCompanion.ps1 -Architecture x64
```

Use `-Architecture ARM64` for an ARM64 package. The packaging script requires the Windows SDK and validates the package with MakeAppx. It produces a versioned **unsigned** MSIX, a development ZIP with registration instructions, and `POWERTOYS_SHA256SUMS.txt` under `artifacts/powertoys/win-x64/` or `win-arm64/`. Building does not install packages, register extensions, generate or trust certificates, or publish anything. Normal MSIX installation requires signing with a trusted certificate whose subject matches the manifest publisher, or Store distribution.

For local development, run the registration script from the generated staging directory or register its `AppxManifest.xml` with Windows' development package registration tools. Then reload Command Palette. Keep that directory in place while registered. Uninstall **ExactFrame for PowerToys** from Windows Installed apps before removing the directory. Building alone does not register an extension.

## PowerToys Workspaces

Workspaces can launch ExactFrame alongside a browser, OBS, an editor and SnapStudio. Apply the desired ExactFrame profile, open the optional PowerToys dialog, and select **Copy launch arguments**. Paste those arguments into ExactFrame's entry in your Workspace.

For example:

```text
--profile "youtube-1080p" --show-outline --background --delay-ms 2000
```

Workspaces arranges applications first; ExactFrame applies the profile after the delay. Increase the delay if other apps take longer to settle. It is a bounded delay, not a notification that Workspaces has finished. Avoid having Workspaces resize the same app after ExactFrame's exact resize. Matching zero or multiple windows retains ExactFrame's existing targeting safeguards and opens ExactFrame with an explanation.

### Launch commands

These commands also work without PowerToys, from ordinary Windows shortcuts or scripts:

| Argument | Action |
| --- | --- |
| `--profile <ID or name>` | Apply one saved profile; exact ID takes precedence, otherwise name matching ignores case |
| `--show-outline` / `--hide-outline` / `--toggle-outline` | Control the current outline |
| `--toggle-click-through` | Toggle click-through |
| `--copy-bounds` | Copy the current frame bounds, including additional guides |
| `--capture-snapstudio` | Capture the main frame using the existing SnapStudio integration |
| `--obs` | Open the OBS connection and preview dialog |
| `--powertoys` | Open optional PowerToys setup |
| `--background` | Keep the main controls hidden unless an action needs input or fails |
| `--delay-ms <0–30000>` | Wait before applying the profile and actions |

The profile is applied before the listed actions. Validation rejects unknown options and malformed input before changing the frame. Launches are serialized and forwarded to the existing instance rather than creating a second tray icon or database owner. Errors appear in ExactFrame; this GUI executable's exit status is not an acknowledgment that a forwarded action completed. Launch ExactFrame with the same Windows permissions as its running copy.

## FancyZones

Open the optional dialog to choose a built-in or saved custom layout. **Apply layout** calls the supported FancyZones command tool. This first version targets **FancyZones' first monitor**, which need not be Windows Display 1. Use **Open layout editor** to configure other monitors. Enable FancyZones in PowerToys before using these actions.

Snap the windows into their zones, then apply the ExactFrame profile for precise content sizing. Layout selection alone does not move every existing window into a zone. Custom layout names and IDs are read only when the optional dialog is opened or refreshed; ExactFrame never rewrites PowerToys JSON settings or imports zone geometry.

To reuse a layout, apply a saved ExactFrame profile, choose a layout in the optional dialog, then select **Link to active profile**. Future applications of that profile request the layout before sizing the frame or target window. Links are machine-local and are not included in portable profile exports. **Remove link** stops this behavior. Profiles without a link never discover or call PowerToys. If PowerToys is removed, FancyZones is stopped, or a linked layout is deleted, the frame profile still applies and the optional dialog reports the layout issue.

## Awake

Choose 1–240 minutes and select **Start Awake**. ExactFrame requests a standalone timed Awake session with the display on, attached to ExactFrame's process lifetime. Exiting ExactFrame or the timer expiring ends that request. **Stop ExactFrame's session** stops only the process ExactFrame started.

If Awake is already running, ExactFrame leaves it alone and explains how to manage it in PowerToys. It does not enable a disabled PowerToys module or change an existing Awake configuration. The operating system's lock-screen and manual-sleep behavior still applies.

## ZoomIt

Prepare the shot with ExactFrame, then use ZoomIt's configured zoom, drawing or recording shortcuts. For region recording, select the area against ExactFrame's outline, and hide the outline if your capture method includes it. ExactFrame does not inject shortcuts or claim to transfer its rectangle automatically to ZoomIt.

## Verification

Core tests cover default-off behavior, catalog save failures, publication/revocation, malformed input, profile/window ambiguity, independent clipboard/outline commands and optional-tool absence/removal. The separate Windows suite uses the real Command Palette SDK to check profile results, refresh notifications, revoked results and argument construction; it also checks the production PowerToys service with absent and incomplete installations. Windows' own command-line parser verifies escaping of imported profile IDs.

An absent-installation fixture uses an empty directory; it never uninstalls or disables the user's PowerToys. UI checks use `EXACTFRAME_DATA_ROOT` for an isolated ExactFrame database and catalog. This override does not change the single-instance rule. Only one ExactFrame may run in a Windows session.

### Local results on 2026-11-05

- 472 Core tests and 17 optional Windows/SDK tests passed, with no failures or skips. The companion's startup test accepts Windows' additional `-Embedding` argument.
- Release builds of the main app passed for x64 and ARM64 with no warnings or errors. Separate self-contained companion packages passed MakeAppx validation for both architectures.
- Windows UI checks used an isolated database. Normal startup showed the existing framing interface and created no catalog. Opening optional setup detected the installed PowerToys and showed sharing off. A second launch applied Course 1440p to the existing instance and displayed a 2560 × 1440 outline. An unavailable profile showed an explanation and did not run a following hide action. An explicit hide command then hid the outline. A separate cold launch with a linked but unavailable FancyZones layout still showed the correct 2560 × 1440 frame; its layout error appeared only in optional setup. Isolated processes were closed afterward.
- The x64 development companion was temporarily registered on this development-enabled PC. The installed Command Palette 0.13 host activated the corrected companion in 238 ms and loaded its top-level command provider. No real profile-sharing catalog was created, and no capture or profile command was sent to the existing ExactFrame instance. Temporary registration was removed afterward.
- The extracted 2.3.0 x64 portable ZIP started successfully with isolated settings and opened optional setup with sharing off. The extracted companion development ZIP's installation script registered the expected 2.3.0.0 package; its temporary registration was then removed. Release-workflow checksum verification passed against all six local binaries.
- Live changes to FancyZones layouts and Awake sessions were not exercised against the user's desktop. Trusted signing, normal MSIX installation, ARM64 runtime checks and new recorder checks remain unverified. Production frame profiles and recorder settings were preserved.

The separate `Optional PowerToys companion` CI workflow restores and tests the companion and validates unsigned packages when related files change. The release workflow calls it alongside the normal app build and verifies all package checksums before creating a draft. The portable app keeps its existing dependency graph.

References: [Command Palette extensions](https://learn.microsoft.com/en-us/windows/powertoys/command-palette/extensibility-overview), [Workspaces](https://learn.microsoft.com/en-us/windows/powertoys/workspaces), [FancyZones commands](https://learn.microsoft.com/en-us/windows/powertoys/fancyzones#command-line-reference), [Awake commands](https://learn.microsoft.com/en-us/windows/powertoys/awake#command-line-interface-cli), [ZoomIt](https://learn.microsoft.com/en-us/windows/powertoys/zoomit).
