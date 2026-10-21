// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Globalization;

namespace Files.App.Data.Models
{
	internal sealed class SidebarCustomizationState
	{
		internal sealed record Entry(
			string Key,
			string Label = "",
			string Target = "",
			string Group = "",
			int Order = int.MaxValue,
			int Size = -1,
			bool HideIcon = false,
			double TextPt = 0,
			double IconPt = 0,
			int Gap = 12,
			bool Separator = false,
			bool HideArrow = false,
			int ExpansionLock = 0,
			bool Hidden = false,
			bool SizesLinked = true,
			int? LegacyExpansion = null,
			bool HidePin = false,
			string IconSource = "",
			string SourceKey = "",
			// A pinned folder's file ID or a drive's volume serial number, so the entry follows it under another path.
			string Id = "");
		public Dictionary<string, Entry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

		private sealed record ParsedSidebar(string Source, SidebarCustomizationState Value);
		private static ParsedSidebar? lastParsed;
		internal static SidebarCustomizationState ReadSnapshot(string value)
		{
			var cached = System.Threading.Volatile.Read(ref lastParsed);
			if (cached is not null && cached.Source == value) return cached.Value;
			var parsed = ParseCore(value);
			System.Threading.Volatile.Write(ref lastParsed, new ParsedSidebar(value, parsed));
			return parsed;
		}

		public static SidebarCustomizationState Parse(string value)
		{
			var editable = new SidebarCustomizationState();
			foreach (var pair in ReadSnapshot(value).Entries) editable.Entries.Add(pair.Key, pair.Value);
			return editable;
		}

		private static SidebarCustomizationState ParseCore(string value)
		{
			var state = new SidebarCustomizationState();
			foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				var fields = line.Split('\t');
				// Saved layouts have had 7, 12 or 14 to 23 fields over time. The 23rd (a retired per-item spacing) is ignored.
				if (fields.Length is not (7 or 12 or (>= 14 and <= 23)) ||
					!int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) ||
					!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
					continue;
				try
				{
					var entry = new Entry(Uri.UnescapeDataString(fields[0]), Uri.UnescapeDataString(fields[1]),
						Uri.UnescapeDataString(fields[2]), Uri.UnescapeDataString(fields[4]), order, Math.Clamp(size, -1, 3));
					if (fields.Length >= 12)
					{
						double Point(int index, double maximum) =>
							double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
								? Math.Clamp(value, 0, maximum)
								: 0;
						entry = entry with
						{
							HideIcon = fields[7] == "1",
							TextPt = Point(8, 36),
							IconPt = Point(9, 48),
							Gap = int.TryParse(fields[10], out var gap) ? Math.Clamp(gap, 4, 64) : 12,
							Separator = fields[11] == "1"
						};
					}
					if (fields.Length >= 14)
					{
						var hidden = fields[12] == "1" || fields[13] == "2";
						var expanded = fields[13] == "1";
						entry = entry with { HideArrow = hidden, ExpansionLock = hidden ? 2 : expanded ? 1 : 0 };
					}
					if (fields.Length >= 15) entry = entry with { Hidden = fields[14] == "1" };
					if (fields.Length >= 17) entry = entry with { SizesLinked = fields[16] != "0" };
					if (fields.Length >= 18 &&
						int.TryParse(fields[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out var legacyExpansion) &&
						legacyExpansion is >= 0 and <= 2)
						entry = entry with { LegacyExpansion = legacyExpansion };
					if (fields.Length >= 19) entry = entry with { HidePin = fields[18] == "1" };
					if (fields.Length >= 20) entry = entry with { IconSource = Uri.UnescapeDataString(fields[19]) };
					if (fields.Length >= 21) entry = entry with { SourceKey = Uri.UnescapeDataString(fields[20]) };
					if (fields.Length >= 22) entry = entry with { Id = Uri.UnescapeDataString(fields[21]) };
					if (!string.IsNullOrWhiteSpace(entry.Key))
						state.Entries[entry.Key] = entry;
				}
				catch (UriFormatException) { }
			}
			return state;
		}

		internal bool HasSameStructure(SidebarCustomizationState other)
		{
			if (Entries.Count != other.Entries.Count) return false;
			foreach (var pair in Entries)
			{
				if (!other.Entries.TryGetValue(pair.Key, out var entry) ||
					pair.Value.Group != entry.Group ||
					pair.Value.Order != entry.Order ||
					pair.Value.Target != entry.Target ||
					pair.Value.SourceKey != entry.SourceKey ||
					pair.Value.Hidden != entry.Hidden)
					return false;
			}
			return true;
		}

		public Entry Get(string key) => Entries.TryGetValue(key, out var entry) ? entry : new(key);

		// Keep retired field positions empty so earlier saved layouts remain readable.
		public string Serialize() => string.Join("\n", Entries.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x =>
			string.Join("\t", Fields(x))));

		private static IEnumerable<string> Fields(Entry x) =>
			[
				Uri.EscapeDataString(x.Key),
				Uri.EscapeDataString(x.Label),
				Uri.EscapeDataString(x.Target),
				"",
				Uri.EscapeDataString(x.Group),
				x.Order.ToString(CultureInfo.InvariantCulture),
				x.Size.ToString(CultureInfo.InvariantCulture),
				x.HideIcon ? "1" : "0",
				x.TextPt.ToString(CultureInfo.InvariantCulture),
				x.IconPt.ToString(CultureInfo.InvariantCulture),
				x.Gap.ToString(CultureInfo.InvariantCulture),
				x.Separator ? "1" : "0",
				x.HideArrow ? "1" : "0",
				x.ExpansionLock.ToString(CultureInfo.InvariantCulture),
				x.Hidden ? "1" : "0",
				"0",
				x.SizesLinked ? "1" : "0",
				x.LegacyExpansion?.ToString(CultureInfo.InvariantCulture) ?? "",
				x.HidePin ? "1" : "0",
				Uri.EscapeDataString(x.IconSource),
				Uri.EscapeDataString(x.SourceKey),
				Uri.EscapeDataString(x.Id)
			];
	}
}
