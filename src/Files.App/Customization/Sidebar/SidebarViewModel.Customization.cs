// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Windows.Input;
using WinRT;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		private const string VisualGroupPrefix = "sidebar-group:";
		private const string VisualShortcutPrefix = "sidebar-shortcut:";
		private const string VisualDecorationPrefix = "sidebar-decoration:";
		private int visualReorderDepth;
		private readonly System.Runtime.CompilerServices.ConditionalWeakTable<INavigationControlItem, VisualIconRequest> visualIconRequests = new();
		private sealed class VisualIconRequest
		{
			public int Size;
			public int Version;
			public string? Path;
			public BitmapImage? OriginalIcon;
			public BitmapImage? UpgradedIcon;
		}
		private CustomizationPreferences? lastSidebarAppearance;

		private static bool HasSameSidebarAppearance(CustomizationPreferences options, CustomizationPreferences previous)
			=> options.SidebarEnabled == previous.SidebarEnabled && WithSidebarOptions(new(), options) == WithSidebarOptions(new(), previous);

		private void RefreshVisualAppearanceOnly(bool force = false)
		{
			var options = CurrentOptions;
			// Nothing of ours to refresh while customization stays off (switching it off resets the items in RefreshVisualSidebar),
			// and nothing to do when the look didn't change, unless a refresh is forced.
			if (lastSidebarAppearance is { } previous &&
				(!options.SidebarEnabled && !previous.SidebarEnabled || !force && HasSameSidebarAppearance(options, previous)))
				return;

			var compactContentsChanged = lastSidebarAppearance?.SidebarCompactGroupContents != options.SidebarCompactGroupContents;
			lastSidebarAppearance = options;
			var state = ReadCustomization;
			Refresh(sidebarItems);
			ApplyVisualPreferences(SettingsSidebarItem, options, state);
			OnPropertyChanged(nameof(SettingsFooterVisibility));
			if (compactContentsChanged && _flatSidebarItems is not null) RebuildFlatTree();
			UpdateLegacySectionInsets();
			void Refresh(IEnumerable<INavigationControlItem> items)
			{
				foreach (var item in items)
				{
					if (item.Children is IEnumerable<INavigationControlItem> children) Refresh(children);
					ApplyVisualPreferences(item, options, state);
					if (item is ExpandableSidebarItemBase appearance) appearance.RefreshSidebarAppearance();
					else if (item is FileTagItem tag) tag.RefreshSidebarAppearance();
				}
			}
		}

		private async void UpgradeVisualIcon(INavigationControlItem item, double size)
		{
			var scale = MainWindow.Instance?.Content?.XamlRoot?.RasterizationScale ?? 1;
			if (item is LocationItem { IsHeader: true } section && IsBuiltInSectionIcon(section.Icon))
			{
				var uri = SectionIconForSize(section.Section, size, scale);
				if (uri is not null && section.Icon?.UriSource?.AbsoluteUri != uri)
				{
					section.Icon = new BitmapImage(new Uri(uri));
					NotifyEditorIconChanged();
				}
				return;
			}

			var path = VisualTarget(item);
			if (string.IsNullOrEmpty(path)) return;
			var iconPath = string.Equals(path, Constants.UserEnvironmentPaths.RecycleBinPath, StringComparison.OrdinalIgnoreCase)
				? Constants.UserEnvironmentPaths.ShellPlaces.First(place =>
					place.Key.StartsWith("::{", StringComparison.Ordinal) &&
					place.Value == Constants.UserEnvironmentPaths.RecycleBinPath).Key
				: path;
			if (!Path.IsPathRooted(iconPath) && !iconPath.StartsWith("::{", StringComparison.Ordinal)) return;
			var requestedSize = (int)Math.Clamp(Math.Ceiling(size * Math.Max(2, scale)), 32, 256);
			var request = visualIconRequests.GetValue(item, _ => new());
			var currentIcon = GetVisualIcon(item);
			// Exact size, so leaving compact mode doesn't keep the larger compact icon.
			if (request.Path == path && request.Size == requestedSize && (request.UpgradedIcon is null || ReferenceEquals(currentIcon, request.UpgradedIcon))) return;
			if (request.UpgradedIcon is null || !ReferenceEquals(currentIcon, request.UpgradedIcon)) request.OriginalIcon = currentIcon;
			request.Size = requestedSize;
			request.Path = path;
			var version = ++request.Version;
			try
			{
				var bytes = await FileThumbnailHelper.GetIconAsync(iconPath, (uint)requestedSize, false, IconOptions.ReturnIconOnly);
				if (request.Version != version || request.Path != path) return;
				if (bytes is null) { request.Size = 0; return; }
				var bitmap = await bytes.ToBitmapAsync();
				if (request.Version != version || request.Path != path || VisualTarget(item) != path) return;
				currentIcon = GetVisualIcon(item);
				if (request.UpgradedIcon is null || !ReferenceEquals(currentIcon, request.UpgradedIcon)) request.OriginalIcon = currentIcon;
				request.UpgradedIcon = bitmap;
				SetVisualIcon(item, bitmap);
			}
			catch (Exception) { if (request.Version == version) request.Size = 0; }
		}

		// The app's section icon in the scale that suits the drawn size: the same file name with its ".scale-N" qualifier.
		private static string? SectionIconForSize(SectionType section, double size, double rasterizationScale)
		{
			if (SidebarSectionIcons.For(section) is not { } uri) return null;
			var pixels = Math.Clamp(Math.Ceiling(size * rasterizationScale), 16, 64);
			var scale = pixels <= 16 ? 100 : pixels <= 20 ? 125 : pixels <= 24 ? 150 : pixels <= 32 ? 200 : 400;
			return $"{uri[..^4]}.scale-{scale}.png";
		}

		private static bool IsBuiltInSectionIcon(BitmapImage? icon)
			=> icon?.UriSource?.AbsoluteUri.StartsWith("ms-appx:///Assets/FluentIcons/SidebarSections/", StringComparison.OrdinalIgnoreCase) == true;

		private static BitmapImage? GetVisualIcon(INavigationControlItem item)
			=> item switch { LocationItem location => location.Icon, DriveItem drive => drive.Icon, _ => null };

		private void SetVisualIcon(INavigationControlItem item, BitmapImage? icon)
		{
			if (item is LocationItem location) location.Icon = icon;
			else if (item is DriveItem drive) drive.Icon = icon;
			NotifyEditorIconChanged();
		}

		private void RestoreVisualIcon(INavigationControlItem item)
		{
			if (item is LocationItem { IsHeader: true } section && IsBuiltInSectionIcon(section.Icon))
			{
				var uri = SidebarSectionIcons.For(section.Section);
				if (uri is not null && section.Icon?.UriSource?.AbsoluteUri != uri)
				{
					section.Icon = new BitmapImage(new Uri(uri));
					NotifyEditorIconChanged();
				}
			}
			if (!visualIconRequests.TryGetValue(item, out var request)) return;
			request.Version++;
			request.Size = 0;
			request.Path = null;
			if (request.UpgradedIcon is not null && ReferenceEquals(GetVisualIcon(item), request.UpgradedIcon))
				SetVisualIcon(item, request.OriginalIcon);
			request.UpgradedIcon = null;
			request.OriginalIcon = null;
		}

		private SidebarVisualDecoration CreateVisualDecoration(SidebarCustomizationState.Entry entry)
		{
			var item = new SidebarVisualDecoration
			{
				Path = entry.Key,
				Text = "",
				ToolTip = entry.Separator ? Strings.CustomizationSidebarAddSeparator.GetLocalizedResource() : Strings.CustomizationSidebarAddGap.GetLocalizedResource(),
				Section = SectionType.Pinned,
				SelectsOnInvoked = false,
				MenuOptions = new ContextMenuOptions()
			};
			ApplyVisualPreferences(item);
			return item;
		}

		// expanded: the state of the group this one replaces; a new group opens as it was left (saved like the app saves its sections).
		private SidebarVisualGroup CreateVisualGroup(SidebarCustomizationState.Entry entry, bool? expanded = null)
		{
			var group = new SidebarVisualGroup
			{
				Path = entry.Key,
				Text = entry.Label,
				Section = SectionType.Pinned,
				SelectsOnInvoked = false,
				ChildItems = [],
				IsExpanded = expanded ?? !CollapsedVisualGroups().Contains(entry.Key),
				MenuOptions = new ContextMenuOptions()
			};
			group.PropertyChanged += VisualGroup_PropertyChanged;
			return group;
		}

		// The added row a saved entry stands for (group, separator or gap, shortcut); null for the app's own items and sections.
		private INavigationControlItem? CreateVisualItem(SidebarCustomizationState.Entry entry, bool? expanded = null)
			=> entry.Key.StartsWith(VisualGroupPrefix, StringComparison.Ordinal) ? CreateVisualGroup(entry, expanded)
				: entry.Key.StartsWith(VisualDecorationPrefix, StringComparison.Ordinal) ? CreateVisualDecoration(entry)
				: entry.Key.StartsWith(VisualShortcutPrefix, StringComparison.Ordinal) ? CreateVisualShortcut(entry)
				: null;

		private static HashSet<string> CollapsedVisualGroups()
			=> new(CustomizationSettingsStore.Instance.TryRead(CustomizationSettingsStore.SidebarCollapsedGroupsKey, out var value)
				? value.Split('\n', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

		// Opening or closing a group is remembered; switching tabs and an "always expanded" or "always collapsed" setting are not.
		private void VisualGroup_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName != nameof(SidebarVisualGroup.IsExpanded) || IsApplyingTabState || sender is not SidebarVisualGroup { Path: { } key } group)
				return;
			var collapsed = CollapsedVisualGroups();
			if (!(group.IsExpanded ? collapsed.Remove(key) : collapsed.Add(key))) return;
			// Groups that were removed are dropped from the list.
			var state = ReadCustomization;
			collapsed.RemoveWhere(saved => !state.Entries.ContainsKey(saved));
			CustomizationSettingsStore.Instance.Write(CustomizationSettingsStore.SidebarCollapsedGroupsKey, string.Join('\n', collapsed));
		}

		private void RestoreVisualSectionDecorations(LocationItem section)
		{
			if (!SidebarCustomizationEnabled) return;
			if (section.ChildItems is not { } children) return;
			foreach (var entry in Customization.Entries.Values.Where(x =>
				x.Key.StartsWith(VisualDecorationPrefix, StringComparison.Ordinal) && x.Group == VisualKey(section)))
			{
				if (children.Any(x => x.Path == entry.Key)) continue;
				var item = CreateVisualDecoration(entry);
				children.Add(item);
				TrackSidebarItem(item);
			}
			ApplyVisualOrder(children);
		}

		// The options in effect (a switched-off feature's options at their off values), and an editable copy of the saved sidebar.
		private CustomizationPreferences CurrentOptions => CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences);
		private SidebarCustomizationState EditableCustomization => SidebarCustomizationState.Parse(UserSettingsService.AppearanceSettingsService.SidebarCustomization);

		private SidebarCustomizationState Customization => CurrentOptions.SidebarEnabled ? EditableCustomization : new();

		private SidebarCustomizationState ReadCustomization =>
			CurrentOptions.SidebarEnabled
				? SidebarCustomizationState.ReadSnapshot(UserSettingsService.AppearanceSettingsService.SidebarCustomization)
				: emptyCustomization;
		private static readonly SidebarCustomizationState emptyCustomization = new();

		// Hooks called from the original sidebar code. Each one does nothing while sidebar customization is off.

		// A built-in section heading got its icon again (theme change).
		private void RefreshVisualSectionIcon(LocationItem section)
		{
			if (!SidebarCustomizationEnabled) return;
			ApplyVisualPreferences(section);
			NotifyEditorIconChanged();
		}

		// An item was added to a built-in section.
		private void CustomizeAddedSectionItem(INavigationControlItem item, LocationItem section, BulkConcurrentObservableCollection<INavigationControlItem> children)
		{
			if (!SidebarCustomizationEnabled) return;
			ApplyVisualPreferences(item);
			RestoreVisualSectionDecorations(section);
			ApplyVisualOrder(children);
			QueueVisualId(item);
		}

		// The app adds a section's items again each time it reloads that section. An item already moved into a group stays there instead
		// of being added to its section a second time.
		private bool IsPlacedOutsideSection(INavigationControlItem item, BulkConcurrentObservableCollection<INavigationControlItem> sectionItems)
			=> SidebarCustomizationEnabled && FindVisualSiblings(item) is { } siblings && !ReferenceEquals(siblings, sectionItems);

		// A built-in section was added to the sidebar.
		private void CustomizeAddedSection(LocationItem section)
		{
			ApplyVisualPreferences(section);
			RestoreVisualSectionDecorations(section);
			ApplyVisualOrder(sidebarItems);
		}

		// Customization can move sections into groups, so every level is searched for the heading.
		private LocationItem? FindVisualSection(SectionType sectionType)
			=> sidebarItems.OfType<LocationItem>().FirstOrDefault(x => x.IsHeader && x.Section == sectionType)
				?? VisualItems().OfType<LocationItem>().FirstOrDefault(x => x.IsHeader && x.Section == sectionType);

		// Clicking an added item: separators and gaps do nothing, a shortcut without a target does nothing, and a group opens or closes.
		private bool HandleVisualItemInvoked(INavigationControlItem item)
		{
			if (item is SidebarVisualDecoration) return true;
			if (item is SidebarVisualShortcut && ReadCustomization.Get(VisualKey(item)).Target.Length == 0) return true;
			if (item is not SidebarVisualGroup) return false;
			item.IsExpanded = !item.IsExpanded;
			return true;
		}

		// A row dragged to rearrange the sidebar ("Drag to rearrange").
		private async Task<bool> HandleVisualDragOverAsync(ItemDragOverEventArgs args)
		{
			if (!args.DroppedItem.Contains(SidebarView.ReorderDataFormat))
			{
				if (!IsVisualDropTarget(args.DropTarget)) return false;
				// Added rows have no folder of their own (their path is a key), so files can't be dropped on them.
				args.RawEvent.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
				args.RawEvent.Handled = true;
				return true;
			}
			var key = await args.DroppedItem.GetDataAsync(SidebarView.ReorderDataFormat) as string;
			args.RawEvent.AcceptedOperation = TryGetVisualDrop(key, args.DropTarget, args.dropPosition, out _, out _, out _, out _)
				? global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move : global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
			args.RawEvent.Handled = true;
			return true;
		}

		private static bool IsVisualDropTarget(object target) => target is SidebarVisualGroup or SidebarVisualDecoration or SidebarVisualShortcut;

		private async Task<bool> HandleVisualDropAsync(ItemDroppedEventArgs args)
		{
			if (!args.DroppedItem.Contains(SidebarView.ReorderDataFormat)) return IsVisualDropTarget(args.DropTarget);
			var key = await args.DroppedItem.GetDataAsync(SidebarView.ReorderDataFormat) as string;
			MoveVisualDrop(key, args.DropTarget, args.dropPosition);
			args.RawEvent.Handled = true;
			return true;
		}

		private static bool IsCustomizationGroup(INavigationControlItem item) =>
			item is SidebarVisualGroup or LocationItem { IsHeader: true };

		private static string VisualKey(INavigationControlItem item) =>
			item is SidebarVisualGroup or SidebarVisualDecoration or SidebarVisualShortcut
				? item.Path!
				: item is LocationItem { IsHeader: true }
					? "section:" + item.Section
					: "item:" + item.Section + ":" + item.Path;

		// Sub-folders come from LocationItem.CreateSubfolder, which leaves Section at Home and uses a real folder path.
		private static bool IsVisualSubFolderKey(string key)
		{
			return key.StartsWith("item:Home:", StringComparison.Ordinal) && Path.IsPathRooted(key[10..]);
		}

		private static bool IsVisualSubFolder(INavigationControlItem item)
		{
			return item is LocationItem { IsHeader: false } and not SidebarVisualGroup and not SidebarVisualDecoration &&
				IsVisualSubFolderKey(VisualKey(item));
		}

		private Dictionary<INavigationControlItem, INavigationControlItem>? visualParents;

		private bool TryGetVisualParent(INavigationControlItem item, out INavigationControlItem? parent)
		{
			if (visualParents is not null && visualParents.TryGetValue(item, out parent))
				return true;

			var parents = new Dictionary<INavigationControlItem, INavigationControlItem>();
			void Visit(INavigationControlItem owner)
			{
				if (owner.Children is not IEnumerable<INavigationControlItem> children)
					return;

				foreach (var child in children.ToArray())
				{
					if (!parents.TryAdd(child, owner))
						continue;

					Visit(child);
				}
			}

			foreach (var root in sidebarItems.ToArray())
				Visit(root);

			visualParents = parents;
			return parents.TryGetValue(item, out parent);
		}

		// Returns the sub-item that owns a sub-folder, or null when the item is not a sub-folder.
		private INavigationControlItem? VisualSubFolderOwner(INavigationControlItem item)
		{
			var current = item;
			var visited = new HashSet<INavigationControlItem>();
			while (IsVisualSubFolder(current) && visited.Add(current))
			{
				if (!TryGetVisualParent(current, out var parent) || parent is null)
					return null;

				current = parent;
			}

			return ReferenceEquals(current, item) ? null : current;
		}

		// Sub-folders have no saved settings of their own. They use their sub-item's separate folder appearance when it exists,
		// otherwise the sub-item's own appearance, and always expand normally.
		private SidebarCustomizationState.Entry VisualSubFolderEntry(INavigationControlItem item, SidebarCustomizationState state)
		{
			var entry = new SidebarCustomizationState.Entry(VisualKey(item));
			if (VisualSubFolderOwner(item) is not { } owner)
				return entry with { LegacyExpansion = 0 };

			var ownerKey = VisualKey(owner);
			var source = state.Entries.TryGetValue(EditorFolderKey(ownerKey), out var folders) ? folders : state.Get(ownerKey);
			return SidebarCustomizationStyle.CopyAppearance(entry, source, 0);
		}

		private string? VisualTarget(INavigationControlItem item)
		{
			var target = ReadCustomization.Get(VisualKey(item)).Target;
			return string.IsNullOrEmpty(target) ? item.Path : target;
		}

		private static bool HasLegacyPin(INavigationControlItem item, SidebarCustomizationState state)
		{
			if (item is not LocationItem { IsHeader: false } || item is SidebarVisualGroup or SidebarVisualDecoration) return false;
			var group = state.Get(VisualKey(item)).Group;
			return group.Length > 0 ? string.Equals(group, "section:Pinned", StringComparison.OrdinalIgnoreCase)
				: item.Section == SectionType.Pinned && item is not SidebarVisualShortcut;
		}

		// fixedEntry styles an item the sidebar doesn't hold (folders listed only for the panel preview) with a given look.
		private void ApplyVisualPreferences(INavigationControlItem item, CustomizationPreferences? options = null, SidebarCustomizationState? state = null,
			SidebarCustomizationState.Entry? fixedEntry = null)
		{
			options ??= CurrentOptions;
			if (!options.SidebarEnabled)
			{
				ResetVisualPreferences(item);
				return;
			}
			var subFolder = IsVisualSubFolder(item);
			// While rearranging is on, every item, sub-folders included, drags as a rearrange instead of moving or copying the folder.
			// Sub-folder drops are rejected in TryGetVisualDrop, so they stay in place.
			if (item is ISidebarItemReorderModel reorder) reorder.SidebarDragKey = options.SidebarDragEnabled ? VisualKey(item) : null;
			if (item is not ISidebarItemAppearanceModel appearance)
				return;
			var customization = state ?? ReadCustomization;
			var entry = fixedEntry ?? (subFolder ? VisualSubFolderEntry(item, customization) : customization.Get(VisualKey(item)));
			if (item is SidebarVisualGroup group) ApplyGroupIcon(group, entry.IconSource);
			if (item is SidebarVisualShortcut shortcut && entry.SourceKey.Length > 0 &&
				ResolveVisualShortcutSource(entry.SourceKey) is { } original)
			{
				shortcut.SourceItem = original;
				if (GetVisualIcon(original) is { } originalIcon) shortcut.Icon = originalIcon;
			}

			// A tag always shows its own name (renamed in Settings > Tags or the panel's Tag name box). A duplicate shows exactly its
			// label, blank included, and never takes a new name from what it copies.
			appearance.SidebarDisplayName = item is SidebarVisualDecoration ? "" : item is FileTagItem ? null : item is SidebarVisualShortcut ? entry.Label :
				string.IsNullOrWhiteSpace(entry.Label) ? null : entry.Label;
			var legacy = options.SidebarAppearance == 1;
			if (item is LocationItem location) location.SidebarPinVisible = legacy && HasLegacyPin(item, customization) && !entry.HidePin;
			var legacyHeader = SidebarCustomizationStyle.IsLegacyHeader(IsCustomizationGroup(item), VisualKey(item));
			var size = Math.Max(0, entry.Size);
			var rowHeight = size switch { 1 => 48, 2 => 64, _ => 32 };
			var points = SidebarCustomizationStyle.DefaultPoints(size, options, legacyHeader);
			var iconSize = CustomizationPreferences.ToPixels(entry.IconPt > 0 ? entry.IconPt : points.Icon);
			var fontSize = CustomizationPreferences.ToPixels(entry.TextPt > 0 ? entry.TextPt : points.Text);
			var iconVisible = SidebarCustomizationStyle.IconVisible(entry, legacy && legacyHeader);
			var decoration = item is SidebarVisualDecoration;
			// The Settings button at the bottom keeps the original compact icon size.
			var settingsFooter = ReferenceEquals(item, SettingsSidebarItem);
			// Inner folder entries always use the normal expansion, so they expand normally.
			var expansion = SidebarCustomizationStyle.ExpansionMode(entry, IsCustomizationGroup(item), options);
			var look = new SidebarLook(
				RowHeight: decoration ? entry.Gap : Math.Max(rowHeight, Math.Max(fontSize * 1.35, iconVisible ? iconSize : 0) + 12),
				IconSize: iconSize, FontSize: fontSize, IconVisible: iconVisible, KeepTextPosition: options.SidebarKeepTextPosition,
				LegacyAppearance: legacy && !decoration, LegacyHeader: legacyHeader, CompactShowIcons: options.SidebarCompactShowIcons,
				CompactIconMode: settingsFooter ? 0 : options.SidebarCompactIconMode, CompactIconSize: options.SidebarCompactIconSize,
				CompactHideGroupIcon: options.SidebarCompactHideGroupIcons && IsCustomizationGroup(item),
				CompactGroupContents: options.SidebarCompactGroupContents, ArrowVisible: expansion != 2, Decoration: decoration,
				Separator: decoration && entry.Separator, Hidden: entry.Hidden || IsSectionSwitchedOff(item),
				// Separators and gaps are spacing already, and the Settings button sits apart at the bottom, so the extra space between
				// rows leaves them as set.
				Spacing: decoration || settingsFooter ? 0 : options.SidebarItemSpacing,
				CompactSpacing: decoration || settingsFooter ? 0 : options.SidebarCompactItemSpacing);
			var lockBefore = appearance.SidebarExpansionLock;
			// A forced expansion isn't the user's choice, so the app's own expansion memory (per tab and its saved sections) doesn't record it.
			Interlocked.Increment(ref applyingTabStateDepth);
			try { appearance.SidebarExpansionLock = CanExpandEditorItem(item) ? expansion : 0; }
			finally { Interlocked.Decrement(ref applyingTabStateDepth); }
			// No longer forced: the item opens or closes as the user last left it.
			if (lockBefore != 0 && appearance.SidebarExpansionLock == 0) ApplyTabStateToNewlyAddedItem(item);
			// The compact icon size only applies while the sidebar is compact; the full sidebar keeps its own icon size.
			var compact = IsCompactDisplayMode;
			var displayIconSize = compact ? settingsFooter ? 16 : options.CompactIconPixels : iconSize;
			if (options.SharpLargeIcons && (iconVisible || compact && look.CompactShowIcons) && displayIconSize > 16)
				UpgradeVisualIcon(item, displayIconSize);
			else RestoreVisualIcon(item);
			appearance.SidebarLook = look;
		}

		// Sidebar customization is off: the item goes back to its original values once, and is left alone after that.
		private void ResetVisualPreferences(INavigationControlItem item)
		{
			var appearance = item as ISidebarItemAppearanceModel;
			if (appearance?.SidebarLook is null && (item as ISidebarItemReorderModel)?.SidebarDragKey is null)
				return;
			if (item is ISidebarItemReorderModel reorder) reorder.SidebarDragKey = null;
			if (appearance is null) return;
			appearance.SidebarLook = null;
			appearance.SidebarDisplayName = null;
			if (item is LocationItem location) location.SidebarPinVisible = false;
			var lockBefore = appearance.SidebarExpansionLock;
			appearance.SidebarExpansionLock = 0;
			if (lockBefore != 0) ApplyTabStateToNewlyAddedItem(item);
			RestoreVisualIcon(item);
		}

		private void ApplyVisualOrder(BulkConcurrentObservableCollection<INavigationControlItem> items)
		{
			if (!SidebarCustomizationEnabled) return;
			var state = Customization;
			var sorted = KeepUnplacedInPlace(items.ToArray(), x => state.Get(VisualKey(x)).Order);
			visualReorderDepth++;
			try
			{
				for (int i = 0; i < sorted.Length; i++)
				{
					var oldIndex = items.IndexOf(sorted[i]);
					if (oldIndex == i) continue;
					items.RemoveAt(oldIndex);
					items.Insert(i, sorted[i]);
				}
			}
			finally { visualReorderDepth--; }
		}

		// Items with a saved position are sorted by it. An item without one (a section the app had switched off when the order
		// was saved, a drive plugged in later) stays right after the item it currently follows, instead of going to the end.
		// It follows a neighbour rather than keeping a fixed slot: groups, separators and shortcuts are rebuilt at the end of
		// the list on every change, so a fixed slot would push them to the bottom.
		internal static T[] KeepUnplacedInPlace<T>(IReadOnlyList<T> items, Func<T, int> order)
		{
			var keys = new double[items.Count];
			var follows = -0.5;
			for (int i = 0; i < items.Count; i++)
			{
				var saved = order(items[i]);
				if (saved == int.MaxValue)
					keys[i] = follows;
				else
				{
					keys[i] = saved;
					follows = saved + 0.5;
				}
			}
			// OrderBy is stable, so items with the same key keep their current order.
			return Enumerable.Range(0, items.Count).OrderBy(i => keys[i]).Select(i => items[i]).ToArray();
		}

		private BulkConcurrentObservableCollection<INavigationControlItem>? FindVisualSiblings(INavigationControlItem item)
		{
			if (sidebarItems.Contains(item)) return sidebarItems;
			return FindChildren(sidebarItems);
			BulkConcurrentObservableCollection<INavigationControlItem>? FindChildren(IEnumerable<INavigationControlItem> parents)
			{
				foreach (var parent in parents)
				{
					if (parent.Children is not BulkConcurrentObservableCollection<INavigationControlItem> children) continue;
					if (children.Contains(item)) return children;
					var found = FindChildren(children);
					if (found is not null) return found;
				}
				return null;
			}
		}

		private void SaveVisualPreferences(SidebarCustomizationState state)
		{
			UserSettingsService.AppearanceSettingsService.SidebarCustomization = state.Serialize();
		}

		private SidebarCustomizationState? lastSidebarStructure;
		private void RefreshVisualSidebar()
		{
			var options = CurrentOptions;
			var state = ReadCustomization;
			visualParents = null;
			var expandedGroups = VisualItems()
				.OfType<SidebarVisualGroup>()
				.GroupBy(group => group.Path ?? "")
				.ToDictionary(group => group.Key, group => group.First().IsExpanded);
			visualReorderDepth++;
			try
			{
				RestoreOriginalVisualPlacements(state);
				FindVisualSiblings(SettingsSidebarItem)?.Remove(SettingsSidebarItem);
				foreach (var group in VisualItems().OfType<SidebarVisualGroup>())
					if (group.ChildItems is { } children)
						foreach (var child in children.Where(child =>
							child is not SidebarVisualGroup and not SidebarVisualDecoration &&
							child is not SidebarVisualShortcut).ToArray())
						{
							children.Remove(child);
							if (!sidebarItems.Contains(child)) sidebarItems.Add(child);
						}
				foreach (var item in VisualItems().Where(item => item is SidebarVisualGroup or SidebarVisualDecoration or SidebarVisualShortcut))
				{
					// An expanded shortcut watches its folder; stop it, as the app does for items it removes.
					(item as ExpandableSidebarItemBase)?.StopWatchingSubfoldersAndDescendants();
					FindVisualSiblings(item)?.Remove(item);
					UntrackSidebarItem(item);
				}
				foreach (var entry in state.Entries.Values)
				{
					if (CreateVisualItem(entry, expandedGroups.TryGetValue(entry.Key, out var expanded) ? expanded : null) is not { } item) continue;
					sidebarItems.Add(item);
					TrackSidebarItem(item);
				}
				foreach (var item in VisualItems()) { ApplyVisualPreferences(item, options, state); TrackSidebarItem(item); }
			}
			finally { visualReorderDepth--; }
			RestoreVisualPlacements(true);
			ApplyVisualPreferences(SettingsSidebarItem, options, state);
			OnPropertyChanged(nameof(SettingsFooterVisibility));
			lastSidebarStructure = state;
			lastSidebarAppearance = options;
			if (_flatSidebarItems is not null) RebuildFlatTree();
			// Saved pinned folders and drives that don't have their ID yet.
			if (SidebarCustomizationEnabled)
				foreach (var item in VisualItems())
					if (state.Entries.TryGetValue(VisualKey(item), out var saved) && saved.Id.Length == 0) QueueVisualId(item);
		}
	}

	internal sealed partial class SidebarVisualGroup : LocationItem, ISidebarItemModel
	{
		internal string? IconSource { get; set; }
		public override IconElement? IconElement => Icon is null ? SidebarFallbackIcon() : base.IconElement;
		bool ISidebarItemModel.HasUnrealizedChildren => true;
		bool ISidebarItemModel.IsLeafWithChildren => true;
	}

	internal sealed partial class SidebarVisualShortcut : LocationItem
	{
		internal string Target { get; init; } = "";
		private INavigationControlItem? sourceItem;
		internal INavigationControlItem? SourceItem
		{
			get => sourceItem;
			set
			{
				if (SetProperty(ref sourceItem, value)) OnPropertyChanged(nameof(IconElement));
			}
		}
		public override IconElement? IconElement
		{
			[DynamicWindowsRuntimeCast(typeof(IconElement))]
			get => Icon is not null ? base.IconElement :
				string.Equals(Target, "Settings", StringComparison.OrdinalIgnoreCase) ? new SymbolIcon(Symbol.Setting) :
				sourceItem?.IconElement as IconElement ?? SidebarFallbackIcon();
		}
		protected override string ExpansionPath => Target;
	}

	internal sealed partial class SidebarVisualDecoration : LocationItem { }
}
