// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.App.Controls;
using System.IO;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		private const string VisualRootKey = "sidebar-root:";
		private bool restoringVisualPlacements;
		private readonly Dictionary<string, (string Parent, int Index)> originalVisualPlacements = new(StringComparer.OrdinalIgnoreCase);

		private void RememberVisualPlacement(INavigationControlItem item)
		{
			var key = VisualKey(item);
			if (key.StartsWith("sidebar-", StringComparison.Ordinal) || ReferenceEquals(item, SettingsSidebarItem) || originalVisualPlacements.ContainsKey(key)) return;
			if (IsVisualSubFolder(item))
				return;
			var siblings = FindVisualSiblings(item);
			if (siblings is null) return;
			var parent = VisualItems().FirstOrDefault(candidate => ReferenceEquals(candidate.Children, siblings));
			originalVisualPlacements[key] = (parent is null ? VisualRootKey : VisualKey(parent), siblings.IndexOf(item));
		}

		private void RestoreOriginalVisualPlacements(SidebarCustomizationState state)
		{
			foreach (var pair in originalVisualPlacements)
			{
				if (state.Get(pair.Key).Group.Length > 0) continue;
				var items = VisualItems();
				var item = items.FirstOrDefault(candidate => string.Equals(VisualKey(candidate), pair.Key, StringComparison.OrdinalIgnoreCase));
				if (item is null) continue;
				var parent = items.FirstOrDefault(candidate => string.Equals(VisualKey(candidate), pair.Value.Parent, StringComparison.OrdinalIgnoreCase));
				var destination = pair.Value.Parent == VisualRootKey ? sidebarItems : parent?.Children as BulkConcurrentObservableCollection<INavigationControlItem>;
				if (destination is null) continue;
				var origin = FindVisualSiblings(item);
				if (ReferenceEquals(origin, destination) && state.Get(pair.Key).Order != int.MaxValue) continue;
				origin?.Remove(item); destination.Insert(Math.Clamp(pair.Value.Index, 0, destination.Count), item);
			}
		}

		// Removes moved items that no section's list holds any more, such as an unpinned folder placed in a custom group or an
		// unplugged drive moved to the top level. Items still in their own section are left to the app's code. Their saved place
		// is kept: right after Windows starts, Quick Access can briefly report no pinned folders, and the folders must come back
		// where they were.
		private void RemoveMovedSectionItems()
		{
			if (!SidebarCustomizationEnabled) return;
			var state = ReadCustomization;
			foreach (var item in VisualItems())
			{
				if (item is SidebarVisualGroup or SidebarVisualDecoration or SidebarVisualShortcut or LocationItem { IsHeader: true } ||
					ReferenceEquals(item, SettingsSidebarItem) || IsVisualSubFolder(item) ||
					state.Get(VisualKey(item)).Group.Length == 0 ||
					SectionOrder.Any(section => IsInSectionList(item, section)))
					continue;

				if (item is ExpandableSidebarItemBase expandable)
					expandable.StopWatchingSubfoldersAndDescendants();
				FindVisualSiblings(item)?.Remove(item);
			}
		}

		// An item moved here from another section (a drive placed in Pinned) isn't in this section's list; a reload must not drop it.
		private bool IsMovedFromOtherSection(INavigationControlItem item, LocationItem section)
			=> SidebarCustomizationEnabled && SectionOrder.Any(other => other != section.Section && IsInSectionList(item, other));

		// Whether a section's own list (its manager) holds the item. Items are matched by this, not by Section: drives, cloud
		// drives, network, WSL and tags leave Section at Home.
		private bool IsInSectionList(INavigationControlItem item, SectionType section)
		{
			IEnumerable<object> elements = section switch
			{
				SectionType.Pinned => App.QuickAccessManager.Model.PinnedFolderItems,
				SectionType.Library => App.LibraryManager.Libraries,
				SectionType.Drives => drivesViewModel.Drives,
				SectionType.CloudDrives => CloudDrivesManager.Drives,
				SectionType.Network => NetworkService.Computers,
				SectionType.WSL => WSLDistroManager.Distros,
				SectionType.FileTag => App.FileTagsManager.FileTags,
				_ => []
			};
			return elements.Contains(item);
		}

		private INavigationControlItem[] VisualItems()
		{
			var items = new List<INavigationControlItem>();
			var visited = new HashSet<INavigationControlItem>();
			void Visit(IEnumerable<INavigationControlItem> children)
			{
				foreach (var item in children.ToArray())
				{
					if (!visited.Add(item)) continue;
					items.Add(item);
					if (item.Children is IEnumerable<INavigationControlItem> nested) Visit(nested);
				}
			}
			Visit(sidebarItems);
			return items.ToArray();
		}

		private static bool IsVisualDescendant(INavigationControlItem source, INavigationControlItem target)
		{
			var pending = new Stack<INavigationControlItem>();
			var visited = new HashSet<INavigationControlItem>();
			pending.Push(source);
			while (pending.TryPop(out var item))
			{
				if (!visited.Add(item)) continue;
				if (ReferenceEquals(item, target)) return true;
				if (item.Children is IEnumerable<INavigationControlItem> children)
					foreach (var child in children) pending.Push(child);
			}
			return false;
		}

		private bool TryGetVisualDrop(string? key, object target, SidebarItemDropPosition position,
			out INavigationControlItem? source, out BulkConcurrentObservableCollection<INavigationControlItem>? destination,
			out INavigationControlItem? parent, out int index)
		{
			source = null; destination = null; parent = null; index = 0;
			if (!CurrentOptions.SidebarDragEnabled ||
				key is null || target is not INavigationControlItem item) return false;
			var items = VisualItems();
			source = items.FirstOrDefault(candidate => VisualKey(candidate) == key);
			if (source is null || IsVisualDescendant(source, item)) return false;
			// Sub-folders keep the folder order; nothing can be moved among them.
			if (IsVisualSubFolder(source) || IsVisualSubFolder(item))
				return false;
			if (position == SidebarItemDropPosition.Center)
			{
				if (!IsCustomizationGroup(item))
					return false;
				destination = item.Children as BulkConcurrentObservableCollection<INavigationControlItem>;
				parent = item;
				if (destination is null) return false;
				index = destination.Count;
			}
			else
			{
				destination = FindVisualSiblings(item);
				if (destination is null) return false;
				var collection = destination;
				parent = ReferenceEquals(collection, sidebarItems) ? null : items.FirstOrDefault(candidate => ReferenceEquals(candidate.Children, collection));
				index = destination.IndexOf(item) + (position == SidebarItemDropPosition.Bottom ? 1 : 0);
			}
			// Groups (built-in sections and added groups) only sit at the top level, never inside another group.
			if (IsCustomizationGroup(source) && parent is not null) return false;
			return parent is null || !IsVisualDescendant(source, parent);
		}

		private void MoveVisualDrop(string? key, object target, SidebarItemDropPosition position)
		{
			if (!TryGetVisualDrop(key, target, position, out var source, out var destination, out var parent, out var index)) return;
			var origin = FindVisualSiblings(source!);
			if (origin is null || destination is null) return;
			var oldIndex = origin.IndexOf(source!);
			RememberVisualPlacement(source!);
			if (ReferenceEquals(origin, destination) && oldIndex < index) index--;
			visualReorderDepth++;
			try
			{
				origin.RemoveAt(oldIndex);
				destination.Insert(Math.Clamp(index, 0, destination.Count), source!);
			}
			finally { visualReorderDepth--; }
			var state = Customization;
			var entry = state.Get(VisualKey(source!));
			state.Entries[entry.Key] = entry with
			{
				Group = parent is null ? VisualRootKey : VisualKey(parent),
				Target = entry.Target.Length > 0 ? entry.Target : VisualTarget(source!) ?? "",
				Label = entry.Label.Length > 0 ? entry.Label : source!.Text ?? ""
			};
			foreach (var collection in new[] { origin, destination }.Distinct())
				for (var i = 0; i < collection.Count; i++)
				{
					var saved = state.Get(VisualKey(collection[i]));
					state.Entries[saved.Key] = saved with { Order = i };
				}
			SaveVisualPreferences(state);
		}

		// Items were added to a list. Sub-folders keep the folder order and are never placed, so a list of new sub-folders is left to the
		// original code, which adds just their rows. Other additions are sorted, and one placement pass is queued for all of them.
		private bool visualPlacementsQueued;
		private void PlaceAddedVisualItems(object? list, System.Collections.IList added)
		{
			if (added.OfType<INavigationControlItem>().All(IsVisualSubFolder)) return;
			if (list is BulkConcurrentObservableCollection<INavigationControlItem> children) ApplyVisualOrder(children);
			if (visualPlacementsQueued || dispatcherQueue is null) return;
			visualPlacementsQueued = true;
			dispatcherQueue.TryEnqueue(() =>
			{
				visualPlacementsQueued = false;
				RestoreVisualPlacements();
			});
		}

		// Pinned folders and drives are saved by path. Their entries also keep the folder's file ID or the drive's volume serial number,
		// so a renamed or moved pinned folder, or a drive under another letter, keeps its settings, and another drive given the same
		// letter starts without them. Tags are saved by name and keep the tag's own ID, so a renamed tag keeps its settings too.
		private readonly List<INavigationControlItem> pendingVisualIds = [];
		private bool visualIdsQueued;

		private void QueueVisualId(INavigationControlItem item)
		{
			var tag = item is FileTagItem { FileTag.Uid.Length: > 0 };
			if (!tag && (!(item is DriveItem && IsInSectionList(item, SectionType.Drives) || item is LocationItem { IsHeader: false, Section: SectionType.Pinned }) ||
				item is SidebarVisualGroup or SidebarVisualShortcut or SidebarVisualDecoration ||
				item.Path is not { } path || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal)))
				return;
			pendingVisualIds.Add(item);
			if (visualIdsQueued || dispatcherQueue is null) return;
			visualIdsQueued = true;
			dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MatchVisualIds);
		}

		private async void MatchVisualIds()
		{
			var items = pendingVisualIds.Select(item => (Key: VisualKey(item), Path: item.Path!, Drive: item is DriveItem, Tag: (item as FileTagItem)?.FileTag?.Uid)).Distinct().ToArray();
			pendingVisualIds.Clear();
			visualIdsQueued = false;
			var ids = await Task.Run(() => items.Select(item => (item.Key, Id: item.Tag is { } uid ? "tag:" + uid : VisualId(item.Path, item.Drive))).ToArray());
			if (!SidebarCustomizationEnabled) return;
			var state = Customization;
			var present = VisualItems().Select(VisualKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var changed = false;
			var renames = new List<(string From, string To)>();
			foreach (var (key, id) in ids)
				if (id is not null) changed |= MatchVisualId(state, key, id, present, renames);
			if (!changed) return;
			// The panel's Undo history and open panels follow the new keys; Undo doesn't reverse what the app did by itself.
			if (renames.Count > 0)
			{
				EditorHistory.Map(snapshot => snapshot with { Sidebar = RenameEditorEntries(snapshot.Sidebar, renames) });
				EditorEntriesRenamed?.Invoke(renames);
			}
			SaveVisualPreferences(state);
		}

		private static string? VisualId(string path, bool drive)
		{
			try
			{
				// A file ID is only unique on its own drive (every NTFS drive's root folder has the same one), so folders also keep
				// their drive's serial number.
				if (!global::Windows.Win32.PInvoke.GetVolumeInformation(Path.GetPathRoot(path) ?? path, [], out var serial, out _, out _, []))
					return null;
				if (drive)
					return "vol:" + serial.ToString("X8");
				return Win32Helper.GetFolderFRN(path) is { } frn ? "frn:" + serial.ToString("X8") + ":" + frn : null;
			}
			catch (Exception) { return null; }
		}

		private static bool MatchVisualId(SidebarCustomizationState state, string key, string id, HashSet<string> present, List<(string From, string To)> renames)
		{
			var prefix = key[..(key.IndexOf(':', 5) + 1)];
			var changed = false;
			var match = state.Entries.Values.FirstOrDefault(entry => entry.Id == id &&
				entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !present.Contains(entry.Key));
			if (state.Entries.TryGetValue(key, out var own))
			{
				// Saved under the same name in other capitals (a tag renamed from "Photos" to "photos"): moved to the new spelling
				// like any rename, so the panel and the duplicates' paths follow it too.
				if (own.Key != key)
				{
					RenameVisualEntry(state, own.Key, key);
					renames.Add((own.Key, key));
					own = state.Entries[key];
					changed = true;
				}
				if (own.Id == id) return changed;
				// No ID yet, a folder ID saved without its drive's serial number, or a folder made again at the same path (it gets a new
				// file ID): the path keeps its settings. Only a drive, or a folder whose own entry was saved elsewhere, puts this one aside.
				if (own.Id.Length == 0 || own.Id.StartsWith("frn:", StringComparison.Ordinal) && (own.Id.IndexOf(':', 4) < 0 || match is null))
				{
					state.Entries[key] = own with { Id = id };
					return true;
				}
				// Saved for another folder or drive that had this path: put aside under its ID until that one shows up again.
				var aside = prefix + "#" + own.Id + "#" + key[prefix.Length..];
				RenameVisualEntry(state, key, aside);
				renames.Add((key, aside));
				changed = true;
			}
			if (match is null) return changed;
			RenameVisualEntry(state, match.Key, key);
			renames.Add((match.Key, key));
			return true;
		}

		// Moves an entry to another key, with the entries that point to it: its separate folder appearance and shortcuts made from it.
		private static void RenameVisualEntry(SidebarCustomizationState state, string from, string to)
		{
			if (!state.Entries.Remove(from, out var entry)) return;
			var fromPath = VisualEntryPath(from);
			var toPath = VisualEntryPath(to);
			string Retarget(string target) => fromPath is not null && toPath is not null && string.Equals(target, fromPath, StringComparison.OrdinalIgnoreCase) ? toPath : target;
			state.Entries[to] = entry with { Key = to, Target = Retarget(entry.Target) };
			if (state.Entries.Remove(EditorFolderKey(from), out var folders))
				state.Entries[EditorFolderKey(to)] = folders with { Key = EditorFolderKey(to) };
			foreach (var other in state.Entries.Values.Where(other => string.Equals(other.SourceKey, from, StringComparison.OrdinalIgnoreCase)).ToArray())
				state.Entries[other.Key] = other with { SourceKey = to, Target = Retarget(other.Target) };
		}

		// The folder or drive path (or a tag's "tag:" path) in an item's key; null for an entry put aside under its ID.
		private static string? VisualEntryPath(string key)
		{
			var split = key.IndexOf(':', 5);
			return split > 0 && (Path.IsPathRooted(key[(split + 1)..]) || key[(split + 1)..].StartsWith("tag:", StringComparison.Ordinal)) ? key[(split + 1)..] : null;
		}

		// force: switching customization off puts the sections back in their original order.
		private void RestoreVisualPlacements(bool force = false)
		{
			if (!force && !SidebarCustomizationEnabled) return;
			if (restoringVisualPlacements) return;
			restoringVisualPlacements = true;
			visualReorderDepth++;
			try
			{
				var state = Customization;
				if (state.Entries.Count == 0)
				{
					var defaults = sidebarItems.OrderBy(item => Array.IndexOf(SectionOrder, item.Section)).ToArray();
					for (var i = 0; i < defaults.Length; i++) { sidebarItems.Remove(defaults[i]); sidebarItems.Insert(i, defaults[i]); }
				}
				foreach (var entry in state.Entries.Values.Where(entry => entry.Group.Length > 0).OrderBy(entry => entry.Order))
				{
					var items = VisualItems();
					var source = items.FirstOrDefault(item => string.Equals(VisualKey(item), entry.Key, StringComparison.OrdinalIgnoreCase)) ?? (entry.Key == VisualKey(SettingsSidebarItem) ? SettingsSidebarItem : null);
					// Older saved placements for sub-folders are ignored. Checked on the item, not its key: drives, cloud drives and
					// network keep Section at Home, so their keys look like sub-folders' ("item:Home:D:\").
					if (source is not null && IsVisualSubFolder(source))
						continue;
					var parent = entry.Group == VisualRootKey ? null : items.FirstOrDefault(item => string.Equals(VisualKey(item), entry.Group, StringComparison.OrdinalIgnoreCase));
					if (entry.Group != VisualRootKey && parent is null) continue;
					var destination = parent is null ? sidebarItems : parent.Children as BulkConcurrentObservableCollection<INavigationControlItem>;
					if (destination is null) continue;
					if (source is null)
					{
						source = CreateVisualItem(entry);
						if (source is not null) TrackSidebarItem(source);
					}
					// Folders, drives and sections are placed when the app has loaded them (see IsAwaitingPlacement).
					if (source is null || parent is not null && IsVisualDescendant(source, parent)) continue;
					foreach (var duplicate in items.Where(item => !ReferenceEquals(item, source) && string.Equals(VisualKey(item), entry.Key, StringComparison.OrdinalIgnoreCase)))
					{
						(duplicate as ExpandableSidebarItemBase)?.StopWatchingSubfoldersAndDescendants();
						FindVisualSiblings(duplicate)?.Remove(duplicate);
						UntrackSidebarItem(duplicate);
					}
					RememberVisualPlacement(source);
					var origin = FindVisualSiblings(source);
					if (!ReferenceEquals(origin, destination))
					{
						origin?.Remove(source);
						destination.Add(source);
					}
					ApplyVisualPreferences(source);
				}
				ApplyVisualOrder(sidebarItems);
				foreach (var item in VisualItems())
				{
					if (IsCustomizationGroup(item) && item.Children is BulkConcurrentObservableCollection<INavigationControlItem> children) ApplyVisualOrder(children);
					// Every item here has had its place; one that couldn't be placed (its group was removed) shows where it is.
					placedVisualItems.AddOrUpdate(item, placedVisualItems);
				}
			}
			finally { visualReorderDepth--; restoringVisualPlacements = false; }
			RebuildFlatTree();
		}

		// An item with a saved place stays out of the sidebar until it has been put there, so it doesn't show in its own section first.
		private readonly System.Runtime.CompilerServices.ConditionalWeakTable<INavigationControlItem, object> placedVisualItems = new();
		private bool IsAwaitingPlacement(INavigationControlItem item, string key)
			=> (key.StartsWith("item:", StringComparison.Ordinal) || key.StartsWith("section:", StringComparison.Ordinal)) && !IsVisualSubFolder(item) &&
				!placedVisualItems.TryGetValue(item, out _) && ReadCustomization.Get(key).Group.Length > 0;

		// Rows the flat sidebar list leaves out while customization is on: hidden items, switched-off sections and items not yet placed.
		private bool IsVisualRowHidden(object item)
		{
			if (!SidebarCustomizationEnabled || item is not INavigationControlItem navigationItem) return false;
			var key = VisualKey(navigationItem);
			return ReadCustomization.Get(key).Hidden || IsSectionSwitchedOff(navigationItem) || IsAwaitingPlacement(navigationItem, key);
		}
	}
}
