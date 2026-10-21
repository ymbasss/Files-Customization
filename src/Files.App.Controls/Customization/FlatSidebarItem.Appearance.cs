// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Controls
{
	public sealed partial class FlatSidebarItem
	{
		private double legacyTopInset;
		private double legacyBottomInset;
		// Files 4.3's 12 px gap before a section that follows an expanded one; customization can change it.
		private double sectionGap = 12;

		// Preview rows (sidebar customization panel): whether the row is expanded in the preview, and whether a dragged row
		// can be dropped inside it. Both are decided by the panel, not by the sidebar item.
		public bool PreviewExpanded { get; init; }
		public bool PreviewAcceptsInside { get; init; }

		public void SetLegacySectionInsets(bool firstChild, bool lastChild, double sectionGap = 12)
		{
			var top = firstChild ? 2d : 0;
			var bottom = lastChild ? 2d : 0;
			if (legacyTopInset == top && legacyBottomInset == bottom && this.sectionGap == sectionGap) return;
			legacyTopInset = top;
			legacyBottomInset = bottom;
			this.sectionGap = sectionGap;
			PropertyChanged?.Invoke(this, SectionGapMarginChangedArgs);
		}
	}
}
