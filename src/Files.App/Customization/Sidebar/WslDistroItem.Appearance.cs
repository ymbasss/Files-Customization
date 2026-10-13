// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Items
{
	public sealed partial class WslDistroItem : Files.App.Controls.ISidebarItemReorderModel
	{
		// The key this item is dragged with while "Drag to rearrange" is on.
		private string? sidebarDragKey;
		public string? SidebarDragKey { get => sidebarDragKey; set => SetProperty(ref sidebarDragKey, value); }
	}
}
