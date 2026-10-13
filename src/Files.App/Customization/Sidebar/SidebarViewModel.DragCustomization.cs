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
				var item = items.FirstOrDefault(candidate => VisualKey(candidate) == pair.Key);
				if (item is null) continue;
				var parent = items.FirstOrDefault(candidate => VisualKey(candidate) == pair.Value.Parent);
				var destination = pair.Value.Parent == VisualRootKey ? sidebarItems : parent?.Children as BulkConcurrentObservableCollection<INavigationControlItem>;
				if (destination is null) continue;
				var origin = FindVisualSiblings(item);
				if (ReferenceEquals(origin, destination) && state.Get(pair.Key).Order != int.MaxValue) continue;
				origin?.Remove(item); destination.Insert(Math.Clamp(pair.Value.Index, 0, destination.Count), item);
			}
		}

		// Items were removed from a section (a folder unpinned, a drive removed). Those moved into a group leave it too, and an unpinned
		// folder forgets its saved place and group, so pinning it again puts it where the app pins it. Its look is kept.
		private void RemoveSectionItems(LocationItem section, System.Collections.IList removed)
		{
			RemoveMovedSectionItems(section, path => removed.OfType<INavigationControlItem>().Any(x => x.Path == path));
			if (!SidebarCustomizationEnabled || section.Section != SectionType.Pinned) return;
			var state = Customization;
			var changed = false;
			foreach (var item in removed.OfType<INavigationControlItem>())
			{
				var key = VisualKey(item);
				if (!state.Entries.TryGetValue(key, out var entry) || entry.Order == int.MaxValue && entry.Group.Length == 0) continue;
				state.Entries[key] = entry with { Order = int.MaxValue, Group = "" };
				changed = true;
			}
			if (changed) SaveVisualPreferences(state);
		}

		// Removes manager items that were moved out of their section, such as an unpinned folder placed in a custom group.
		private void RemoveMovedSectionItems(LocationItem section, Func<string?, bool> isRemoved)
		{
			if (!SidebarCustomizationEnabled) return;
			var state = ReadCustomization;
			foreach (var item in VisualItems())
			{
				if (item is SidebarVisualGroup or SidebarVisualDecoration or LocationItem { IsHeader: true } ||
					ReferenceEquals(item, SettingsSidebarItem) ||
					item.Path?.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal) == true ||
					item.Section != section.Section ||
					section.ChildItems?.Contains(item) == true ||
					state.Get(VisualKey(item)).Group.Length == 0 ||
					!isRemoved(item.Path))
					continue;

				if (item is ExpandableSidebarItemBase expandable)
					expandable.StopWatchingSubfoldersAndDescendants();
				FindVisualSiblings(item)?.Remove(item);
			}
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
			if (!CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences).SidebarDragEnabled ||
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
		// letter starts without them.
		private readonly List<INavigationControlItem> pendingVisualIds = [];
		private bool visualIdsQueued;

		private void QueueVisualId(INavigationControlItem item)
		{
			if (item is not (DriveItem { Section: SectionType.Drives } or LocationItem { IsHeader: false, Section: SectionType.Pinned }) ||
				item is SidebarVisualGroup or SidebarVisualShortcut or SidebarVisualDecoration ||
				item.Path is not { } path || !Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
				return;
			pendingVisualIds.Add(item);
			if (visualIdsQueued || dispatcherQueue is null) return;
			visualIdsQueued = true;
			dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MatchVisualIds);
		}

		private async void MatchVisualIds()
		{
			var items = pendingVisualIds.Select(item => (Key: VisualKey(item), Path: item.Path!, Drive: item is DriveItem)).Distinct().ToArray();
			pendingVisualIds.Clear();
			visualIdsQueued = false;
			var ids = await Task.Run(() => items.Select(item => (item.Key, Id: VisualId(item.Path, item.Drive))).ToArray());
			if (!SidebarCustomizationEnabled) return;
			var state = Customization;
			var present = VisualItems().Select(VisualKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var changed = false;
			foreach (var (key, id) in ids)
				if (id is not null) changed |= MatchVisualId(state, key, id, present);
			if (changed) SaveVisualPreferences(state);
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

		private static bool MatchVisualId(SidebarCustomizationState state, string key, string id, HashSet<string> present)
		{
			var prefix = key[..(key.IndexOf(':', 5) + 1)];
			var changed = false;
			if (state.Entries.TryGetValue(key, out var own))
			{
				if (own.Id == id) return false;
				// No ID yet, or a folder ID saved without its drive's serial number.
				if (own.Id.Length == 0 || own.Id.StartsWith("frn:", StringComparison.Ordinal) && own.Id.IndexOf(':', 4) < 0)
				{
					state.Entries[key] = own with { Id = id };
					return true;
				}
				// Saved for another folder or drive that had this path: put aside under its ID until that one shows up again.
				RenameVisualEntry(state, key, prefix + "#" + own.Id + "#" + key[prefix.Length..]);
				changed = true;
			}
			var match = state.Entries.Values.FirstOrDefault(entry => entry.Id == id &&
				entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !present.Contains(entry.Key));
			if (match is null) return changed;
			RenameVisualEntry(state, match.Key, key);
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

		// The folder or drive path in an item's key; null for an entry put aside under its ID.
		private static string? VisualEntryPath(string key)
		{
			var split = key.IndexOf(':', 5);
			return split > 0 && Path.IsPathRooted(key[(split + 1)..]) ? key[(split + 1)..] : null;
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
					// Older saved placements for sub-folders are ignored.
					if (IsVisualSubFolderKey(entry.Key))
						continue;
					var items = VisualItems();
					var source = items.FirstOrDefault(item => VisualKey(item) == entry.Key) ?? (entry.Key == VisualKey(SettingsSidebarItem) ? SettingsSidebarItem : null);
					var parent = entry.Group == VisualRootKey ? null : items.FirstOrDefault(item => VisualKey(item) == entry.Group);
					if (entry.Group != VisualRootKey && parent is null) continue;
					var destination = parent is null ? sidebarItems : parent.Children as BulkConcurrentObservableCollection<INavigationControlItem>;
					if (destination is null) continue;
					if (source is null)
					{
						if (entry.Key.StartsWith(VisualDecorationPrefix, StringComparison.Ordinal)) source = CreateVisualDecoration(entry);
						else if (entry.Key.StartsWith(VisualGroupPrefix, StringComparison.Ordinal))
							source = CreateVisualGroup(entry);
						else if (entry.Key.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal))
							source = CreateVisualShortcut(entry);
						if (source is not null) TrackSidebarItem(source);
					}
					// Folders, drives and sections are placed when the app has loaded them (see IsAwaitingPlacement).
					if (source is null || parent is not null && IsVisualDescendant(source, parent)) continue;
					foreach (var duplicate in items.Where(item => !ReferenceEquals(item, source) && VisualKey(item) == entry.Key))
					{
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
			=> (key.StartsWith("item:", StringComparison.Ordinal) || key.StartsWith("section:", StringComparison.Ordinal)) && !IsVisualSubFolderKey(key) &&
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
