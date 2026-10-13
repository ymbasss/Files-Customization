// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Globalization;

namespace Files.App.Data.Models
{
	internal sealed record CustomizationPreferences
	{
		public bool CustomizationEnabled { get; init; } = true;
		public bool SidebarEnabled { get; init; }
		public bool ColumnsEnabled { get; init; }
		public bool ShortcutBadgesEnabled { get; init; }
		public bool SidebarDragEnabled { get; init; } = true;
		public bool SidebarCompactShowIcons { get; init; } = true;
		public bool SidebarCompactGroupContents { get; init; }
		public int SidebarCompactIconMode { get; init; }
		public int SidebarCompactIconSize { get; init; } = 24;
		public int CompactIconPixels => SidebarCompactIconMode switch { 1 => 24, 2 => Math.Clamp(SidebarCompactIconSize, 8, 32), _ => 16 };
		public bool SidebarCompactHideGroupIcons { get; init; }
		public bool SharpLargeIcons { get; init; } = true;
		public bool SidebarKeepTextPosition { get; init; } = true;
		public int SidebarAppearance { get; init; }
		public bool FitColumnsToWidth { get; init; }
		public int ShortcutArrowMode { get; init; }
		// Desktop only: also shortcuts in folders inside the Desktop.
		public bool ShortcutArrowDesktopSubfolders { get; init; }
		// Also the shortcut badges in the search suggestions under the address bar.
		public bool ShortcutArrowSearchSuggestions { get; init; } = true;
		public string ColumnOrder { get; init; } = "";

		public static double ToPixels(double points) => points * 96d / 72d;

		private sealed record ParsedPreferences(string Source, CustomizationPreferences Value);
		private static ParsedPreferences? lastParsed;
		private static ParsedPreferences? lastEffective;
		private static readonly CustomizationPreferences disabled = new()
		{
			CustomizationEnabled = false,
			SidebarDragEnabled = false,
			SidebarCompactGroupContents = false,
			SharpLargeIcons = false,
			SidebarKeepTextPosition = false,
			ShortcutArrowSearchSuggestions = false,
		};

		public static CustomizationPreferences Parse(string value)
		{
			var cached = System.Threading.Volatile.Read(ref lastParsed);
			if (cached is not null && string.Equals(cached.Source, value, StringComparison.Ordinal))
				return cached.Value;

			var parsed = ParseCore(value);
			System.Threading.Volatile.Write(ref lastParsed, new ParsedPreferences(value, parsed));
			return parsed;
		}

		public static CustomizationPreferences Effective(string value)
		{
			var cached = System.Threading.Volatile.Read(ref lastEffective);
			if (cached is not null && string.Equals(cached.Source, value, StringComparison.Ordinal))
				return cached.Value;

			var effective = EffectiveCore(Parse(value));
			System.Threading.Volatile.Write(ref lastEffective, new ParsedPreferences(value, effective));
			return effective;
		}

		private static CustomizationPreferences EffectiveCore(CustomizationPreferences options)
		{
			if (!options.CustomizationEnabled)
				return disabled;

			return options with
			{
				SidebarDragEnabled = options.SidebarEnabled && options.SidebarDragEnabled,
				SidebarCompactShowIcons = !options.SidebarEnabled || options.SidebarCompactShowIcons,
				SidebarCompactGroupContents = options.SidebarEnabled && options.SidebarCompactGroupContents,
				SidebarCompactIconMode = options.SidebarEnabled ? options.SidebarCompactIconMode : 0,
				SidebarCompactHideGroupIcons = options.SidebarEnabled && options.SidebarCompactHideGroupIcons,
				SharpLargeIcons = options.SidebarEnabled && options.SharpLargeIcons,
				SidebarKeepTextPosition = options.SidebarEnabled && options.SidebarKeepTextPosition,
				SidebarAppearance = options.SidebarEnabled ? options.SidebarAppearance : 0,
				ColumnOrder = options.ColumnsEnabled ? options.ColumnOrder : "",
				FitColumnsToWidth = options.ColumnsEnabled && options.FitColumnsToWidth,
				ShortcutArrowMode = options.ShortcutBadgesEnabled ? Math.Clamp(options.ShortcutArrowMode, 1, 2) : 0,
				ShortcutArrowDesktopSubfolders = options.ShortcutBadgesEnabled && options.ShortcutArrowDesktopSubfolders,
				ShortcutArrowSearchSuggestions = options.ShortcutBadgesEnabled && options.ShortcutArrowSearchSuggestions
			};
		}

		private static CustomizationPreferences ParseCore(string value)
		{
			var fields = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var line in value.Split('\n'))
			{
				var split = line.IndexOf('=');
				if (split > 0)
					fields[line[..split]] = Uri.UnescapeDataString(line[(split + 1)..]);
			}

