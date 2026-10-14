# Files Customization

This is an unofficial fork of [Files 4.3](https://github.com/files-community/Files) that adds a few more ways to customize the app.

You’ll find everything under **Settings > Customization**. Each feature can be turned on separately, so you can choose what you want to use.

## Features

**Sidebar** — Arrange items in the order you prefer, put them into custom groups, and add separators or extra space. You can change labels, text and icon sizes, visibility, and expansion behavior, or duplicate an item to place another link elsewhere. The editor includes a live preview and a choice between the default layout and a Legacy style inspired by the classic Files sidebar.

**Column details** — Choose your default columns, drag them into order, and decide how they should fit: to their contents, to the window width, or using the app’s default behavior. You can also right-click a column heading to fit or reorder columns in the current view.

**Shortcut badges** — Hide the small arrow on shortcut icons for Desktop items or throughout Files. There are also options for folders inside the Desktop and Search suggestions. These settings affect how badges appear inside Files.

<!-- Suggested screenshots:
1. The sidebar editor with a custom group and an item's settings visible.
2. Default and Legacy sidebar styles side by side.
3. Column details settings, showing the column list and sizing options.
4. The column-heading menu with Reorder columns expanded.
5. The same shortcut items with badges shown and hidden.
-->

See the [feature guide](../FEATURES.md) for all available options.

## Build and run

You’ll need Git and **Visual Studio 2026** with these components installed:

- WinUI application development workload.
- .NET 10 SDK. The repository specifies version 10.0.102 and allows newer compatible SDKs.
- Windows 11 SDK 10.0.26100.0.
- MSVC v145 build tools and C++ ATL for the architecture you’re building.

Clone the customization branch in PowerShell:

```powershell
git clone --branch customization https://github.com/ymbasss/Files-Customization.git
cd Files-Customization
```

Then:

1. Open `Files.slnx` in Visual Studio.
2. Let Visual Studio restore the NuGet packages.
3. In Solution Explorer, right-click **Files.App** and select **Set as Startup Project**.
4. Select **Debug** and your architecture: **x64** for most PCs, or **ARM64** for Windows on Arm.
5. Build the solution, then press **F5** to deploy and run it.

The development app appears as **Files - Dev**. Open **Settings > Customization** to enable the added features.

For more help with your development environment, see the [official build guide](https://files.community/docs/contributing/building-from-source). Packaged builds and installation instructions are available through [Releases](https://github.com/ymbasss/Files-Customization/releases) when published.

## Credits

Files is developed by the [Files Community](https://github.com/files-community). This fork builds on their work and is not affiliated with or endorsed by the Files team.

Please report issues specific to this fork in [this repository](https://github.com/ymbasss/Files-Customization/issues). You can review the changes in the [change map](../CHANGES.md) or [compare this branch with Files 4.3](https://github.com/ymbasss/Files-Customization/compare/v4.3...customization).

The source retains the [MIT](../LICENSE-MIT) and [MPL-2.0](../LICENSE-MPL) license files from Files.
