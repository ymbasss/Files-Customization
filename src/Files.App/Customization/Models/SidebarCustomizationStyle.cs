// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Models
{
	internal static class SidebarCustomizationStyle
	{
		// Separators and gaps start (and reset to) as tall as an item at its default size.
		internal const int DefaultDecorationHeight = 32;
		// Extra space between rows, in pixels. Below zero rows move closer: at -4 their highlights touch, at -8 they overlap a little.
		internal const int MinItemSpacing = -8;
		internal const int MaxItemSpacing = 16;
		// Files 4.3's gap before a section that follows an expanded one, in pixels.
		internal const int DefaultSectionSpacing = 12;
		internal const int MaxSectionSpacing = 48;

		// Default (-1 or 0) is the same for every item, drives included.
		internal static (double Text, double Icon) DefaultPoints(int size, CustomizationPreferences options, bool legacyHeader = false)
			=> size switch { 1 => (12, 18), 2 => (13.5, 24), _ => (options.SidebarAppearance == 1 && legacyHeader ? 9 : 10.5, 12) };
		internal static bool IsLegacyHeader(bool group, string key) => group && !string.Equals(key, "section:Home", StringComparison.Ordinal);

		internal static int ExpansionMode(SidebarCustomizationState.Entry entry, bool group, CustomizationPreferences options)
			=> options.SidebarAppearance == 1 ? entry.LegacyExpansion ?? (group ? 0 : 2) : entry.HideArrow ? 2 : entry.ExpansionLock;

		internal static bool IconVisible(SidebarCustomizationState.Entry entry, bool legacyHeader)
			=> !entry.HideIcon && !legacyHeader;

		internal static SidebarCustomizationState.Entry CopyAppearance(
			SidebarCustomizationState.Entry target, SidebarCustomizationState.Entry source, int? legacyExpansion = null) =>
			target with
			{
				Size = source.Size,
				TextPt = source.TextPt,
				IconPt = source.IconPt,
				HideIcon = source.HideIcon,
				HideArrow = legacyExpansion.HasValue ? target.HideArrow : source.HideArrow,
				ExpansionLock = legacyExpansion.HasValue ? target.ExpansionLock : source.ExpansionLock,
				LegacyExpansion = legacyExpansion ?? target.LegacyExpansion,
				SizesLinked = source.SizesLinked
			};
	}
}
