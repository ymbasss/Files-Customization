# Customization features

Open **Settings > Customization** from the bottom of the Settings sidebar.

The page contains three independently enabled features:

- **Sidebar** — edit sidebar layout and appearance.
- **Column details** — configure default columns, ordering, and sizing.
- **Shortcut badges** — control where shortcut arrow badges are hidden.

Customization settings are stored in `customization.json` in the app’s settings folder.

## Sidebar

Expand **Sidebar** to open the editor. The preview shows the sidebar layout, with controls for the selected item beside it. Changes apply as you edit.

### Toolbar

| Control | Function |
|---|---|
| **Add** | Add a Group, Separator, or Gap. |
| **Duplicate item** | Create another sidebar entry pointing to the selected item’s destination. |
| **Options** | Open sidebar style and behavior settings. |
| **Undo / Redo** | Step through available changes from the current app session. |
| **Reset to default** | Reset sidebar layout, appearance, and options after confirmation. |

### Arranging items

- Drag items in the preview, or use **Move up / Move down**.
- Move items between built-in sections and custom groups.
- Groups remain at the top level; groups cannot be nested.
- Separators add dividing lines. Gaps add empty space.
- Duplicate available navigation items, including Home. Groups, separators, gaps, and preview-only inner folders cannot be duplicated.
- Duplicates show **(duplicate)** beside their heading and have an **X** button to remove them.
- Custom groups, separators, and gaps can also be removed.
- Sections disabled in the app’s settings remain available in the editor.
- Inner folders shown by expanding an item are preview-only. Their appearance is controlled through the parent item.

The **Add** menu does not create folder shortcuts.

### Item settings

Controls depend on the selected item.

| Setting | Function |
|---|---|
| **Show in sidebar** | Show or hide the entry. Hidden entries are dimmed in the preview. |
| **Label** | Change the displayed name without renaming the destination. |
| **Path** | Show the destination of a duplicated entry. Duplicate paths are read-only. |
| **Size** | Choose Default, Medium, Large, or Custom. |
| **Custom size** | Set text and icon sizes in points, linked or independently. |
| **Icon** | For custom groups, choose an app icon or browse for an image. |
| **Hide icon** | Hide the selected item’s icon. |
| **Expansion arrow** | Choose Default, Always expanded, or Always collapsed for expandable items. Always collapsed hides the arrow. |
| **Hide pin icon** | Hide the pin indicator on supported entries in Legacy style. |
| **Row height (px)** | Adjust the space occupied by a separator or gap, from 4 to 64 pixels. |
| **Reset this item's appearance** | Reset the selected item’s customization without changing its position. Duplicates offer removal instead. |

Imported group icons appear in the icon list with a preview and filename. Stored imported icons can be deleted using their X button.

Legacy section headings do not display icons, so their icon controls are disabled.

### Inner folders

**Style inner folders separately** controls the appearance of folders inside an expandable item.

When enabled, it provides:

- Default, Medium, Large, or Custom sizes.
- Linked or independent text and icon sizes.
- **Hide icons**.

When disabled, inner folders inherit the parent item’s appearance.

### Unify Appearance

Copy the selected item’s appearance to:

- **The same group**, including its contents.
- **Entire sidebar**.

This copies sizing, icon visibility, and applicable expansion and pin settings. Labels, destinations, positions, and custom group icon images are not copied.

**Apply** is disabled when there is nothing to change. **Undo changes** is enabled while the latest application for the selected item can still be undone.

The section collapses when a different item is selected.

### Options

| Option | Function |
|---|---|
| **Style** | Choose Default or Legacy. Legacy uses classic-style section headings, arrows on the right, and pin indicators on supported entries. |
| **Drag to rearrange sidebar items** | Enable rearranging entries directly in the main sidebar. While enabled, dragging folders from the sidebar does not move or copy the folders. |
| **Use higher resolution icons for larger sizes** | Request sharper icons where available. |
| **Keep text in place when an icon is hidden** | Preserve text alignment when an icon is hidden. When disabled, text moves into the icon’s space. |

### Compact mode

These options affect the narrow sidebar:

| Option | Function |
|---|---|
| **Show hidden icons** | Show icons that are hidden in the expanded sidebar. |
| **Show items from expanded groups** | Show the contents of expanded groups in compact mode. |
| **Hide group icons** | Hide group icons without hiding individual item icons. |
| **Icon size** | Choose Default, Larger, or Custom. Custom sizes range from 8 to 32 pixels. |

## Column details

Expand **Column details** to configure the default Details-view layout. Right-click a column heading to adjust the current view.

### Settings

| Setting | Function |
|---|---|
| **Columns** | Choose default column visibility and drag rows to set their order. Name remains visible. |
| **Column sizing** | Choose App’s default, Automatically fit columns to content, or Automatically fit columns to width. |
| **Options** | Access Sync layout and sorting preferences across directories, also available in Settings > Layout. |
| **Undo / Redo** | Undo or redo column visibility, order, and sizing changes. |
| **Reset to default** | Reset default column visibility, order, and sizing after confirmation. |

Git, Search, Recycle Bin, and cloud-specific columns appear only in relevant views. They retain their configured position.

**Automatically fit columns to content** uses the app’s existing Layout setting. Its link indicator appears while the sizing list is open.

Turning Column details off restores the linked Layout settings saved before it was enabled. Turning it back on restores its saved configuration.

### Column heading menu

| Command | Function |
|---|---|
| **Fit all columns to width** | Fit the current view’s columns to the available width and save the widths. |
| **Fit all columns to content** | Size columns to their contents. |
| **Reorder columns** | Move columns up or down in the current view’s order. |
| **Restore to default** | Restore default columns, widths, order, and automatic sizing. |
| **Restore column order** | With App’s default sizing selected, restore only the column order. |

Commands are disabled when unavailable or when they would make no change.

The Reorder columns controls keep the menu open. Moving the mouse outside both the menu and submenu closes them after a short delay.

### Folder preferences

- With App’s default sizing, folders retain their saved layouts.
- Manually resizing or fitting columns gives that folder its own widths and pauses automatic sizing for it.
- Restore to default removes that width override.
- Folders restored to the default columns follow later changes to those defaults.
- Enabling Sync layout and sorting preferences across directories shares layout and sorting preferences between folders.

## Shortcut badges

This feature hides the small arrow badge drawn on shortcut icons inside Files.

| Setting | Function |
|---|---|
| **Hide badges on Desktop only** | Hide badges for shortcuts located on the Desktop. |
| **Hide badges everywhere** | Hide badges throughout Files. |
| **Hide badges in folders inside the Desktop** | Include Desktop subfolders when using Desktop-only mode. |
| **Hide badges in Search suggestions** | Apply badge hiding to address-bar Search suggestions. |

Open the **Settings gear** beside Shortcut arrow visibility to access the additional options.

When Hide badges everywhere is selected, Desktop subfolders are already included, so that checkbox is checked and disabled.
