// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Models
{
	internal static class CustomColumnOrder
	{
		internal static readonly string[] Keys =
		[
			"Name", "GitStatus", "GitLastCommitDate", "GitLastCommitMessage", "GitCommitAuthor", "GitLastCommitSha",
			"Tag", "Path", "OriginalPath", "DateDeleted", "DateModified", "DateCreated", "ItemType", "Size", "Status"
		];

		private sealed record ParsedOrder(string Source, int[] Value);
		private static ParsedOrder? lastParsed;
		internal static int[] Parse(string value)
		{
			var cached = System.Threading.Volatile.Read(ref lastParsed);
			if (cached is not null && cached.Source == value) return cached.Value;
			var parsed = ParseCore(value);
			System.Threading.Volatile.Write(ref lastParsed, new ParsedOrder(value, parsed));
			return parsed;
		}

		private static int[] ParseCore(string value)
		{
			var result = new List<int>();
			foreach (var key in value.Split(','))
			{
				var index = Array.IndexOf(Keys, key);
				if (index >= 0 && !result.Contains(index)) result.Add(index);
			}
			for (int index = 0; index < Keys.Length; index++)
				if (!result.Contains(index)) result.Add(index);
			return result.ToArray();
		}

		internal static string Serialize(IEnumerable<int> order) => string.Join(",", order.Select(index => Keys[index]));

		// Moves a column past the next column that is shown. Columns not shown in this folder (Sync status outside cloud
		// folders, Git columns outside repositories) aren't swapped around; they keep their place next to the same columns.
		internal static List<int> MoveShown(IEnumerable<int> order, int column, int direction, Func<int, bool> shown)
		{
			var result = order.ToList();
			var visible = result.Where(shown).ToList();
			var position = visible.IndexOf(column);
			var destination = position + direction;
			if (position < 0 || direction is not (-1 or 1) || destination < 0 || destination >= visible.Count)
				return result;
			var neighbour = visible[destination];
			result.Remove(column);
			var at = result.IndexOf(neighbour);
			result.Insert(direction < 0 ? at : at + 1, column);
			return result;
		}
	}
}
