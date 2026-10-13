// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Models
{
	internal sealed class CustomizationChangeHistory<T>
	{
		private readonly Func<T, long>? measure;
		private readonly long maximumWeight;
		internal CustomizationChangeHistory(Func<T, long>? measure = null, long maximumWeight = long.MaxValue)
		{
			this.measure = measure;
			this.maximumWeight = maximumWeight;
		}
		private readonly List<(T Before, T After)> undo = [];
		private readonly List<(T Before, T After)> redo = [];
		private static bool Same(T left, T right) => EqualityComparer<T>.Default.Equals(left, right);
		internal bool CanUndo(T current) => undo.Count > 0 && Same(undo[^1].After, current);
		internal bool CanRedo(T current) => redo.Count > 0 && Same(redo[^1].Before, current);
		internal void Record(T before, T after)
		{
			if (Same(before, after)) return;
			if (undo.Count > 0 && !Same(undo[^1].After, before)) undo.Clear();
			redo.Clear(); undo.Add((before, after));
			if (undo.Count > 50) undo.RemoveAt(0);
			if (measure is not null)
			{
				long weight = undo.Sum(change => measure(change.Before) + measure(change.After));
				while (undo.Count > 1 && weight > maximumWeight)
				{
					weight -= measure(undo[0].Before) + measure(undo[0].After);
					undo.RemoveAt(0);
				}
			}
		}
		internal bool Undo(T current, out T result)
		{
			result = current;
			if (!CanUndo(current)) return false;
			var change = undo[^1]; undo.RemoveAt(undo.Count - 1); redo.Add(change);
			result = change.Before; return true;
		}
		internal bool Redo(T current, out T result)
		{
			result = current;
			if (!CanRedo(current)) return false;
			var change = redo[^1]; redo.RemoveAt(redo.Count - 1); undo.Add(change);
			result = change.After; return true;
		}
	}
}
