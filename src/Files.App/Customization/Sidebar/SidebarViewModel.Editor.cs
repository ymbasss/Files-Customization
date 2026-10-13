// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using WinRT;


namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		internal const string EditorFooterKey = "sidebar-footer:";
		private const string EditorFolderPrefix = "folders:";

		// Separate appearance for the folders inside a sub-item; the entry exists only while the switch is on.
		internal static string EditorFolderKey(string key)
		{
			return EditorFolderPrefix + key;
		}

		internal static bool CanStyleEditorFolders(EditorRow row)
		{
			return row.CanExpand && !row.Group && !row.Decoration &&
				(row.Key.StartsWith("item:", StringComparison.Ordinal) || row.Key.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal));
		}

		internal bool HasEditorFolderAppearance(string key)
		{
			return SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization).Entries.ContainsKey(EditorFolderKey(key));
		}

		internal SidebarCustomizationState.Entry EditorFolderEntry(string key)
		{
			return SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization).Get(EditorFolderKey(key));
		}

		internal void SetEditorFolderAppearance(string key, bool separate)
		{
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var folderKey = EditorFolderKey(key);
			if (separate == state.Entries.ContainsKey(folderKey))
				return;

			// Start from the item's current look so turning the switch on changes nothing visible.
			if (separate)
				state.Entries[folderKey] = SidebarCustomizationStyle.CopyAppearance(new(folderKey), state.Get(key), 0);
			else
				state.Entries.Remove(folderKey);

			SaveVisualPreferences(state);
		}
		internal sealed record EditorRow(string Key, string Name, string Parent, int Depth, bool Group, bool Decoration,
			bool Removable, bool Available, SidebarCustomizationState.Entry Entry, bool CanExpand = false, bool CanEditName = true, bool CanHidePin = false,
			bool ViewOnly = false);
		internal sealed record EditorSnapshot(string Sidebar, string Options, int Sections);

		internal event EventHandler? EditorIconsChanged;
		private void NotifyEditorIconChanged() => EditorIconsChanged?.Invoke(this, EventArgs.Empty);
		internal bool SidebarCustomizationEnabled => EditorOptions.CustomizationEnabled && EditorOptions.SidebarEnabled;
		public Visibility SettingsFooterVisibility => ReadCustomization.Get(VisualKey(SettingsSidebarItem)) is { Hidden: false, Group.Length: 0 }
			? Visibility.Visible : Visibility.Collapsed;

		// Each Settings page (Customization > Sidebar) has its own panel, which works only while its page is shown. What the panels
		// share is kept here, so a panel shown again in another tab picks up where the last one left off.
		// The panel's Undo and Redo history, kept in memory until the app closes so it survives closing and reopening the panel.
		internal CustomChangeHistory<EditorSnapshot> EditorHistory { get; } = new(snapshot => (snapshot.Sidebar.Length + snapshot.Options.Length) * 2L, 4 * 1024 * 1024);
		// Preview rows collapsed by the user, and rows whose starting state has been decided.
		internal HashSet<string> EditorCollapsed { get; } = new(StringComparer.OrdinalIgnoreCase);
		internal HashSet<string> EditorPreviewFolderParents { get; } = new(StringComparer.OrdinalIgnoreCase);
		// The item selected in a panel, where a panel shown next starts.
		internal string? EditorSelectedKey { get; private set; }
		private int editorPanelsShown;

		internal void RememberEditorSelection(string? key)
		{
			if (key is not null) EditorSelectedKey = key;
		}

		// A panel was shown (shown even while the Sidebar feature is off; Settings greys it out then).
		internal void EditorPanelShown()
		{
			editorPanelsShown++;
			foreach (var item in VisualItems()) RememberVisualPlacement(item);
			LoadSwitchedOffSections();
		}

		// A panel was hidden. Once none is shown, the preview's own folder lists are dropped so the next one lists folders fresh from disk.
		internal void EditorPanelHidden()
		{
			if (--editorPanelsShown > 0) return;
			ForgetEditorFolders();
			ClearEditorFolderRows();
			editorStandIns.Clear();
		}

		// The app doesn't load switched-off Cloud drives, WSL and Tags (Network and the rest always load), so they're loaded when the
		// panel opens, with the same updates the app runs when a section is switched on; the kept sections then show their items.
		private async void LoadSwitchedOffSections()
		{
			if (!SidebarCustomizationEnabled) return;
			try
			{
				if (!ShowCloudDrivesSection) await CloudDrivesManager.UpdateDrivesAsync();
				if (!ShowWslSection) await WSLDistroManager.UpdateDrivesAsync();
				if (!ShowFileTagsSection) await App.FileTagsManager.UpdateFileTagsAsync();
			}
			catch (Exception ex)
			{
				App.Logger?.LogWarning(ex, "Switched-off sidebar sections could not be loaded");
			}
		}

		internal CustomizationPreferences EditorOptions => CustomizationPreferences.Parse(UserSettingsService.AppearanceSettingsService.CustomizationPreferences);
		internal FrameworkElement? EditorIcon(string key)
		{
			var item = ResolveEditorPreviewItem(key) ?? (key == VisualKey(SettingsSidebarItem) ? SettingsSidebarItem : null);
			if ((item as ISidebarItemPresentationModel)?.IconElement is { } icon)
				return icon;

			// A hidden built-in section is removed from the sidebar, so the preview shows that section's own icon instead of a plain glyph.
			if (key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var section) &&
				SidebarSectionIcons.For(section) is { } uri)
				return new ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uri)) };

			return null;
		}

		// Inner folders appear in the panel preview as view-only rows. Their keys are never saved and stay unique
		// even when the same folder is shown under two items.
		private const string EditorPreviewFolderPrefix = "preview-folder:";
		private readonly Dictionary<string, INavigationControlItem> editorPreviewItems = new(StringComparer.OrdinalIgnoreCase);

		private INavigationControlItem? ResolveEditorPreviewItem(string key)
		{
			if (key.StartsWith(EditorPreviewFolderPrefix, StringComparison.Ordinal))
				return editorPreviewItems.TryGetValue(key, out var folder) ? folder : null;

			return key == VisualKey(SettingsSidebarItem) ? SettingsSidebarItem :
				VisualItems().FirstOrDefault(item => VisualKey(item) == key);
		}

		// Called before the panel rebuilds its preview rows so removed folders are not kept alive.
		internal void ClearEditorFolderRows()
		{
			editorPreviewItems.Clear();
		}

		// The preview only reads the sidebar. Folders inside an item come from the sidebar when it has already loaded them;
		// otherwise the preview lists them itself into its own collection, without expanding, loading or watching the sidebar item.
		private readonly Dictionary<string, BulkConcurrentObservableCollection<INavigationControlItem>> editorFolderLists = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> editorFolderListsLoaded = new(StringComparer.OrdinalIgnoreCase);

		// Drops the preview's own folder lists, so the next expand lists the folders again from disk.
		internal void ForgetEditorFolders(string? key = null)
		{
			if (key is null)
			{
				editorFolderLists.Clear();
				editorFolderListsLoaded.Clear();
				return;
			}

			editorFolderLists.Remove(key);
			editorFolderListsLoaded.Remove(key);
		}

		private IEnumerable<INavigationControlItem>? EditorFolderChildren(string parentKey, out bool previewOwned)
		{
			previewOwned = false;
			if (ResolveEditorPreviewItem(parentKey) is { } item && item is ExpandableSidebarItemBase { IsLoaded: true } && item.Children is IEnumerable<INavigationControlItem> sidebarChildren)
				return sidebarChildren;

			previewOwned = true;
			return editorFolderLists.TryGetValue(parentKey, out var own) ? own : null;
		}

		// inherited: the look of the folder row these folders are inside, for folders deeper than the first level.
		internal List<EditorRow> EditorFolderRows(string parentKey, int depth, SidebarCustomizationState.Entry? inherited = null)
		{
			var rows = new List<EditorRow>();
			if (EditorFolderChildren(parentKey, out var previewOwned) is not { } children)
				return rows;

			// Inner folders use the item's separate folder appearance when it exists, otherwise the item's own appearance.
			var state = ReadCustomization;
			var source = inherited ?? (state.Entries.TryGetValue(EditorFolderKey(parentKey), out var folders) ? folders : state.Get(parentKey));
			foreach (var child in children.ToArray())
			{
				var key = EditorPreviewFolderPrefix + parentKey + "|" + child.Path;
				editorPreviewItems[key] = child;
				var entry = SidebarCustomizationStyle.CopyAppearance(new SidebarCustomizationState.Entry(key), source, 0);
				// Folders the sidebar has loaded already carry their look; folders listed only for the preview get it here.
				if (previewOwned) ApplyVisualPreferences(child, fixedEntry: entry);
				rows.Add(new(key, child.Text ?? "", parentKey, depth, false, false, false, true,
					entry, CanExpandEditorItem(child), false, false, true));
			}

			return rows;
		}

		// The item the preview draws for a row: the sidebar's own item, or, for a row whose item isn't in the sidebar
		// (a section with nothing in it, a moved drive that isn't connected), a stand-in styled the same way.
		private readonly Dictionary<string, LocationItem> editorStandIns = new(StringComparer.OrdinalIgnoreCase);
		internal ISidebarItemModel? EditorPreviewItem(EditorRow row)
		{
			if (ResolveEditorPreviewItem(row.Key) is { } item)
				return item;

			if (!editorStandIns.TryGetValue(row.Key, out var standIn))
			{
				if (CreateEditorStandIn(row.Key) is not { } created)
					return null;
				editorStandIns[row.Key] = standIn = created;
			}
			standIn.Text = row.Name;
			ApplyVisualPreferences(standIn);
			return standIn;
		}

		private static LocationItem? CreateEditorStandIn(string key)
		{
			if (key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var section))
			{
				var header = new LocationItem { Section = section, IsHeader = true, SelectsOnInvoked = false, ChildItems = [], MenuOptions = new ContextMenuOptions() };
				if (SidebarSectionIcons.For(section) is { } uri)
					header.Icon = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uri));
				return header;
			}
			if (key.StartsWith("item:", StringComparison.Ordinal) && key.IndexOf(':', 5) is var split && split > 5 &&
				Enum.TryParse<SectionType>(key[5..split], out var itemSection))
				return new LocationItem { Path = key[(split + 1)..], Section = itemSection, MenuOptions = new ContextMenuOptions() };

			return null;
		}

		// The sidebar lists the preview watches, read-only, so drives, pinned folders, tags or folders that appear or
		// disappear while the panel is open show up in the preview. Inner sub-folders are not watched.
		internal List<System.Collections.Specialized.INotifyCollectionChanged> EditorWatchLists()
		{
			var lists = new List<System.Collections.Specialized.INotifyCollectionChanged> { sidebarItems };
			foreach (var item in VisualItems())
				if (!IsVisualSubFolder(item) && item.Children is System.Collections.Specialized.INotifyCollectionChanged children)
					lists.Add(children);

			return lists;
		}

		internal bool NeedsEditorFolders(string key)
		{
			return ResolveEditorPreviewItem(key) is ExpandableSidebarItemBase { IsExpandableFolder: true, IsLoaded: false } &&
				!editorFolderListsLoaded.Contains(key);
		}

		// Lists an item's folders for the preview into the preview's own collection; the sidebar item is not touched.
		internal async Task LoadEditorFoldersAsync(string key)
		{
			if (ResolveEditorPreviewItem(key) is not ExpandableSidebarItemBase { IsExpandableFolder: true, IsLoaded: false, SidebarExpansionPath: { Length: > 0 } path } ||
				editorFolderLists.ContainsKey(key))
				return;

			var list = new BulkConcurrentObservableCollection<INavigationControlItem>();
			editorFolderLists[key] = list;
			// Runs even when listing fails, so an unreadable folder is not asked for again.
			await LocationItem.LoadSubfoldersIntoAsync(path, list, () =>
			{
				if (editorFolderLists.TryGetValue(key, out var current) && ReferenceEquals(current, list))
					editorFolderListsLoaded.Add(key);
			});
		}

		internal bool IsEditorItemExpanded(string key)
		{
			return ResolveEditorPreviewItem(key) is ExpandableSidebarItemBase { IsExpanded: true };
		}
		internal bool IsEditorSettingsRow(string key) => key == VisualKey(SettingsSidebarItem);
		private static bool CanExpandEditorItem(INavigationControlItem item) => item is not SidebarVisualDecoration &&
			(item.HasUnrealizedChildren || item.Children is System.Collections.ICollection { Count: > 0 });
		// Undo and Redo snapshots hold only the sidebar's own options. Other settings saved in the same place (columns,
		// shortcut badges) change on their own and must not break the history.
		internal EditorSnapshot CaptureEditor() => new(UserSettingsService.AppearanceSettingsService.SidebarCustomization,
			WithSidebarOptions(new(), EditorOptions).Serialize(),
			(ShowPinnedFoldersSection ? 1 : 0) | (ShowLibrarySection ? 2 : 0) | (ShowDrivesSection ? 4 : 0) |
			(ShowCloudDrivesSection ? 8 : 0) | (ShowNetworkSection ? 16 : 0) | (ShowWslSection ? 32 : 0) | (ShowFileTagsSection ? 64 : 0));
		private static CustomizationPreferences WithSidebarOptions(CustomizationPreferences target, CustomizationPreferences source) => target with
		{
			SidebarDragEnabled = source.SidebarDragEnabled,
			SharpLargeIcons = source.SharpLargeIcons,
			SidebarKeepTextPosition = source.SidebarKeepTextPosition,
			SidebarAppearance = source.SidebarAppearance,
			SidebarCompactShowIcons = source.SidebarCompactShowIcons,
			SidebarCompactGroupContents = source.SidebarCompactGroupContents,
			SidebarCompactIconMode = source.SidebarCompactIconMode,
			SidebarCompactIconSize = source.SidebarCompactIconSize,
			SidebarCompactHideGroupIcons = source.SidebarCompactHideGroupIcons
		};
		internal void RestoreEditor(EditorSnapshot snapshot)
		{
			UserSettingsService.AppearanceSettingsService.SidebarCustomization = snapshot.Sidebar;
			// Only the sidebar's options come back; everything else keeps its current value.
			SaveEditorOptions(WithSidebarOptions(EditorOptions, CustomizationPreferences.Parse(snapshot.Options)));
			ShowPinnedFoldersSection = (snapshot.Sections & 1) != 0; ShowLibrarySection = (snapshot.Sections & 2) != 0;
			ShowDrivesSection = (snapshot.Sections & 4) != 0; ShowCloudDrivesSection = (snapshot.Sections & 8) != 0;
			ShowNetworkSection = (snapshot.Sections & 16) != 0; ShowWslSection = (snapshot.Sections & 32) != 0;
			ShowFileTagsSection = (snapshot.Sections & 64) != 0;
		}
		internal void SaveEditorOptions(CustomizationPreferences options)
			=> UserSettingsService.AppearanceSettingsService.CustomizationPreferences = options.Serialize();

		internal List<EditorRow> EditorRows()
		{
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var rows = new List<EditorRow>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			// Rows inside something hidden (a hidden group, or a section switched off with the app's switch) are dimmed with it;
			// only their display changes, not their own saved settings.
			void Visit(IEnumerable<INavigationControlItem> items, string parent, int depth, bool parentIsGroup = true, bool insideHidden = false)
			{
				foreach (var item in items.ToArray())
				{
					var key = VisualKey(item);
					if (!seen.Add(key)) continue;
					var entry = state.Get(key);
					var hidden = entry.Hidden || insideHidden || IsSectionSwitchedOff(item);
					entry = entry with { Hidden = hidden };
					var name = entry.Label.Length > 0 ? entry.Label : item.Text ?? "";
					if (item is SidebarVisualDecoration) name = (entry.Separator ? Strings.SidebarEditorSeparator : Strings.SidebarEditorGap).GetLocalizedResource();
					var group = IsCustomizationGroup(item);
					var canEditName = group || parentIsGroup || key.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal) ||
						item is not LocationItem { IsExpandableFolder: true } folder || folder.IsPinned;
					rows.Add(new(key, name, parent, depth, group, item is SidebarVisualDecoration,
						key.StartsWith(VisualGroupPrefix, StringComparison.Ordinal) ||
							key.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal) ||
							item is SidebarVisualDecoration,
						true, entry, CanExpandEditorItem(item), canEditName, HasLegacyPin(item, state)));
					// Sub-folders follow their sub-item and are not listed in the panel.
					if (group && item.Children is IEnumerable<INavigationControlItem> children) Visit(children, key, depth + 1, group, hidden);
				}
			}
			Visit(sidebarItems, VisualRootKey, 0);
			foreach (var section in new[] { (SectionType.Pinned, Strings.Pinned), (SectionType.Library, Strings.SidebarLibraries),
				(SectionType.Drives, Strings.Drives), (SectionType.CloudDrives, Strings.SidebarCloudDrives),
				(SectionType.Network, Strings.Network), (SectionType.WSL, Strings.WSL), (SectionType.FileTag, Strings.FileTags) })
			{
				var key = "section:" + section.Item1;
				var entry = state.Get(key);
				// Sections not in the sidebar at all (for example Cloud Drives with no cloud drives) still get a row.
				if (seen.Add(key)) rows.Add(new(key, entry.Label is { Length: > 0 } name ? name : section.Item2.GetLocalizedResource(),
					VisualRootKey, 0, true, false, false, false, entry with { Hidden = entry.Hidden || !EditorSectionVisible(section.Item1) }));
			}
			if (!seen.Contains(VisualKey(SettingsSidebarItem))) Visit([SettingsSidebarItem], EditorFooterKey, 0);
			foreach (var saved in state.Entries.Values.Where(entry => !seen.Contains(entry.Key) && !IsVisualSubFolderKey(entry.Key) &&
				!entry.Key.StartsWith(EditorFolderPrefix, StringComparison.Ordinal)))
			{
				var entry = state.Get(saved.Key);
				var name = entry.Key.StartsWith(VisualDecorationPrefix, StringComparison.Ordinal)
					? (entry.Separator ? Strings.SidebarEditorSeparator : Strings.SidebarEditorGap).GetLocalizedResource()
					: entry.Label.Length > 0 ? entry.Label : AbsentEditorName(entry.Key);
				rows.Add(new(entry.Key, name, entry.Group.Length > 0 ? entry.Group : VisualRootKey, 0,
					entry.Key.StartsWith(VisualGroupPrefix, StringComparison.Ordinal), entry.Key.StartsWith(VisualDecorationPrefix, StringComparison.Ordinal),
					entry.Key.StartsWith("sidebar-", StringComparison.Ordinal), false, entry));
			}
			var ordered = new List<EditorRow>();
			var orderedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			void Append(string parent, int depth)
			{
				foreach (var row in KeepUnplacedInPlace(rows.Where(row => string.Equals(row.Parent, parent, StringComparison.OrdinalIgnoreCase)).ToArray(), row => row.Entry.Order))
				{
					if (!orderedKeys.Add(row.Key)) continue;
					ordered.Add(row with { Depth = depth }); Append(row.Key, depth + 1);
				}
			}
			Append(VisualRootKey, 0); Append(EditorFooterKey, 0);
			ordered.AddRange(rows.Where(row => orderedKeys.Add(row.Key)));
			return ordered;
		}

		// The name of a saved item the sidebar doesn't show: its path, also for a drive's entry put aside under its ID ("#id#path").
		private static string AbsentEditorName(string key)
		{
			var name = key[((key.StartsWith("item:", StringComparison.Ordinal) ? key.IndexOf(':', 5) : key.LastIndexOf(':')) + 1)..];
			return name.StartsWith('#') && name.IndexOf('#', 1) is > 0 and var split ? name[(split + 1)..] : name;
		}
		private bool EditorSectionVisible(SectionType section) => section switch
		{
			SectionType.Pinned => ShowPinnedFoldersSection, SectionType.Library => ShowLibrarySection, SectionType.Drives => ShowDrivesSection,
			SectionType.CloudDrives => ShowCloudDrivesSection, SectionType.Network => ShowNetworkSection,
			SectionType.WSL => ShowWslSection, SectionType.FileTag => ShowFileTagsSection, _ => true
		};
		private void ShowEditorSection(string key) => SetEditorSectionVisible(key, true);
		// Whether the item itself is hidden: built-in sections by the app's section switch, everything else by its saved setting.
		// Items inside a hidden section are only dimmed in the preview and are not hidden themselves.
		internal bool IsEditorEntryHidden(string key)
			=> key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var section) && section != SectionType.Home && !EditorSectionVisible(section) ||
				SidebarCustomizationState.ReadSnapshot(UserSettingsService.AppearanceSettingsService.SidebarCustomization).Get(key).Hidden;
		// The app's own section switch, also used by the sidebar's right-click menu.
		private void SetEditorSectionVisible(string key, bool visible)
		{
			if (!key.StartsWith("section:", StringComparison.Ordinal) || !Enum.TryParse<SectionType>(key[8..], out var section)) return;
			switch (section)
			{
				case SectionType.Pinned: ShowPinnedFoldersSection = visible; break;
				case SectionType.Library: ShowLibrarySection = visible; break;
				case SectionType.Drives: ShowDrivesSection = visible; break;
				case SectionType.CloudDrives: ShowCloudDrivesSection = visible; break;
				case SectionType.Network: ShowNetworkSection = visible; break;
				case SectionType.WSL: ShowWslSection = visible; break;
				case SectionType.FileTag: ShowFileTagsSection = visible; break;
			}
		}
		internal void EditSidebarEntry(string key, Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update)
		{
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var original = state.Get(key);
			if (key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var section) && !EditorSectionVisible(section))
				original = original with { Hidden = true };
			var updated = update(original);
			if (updated.Label != original.Label && EditorRows().FirstOrDefault(row => row.Key == key) is { CanEditName: false })
				updated = updated with { Label = original.Label };
			// Built-in sections are shown and hidden with the app's own section switch, the same one as the sidebar's
			// right-click menu, so the panel and that menu always agree.
			var builtInSection = key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var sectionType) && sectionType != SectionType.Home;
			var hideSection = builtInSection && updated.Hidden;
			if (builtInSection) updated = updated with { Hidden = false };
			state.Entries[key] = updated;
			SaveVisualPreferences(state);
			if (builtInSection) SetEditorSectionVisible(key, !hideSection);
		}

		internal bool CanMoveEditorRow(string key, string target, bool inside)
		{
			var rows = EditorRows(); var source = rows.FirstOrDefault(row => row.Key == key); var destination = rows.FirstOrDefault(row => row.Key == target);
			if (source is null || key == target || destination is null || destination.Parent == EditorFooterKey) return false;
			if (inside && !destination.Group) return false;
			var parent = inside ? destination.Key : destination.Parent;
			// Groups (built-in sections and added groups) only sit at the top level, never inside another group.
			if (source.Group && parent != VisualRootKey) return false;
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			while (visited.Add(parent))
			{
				if (parent == key) return false;
				var ancestor = rows.FirstOrDefault(row => row.Key == parent);
				if (ancestor is null) return true;
				parent = ancestor.Parent;
			}
			return false;
		}
		internal void MoveEditorRow(string key, string target, bool inside, bool after = false)
		{
			if (!CanMoveEditorRow(key, target, inside)) return;
			var rows = EditorRows(); var source = rows.First(row => row.Key == key); var destination = rows.First(row => row.Key == target);
			var parent = inside ? target : destination.Parent;
			var siblings = rows.Where(row => row.Parent == parent && row.Key != key).Select(row => row.Key).ToList();
			var index = inside ? siblings.Count : siblings.IndexOf(target) + (after ? 1 : 0);
			siblings.Insert(Math.Clamp(index, 0, siblings.Count), key);
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var item = VisualItems().FirstOrDefault(item => VisualKey(item) == key);
			var entry = state.Get(key);
			if (item is not null) RememberVisualPlacement(item);
			state.Entries[key] = entry with { Group = parent,
				Label = entry.Label.Length > 0 ? entry.Label : source.Name,
				Target = entry.Target.Length > 0 ? entry.Target : item is null ? "" : VisualTarget(item) ?? "" };
			for (var i = 0; i < siblings.Count; i++) { var saved = state.Get(siblings[i]); state.Entries[saved.Key] = saved with { Order = i }; }
			SaveVisualPreferences(state);
		}
		internal bool CanDuplicateEditorRow(EditorRow row)
		{
			if (!row.Available || row.Group && row.Key != "section:Home" || row.Decoration || row.ViewOnly) return false;
			return ResolveEditorPreviewItem(row.Key) is { } item && !string.IsNullOrEmpty(VisualTarget(item));
		}

		internal string? DuplicateEditorEntry(string? key)
		{
			var rows = EditorRows();
			var source = rows.FirstOrDefault(row => row.Key == key);
			if (source is null || !CanDuplicateEditorRow(source)) return key;
			var item = ResolveEditorPreviewItem(source.Key)!;
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var entry = state.Get(source.Key);
			var copyKey = VisualShortcutPrefix + Guid.NewGuid().ToString("N");
			var parent = source.Parent == EditorFooterKey ? VisualRootKey : source.Parent;
			state.Entries[copyKey] = entry with
			{
				Key = copyKey, Label = source.Name, Target = VisualTarget(item) ?? "", Group = parent,
				SourceKey = entry.SourceKey.Length > 0 ? entry.SourceKey : source.Key
			};
			if (state.Entries.TryGetValue(EditorFolderKey(source.Key), out var folders))
				state.Entries[EditorFolderKey(copyKey)] = folders with { Key = EditorFolderKey(copyKey) };
			var siblings = rows.Where(row => row.Parent == parent).Select(row => row.Key).ToList();
			var sourceIndex = siblings.IndexOf(source.Key);
			siblings.Insert(sourceIndex < 0 ? siblings.Count : sourceIndex + 1, copyKey);
			for (var i = 0; i < siblings.Count; i++)
			{
				var saved = state.Get(siblings[i]);
				state.Entries[saved.Key] = saved with { Order = i };
			}
			SaveVisualPreferences(state);
			return copyKey;
		}

		internal string AddEditorEntry(string kind, string? selectedKey)
		{
			// A new item goes at the end of the selected group, or right below the selected item. Items never get anything
			// inside them (they only hold folders), so a selected item always means "after it". With nothing selected, or
			// Settings selected, it goes to the end of the sidebar, still above Settings when that is listed there.
			var rows = EditorRows();
			var selected = rows.FirstOrDefault(row => row.Key == selectedKey && row.Parent != EditorFooterKey && !IsEditorSettingsRow(row.Key));
			// A new group never goes inside another group: it goes at the top level, right after the top-level row holding the selection.
			if (kind == "group" && selected is not null)
			{
				while (selected.Parent != VisualRootKey && rows.FirstOrDefault(row => row.Key == selected.Parent) is { } ancestor) selected = ancestor;
				if (selected.Parent != VisualRootKey) selected = null;
			}
			// Home is a heading without contents, so it counts as an item here.
			var intoGroup = kind != "group" && selected is { Group: true } && selected.Key != "section:Home";
			var parent = selected is null ? VisualRootKey : intoGroup ? selected.Key : selected.Parent;
			var key = (kind == "group" ? VisualGroupPrefix : kind == "shortcut" ? VisualShortcutPrefix : VisualDecorationPrefix) + Guid.NewGuid().ToString("N");
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var siblings = rows.Where(row => row.Parent == parent).Select(row => row.Key).ToList();
			var settingsIndex = siblings.FindIndex(IsEditorSettingsRow);
			var index = selected is not null && !intoGroup ? siblings.IndexOf(selected.Key) + 1
				: settingsIndex >= 0 ? settingsIndex : siblings.Count;
			siblings.Insert(Math.Clamp(index, 0, siblings.Count), key);
			state.Entries[key] = state.Get(key) with
			{
				Label = kind == "group"
					? Strings.CustomizationSidebarNewGroupName.GetLocalizedResource()
					: kind == "shortcut" ? Strings.CustomizationSidebarNewShortcutName.GetLocalizedResource() : "",
				Group = parent, Separator = kind == "separator",
				// Only separators and gaps have a height; groups keep the default, so their Reset stays greyed out when unchanged.
				Gap = kind == "separator" ? SidebarCustomizationStyle.DefaultSeparatorHeight
					: kind == "gap" ? SidebarCustomizationStyle.DefaultDecorationHeight : new SidebarCustomizationState.Entry(key).Gap
			};
			for (var i = 0; i < siblings.Count; i++) { var entry = state.Get(siblings[i]); state.Entries[entry.Key] = entry with { Order = i }; }
			SaveVisualPreferences(state); return key;
		}
		internal void ResetEditorEntry(string key, bool remove = false)
		{
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var previous = state.Get(key);
			if (remove)
			{
				state.Entries.Remove(key);
				foreach (var child in state.Entries.Values.Where(entry => entry.Group == key).ToArray())
					state.Entries[child.Key] = child with { Group = previous.Group.Length > 0 ? previous.Group : VisualRootKey };
			}
			else
				state.Entries[key] = DefaultEditorEntry(key, previous);
			state.Entries.Remove(EditorFolderKey(key));
			SaveVisualPreferences(state); ShowEditorSection(key);
		}
		private static SidebarCustomizationState.Entry DefaultEditorEntry(string key, SidebarCustomizationState.Entry previous)
			=> key.StartsWith("sidebar-", StringComparison.Ordinal)
				? previous with
				{
					Size = -1,
					Hidden = false,
					HideIcon = false,
					HideArrow = false,
					TextPt = 0,
					IconPt = 0,
					SizesLinked = true,
					ExpansionLock = 0,
					LegacyExpansion = null,
					HidePin = false,
					IconSource = "",
					Gap = 12
				}
				: new(key, Group: previous.Group, Order: previous.Order, Id: previous.Id);
		// True when resetting this item would change nothing, so its Reset button can be greyed out.
		internal bool IsEditorEntryDefault(string key)
		{
			var state = SidebarCustomizationState.ReadSnapshot(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			var previous = state.Get(key);
			if (key.StartsWith("section:", StringComparison.Ordinal) && Enum.TryParse<SectionType>(key[8..], out var section) && !EditorSectionVisible(section))
				return false;
			return DefaultEditorEntry(key, previous) == previous && !state.Entries.ContainsKey(EditorFolderKey(key));
		}
		internal bool CanApplyEditorAppearance(string key, int scope) => GetEditorAppearanceChanges(key, scope).Count > 0;

		internal void ApplyEditorAppearance(string key, int scope)
		{
			var changes = GetEditorAppearanceChanges(key, scope);
			if (changes.Count == 0) return;
			var state = SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);
			foreach (var change in changes) state.Entries[change.Key] = change.Value;
			SaveVisualPreferences(state);
		}

		private Dictionary<string, SidebarCustomizationState.Entry> GetEditorAppearanceChanges(string key, int scope)
		{
			var changes = new Dictionary<string, SidebarCustomizationState.Entry>(StringComparer.OrdinalIgnoreCase);
			if (!SidebarCustomizationEnabled || scope is < 1 or > 3) return changes;
			var rows = EditorRows(); var selected = rows.FirstOrDefault(row => row.Key == key);
			if (selected is null) return changes;
			var group = selected.Group ? selected.Key : selected.Parent;
			bool InGroup(EditorRow row)
			{
				var ancestors = new HashSet<string>();
				while (ancestors.Add(row.Key))
				{
					if (row.Key == group || row.Parent == group) return true;
					var parent = rows.FirstOrDefault(parent => parent.Key == row.Parent);
					if (parent is null) return false; row = parent;
				}
				return false;
			}
			var options = EditorOptions;
			var legacy = options.SidebarAppearance == 1;
			bool LegacyHeading(EditorRow row) => legacy && SidebarCustomizationStyle.IsLegacyHeader(row.Group, row.Key);
			// Only what can be changed on the source item is copied, in both appearances: its expansion only when it has an
			// arrow to change, its icon setting only when it isn't a Legacy heading (those never show an icon), and its pin
			// only when it shows one. Each is copied only to items that can take it; anything else keeps its own value.
			var copyExpansion = selected.CanExpand;
			var copyIcon = !LegacyHeading(selected);
			var copyPin = legacy && selected.CanHidePin;
			var legacyExpansion = legacy && copyExpansion ? (int?)SidebarCustomizationStyle.ExpansionMode(selected.Entry, selected.Group, options) : null;
			foreach (var row in rows.Where(row =>
				row.Key != key && !row.Decoration && !IsEditorSettingsRow(row.Key) &&
				(scope == 3 || scope == 2 && InGroup(row) || scope == 1 && row.Parent == selected.Parent)))
			{
				var appearance = SidebarCustomizationStyle.CopyAppearance(row.Entry, selected.Entry, legacyExpansion);
				if (!copyExpansion || !row.CanExpand)
					appearance = appearance with { HideArrow = row.Entry.HideArrow, ExpansionLock = row.Entry.ExpansionLock, LegacyExpansion = row.Entry.LegacyExpansion };
				else if (legacyExpansion.HasValue &&
					SidebarCustomizationStyle.ExpansionMode(appearance, row.Group, options) == SidebarCustomizationStyle.ExpansionMode(row.Entry, row.Group, options))
					appearance = appearance with { LegacyExpansion = row.Entry.LegacyExpansion };
				if (!copyIcon || LegacyHeading(row)) appearance = appearance with { HideIcon = row.Entry.HideIcon };
				// A Legacy heading shows no icon, so its custom text size sets the icon size too, in the default proportion, linked.
				if (!copyIcon && selected.Entry.TextPt > 0)
				{
					var ratio = SidebarCustomizationStyle.DefaultPoints(0, false, options);
					appearance = appearance with { IconPt = Math.Clamp(selected.Entry.TextPt * ratio.Icon / ratio.Text, 6, 48), SizesLinked = true };
				}
				if (copyPin && row.CanHidePin) appearance = appearance with { HidePin = selected.Entry.HidePin };
				if (appearance != row.Entry) changes.Add(row.Key, appearance);
			}
			return changes;
		}
		internal void ResetSidebarEditor()
		{
			SaveVisualPreferences(new());
			SaveEditorOptions(DefaultSidebarOptions(EditorOptions));

			var sections = new[]
			{
				SectionType.Pinned,
				SectionType.Library,
				SectionType.Drives,
				SectionType.CloudDrives,
				SectionType.Network,
				SectionType.WSL,
				SectionType.FileTag
			};
			foreach (var section in sections)
				ShowEditorSection("section:" + section);
			ShowLibrarySection = false;
		}
		private static CustomizationPreferences DefaultSidebarOptions(CustomizationPreferences options) => options with
		{
			SidebarDragEnabled = true,
			SharpLargeIcons = true,
			SidebarKeepTextPosition = true,
			SidebarAppearance = 0,
			SidebarCompactShowIcons = true,
			SidebarCompactGroupContents = false,
			SidebarCompactIconMode = 0,
			SidebarCompactIconSize = 24,
			SidebarCompactHideGroupIcons = false
		};
		// True when Reset sidebar would change nothing, so its Reset buttons can be greyed out.
		internal bool IsSidebarEditorDefault()
		{
			var options = EditorOptions;
			return options == DefaultSidebarOptions(options)
				&& SidebarCustomizationState.ReadSnapshot(UserSettingsService.AppearanceSettingsService.SidebarCustomization).Entries.Values.All(entry => entry == new SidebarCustomizationState.Entry(entry.Key))
				&& ShowPinnedFoldersSection && !ShowLibrarySection && ShowDrivesSection && ShowCloudDrivesSection && ShowNetworkSection && ShowWslSection && ShowFileTagsSection;
		}
	}
}
