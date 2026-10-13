// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace Files.App.Data.Models
{
	internal static class SidebarPreviewHierarchy
	{
		internal static HashSet<string> VisibleKeys(IEnumerable<(string Key, string Parent)> rows, IReadOnlySet<string> collapsed)
		{
			var parents = rows.ToDictionary(row => row.Key, row => row.Parent, StringComparer.OrdinalIgnoreCase);
			var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var row in parents)
			{
				var ancestor = row.Value;
				var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.Key };
				var hidden = false;
				while (parents.TryGetValue(ancestor, out var parent))
				{
					// Invalid saved links must not hide the item or unrelated rows.
					if (!visited.Add(ancestor)) { hidden = false; break; }
					hidden |= collapsed.Contains(ancestor);
					ancestor = parent;
				}
				if (!hidden) visible.Add(row.Key);
			}
			return visible;
		}
	}
}
