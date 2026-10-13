// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Items
{
	public abstract partial class ExpandableSidebarItemBase
	{
		private bool sidebarCustomized;
		public bool SidebarCustomized { get => sidebarCustomized; set => SetProperty(ref sidebarCustomized, value); }
		private string? sidebarDragKey;
		public string? SidebarDragKey { get => sidebarDragKey; set => SetProperty(ref sidebarDragKey, value); }
		private string? sidebarDisplayName;
		public string? SidebarDisplayName
		{
			get => sidebarDisplayName;
			set { if (SetProperty(ref sidebarDisplayName, value)) OnPropertyChanged("Text"); }
		}

		private double sidebarRowHeight = 32;
		public double SidebarRowHeight { get => sidebarRowHeight; set => SetProperty(ref sidebarRowHeight, value); }
		private double sidebarIconSize = 16;
		public double SidebarIconSize { get => sidebarIconSize; set => SetProperty(ref sidebarIconSize, value); }
		private double sidebarFontSize = 14;
		public double SidebarFontSize { get => sidebarFontSize; set => SetProperty(ref sidebarFontSize, value); }
		private bool sidebarIconVisible = true;
		public bool SidebarIconVisible { get => sidebarIconVisible; set => SetProperty(ref sidebarIconVisible, value); }
		private bool sidebarKeepTextPosition;
		public bool SidebarKeepTextPosition { get => sidebarKeepTextPosition; set => SetProperty(ref sidebarKeepTextPosition, value); }
		private bool sidebarLegacyAppearance;
		public bool SidebarLegacyAppearance { get => sidebarLegacyAppearance; set => SetProperty(ref sidebarLegacyAppearance, value); }
		private bool sidebarLegacyHeader;
		public bool SidebarLegacyHeader { get => sidebarLegacyHeader; set => SetProperty(ref sidebarLegacyHeader, value); }
		private bool sidebarCompactShowIcons = true;
		public bool SidebarCompactShowIcons { get => sidebarCompactShowIcons; set => SetProperty(ref sidebarCompactShowIcons, value); }
		private int sidebarCompactIconMode;
		public int SidebarCompactIconMode { get => sidebarCompactIconMode; set => SetProperty(ref sidebarCompactIconMode, value); }
		private int sidebarCompactIconSize = 24;
		public int SidebarCompactIconSize { get => sidebarCompactIconSize; set => SetProperty(ref sidebarCompactIconSize, Math.Clamp(value, 8, 32)); }
		private bool sidebarCompactHideGroupIcon;
		public bool SidebarCompactHideGroupIcon { get => sidebarCompactHideGroupIcon; set => SetProperty(ref sidebarCompactHideGroupIcon, value); }
		public bool SidebarCompactGroupContents { get; set; }
		private bool sidebarPinVisible;
		public bool SidebarPinVisible
		{
			get => sidebarPinVisible;
			set { if (SetProperty(ref sidebarPinVisible, value)) OnPropertyChanged("ItemDecorator"); }
		}
		private bool sidebarArrowVisible = true;
		public bool SidebarArrowVisible { get => sidebarArrowVisible; set => SetProperty(ref sidebarArrowVisible, value); }
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
		private bool sidebarHidden;
		public bool SidebarHidden { get => sidebarHidden; set => SetProperty(ref sidebarHidden, value); }
		private bool sidebarDecoration;
		public bool SidebarDecoration { get => sidebarDecoration; set => SetProperty(ref sidebarDecoration, value); }
		private bool sidebarSeparator;
		public bool SidebarSeparator { get => sidebarSeparator; set => SetProperty(ref sidebarSeparator, value); }
		internal void RefreshSidebarAppearance() => OnPropertyChanged(nameof(SidebarRowHeight));

		// The Legacy look's pin after pinned folders; nothing otherwise, as in the original app.
		[WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Style))]
		protected Microsoft.UI.Xaml.FrameworkElement? SidebarPinDecorator()
			=> SidebarPinVisible
				? new Files.App.Controls.ThemedIcon { Style = Microsoft.UI.Xaml.Application.Current.Resources["SidebarFavouritesPinnedIcon"] as Microsoft.UI.Xaml.Style }
				: null;

		// Added groups and shortcuts without an icon of their own show a folder glyph.
		protected Microsoft.UI.Xaml.Controls.IconElement SidebarFallbackIcon()
			=> new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "", FontSize = SidebarIconSize };

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
