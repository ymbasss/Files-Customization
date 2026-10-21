// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Items
{
	public abstract partial class ExpandableSidebarItemBase
	{
		private Files.App.Controls.SidebarLook? sidebarLook;
		public Files.App.Controls.SidebarLook? SidebarLook { get => sidebarLook; set => SetProperty(ref sidebarLook, value); }
		private string? sidebarDragKey;
		public string? SidebarDragKey { get => sidebarDragKey; set => SetProperty(ref sidebarDragKey, value); }
		private string? sidebarDisplayName;
		public string? SidebarDisplayName
		{
			get => sidebarDisplayName;
			set { if (SetProperty(ref sidebarDisplayName, value)) OnPropertyChanged("Text"); }
		}

		private bool sidebarPinVisible;
		public bool SidebarPinVisible
		{
			get => sidebarPinVisible;
			set { if (SetProperty(ref sidebarPinVisible, value)) OnPropertyChanged("ItemDecorator"); }
		}
		private int sidebarExpansionLock;
		public int SidebarExpansionLock
		{
			get => sidebarExpansionLock;
			set
			{
				if (SetProperty(ref sidebarExpansionLock, Math.Clamp(value, 0, 2)) && sidebarExpansionLock != 0)
					IsExpanded = sidebarExpansionLock == 1;
			}
		}
		internal void RefreshSidebarAppearance() => OnPropertyChanged(nameof(SidebarLook));

		// The Legacy look's pin after pinned folders; nothing otherwise, as in the original app.
		[WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Style))]
		protected Microsoft.UI.Xaml.FrameworkElement? SidebarPinDecorator()
			=> SidebarPinVisible
				? new Files.App.Controls.ThemedIcon { Style = Microsoft.UI.Xaml.Application.Current.Resources["SidebarFavouritesPinnedIcon"] as Microsoft.UI.Xaml.Style }
				: null;

		// Added groups and shortcuts without an icon of their own show a folder glyph.
		protected Microsoft.UI.Xaml.Controls.IconElement SidebarFallbackIcon()
			=> new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = SidebarLook?.IconSize ?? 16 };

		// The folder this item lists when expanded, for readers such as the customization preview; null when it has none.
		internal string? SidebarExpansionPath
		{
			get
			{
				try { return ExpansionPath; }
				catch (InvalidOperationException) { return null; }
			}
		}
	}
}
