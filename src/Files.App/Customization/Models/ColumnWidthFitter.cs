// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Data.Models
{
	internal static class ColumnWidthFitter
	{
		// Each column starts at its preferred width (its content). The space left in the view is shared equally between the
		// columns; an overflow is taken from them in proportion to their widths, so narrow columns keep their size longer.
		// Both stay within each column's minimum and maximum.
		internal static double[] Fit(double width, double[] preferred, double[] minimum, double[] maximum)
		{
			if (preferred.Length != minimum.Length || preferred.Length != maximum.Length) throw new ArgumentException("Column dimensions must match.");
			if (!double.IsFinite(width) || width < 0) throw new ArgumentOutOfRangeException(nameof(width));
			var result = new double[preferred.Length];
			for (int i = 0; i < result.Length; i++)
			{
				if (!double.IsFinite(preferred[i]) || !double.IsFinite(minimum[i]) || !double.IsFinite(maximum[i]) ||
					minimum[i] < 0 || maximum[i] < minimum[i])
					throw new ArgumentOutOfRangeException(nameof(preferred));
				result[i] = Math.Clamp(preferred[i], minimum[i], maximum[i]);
			}
			var difference = width - result.Sum();
			var active = Enumerable.Range(0, result.Length).ToList();
			while (Math.Abs(difference) > 0.001)
			{
				// Columns already at their limit in this direction take no more.
				active.RemoveAll(i => difference > 0 ? maximum[i] - result[i] < 0.001 : result[i] - minimum[i] < 0.001);
				if (active.Count == 0) break;
				var total = active.Sum(i => result[i]);
				var applied = 0d;
				foreach (var i in active)
				{
					var share = difference > 0 || total <= 0 ? difference / active.Count : difference * result[i] / total;
					var next = Math.Clamp(result[i] + share, minimum[i], maximum[i]);
					applied += next - result[i];
					result[i] = next;
				}
				difference -= applied;
				if (Math.Abs(applied) < 0.001) break;
			}
			return result;
		}
	}
}
