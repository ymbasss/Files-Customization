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
		// Sidebar customization is on. While it's off the row uses the original template and code only.
		bool SidebarCustomized { get; set; }
		string? SidebarDisplayName { get; set; }
		double SidebarRowHeight { get; set; }
		double SidebarIconSize { get; set; }
		double SidebarFontSize { get; set; }
		bool SidebarIconVisible { get; set; }
		// When the icon is hidden, the text keeps the place it has with the icon shown.
		bool SidebarKeepTextPosition { get; set; }
		bool SidebarLegacyAppearance { get; set; }
		bool SidebarLegacyHeader { get; set; }
		bool SidebarCompactShowIcons { get; set; }
		int SidebarCompactIconMode { get; set; }
		int SidebarCompactIconSize { get; set; }
		bool SidebarCompactHideGroupIcon { get; set; }
		// In the compact sidebar a group's items are listed under it ("Show items from expanded groups") instead of in a flyout.
		bool SidebarCompactGroupContents { get; set; }
		bool SidebarArrowVisible { get; set; }
		int SidebarExpansionLock { get; set; }
		bool SidebarDecoration { get; set; }
		bool SidebarSeparator { get; set; }
		bool SidebarHidden { get; set; }
	}

}
