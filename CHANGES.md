# How this fork changes Files 4.3

A guide for working on the code. What the features do is in `.github/README.md`. Paths are relative to `src\`.

## How it is built

- **Our code is in two folders:** `Files.App\Customization` and `Files.App.Controls\Customization`. Where it extends a Files
  type, it is a `partial` class with the same name (for example `SidebarViewModel.Editor.cs` extends `SidebarViewModel`).
- **Files' own files only get hooks:** usually one line that calls into our code. There are 26 such files; every hook is listed below.
- **A feature that is off runs Files 4.3's code.** Each hook first checks the feature's switch (`SidebarCustomizationEnabled`,
  `ColumnsActive`, `SidebarLook is null`, ...) and otherwise does exactly what 4.3 did.
- **Settings** are in `customization.json` in the app's settings folder, read and written by `CustomizationSettingsStore`. Files'
  `IAppearanceSettingsService` passes two of its keys through: `SidebarCustomization` (the saved sidebar items) and
  `CustomizationPreferences` (every option). Other keys: `ColumnDefaultFolders`, `ColumnOwnWidthFolders`, `SidebarCollapsedGroups`,
  `LinkedAppSettings`.

## Where things are

| To change... | Look in |
|---|---|
| Settings > Customization (switches, Column details and Shortcut badges options) | `Customization\Settings\CustomizationSettingsPage.xaml(.cs)` |
| The sidebar panel (preview, item settings, Unify Appearance, Options, Undo) | `Customization\Sidebar\SidebarCustomizationPanel.cs` |
| What the panel does to the sidebar (rows, edits, Undo snapshots, tags) | `Customization\Sidebar\SidebarViewModel.Editor.cs` |
| How saved settings are applied to the real sidebar | `SidebarViewModel.Customization.cs` (look, labels, sections), `.DragCustomization.cs` (placement, drag, item IDs), `.LegacyAppearance.cs`, `.GroupIcons.cs`, `.Settings.cs` (switching on and off) |
| How a row is drawn | `Files.App.Controls\Customization\SidebarItem.Appearance.cs` (from the `SidebarLook` record in `SidebarContracts.cs`), `SidebarItem.LegacyAppearance.cs` |
| The panel's preview | `Files.App.Controls\Customization\SidebarView.Preview.cs`, `SidebarItem.Preview.cs` |
| Saved formats and rules | `Customization\Models\`: `CustomizationPreferences.cs` (options and their defaults), `SidebarCustomizationState.cs` (one entry per sidebar item), `SidebarCustomizationStyle.cs` (sizes, expansion), `CustomizationChangeHistory.cs` (Undo) |
| Column details | `Customization\Columns\DetailsLayoutPage.Customization.cs` (sizing, fitting, per-folder widths), `.ColumnMenu.cs`, `.ColumnOrder.cs`; `Models\ColumnWidthFitter.cs`, `CustomizationColumnOrder.cs` |
| Shortcut badges | `Customization\Services\CustomizationShortcutArrow.cs`, `Models\CustomizationShortcutArrowPolicy.cs` |
| Enter / Escape / click-away in boxes; colours that follow Files' theme | `Customization\Services\SettingsValueEditing.cs`, `CustomizationThemeBrushes.cs` |
| Text shown to the user | `Files.App\Strings\en-US\Resources.resw`, keys starting with `Customization` or `SidebarEditor` |

## Hooks in Files 4.3's files

### Settings page
| File | Hook |
|---|---|
| `Files.App\Data\Contracts\IAppearanceSettingsService.cs` | Adds `SidebarCustomization` and `CustomizationPreferences`. |
| `Files.App\Data\Enums\SettingsPageKind.cs` | Adds `CustomizationPage`. |
| `Files.App\ViewModels\Settings\SettingsPageViewModel.cs` | `CustomizationNavigationItem`, kept out of the main list; `SettingsScrollBarVisibility` (always shown on Customization). |
| `Files.App\Views\Settings\SettingsPage.xaml` / `.xaml.cs` | The item in the Settings sidebar's footer; the scroll bar binding; `CustomizationPage => typeof(CustomizationSettingsPage)`. |
| `Files.App\ViewModels\Settings\SettingsSearchIndexer.cs` | Adds the page to Settings search. |
| `Files.App\Strings\en-US\Resources.resw` | Our strings. |

### Sidebar
| File | Hook |
|---|---|
| `Files.App\ViewModels\UserControls\SidebarViewModel.cs` | `AreSectionsHidden` is false while on; `RefreshVisualSidebar`, `RefreshVisualSectionIcon`, `RestoreVisualPlacements` after loading; in the section list handlers `RemoveMovedSectionItems`, `IsMovedFromOtherSection`, `IsPlacedOutsideSection`, `CustomizeAddedSectionItem`, `CustomizeAddedSection`; `FindVisualSection`; switched-off sections kept with `KeepsSwitchedOffSections` / `RefreshSectionVisibility`; our two settings go to `HandleCustomSidebarSettingChangedAsync`; clicks go through `HandleVisualItemInvoked` / `VisualTarget`; drag and drop through `HandleVisualDragOverAsync` / `HandleVisualDropAsync`; "Reorder sidebar items" hidden while on. |
| `Files.App\ViewModels\UserControls\SidebarViewModel.FlatTree.cs` | Skips hidden rows (`IsVisualRowHidden`), lists compact section contents (`ShowsCompactGroupContents`), refreshes icons and Legacy insets. |
| `Files.App\ViewModels\UserControls\SidebarViewModel.TabExpansion.cs` | `ApplyVisualPreferences` for new items; calls Files' own `HookChildrenCollection` again when a customized item gets a new children list; `PlaceAddedVisualItems`. |
| `Files.App\Data\Items\ExpandableSidebarItemBase.cs` | Implements `ISidebarItemAppearanceModel` and `ISidebarItemReorderModel`; `IsExpanded` follows `SidebarExpansionLock`. |
| `Files.App\Data\Items\LocationItem.cs` | `ChildItems` raises change notifications only while customized; `IconElement` is `virtual`; `ItemDecorator => SidebarPinDecorator()`; shows the label. |
| `Files.App\Data\Items\DriveItem.cs` | Shows the label. |
| `Files.App\Views\MainPage.xaml` | The sidebar's Settings button hides with `SettingsFooterVisibility`. |
| `Files.App.Controls\Sidebar\SidebarItem.cs` | Calls `UpdateItemAppearance` / `ReleaseSidebarTemplateParts` on template, item and display-mode changes; `ToggleExpandedByUser`; preview and rearrange drags (`TryStartPreviewDrag`, `TryStartRearrangeDrag`); `TryPreviewClick`, `TryOpenCompactGroupFlyout`; `SidebarFlyoutChildren`. |
| `Files.App.Controls\Sidebar\SidebarItem.Properties.cs` | Expansion and indent updates for customized rows; `KeepsPreviewExpansion`, `FollowsLockedExpansion`. |
| `Files.App.Controls\Sidebar\SidebarView.xaml.cs` | `ReorderDataFormat`; separators and gaps can't be clicked; preview selection rules; the preview fills its pane. |
| `Files.App.Controls\Sidebar\FlatSidebarItem.cs` | `SectionGapMargin` uses `sectionGap` (12 unless changed) and the Legacy insets. |

### Column details
| File | Hook |
|---|---|
| `Files.App\Views\Layouts\DetailsLayoutPage.xaml.cs` | Calls `CustomColumnsNavigatedTo`, `CustomColumnsNavigatingFrom`, `CustomColumnsUnloaded`, `QueueCustomColumnFit`, `QueueCustomAutoFit`, `MarkCustomColumnWidthsChanged`, `RefreshColumnMenuState`, `RememberColumnRow`, `CustomColumnsShownChanged`, `AttachCustomColumnFitScroller`, `ColumnSplitterIndex`; listens to `CustomizationSettingsChanged`. |

### Shortcut badges
| File | Hook |
|---|---|
| `Files.App\Views\Layouts\ColumnLayoutPage.xaml`, `DetailsLayoutPage.xaml`, `GridLayoutPage.xaml` | `helpers:CustomizationShortcutArrow.ItemPath` on each `ShortcutGlyphElement`. |
| `Files.App\UserControls\NavigationToolbar.xaml` | `helpers:CustomizationShortcutArrow.SuggestionPath` on the suggestions' `ShortcutGlyphElement`. |

### Outside `src`
| File | Change |
|---|---|
| `..\.github\README.md`, `..\AGENTS.md`, `..\CLAUDE.md` | Fork introduction and rules for AI coding agents. |
| `..\.github\workflows\release.yml` (added) | Builds, signs and drafts a GitHub Release; run by hand from Actions with a tag such as `v4.3-0.2`. |
