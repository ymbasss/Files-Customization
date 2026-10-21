// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Items
{
	public sealed partial class FileTagItem : Files.App.Controls.ISidebarItemAppearanceModel, Files.App.Controls.ISidebarItemReorderModel
	{
		private string? sidebarDragKey;
		public string? SidebarDragKey { get => sidebarDragKey; set => SetProperty(ref sidebarDragKey, value); }

		private Files.App.Controls.SidebarLook? sidebarLook;
		public Files.App.Controls.SidebarLook? SidebarLook { get => sidebarLook; set => SetProperty(ref sidebarLook, value); }
		private string? sidebarDisplayName;
		public string? SidebarDisplayName
		{
			get => sidebarDisplayName;
			set { if (SetProperty(ref sidebarDisplayName, value)) OnPropertyChanged("Text"); }
		}
		public int SidebarExpansionLock { get; set; }
		internal void RefreshSidebarAppearance() => OnPropertyChanged(nameof(SidebarLook));
	}
}
