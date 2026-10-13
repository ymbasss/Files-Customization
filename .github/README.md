# Files – Customization

An **unofficial fork of [Files](https://github.com/files-community/Files) 4.3** that adds **Settings > Customization**:
three optional features for shaping the sidebar, the details view and shortcut icons.

Each feature has its own switch. When a feature is off, the app runs Files 4.3's original code.

## Features

- **Sidebar**: a live editor for the whole sidebar.
  - Rearrange items by dragging, and create your own groups, separators, gaps and folder shortcuts.
  - Relabel, hide or restyle any item: icon, size, expansion arrow, inner folders.
  - Copy one item's look to others with Unify Appearance.
  - Choose between Files 4.3's look and a **Legacy** style based on the classic Files sidebar.
  - Undo, Redo and Reset at any time.
- **Column details**: set the default columns, their order and how they're sized (fit to content, or fit to the window width).
  The column heading menu gains Fit all columns to width, Reorder columns and Restore to default.
- **Shortcut badges**: hide the arrow badge on shortcut icons on the Desktop or everywhere.

See **[FEATURES.md](https://github.com/ymbasss/Files-Customization/blob/customization/FEATURES.md)** for every option.

## Changes from Files 4.3

This branch is Files 4.3 (tag `v4.3`) plus one set of changes. Almost all the new code is in `Customization` folders, and
Files' own files only gain small hooks.

- **[Compare with Files 4.3](https://github.com/ymbasss/Files-Customization/compare/v4.3...customization)**: every changed and added file.
- **[CHANGES.md](https://github.com/ymbasss/Files-Customization/blob/customization/CHANGES.md)**: what each file does and what changed in Files' files.

## Building

Build it the same way as Files: see the official
[building from source](https://files.community/docs/contributing/building-from-source) guide.
It installs as **Files - Dev**, alongside a Store or installer copy of Files.

## Credits and license

Files is made by the [Files Community](https://github.com/files-community). This fork isn't affiliated with or endorsed
by them. Please report issues with this fork here, not to the Files project.

Licensed like Files itself under the [MIT](https://github.com/ymbasss/Files-Customization/blob/customization/LICENSE-MIT) and
[MPL-2.0](https://github.com/ymbasss/Files-Customization/blob/customization/LICENSE-MPL) licenses.
