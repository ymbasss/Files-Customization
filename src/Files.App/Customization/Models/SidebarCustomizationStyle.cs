// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Models
{
	internal static class SidebarCustomizationStyle
	{
		// New separators and gaps start as tall as an item at its default size.
		internal const int DefaultDecorationHeight = 32;
		// Separators start a little shorter, since their line already divides the items.
		internal const int DefaultSeparatorHeight = 24;

		internal static (double Text, double Icon) DefaultPoints(int size, bool drive, CustomizationPreferences options, bool legacyHeader = false)
		{
			var legacy = options.SidebarAppearance == 1;
			var density = size < 0 ? drive && !legacy ? 1 : 0 : size;
			return density switch { 1 => (12, 18), 2 => (13.5, 24), _ => (legacy && legacyHeader ? 9 : 10.5, 12) };
		}
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
