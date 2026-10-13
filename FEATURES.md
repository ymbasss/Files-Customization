# Customization features

This fork of [Files](https://github.com/files-community/Files) 4.3 adds **Settings > Customization**, which you open from the
bottom of the Settings sidebar. It has three features, each with its own switch:

- [Sidebar](#sidebar): rearrange, group, relabel and restyle everything in the sidebar.
- [Column details](#column-details): set the details view's default columns, order and sizing, with more commands on the column heading.
- [Shortcut badges](#shortcut-badges): hide the arrow badge on shortcut icons.

A feature that is switched off runs Files 4.3's original code, so the app behaves exactly like 4.3.
Settings are saved in `customization.json` in the app's settings folder.

---

## Sidebar

Settings > Customization > **Sidebar** holds the sidebar panel: a live preview of your sidebar on the left and the selected
item's settings on the right. Changes apply to the real sidebar as you edit.

### Toolbar

| Button | What it does |
|---|---|
| **Add** | Adds a **Group**, a **Separator** line or a **Gap** (empty space) below the selected item. |
| **Duplicate item** | Adds a copy of the selected item, such as a second link to the same folder. |
| **Options** | Settings for the whole sidebar (see [Options](#options)). |
| **Undo / Redo** | Steps back and forward through your changes. History is kept until the app closes. |
| **Reset to default** | Restores the default sidebar layout and appearance, after asking you to confirm. |

### Arranging items

- **Drag rows in the preview** to move them before or after another row, or into a group.
- **Move up / Move down** buttons move the selected item one place.
- **Groups** are your own sections. Put any items in them, including pinned folders, drives, tags and other sections.
- **Folder shortcuts** link to any folder by path.
- **Drag to rearrange sidebar items** (in Options) lets you drag items in the real sidebar too. While it's on, dragging a folder
  from the sidebar rearranges it instead of moving or copying the folder.
- Items keep their place when they reappear: a drive plugged in again, or a folder renamed or moved on the same drive.
- Sections you switched off in Settings > General still appear in the panel, so you can place their items.

### Item settings

| Setting | What it does |
|---|---|
| **Label** | Shows a different name in the sidebar. The folder itself isn't renamed. |
| **Path** | The folder a shortcut opens. |
| **Show in sidebar** | Hides the item without removing it. Hidden rows are dimmed in the preview. |
| **Icon** | Choose another icon, or import your own. Imported icons can be deleted from the list. |
| **Hide icon** | Shows the item without an icon. |
| **Size** | Default, Medium, Large or Custom. Custom sets text and icon sizes in points, linked or separately. |
| **Row height** | Height of a gap, in pixels. |
| **Expansion arrow** | Show or hide the arrow. You can also make an item **Always expanded** or **Always collapsed**. |
| **Hide pin icon** | Legacy style only: hides the pin shown on pinned folders. |
| **Style inner folders separately** | Gives the folders inside a drive or folder their own appearance. When off, they match the item. You can also hide their icons. |
| **Reset this item's appearance** | Puts the item back to its default look. |

### Unify Appearance

At the bottom of an item's settings, **Unify Appearance** copies the selected item's look to:

- **The same group**
- **Entire sidebar**

Only the appearance is copied. Labels, paths and positions stay as they are. **Apply** is greyed out when there's nothing to change,
and **Undo changes** reverses the last copy.

### Options

| Option | What it does |
|---|---|
| **Style** | **Default** is Files 4.3's look. **Legacy** recreates the classic Files (UWP) sidebar: grey section headings, the expand arrow at the right end of each row, and pin icons on pinned folders. In Legacy, long names end with "…" so the arrows stay in view. |
| **Show icons** | Ticked: show every icon. Dash: use each item's own setting. Empty: hide every icon. |
| **Show expansion arrows** | The same three states for the arrows. |
| **Drag to rearrange sidebar items** | See [Arranging items](#arranging-items). |
| **Use higher resolution icons for larger sizes** | Uses sharper icons at larger sizes when available. Some icons may not change. |
| **Keep text in place when an icon is hidden** | When off, the text moves into the hidden icon's space. |
| **Compact mode** | For the narrow, icons-only sidebar: **Show hidden icons**, **Show items from expanded groups**, **Hide group icons** and **Icon size** (Default, Larger or a custom size from 8 to 32 px). |

Switching the global icon or arrow option never loses your per-item choices.

---

## Column details

Settings > Customization > **Column details** sets the details view's default layout. To change only the current view,
right-click a column heading.

### Settings

| Setting | What it does |
|---|---|
| **Columns** | Choose which columns show by default. Drag them to set their default order. Columns that only appear in some places (Git repositories, Search results, Recycle Bin, Cloud folders) are labelled and keep their place. |
| **Column sizing** | **App's default**: no automatic fitting, the same as Files 4.3. **Automatically fit columns to content**: Files' own setting, also in Settings > Layout. **Automatically fit columns to width**: columns fill the view and refit as the window is resized. |
| **Undo / Redo / Reset** | Undo and redo your changes, or restore the default columns, order and view. |

Column details changes some of Files' own Layout settings. Switching it off gives them back the values they had before, and
switching it on again brings back its own.

### Column heading menu

Right-click a column heading in the details view for these commands:

| Command | What it does |
|---|---|
| **Fit all columns to width** | Fits the columns to the view once and saves them for this folder. |
| **Fit all columns to content** | Sizes each column to its content (Files' own command). |
| **Reorder columns** | Changes the column order for this view. |
| **Restore to default** | Puts this folder back to the default columns, widths and order. With App's default sizing it reads **Restore column order** and only restores the order. |

Commands are greyed out when they wouldn't change anything.

### Folders and fitting

- A folder whose columns you fitted or resized keeps those widths. Automatic fitting leaves it alone until you restore it.
- A restored folder follows the default layout again, including later changes to the default.
- With App's default sizing, folders keep their saved layouts as in Files 4.3, and nothing extra is recorded.
- With "Sync layout and sorting preferences across directories" on, all folders share one layout, as in Files 4.3.

---

## Shortcut badges

Settings > Customization > **Shortcut badges** hides the small arrow on shortcut icons.

| Setting | What it does |
|---|---|
| **Hide badges on Desktop only** | Hides them for items on the Desktop. |
| **Hide badges everywhere** | Hides them across the app. |
| **Hide badges in folders inside the Desktop** | Also hides them in folders inside the Desktop, when hiding on Desktop only. |
| **Hide badges in Search suggestions** | Also hides them in the address bar's search suggestions. |
