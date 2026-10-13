// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;

namespace Files.App.Data.Models
{
	internal static class CustomShortcutArrowPolicy
	{
		// Mode 1 hides badges of shortcuts on the Desktop (also in its folders with includeSubfolders), mode 2 everywhere.
		internal static bool HideArrow(int mode, string? itemPath, string desktop, string commonDesktop, bool includeSubfolders = false)
		{
			if (mode == 2) return true;
			if (mode != 1 || string.IsNullOrWhiteSpace(itemPath)) return false;
			try
			{
				var parent = Path.GetDirectoryName(itemPath);
				return Matches(parent, desktop) || Matches(parent, commonDesktop) ||
					includeSubfolders && (Inside(parent, desktop) || Inside(parent, commonDesktop));
			}
			catch (ArgumentException) { return false; }
			catch (NotSupportedException) { return false; }
		}

		private static bool Inside(string? folder, string desktop) =>
			!string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(desktop) &&
			folder.StartsWith(desktop.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

		private static bool Matches(string? parent, string desktop) =>
			!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(desktop) &&
			parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(
				desktop.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}
}
