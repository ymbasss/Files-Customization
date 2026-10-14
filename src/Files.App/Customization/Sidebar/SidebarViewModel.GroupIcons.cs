// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		internal static readonly (SectionType Section, string Label)[] EditorGroupIcons =
		[
			(SectionType.Home, Strings.Home), (SectionType.Pinned, Strings.Pinned),
			(SectionType.Library, Strings.SidebarLibraries), (SectionType.Drives, Strings.Drives),
			(SectionType.CloudDrives, Strings.SidebarCloudDrives), (SectionType.Network, Strings.Network),
			(SectionType.WSL, Strings.WSL), (SectionType.FileTag, Strings.FileTags)
		];

		// Imported icons are copied here, so moving or deleting the original file doesn't lose a group's icon.
		// Each copy sits in a folder named after its content, which keeps the original file name and stores the same image only once.
		private static string EditorIconFolder => Path.Combine(ApplicationData.Current.LocalFolder.Path, "SidebarIcons");

		internal static bool IsStoredEditorIcon(string source)
		{
			if (!source.StartsWith("file:", StringComparison.Ordinal))
				return false;

			try
			{
				return Path.GetFullPath(source[5..]).StartsWith(EditorIconFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception) { return false; }
		}

		// Copies an imported icon into the app's data folder and returns the copy's path, or null if it couldn't be copied.
		internal static async Task<string?> StoreEditorIconAsync(string path)
		{
			try
			{
				var bytes = await File.ReadAllBytesAsync(path);
				var folder = Path.Combine(EditorIconFolder, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16]);
				var target = Path.Combine(folder, Path.GetFileName(path));
				if (!File.Exists(target))
				{
					Directory.CreateDirectory(folder);
					await File.WriteAllBytesAsync(target, bytes);
				}
				// Imported again after it was deleted: it's kept.
				File.Delete(target + DeletedIconMarker);

				return target;
			}
			catch (Exception ex)
			{
				App.Logger?.LogWarning(ex, "Sidebar icon could not be copied to the app data folder");
				return null;
			}
		}

		// A deleted icon is only marked, with a ".deleted" file next to it, and left out of the list, so Undo can bring it back.
		// The file itself is removed at the next start of the app, if nothing uses it by then.
		private const string DeletedIconMarker = ".deleted";
		private static bool deletedIconsRemoved;

		// Deletes a stored icon; groups using it go back to the default icon.
		internal void DeleteEditorIcon(string source)
		{
			if (!IsStoredEditorIcon(source))
				return;

			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var changed = false;
			foreach (var entry in state.Entries.Values.ToArray())
			{
				if (!string.Equals(entry.IconSource, source, StringComparison.OrdinalIgnoreCase))
					continue;

				state.Entries[entry.Key] = entry with { IconSource = "" };
				changed = true;
			}

			if (changed)
			{
				SaveVisualPreferences(state);
			}

			try
			{
				File.WriteAllText(source[5..] + DeletedIconMarker, "");
			}
			catch (Exception ex)
			{
				App.Logger?.LogWarning(ex, "Stored sidebar icon could not be marked as deleted");
			}
		}

		// Every stored icon plus any icon a group still uses from elsewhere, so any group can reuse one imported for another.
		internal IReadOnlyList<string> EditorImportedIcons()
		{
			var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var used = SidebarCustomizationState.ReadSnapshot(UserSettingsService.AppearanceSettingsService.SidebarCustomization).Entries.Values
				.Select(entry => entry.IconSource).Where(source => source.StartsWith("file:", StringComparison.Ordinal))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (Directory.Exists(EditorIconFolder))
				{
					foreach (var file in Directory.EnumerateFiles(EditorIconFolder, "*", SearchOption.AllDirectories).ToArray())
					{
						if (file.EndsWith(DeletedIconMarker, StringComparison.OrdinalIgnoreCase)) continue;
						if (!File.Exists(file + DeletedIconMarker)) sources.Add("file:" + file);
						// First listing since the app started: no Undo can bring back icons deleted earlier.
						else if (!deletedIconsRemoved && !used.Contains("file:" + file)) RemoveDeletedIcon(file);
					}
					deletedIconsRemoved = true;
				}
			}
			catch (Exception ex)
			{
				App.Logger?.LogWarning(ex, "Stored sidebar icons could not be listed");
			}

			sources.UnionWith(used);

			return sources
				.OrderBy(source => Path.GetFileName(source[5..]), StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(source => source, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static void RemoveDeletedIcon(string path)
		{
			try
			{
				File.Delete(path);
				File.Delete(path + DeletedIconMarker);
				if (Path.GetDirectoryName(path) is { } folder && !Directory.EnumerateFileSystemEntries(folder).Any())
					Directory.Delete(folder);
			}
			catch (Exception ex)
			{
				App.Logger?.LogWarning(ex, "Stored sidebar icon could not be deleted");
			}
		}

		private SidebarVisualShortcut CreateVisualShortcut(SidebarCustomizationState.Entry entry)
		{
			var source = ResolveVisualShortcutSource(entry.SourceKey);
			// Pinned entries stay flat even before their source has loaded.
			var expandable = source is IExpandableSidebarFolder folder ? folder.IsExpandableFolder :
				entry.SourceKey.Length > 0 && !entry.SourceKey.StartsWith("item:Pinned:", StringComparison.Ordinal) &&
				Path.IsPathFullyQualified(entry.Target);
			return new()
			{
				Path = entry.Key, Target = entry.Target, Text = entry.Label, ToolTip = entry.Target,
				Section = SectionType.Pinned, SelectsOnInvoked = entry.Target.Length > 0,
				SourceItem = source,
				IsExpandableFolder = expandable, HasUnrealizedChildren = expandable,
				Icon = source is null ? null : GetVisualIcon(source),
				MenuOptions = new ContextMenuOptions()
			};
		}

		private INavigationControlItem? ResolveVisualShortcutSource(string key)
		{
			if (key.Length == 0) return null;
			if (key == VisualKey(SettingsSidebarItem)) return SettingsSidebarItem;
			return VisualItems().FirstOrDefault(item => VisualKey(item) == key) ??
				App.QuickAccessManager?.Model?.PinnedFolderItems.FirstOrDefault(item => VisualKey(item) == key);
		}

		private void ApplyGroupIcon(SidebarVisualGroup group, string source)
		{
			if (source.StartsWith("app:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(source[4..], out var section) &&
				EditorGroupIcons.Any(icon => icon.Section == section) && SidebarSectionIcons.For(section) is { } uri)
			{
				group.IconSource = source;
				if (group.Icon?.UriSource?.AbsoluteUri != uri) group.Icon = new BitmapImage(new Uri(uri));
				return;
			}
			if (group.IconSource == source) return;
			group.IconSource = source;
			group.Icon = null;
			if (source.StartsWith("file:", StringComparison.Ordinal)) _ = LoadGroupIconAsync(group, source);
		}

		private async Task LoadGroupIconAsync(SidebarVisualGroup group, string source)
		{
			var bitmap = await LoadEditorIconAsync(source[5..]);
			if (group.IconSource != source || !VisualItems().Contains(group)) return;
			group.Icon = bitmap;
			NotifyEditorIconChanged();
		}

		internal static async Task<BitmapImage?> LoadEditorIconAsync(string path)
		{
			try
			{
				if (!Path.IsPathFullyQualified(path)) return null;
				var file = await StorageFile.GetFileFromPathAsync(path);
				using var stream = await file.OpenReadAsync();
				var decoder = await BitmapDecoder.CreateAsync(stream);
				IBitmapFrame frame = decoder;
				// Decode the artwork itself, using the largest ICO frame rather than a shell file icon.
				if (decoder.DecoderInformation.CodecId == BitmapDecoder.IcoDecoderId)
				{
					for (uint index = 1; index < decoder.FrameCount; index++)
					{
						var candidate = await decoder.GetFrameAsync(index);
						if ((ulong)candidate.PixelWidth * candidate.PixelHeight > (ulong)frame.PixelWidth * frame.PixelHeight)
							frame = candidate;
					}
				}
				if (frame.PixelWidth == 0 || frame.PixelHeight == 0) return null;
				var scale = Math.Min(1, 256d / Math.Max(frame.PixelWidth, frame.PixelHeight));
				var transform = new BitmapTransform
				{
					ScaledWidth = (uint)Math.Max(1, Math.Round(frame.PixelWidth * scale)),
					ScaledHeight = (uint)Math.Max(1, Math.Round(frame.PixelHeight * scale)),
					InterpolationMode = BitmapInterpolationMode.Fant
				};
				var width = (int)Math.Max(1, Math.Round(frame.OrientedPixelWidth * scale));
				var height = (int)Math.Max(1, Math.Round(frame.OrientedPixelHeight * scale));
				var pixels = (await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
					transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb)).DetachPixelData();
				// Transparent canvas must not shrink the visible icon inside its sidebar slot.
				// Nearly invisible pixels (soft shadows and glows) count as empty, so they don't keep the padding.
				const byte visibleAlpha = 24;
				var left = width;
				var top = height;
				var right = -1;
				var bottom = -1;
				for (var y = 0; y < height; y++)
					for (var x = 0; x < width; x++)
					{
						if (pixels[(y * width + x) * 4 + 3] < visibleAlpha) continue;
						left = Math.Min(left, x); top = Math.Min(top, y);
						right = Math.Max(right, x); bottom = Math.Max(bottom, y);
					}
				if (right < left || bottom < top) return null;
				using var output = new InMemoryRandomAccessStream();
				var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
				encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels);
				encoder.BitmapTransform.Bounds = new BitmapBounds
				{
					X = (uint)left, Y = (uint)top,
					Width = (uint)(right - left + 1), Height = (uint)(bottom - top + 1)
				};
				await encoder.FlushAsync();
				output.Seek(0);
				var bitmap = new BitmapImage();
				await bitmap.SetSourceAsync(output);
				return bitmap;
			}
			catch (Exception ex)
			{
				App.Logger?.LogDebug(ex, "Could not load the sidebar group icon.");
				return null;
			}
		}
	}
}
