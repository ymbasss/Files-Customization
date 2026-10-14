# Customization change map

The Files 4.3 source with the **Customization** features added: Sidebar, Column details and Shortcut badges.
Settings > Customization turns each feature on or off. A feature that is off runs 4.3's original code.

Compared with the original Files 4.3 source, this copy has:
- **35 added files**: the features' own code, almost all of it in `Customization` folders.
- **26 changed 4.3 files**: mostly one-line hooks that call into the added files, plus this fork's README and AI agent notes.
- **No deleted 4.3 files.**

The copy has no build output (`bin`, `obj`, `.vs`, `artifacts`, `packages`, the dialog and launcher projects' `x64`/`Debug`/`Release`
folders, `Assets\FilesOpenDialog` binaries or MIDL `*_i.c`/`*_i.h` files). Visual Studio recreates all of them when you restore and build.

Paths are relative to `src\`. User documentation for every feature is in `FEATURES.md`.

## Saved settings

- `customization.json` in the app's settings folder. It replaces `custom-features.json` from earlier versions, which is moved
  automatically the first time the app runs.
- Keys: `SidebarCustomization`, `CustomizationPreferences`, `ColumnDefaultFolders`, `ColumnOwnWidthFolders`, `SidebarCollapsedGroups`, `LinkedAppSettings`.

## Added files

### Settings page (`Files.App\Customization\Settings\`)
| File | Purpose |
|---|---|
| `CustomizationSettingsPage.xaml` / `.xaml.cs` | Settings > Customization: feature switches, Column details options, Shortcut badges options, and the sidebar panel (each Settings page owns its panel). |
| `CustomizationSettingsStore.cs` | Reads and writes `customization.json`, moves `custom-features.json` to it, and keeps the app's linked settings while a feature is on. |

### Models (`Files.App\Customization\Models\`)
| File | Purpose |
|---|---|
| `CustomizationPreferences.cs` | All feature options: parsing, saving, and the effective values when a feature is off. |
| `SidebarCustomizationState.cs` | Saved per-item sidebar entries (label, icon, size, order, group, hidden, expansion). |
| `SidebarCustomizationStyle.cs` | Shared appearance and visibility rules, including the three-state Show icons and Show expansion arrows options. |
| `SidebarPreviewHierarchy.cs` | Which rows the panel preview shows, based on which rows are collapsed. |
| `CustomizationChangeHistory.cs` | Undo and Redo history, with a memory limit. |
| `CustomizationColumnOrder.cs` | Saved column order. |
| `ColumnWidthFitter.cs` | Column width calculations for fitting to the window and fitting to content. |
| `CustomizationShortcutArrowPolicy.cs` | Decides where shortcut badges are hidden. |

### Services (`Files.App\Customization\Services\`)
| File | Purpose |
|---|---|
| `CustomizationShortcutArrow.cs` | Attached properties that hide shortcut badges in layouts and search suggestions. |
| `SettingsValueEditing.cs` | Enter confirms and Escape cancels text and number edits; clicking elsewhere finishes the edit. |

### Column details (`Files.App\Customization\Columns\`)
| File | Purpose |
|---|---|
| `DetailsLayoutPage.Customization.cs` | Column sizing, fitting, saved per-folder layouts and folders that keep their own widths. |
| `DetailsLayoutPage.ColumnMenu.cs` | The column heading's context menu commands. |
| `DetailsLayoutPage.ColumnOrder.cs` | Reorder Columns. |
| `ColumnMenuLayout.cs`, `ColumnMenuResources.xaml` | Menu layout and styles that match the app's own menus. |
| `PersistentColumnMenuItem.cs` | Menu items that stay open when clicked. |

### Sidebar (`Files.App\Customization\Sidebar\`)
| File | Purpose |
|---|---|
| `SidebarCustomizationPanel.cs` | The sidebar panel: preview, item inspector, defaults, Unify Appearance, Undo/Redo, Reset. |
| `SidebarViewModel.Editor.cs` | What the panel shares across Settings tabs (history, collapsed rows, selection) and its edit actions. |
| `SidebarViewModel.Customization.cs` | Applies saved customization to the live sidebar, and handles groups, clicks, dragging and dropping. |
| `SidebarViewModel.DragCustomization.cs` | Placing and restoring moved items; stable item IDs (volume serial / file ID). |
| `SidebarViewModel.LegacyAppearance.cs` | Legacy look, compact group contents and icons for each display mode. |
| `SidebarViewModel.GroupIcons.cs` | Group and custom icons. |
| `SidebarViewModel.Settings.cs` | Reacting to setting changes; restores the original sidebar when the feature is turned off. |
| `ExpandableSidebarItemBase.Appearance.cs`, `FileTagItem.Appearance.cs`, `WslDistroItem.Appearance.cs` | Appearance members for the sidebar item types (pin indicator, fallback icon, drag key, compact contents). |

### Controls (`Files.App.Controls\Customization\`)
| File | Purpose |
|---|---|
| `SidebarContracts.cs` | Interfaces the sidebar items implement for appearance, reordering and the preview. |
| `SidebarItem.Appearance.cs` | Draws a row's customized look and restores the original look. |
| `SidebarItem.LegacyAppearance.cs` | Legacy layout: the up/down arrow at the end of the row, headings, insets. |
| `SidebarItem.Preview.cs` | Row behavior inside the panel preview (click, drag, expansion). |
| `SidebarView.Preview.cs` | Turns a SidebarView into the panel's preview. |
| `FlatSidebarItem.Appearance.cs` | Legacy section insets and preview row state. |

### Release workflow (`..\.github\workflows\`)
| File | Purpose |
|---|---|
| `release.yml` | Builds, signs (self-signed certificate from repository secrets) and drafts a GitHub Release with one install zip. Run by hand from Actions. |

## Changed 4.3 files

| File | Change |
|---|---|
| `..\.github\README.md` (repository root) | Replaced with this fork's README. |
| `..\AGENTS.md` (repository root) | Adds a "Customization fork" section with rules for AI coding agents. `CLAUDE.md` links to this file. |
| `Files.App\Data\Contracts\IAppearanceSettingsService.cs` | Adds `SidebarCustomization` and `CustomizationPreferences` settings. |
| `Files.App\Data\Enums\SettingsPageKind.cs` | Adds `CustomizationPage`. |
| `Files.App\Data\Items\DriveItem.cs` | Sidebar shows the custom label. |
| `Files.App\Data\Items\ExpandableSidebarItemBase.cs` | Implements the appearance interfaces; honours "Always expanded/collapsed". |
| `Files.App\Data\Items\FileTagItem.cs` | `Text` returns the custom label. |
| `Files.App\Data\Items\LocationItem.cs` | `ChildItems` notifies only when customized; `IconElement` is virtual; pin indicator; custom label. |
| `Files.App\Strings\en-US\Resources.resw` | 97 strings added (none changed). |
| `Files.App\UserControls\NavigationToolbar.xaml` | Shortcut badge hook in search suggestions. |
| `Files.App\Views\Layouts\ColumnLayoutPage.xaml`, `DetailsLayoutPage.xaml`, `GridLayoutPage.xaml` | Shortcut badge hook on each `ShortcutGlyphElement`. |
| `Files.App\Views\Layouts\DetailsLayoutPage.xaml.cs` | One-line calls into Column details (navigation, resizing, fitting, menu state). |
| `Files.App\Views\MainPage.xaml` | Settings footer visibility follows the sidebar customization. |
| `Files.App\Views\Settings\SettingsPage.xaml` / `.xaml.cs` | Customization item in the Settings footer; its page type; scroll bar. |
| `Files.App\ViewModels\Settings\SettingsPageViewModel.cs` | Customization navigation item. |
| `Files.App\ViewModels\Settings\SettingsSearchIndexer.cs` | Customization page in Settings search. |
| `Files.App\ViewModels\UserControls\SidebarViewModel.cs` | Hooks for sections, item placement, removed items, clicks, drag and drop; switched-off sections can be kept; "Reorder sidebar items" is hidden while Sidebar is on. |
| `Files.App\ViewModels\UserControls\SidebarViewModel.FlatTree.cs` | Hidden rows, compact group contents, Legacy insets. |
| `Files.App\ViewModels\UserControls\SidebarViewModel.TabExpansion.cs` | Applies customization to new items; follows a new children collection; places added items. |
| `Files.App.Controls\Sidebar\FlatSidebarItem.cs` | Section gap includes the Legacy insets. |
| `Files.App.Controls\Sidebar\SidebarItem.cs` | Appearance updates, locked expansion, preview and rearrange dragging, compact group flyout. |
| `Files.App.Controls\Sidebar\SidebarItem.Properties.cs` | Expansion and indent updates for customized items; preview and locked expansion. |
| `Files.App.Controls\Sidebar\SidebarView.xaml.cs` | Reorder data format; preview selection rules; the preview's pane fills its space. |

To see the exact changes, compare a file with the same file in the original Files 4.3 source.
