# Customization features

This fork of [Files](https://github.com/files-community/Files) 4.3 adds **Settings > Customization**, at the bottom of the
Settings sidebar. It has three features, each with its own switch:

- [Sidebar](#sidebar): rearrange, group, relabel and restyle the sidebar.
- [Column details](#column-details): default columns, order and sizing for the Details view, plus extra column-heading commands.
- [Shortcut badges](#shortcut-badges): hide the arrow badge on shortcut icons.

A switched-off feature runs Files 4.3's original code. Settings are saved in `customization.json` in the app's settings folder.

---

## Sidebar

Settings > Customization > **Sidebar** shows a live preview of your sidebar on the left and the selected item's settings on the
right. Changes apply to the real sidebar as you edit.

### Toolbar

| Button | What it does |
|---|---|
| **Add** | Adds a **Group**, **Separator** or **Gap** below the selected item. |
| **Duplicate item** | Adds a copy of the selected item, such as a second link to the same folder. |
| **Options** | Settings for the whole sidebar (see [Options](#options)). |
| **Undo / Redo** | Steps through your changes. History is kept until the app closes. |
| **Reset to default** | Restores the default sidebar layout and appearance, after confirmation. |

### Arranging items

- Drag rows in the preview to move them before or after another row, or into a group.
- **Move up / Move down** move the selected item one place.
- **Groups** are your own sections. They can hold any item, including pinned folders, drives, tags and other sections.
- **Folder shortcuts** link to any folder by path.
- **Drag to rearrange sidebar items** (in Options) also lets you drag items in the real sidebar. While it's on, dragging a folder
  from the sidebar rearranges it instead of moving or copying the folder.
- Items keep their place when they come back, such as a drive that was unplugged or a folder renamed or moved on the same drive.
- Sections switched off in Settings > General still appear in the panel, so you can place their items.

### Item settings

| Setting | What it does |
|---|---|
| **Label** | Name shown in the sidebar. The folder itself isn't renamed. |
| **Path** | The folder a shortcut opens (shortcuts you added). |
| **Show in sidebar** | Hides the item without removing it. Hidden rows are dimmed in the preview. |
| **Remove from sidebar** | Deletes an item you added (group, separator, gap or shortcut). |
| **Size** | Default, Medium, Large or Custom. Custom sets text and icon sizes in points, linked or separate. |
| **Row height** | Height of a gap, in pixels. |
| **Icon** | For groups: choose an app icon or import your own. Imported icons can be deleted, and Undo brings them back. |
| **Hide icon** | Shows the item without an icon. Not available for Legacy group headings, which have none. |
| **Hide pin icon** | Legacy style only: hides the pin shown on pinned folders. |
| **Expansion arrow** | For items that can expand: Default, Always expanded or Always collapsed. In the Default style, Always collapsed also hides the arrow. |
| **Style inner folders separately** | Gives the folders inside an item their own appearance. When off, they match the item. You can also hide their icons. |
| **Reset this item's appearance** | Returns the item to its default look. |

### Unify Appearance

At the bottom of an item's settings, **Unify Appearance** copies its look to **The same group** or **Entire sidebar**.
Labels, paths and positions are not copied. **Apply** is greyed out when nothing would change, and **Undo changes** reverses
the last copy.

### Options

| Option | What it does |
|---|---|
| **Style** | **Default** is Files 4.3's look. **Legacy** recreates the classic Files sidebar: grey section headings, the expand arrow at the right end of each row, and pin icons. In Legacy, long names end with "…" so the arrows stay visible. |
| **Drag to rearrange sidebar items** | See [Arranging items](#arranging-items). |
| **Use higher resolution icons for larger sizes** | Uses sharper icons at larger sizes when available. Some icons may not change. |
| **Keep text in place when an icon is hidden** | When off, the text moves into the hidden icon's space. |
| **Compact mode** | For the narrow, icons-only sidebar: **Show hidden icons**, **Show items from expanded groups**, **Hide group icons** and **Icon size** (Default, Larger, or Custom from 8 to 32 px). |

---

## Column details

Settings > Customization > **Column details** sets the default layout of the Details view. To change only the current view,
right-click a column heading.

### Settings

| Setting | What it does |
|---|---|
| **Columns** | Choose which columns show by default and drag them into order. Columns that only appear in some places (Git repositories, Search results, Recycle Bin, Cloud folders) are labelled and keep their place. |
| **Column sizing** | **App's default**: no automatic fitting, as in Files 4.3. **Automatically fit columns to content**: Files' own setting, also in Settings > Layout. **Automatically fit columns to width**: columns fill the view and refit when the window is resized. |
| **Undo / Redo / Reset to default** | Step through your changes, or restore the default columns, order and view. |

Column details changes some of Files' own Layout settings. Switching it off restores the values they had before; switching it on
again brings back its own.

### Column heading menu

| Command | What it does |
|---|---|
| **Fit all columns to width** | Fits the columns to the view once and saves them for this folder. |
| **Fit all columns to content** | Sizes each column to its content (Files' own command). |
| **Reorder columns** | Changes the column order for this view, with Move up and Move down. |
| **Restore to default** | Puts this folder back to the default columns, widths and order. With App's default sizing it reads **Restore column order** and restores only the order. |

Commands are greyed out when they wouldn't change anything, and the two fit commands are greyed out in an empty folder.

### Folders and fitting

- A folder you fitted or resized keeps those widths; automatic fitting leaves it alone until you restore it.
- A restored folder follows the default layout again, including later changes to the default.
- With App's default sizing, folders keep their saved layouts as in Files 4.3 and nothing extra is recorded.
- With "Sync layout and sorting preferences across directories" on, all folders share one layout, as in Files 4.3.

---

## Shortcut badges

Settings > Customization > **Shortcut badges** hides the small arrow on shortcut icons.

| Setting | What it does |
|---|---|
| **Hide badges on Desktop only** | Hides them for items on the Desktop. |
| **Hide badges everywhere** | Hides them across the app. |
| **Hide badges in folders inside the Desktop** | With "Desktop only", also hides them in folders inside the Desktop. |
| **Hide badges in Search suggestions** | Also hides them in the address bar's search suggestions. |