			bool Flag(string key, bool fallback = false)
				=> fields.TryGetValue(key, out var text) && bool.TryParse(text, out var parsed) ? parsed : fallback;

			double Number(string key, double fallback, double min, double max)
				=> fields.TryGetValue(key, out var text) &&
					double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
					double.IsFinite(parsed)
						? Math.Clamp(parsed, min, max)
						: fallback;

			string Text(string key, string fallback = "")
				=> fields.TryGetValue(key, out var text) ? text : fallback;

			return new()
			{
				CustomizationEnabled = Flag(nameof(CustomizationEnabled), true),
				SidebarEnabled = Flag(nameof(SidebarEnabled), fields.Count > 0),
				ColumnsEnabled = Flag(nameof(ColumnsEnabled), fields.Count > 0),
				ShortcutBadgesEnabled = Flag(nameof(ShortcutBadgesEnabled), Number(nameof(ShortcutArrowMode), 0, 0, 2) > 0),
				SidebarCompactShowIcons = Flag(nameof(SidebarCompactShowIcons), true),
				SidebarCompactGroupContents = Flag(nameof(SidebarCompactGroupContents)),
				SidebarCompactIconMode = (int)Number(nameof(SidebarCompactIconMode), 0, 0, 2),
				SidebarCompactHideGroupIcons = Flag(nameof(SidebarCompactHideGroupIcons)),
				SidebarCompactIconSize = (int)Number(nameof(SidebarCompactIconSize), 24, 8, 32),
				SidebarDragEnabled = Flag(nameof(SidebarDragEnabled), true),
				SidebarAppearance = (int)Number(nameof(SidebarAppearance), 0, 0, 1),
				SharpLargeIcons = Flag(nameof(SharpLargeIcons), true),
				SidebarKeepTextPosition = Flag(nameof(SidebarKeepTextPosition), true),
				ShortcutArrowMode = (int)Number(nameof(ShortcutArrowMode), 0, 0, 2),
				ShortcutArrowDesktopSubfolders = Flag(nameof(ShortcutArrowDesktopSubfolders)),
				ShortcutArrowSearchSuggestions = Flag(nameof(ShortcutArrowSearchSuggestions), true),
				ColumnOrder = Text(nameof(ColumnOrder)),
				FitColumnsToWidth = Flag(nameof(FitColumnsToWidth)),
			};
		}

		internal static string RetainActiveFields(string value)
		{
			var activeKeys = new CustomizationPreferences().Serialize()
				.Split('\n')
				.Select(line => line[..line.IndexOf('=')])
				.ToHashSet(StringComparer.Ordinal);
			var retained = value
				.Split('\n')
				.Where(line => line.IndexOf('=') is int separator && separator > 0 && activeKeys.Contains(line[..separator]))
				.ToArray();
			return string.Join("\n", retained);
		}

		public string Serialize()
		{
			string N(double n) => n.ToString(CultureInfo.InvariantCulture);

			var fields = new (string Key, string Value)[]
			{
				(nameof(CustomizationEnabled), CustomizationEnabled.ToString()),
				(nameof(SidebarEnabled), SidebarEnabled.ToString()),
				(nameof(ColumnsEnabled), ColumnsEnabled.ToString()),
				(nameof(ShortcutBadgesEnabled), ShortcutBadgesEnabled.ToString()),
				(nameof(SidebarCompactShowIcons), SidebarCompactShowIcons.ToString()),
				(nameof(SidebarCompactGroupContents), SidebarCompactGroupContents.ToString()),
				(nameof(SidebarCompactIconMode), N(SidebarCompactIconMode)),
				(nameof(SidebarCompactHideGroupIcons), SidebarCompactHideGroupIcons.ToString()),
				(nameof(SidebarCompactIconSize), N(SidebarCompactIconSize)),
				(nameof(SidebarDragEnabled), SidebarDragEnabled.ToString()),
				(nameof(SidebarAppearance), N(SidebarAppearance)),
				(nameof(SharpLargeIcons), SharpLargeIcons.ToString()),
				(nameof(SidebarKeepTextPosition), SidebarKeepTextPosition.ToString()),
				(nameof(ShortcutArrowMode), N(ShortcutArrowMode)),
				(nameof(ShortcutArrowDesktopSubfolders), ShortcutArrowDesktopSubfolders.ToString()),
				(nameof(ShortcutArrowSearchSuggestions), ShortcutArrowSearchSuggestions.ToString()),
				(nameof(ColumnOrder), ColumnOrder),
				(nameof(FitColumnsToWidth), FitColumnsToWidth.ToString()),
			};

			return string.Join("\n", fields.Select(x => x.Key + "=" + Uri.EscapeDataString(x.Value)));
		}
	}
}
