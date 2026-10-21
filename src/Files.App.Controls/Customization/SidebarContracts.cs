// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Controls
{
	public interface ISidebarItemReorderModel
	{
		string? SidebarDragKey { get; set; }
	}

	public interface ISidebarItemAppearanceModel
	{
		// How the row is drawn; null while sidebar customization is off, when the row uses the original template and code only.
		SidebarLook? SidebarLook { get; set; }
		string? SidebarDisplayName { get; set; }
		// 1 keeps the item expanded, 2 collapsed, 0 lets it expand normally.
		int SidebarExpansionLock { get; set; }
	}

	// KeepTextPosition: when the icon is hidden, the text keeps the place it has with the icon shown.
	// CompactGroupContents: in the compact sidebar a group's items are listed under it ("Show items from expanded groups") instead of in a flyout.
	public sealed record SidebarLook(
		double RowHeight = 32, double IconSize = 16, double FontSize = 14, bool IconVisible = true, bool KeepTextPosition = false,
		bool LegacyAppearance = false, bool LegacyHeader = false, bool CompactShowIcons = true, int CompactIconMode = 0, int CompactIconSize = 24,
		bool CompactHideGroupIcon = false, bool CompactGroupContents = false, bool ArrowVisible = true, bool Decoration = false,
		bool Separator = false, bool Hidden = false, double Spacing = 0, double CompactSpacing = 0);
}
