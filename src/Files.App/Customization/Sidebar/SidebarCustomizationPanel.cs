// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using CommunityToolkit.WinUI.Controls;
using Files.App.Controls;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Foundation;
using WinRT;
using static Files.App.ViewModels.UserControls.SidebarViewModel;

namespace Files.App.ViewModels.UserControls
{
	internal sealed partial class SidebarCustomizationPanel : Grid
	{
		private readonly SidebarViewModel owner;
		// The preview is the sidebar's own control showing the sidebar's own items, so every row looks exactly as in the sidebar.
		private readonly SidebarView preview = new()
		{
			// The sidebar draws its rows 4 px inside its pane; this lines them up with the Add button above.
			Margin = new Thickness(-4, 0, 0, 0)
		};
		// The panel row each previewed item stands for.
		private readonly Dictionary<ISidebarItemModel, EditorRow> previewRows = new(ReferenceEqualityComparer.Instance);
		// The rows the preview shows. Updated in place (only rows that changed are replaced, added or removed), so the list
		// never resets and keeps its scroll position.
		private readonly System.Collections.ObjectModel.ObservableCollection<FlatSidebarItem> previewNodes = [];
		private readonly StackPanel inspector = new() { Spacing = 10, Padding = new Thickness(0, 0, 20, 24) };
		// Rows take their inset from the settings flyout's content padding.
		private readonly StackPanel defaults = new() { Spacing = 8 };
		// Kept by the sidebar, so Undo and Redo survive closing and reopening the panel until the app closes.
		private CustomChangeHistory<EditorSnapshot> history => owner.EditorHistory;
		// Shared with the other panel (window or Settings), so both show the same rows expanded.
		private HashSet<string> collapsed => owner.EditorCollapsed;
		private HashSet<string> previewFolderParents => owner.EditorPreviewFolderParents;
		private readonly HashSet<string> loadingPreviewFolders = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> requestedPreviewFolders = new(StringComparer.OrdinalIgnoreCase);
		private List<EditorRow> renderedRows = [];
		private readonly Button duplicate;
		private readonly Button undo;
		private readonly Button redo;
		private readonly Button resetAll;

		private Button? itemReset;
		private string? itemResetKey;
		private readonly ComboBox appearanceChoice = new()
		{
			MinWidth = 140,
			SelectedIndex = -1,
			VerticalAlignment = VerticalAlignment.Center
		};
		private string? selectedKey;
		private bool refreshing;
		// The panel works only while its Settings page is shown (see Show and Hide).
		private bool hidden = true;
		private EditorSnapshot? lastRendered;
		private readonly ScrollViewer details;
		private readonly Border dragArea = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
		private string? inspectorKey;
		// The panel lives inside Settings; dialogs it opens (Browse for an icon) belong to the main window.
		private nint HostHandle => MainWindow.Instance.WindowHandle;
		private int renderVersion;
		private int inspectorVersion;
		private bool refreshQueued;
		private bool pendingInspector;
		private bool pendingDefaults;
		private readonly List<Action> pendingTextCommits = [];
		private double previewRatio = 1d / 3;
		private int changeDepth;
		private EditorSnapshot? localChangeSnapshot;
		private int appearanceScope = 2;
		private bool appearanceExpanded;
		private Action? updateAppearanceApply;
		private Action? updateGroupIconPreview;
		private (string Key, EditorSnapshot After)? appearanceUndo;

		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		internal SidebarCustomizationPanel(SidebarViewModel owner)
		{
			this.owner = owner;
			Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
			SettingsValueEditing.Attach(this);
			Margin = new Thickness(0);
			RowDefinitions.Add(new() { Height = GridLength.Auto });
			RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
			RowDefinitions.Add(new() { Height = GridLength.Auto });
			var contentBackground = (Grid)XamlReader.Load(
				"""
				<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" IsHitTestVisible="False">
					<Border Background="{ThemeResource App.Theme.Sidebar.BackgroundBrush}" />
					<Border Background="{ThemeResource App.Theme.FileArea.BackgroundBrush}" />
				</Grid>
				""");
			SetRow(contentBackground, 1); SetRowSpan(contentBackground, 2); Children.Add(contentBackground);
			var headerBackground = (Grid)XamlReader.Load(
				"""
				<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" IsHitTestVisible="False">
					<Border Background="{ThemeResource App.Theme.Sidebar.BackgroundBrush}" />
					<Border Background="{ThemeResource App.Theme.FileArea.BackgroundBrush}" />
				</Grid>
				""");
			Children.Add(headerBackground);
			var headerDivider = (Border)XamlReader.Load(
				"""<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Height="1" VerticalAlignment="Top" Background="{ThemeResource DividerStrokeColorDefaultBrush}" IsHitTestVisible="False" />""");
			SetRow(headerDivider, 1); SetRowSpan(headerDivider, 2); Children.Add(headerDivider);
			// In a narrow panel the buttons wrap onto a second line instead of being cut off.
			var toolbar = new CommunityToolkit.WinUI.Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
			var addMenu = new MenuFlyout();
			foreach (var pair in new[]
			{
				("group", Strings.SidebarVisualNewGroup),
				("separator", Strings.CustomizationSidebarAddSeparator),
				("gap", Strings.CustomizationSidebarAddGap)
			})
			{
				var item = new MenuFlyoutItem { Text = pair.Item2.GetLocalizedResource() };
				item.Click += (_, _) =>
				{
					Change(() => this.selectedKey = owner.AddEditorEntry(pair.Item1, this.selectedKey));
					// Open the group it was added to, so the new item is in view.
					if (owner.EditorRows().FirstOrDefault(row => row.Key == this.selectedKey) is { } added) collapsed.Remove(added.Parent);
				};
				addMenu.Items.Add(item);
			}
			toolbar.Children.Add(new DropDownButton
			{
				Content = Strings.SidebarEditorAdd.GetLocalizedResource(),
				Flyout = addMenu,
				Height = 36,
				Padding = new Thickness(16, 0, 16, 0),
				VerticalAlignment = VerticalAlignment.Center
			});
			duplicate = IconButton("\uE8C8", Strings.SidebarEditorDuplicate, DuplicateSelectedItem);
			CenterIconButton(duplicate, 14);
			toolbar.Children.Add(duplicate);
			VerticalSeparator(toolbar);
			undo = IconButton("\uE7A7", Strings.Undo, () => Restore(false));
			redo = IconButton("\uE7A6", Strings.Redo, () => Restore(true));
			// The same compact confirmation as Reset to default in Settings.
			var confirm = new Button
			{
				Content = Strings.CustomizationReset.GetLocalizedResource(),
				Style = (Style)Application.Current.Resources["AccentButtonStyle"],
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			var cancel = new Button
			{
				Content = Strings.Cancel.GetLocalizedResource(),
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			var confirmButtons = new Grid { ColumnSpacing = 8 };
			confirmButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			confirmButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			SetColumn(cancel, 1);
			confirmButtons.Children.Add(confirm);
			confirmButtons.Children.Add(cancel);
			var confirmation = new Flyout
			{
				Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
				Content = new StackPanel
				{
					Width = 280,
					Spacing = 16,
					Children =
					{
						new TextBlock { Text = Strings.SidebarEditorResetConfirm.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
						confirmButtons
					}
				}
			};
			confirmation.Opening += (_, _) => CommitPendingEdits();
			confirmation.Opened += (_, _) => cancel.Focus(FocusState.Programmatic);
			confirm.Click += (_, _) =>
			{
				confirmation.Hide();
				Change(owner.ResetSidebarEditor);
				collapsed.Clear();
				previewFolderParents.Clear();
				requestedPreviewFolders.Clear();
				Refresh();
			};
			cancel.Click += (_, _) => confirmation.Hide();
			// Undo, Redo and Reset sit on the right, away from the editing buttons.
			var windowActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
			windowActions.Children.Add(undo); windowActions.Children.Add(redo);
			VerticalSeparator(windowActions);
			resetAll = new Button
			{
				Content = Strings.CustomizationResetDefaults.GetLocalizedResource(),
				Height = 36,
				Padding = new Thickness(16, 0, 16, 0),
				VerticalAlignment = VerticalAlignment.Center
			};
			AutomationProperties.SetName(resetAll, Strings.SidebarEditorResetTooltip.GetLocalizedResource());
			ToolTipService.SetToolTip(resetAll, Strings.SidebarEditorResetTooltip.GetLocalizedResource());
			resetAll.Flyout = confirmation;
			windowActions.Children.Add(resetAll);
			var header = new Grid { ColumnSpacing = 8 };
			header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			windowActions.VerticalAlignment = VerticalAlignment.Top;
			SetColumn(windowActions, 1);
			header.Children.Add(toolbar); header.Children.Add(windowActions);
			appearanceChoice.Items.Add(Strings.SidebarEditorDefault.GetLocalizedResource());
			appearanceChoice.Items.Add(Strings.SidebarEditorLegacy.GetLocalizedResource());
			AutomationProperties.SetName(appearanceChoice, Strings.CustomizationSidebarAppearance.GetLocalizedResource());
			appearanceChoice.SelectionChanged += (_, _) =>
			{
				if (refreshing || hidden || appearanceChoice.SelectedIndex < 0 || appearanceChoice.SelectedIndex == owner.EditorOptions.SidebarAppearance) return;
				Change(() => owner.SaveEditorOptions(owner.EditorOptions with { SidebarAppearance = appearanceChoice.SelectedIndex }));
			};
			// One 20 px inset for every row; the presenter adds no padding of its own.
			var settingsContent = new StackPanel
			{
				Spacing = 8,
				Padding = new Thickness(20, 20, 20, 20),
				Children =
				{
					SettingRow(Strings.CustomizationSidebarAppearance, appearanceChoice),
					defaults
				}
			};
			SettingsValueEditing.Attach(settingsContent, useScopeEvents: true);
			var settingsScroll = new ScrollViewer
			{
				Content = settingsContent,
				Width = 400,
				MaxHeight = 520,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
			var settingsFlyout = new Flyout
			{
				Content = settingsScroll,
				Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
				FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
				{
					Setters =
					{
						new Setter(Control.PaddingProperty, new Thickness(0)),
						new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity)
					}
				}
			};
			settingsFlyout.Opening += (_, _) =>
			{
				CommitPendingEdits();
				settingsScroll.Width = Math.Min(400, Math.Max(200, ActualWidth - 48));
				settingsScroll.MaxHeight = Math.Min(520, Math.Max(160, ActualHeight - dragArea.ActualHeight - 48));
			};
			toolbar.Children.Add(new DropDownButton
			{
				Content = Strings.SidebarEditorOptions.GetLocalizedResource(),
				Flyout = settingsFlyout,
				Height = 36,
				Padding = new Thickness(16, 0, 16, 0),
				VerticalAlignment = VerticalAlignment.Center
			});
			dragArea.Padding = new Thickness(20, 16, 20, 16);
			dragArea.Child = header;
			Children.Add(dragArea);
			Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///Styles/DefaultGridSplitterStyle.xaml") });
			// No right margin: the item settings' own 20 px right padding lines their edge up with the top bar's buttons, and the scrollbar sits in that gutter.
			var body = new Grid { RowSpacing = 10, Margin = new Thickness(20, 16, 0, 0) };
			details = new ScrollViewer
			{
				Content = inspector,
				Padding = new Thickness(20, 0, 0, 0),
				VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
			};
			body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
			body.ColumnDefinitions.Add(new() { Width = new GridLength(14) });
			body.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star), MinWidth = 320 });
			body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
			body.RowDefinitions.Add(new() { Height = new GridLength(0) });
			SetColumn(details, 2);
			var splitter = new Files.App.Controls.GridSplitter { Width = 14, HorizontalAlignment = HorizontalAlignment.Stretch,
				Style = (Style)Resources["DefaultGridSplitterStyle"], ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Columns };
			SetColumn(splitter, 1);
			AutomationProperties.SetName(splitter, Strings.SidebarEditorResizePreview.GetLocalizedResource());
			splitter.ManipulationCompleted += (_, _) =>
			{
				var width = body.ColumnDefinitions[0].ActualWidth + body.ColumnDefinitions[2].ActualWidth;
				if (width > 0) previewRatio = Math.Clamp(body.ColumnDefinitions[0].ActualWidth / width, 0.2, 0.6);
			};
			// When the panel is too narrow for side by side, the item settings go below the preview with a divider between them.
			var stackedDivider = (Border)XamlReader.Load(
				"""<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Height="1" VerticalAlignment="Top" Margin="-20,-5,0,0" Background="{ThemeResource DividerStrokeColorDefaultBrush}" IsHitTestVisible="False" Visibility="Collapsed" />""");
			SetRow(stackedDivider, 1); body.Children.Add(stackedDivider);
			SizeChanged += (_, args) =>
			{
				var narrow = args.NewSize.Width < 660;
				stackedDivider.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
				details.Padding = new Thickness(narrow ? 0 : 20, 0, 0, 0);
				body.ColumnDefinitions[0].MinWidth = narrow ? 0 : 180;
				body.ColumnDefinitions[2].MinWidth = narrow ? 0 : 320;
				body.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : previewRatio, GridUnitType.Star);
				body.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 14);
				body.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1 - previewRatio, GridUnitType.Star);
				body.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
				splitter.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
				SetColumn(details, narrow ? 0 : 2); SetRow(details, narrow ? 1 : 0);
			};
			body.Children.Add(preview); body.Children.Add(splitter); body.Children.Add(details); SetRow(body, 1); Children.Add(body);
			AutomationProperties.SetName(preview, Strings.SidebarEditorPreview.GetLocalizedResource());
			// Inner folder rows are view only: they can't be selected or dragged.
			// Rows (and their hover highlight) end 20 px before the right edge, the same as their left inset; the scroll bar stays at the edge.
			preview.EnablePreview(model => previewRows.TryGetValue(model, out var row) && !row.ViewOnly ? row.Key : null, rightInset: 20);
			preview.MenuItemsSource = previewNodes;
			preview.ItemInvoked += (sender, _) =>
			{
				if (sender is SidebarItem { Item: { } model } && previewRows.TryGetValue(model, out var row)) SelectPreviewRow(row);
			};
			preview.PreviewExpansionToggled += (_, model) =>
			{
				if (previewRows.TryGetValue(model, out var row)) TogglePreviewExpansion(row.Key);
			};
			preview.ItemDragOver += async (_, args) => await PreviewDragAsync(args.DroppedItem, args.DropTarget, args.dropPosition, args.RawEvent, false);
			preview.ItemDropped += async (_, args) => await PreviewDragAsync(args.DroppedItem, args.DropTarget, args.dropPosition, args.RawEvent, true);
			Loaded += (_, _) => Show();
			Unloaded += (_, _) => Hide();
		}
		// Shown with its page: connects to the sidebar and draws the preview from the current settings (after the page has
		// appeared, so Settings isn't held up), on the item last selected in any Settings tab.
		private void Show()
		{
			if (!hidden) return;
			hidden = false;
			selectedKey = owner.EditorSelectedKey ?? selectedKey;
			requestedPreviewFolders.Clear();
			owner.PropertyChanged += OwnerChanged;
			owner.EditorIconsChanged += OwnerIconsChanged;
			owner.EditorPanelShown();
			preview.MenuItemsSource = previewNodes;
			DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { if (!hidden) Refresh(true, true); });
		}
		// Hidden with its page (another tab or Settings page shown): keeps a half-typed edit, then stops following the sidebar
		// and empties the preview. WinUI can report Unloaded after the page is already shown again; that is ignored.
		private void Hide()
		{
			if (hidden || IsLoaded) return;
			CommitPendingEdits();
			hidden = true;
			pendingTextCommits.Clear();
			owner.PropertyChanged -= OwnerChanged;
			owner.EditorIconsChanged -= OwnerIconsChanged;
			foreach (var list in watchedSidebarLists) list.CollectionChanged -= SidebarListChanged;
			watchedSidebarLists.Clear();
			// Clearing the preview's rows detaches them from the sidebar items they show.
			preview.MenuItemsSource = null!;
			previewRows.Clear();
			previewNodes.Clear();
			owner.EditorPanelHidden();
		}
		private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName != nameof(SidebarViewModel.SettingsFooterVisibility) || changeDepth > 0) return;
			var snapshot = owner.CaptureEditor();
			if (snapshot != lastRendered && snapshot != localChangeSnapshot) QueueRefresh(true, true);
		}
		private void OwnerIconsChanged(object? sender, EventArgs args)
		{
			updateGroupIconPreview?.Invoke();
			QueueRefresh(false);
		}
		private void Change(Action action, bool rebuildInspector = true)
		{
			var before = owner.CaptureEditor();
			changeDepth++;
			try { action(); }
			finally { changeDepth--; }
			var after = owner.CaptureEditor();
			localChangeSnapshot = after;
			history.Record(before, after);
			updateAppearanceApply?.Invoke();
			QueueRefresh(rebuildInspector, rebuildInspector);
		}
		private void SelectPreviewRow(EditorRow row)
		{
			if (refreshing || hidden) return;
			// Inner folder rows are view only; the selection stays on the edited item.
			if (row.ViewOnly) { ShowPreviewSelection(); return; }
			if (selectedKey == row.Key && inspectorKey == row.Key && inspector.Children.Count > 0) return;
			selectedKey = row.Key;
			QueueRefresh(true);
		}
		private void ShowPreviewSelection()
			=> preview.SelectedItem = previewRows.FirstOrDefault(pair => string.Equals(pair.Value.Key, selectedKey, StringComparison.OrdinalIgnoreCase)).Key!;
		// Rows dragged inside the preview move to before or after another row, or into a group.
		private async Task PreviewDragAsync(DataPackageView data, object target, SidebarItemDropPosition position, DragEventArgs args, bool drop)
		{
			if (hidden || !data.Contains(SidebarView.PreviewDataFormat) || target is not ISidebarItemModel model || !previewRows.TryGetValue(model, out var row)) return;
			args.Handled = true;
			if (!drop)
			{
				// No caption while dragging; the pointer only shows whether a drop is allowed.
				if (args.DragUIOverride is { } overrideUI) overrideUI.IsCaptionVisible = false;
				// Near the top or bottom edge, the preview scrolls so rows out of view can be reached.
				const double edge = 40, step = 16;
				var y = args.GetPosition(preview).Y;
				if (y < edge) preview.ScrollToVerticalOffset(Math.Max(0, preview.VerticalScrollOffset - step));
				else if (y > preview.ActualHeight - edge) preview.ScrollToVerticalOffset(preview.VerticalScrollOffset + step);
			}
			var deferral = args.GetDeferral();
			try
			{
				var key = await data.GetDataAsync(SidebarView.PreviewDataFormat) as string;
				var inside = position == SidebarItemDropPosition.Center;
				var after = position == SidebarItemDropPosition.Bottom;
				var allowed = key is not null && !row.ViewOnly && owner.CanMoveEditorRow(key, row.Key, inside);
				args.AcceptedOperation = allowed ? DataPackageOperation.Move : DataPackageOperation.None;
				if (drop && allowed)
					Change(() => owner.MoveEditorRow(key!, row.Key, inside, after));
			}
			finally { deferral.Complete(); }
		}
		private void QueueRefresh(bool rebuildInspector, bool rebuildDefaults = false)
		{
			pendingInspector |= rebuildInspector;
			pendingDefaults |= rebuildDefaults;
			if (hidden || refreshQueued) return;
			refreshQueued = DispatcherQueue.TryEnqueue(() =>
			{
				refreshQueued = false;
				var rebuild = pendingInspector;
				var defaultsChanged = pendingDefaults;
				pendingInspector = pendingDefaults = false;
				if (!hidden) Refresh(rebuild, defaultsChanged);
			});
		}
		private void CommitPendingEdits()
		{
			foreach (var commit in pendingTextCommits.ToArray()) commit();
		}
		private void Restore(bool forward)
		{
			CommitPendingEdits();
			var current = owner.CaptureEditor();
			EditorSnapshot restored;
			if (forward ? history.Redo(current, out restored) : history.Undo(current, out restored)) owner.RestoreEditor(restored);
			QueueRefresh(true, true);
		}
		private void Refresh(bool rebuildInspector = true, bool rebuildDefaults = false)
		{
			var previousSelection = selectedKey;
			var version = ++renderVersion;
			var offset = preview.VerticalScrollOffset;
			refreshing = true;
			try
			{
				appearanceChoice.SelectedIndex = owner.EditorOptions.SidebarAppearance;
				var baseRows = owner.EditorRows().Where(row => !owner.IsEditorSettingsRow(row.Key)).ToList();
				foreach (var row in baseRows.Where(row => row.CanExpand))
				{
					if (PreviewExpansionMode(row) == 2) collapsed.Add(row.Key);
					else if (PreviewExpansionMode(row) == 1) collapsed.Remove(row.Key);
				}
				var rows = WithPreviewFolders(baseRows);
				renderedRows = rows;
				var shown = RenderPreview();
				if (selectedKey is null || rows.All(row => row.Key != selectedKey))
					selectedKey = shown.FirstOrDefault(row => !row.ViewOnly)?.Key;
				ShowPreviewSelection();
				// After layout (low priority), so the list has measured its new rows and the position isn't cut short.
				DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
				{
					if (!hidden && version == renderVersion && Math.Abs(preview.VerticalScrollOffset - offset) > 0.5) preview.ScrollToVerticalOffset(offset);
				});
				undo.IsEnabled = history.CanUndo(owner.CaptureEditor()); redo.IsEnabled = history.CanRedo(owner.CaptureEditor());
				// Reset buttons are greyed out when there is nothing to reset.
				resetAll.IsEnabled = !owner.IsSidebarEditorDefault();
				duplicate.IsEnabled = baseRows.FirstOrDefault(candidate => candidate.Key == selectedKey) is { } selected && owner.CanDuplicateEditorRow(selected);
			}
			finally { refreshing = false; }
			if (rebuildInspector || previousSelection != selectedKey) BuildInspector();
			else if (itemReset is not null && itemResetKey is not null) itemReset.IsEnabled = !owner.IsEditorEntryDefault(itemResetKey);
			if (rebuildDefaults) BuildDefaults();
			updateAppearanceApply?.Invoke();
			lastRendered = owner.CaptureEditor();
			localChangeSnapshot = null;
			WatchSidebarLists();
		}
		// Read-only: when the sidebar gains or loses items (a drive plugged in, a folder pinned), the preview refreshes.
		private readonly List<System.Collections.Specialized.INotifyCollectionChanged> watchedSidebarLists = [];
		private void WatchSidebarLists()
		{
			if (hidden) return;
			var lists = owner.EditorWatchLists();
			if (lists.Count == watchedSidebarLists.Count && lists.Zip(watchedSidebarLists).All(pair => ReferenceEquals(pair.First, pair.Second))) return;
			foreach (var list in watchedSidebarLists) list.CollectionChanged -= SidebarListChanged;
			watchedSidebarLists.Clear();
			foreach (var list in lists)
			{
				list.CollectionChanged += SidebarListChanged;
				watchedSidebarLists.Add(list);
			}
		}
		private void SidebarListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
			=> DispatcherQueue.TryEnqueue(() => { if (!hidden && changeDepth == 0) QueueRefresh(false); });
		// Draws the visible rows with the sidebar's own control: the sidebar's items, or stand-ins for rows whose item isn't in the sidebar.
		private EditorRow[] RenderPreview()
		{
			var visibleKeys = SidebarPreviewHierarchy.VisibleKeys(renderedRows.Select(row => (row.Key, row.Parent)), collapsed);
			previewRows.Clear();
			var current = previewNodes.ToList();
			var nodes = new List<FlatSidebarItem>();
			var shown = new List<EditorRow>();
			foreach (var row in renderedRows.Where(row => visibleKeys.Contains(row.Key)))
			{
				if (owner.EditorPreviewItem(row) is not { } item || !previewRows.TryAdd(item, row)) continue;
				// Hidden rows are dimmed; hidden files and folders keep the sidebar's own dimming.
				var opacity = row.Entry.Hidden ? 0.45 : item is LocationItem { IsHidden: true } ? Constants.UI.DimItemOpacity : 1;
				var expanded = row.CanExpand && !collapsed.Contains(row.Key);
				var acceptsInside = row.Group && !row.ViewOnly;
				// A row already shown for the same item, in the same place in the tree, is kept as it is.
				nodes.Add(current.FirstOrDefault(node => ReferenceEquals(node.Item, item) && node.Depth == row.Depth &&
						node.PreviewExpanded == expanded && node.PreviewAcceptsInside == acceptsInside && node.RowOpacity == opacity)
					?? new FlatSidebarItem(item, row.Depth, opacity) { PreviewExpanded = expanded, PreviewAcceptsInside = acceptsInside });
				shown.Add(row);
			}
			// The same gaps as the sidebar: 12 px after an expanded section, and Legacy's 2 px insets at both ends of a section.
			var legacy = owner.EditorOptions.SidebarAppearance == 1;
			var previousExpanded = false;
			var legacySection = false;
			for (int index = 0; index < nodes.Count; index++)
			{
				var node = nodes[index];
				if (node.Depth == 0)
				{
					node.HasExpandedPredecessor = previousExpanded;
					previousExpanded = node.Item.Children is null || (shown[index].CanExpand ? node.PreviewExpanded : node.Item.IsExpanded);
					legacySection = node.Item is ISidebarItemAppearanceModel { SidebarLegacyHeader: true };
				}
				var first = legacy && legacySection && node.Depth == 1 && index > 0 && nodes[index - 1].Depth == 0;
				var last = legacy && legacySection && node.Depth > 0 && (index + 1 == nodes.Count || nodes[index + 1].Depth == 0);
				node.SetLegacySectionInsets(first, last);
			}
			// Bring the shown rows to the new ones with the fewest changes: the unchanged rows at the start and end stay,
			// rows in between are replaced, and only the difference in length is added or removed.
			var start = 0;
			while (start < previewNodes.Count && start < nodes.Count && ReferenceEquals(previewNodes[start], nodes[start])) start++;
			var oldEnd = previewNodes.Count;
			var newEnd = nodes.Count;
			while (oldEnd > start && newEnd > start && ReferenceEquals(previewNodes[oldEnd - 1], nodes[newEnd - 1])) { oldEnd--; newEnd--; }
			for (var index = start; index < Math.Min(oldEnd, newEnd); index++)
				if (!ReferenceEquals(previewNodes[index], nodes[index])) previewNodes[index] = nodes[index];
			for (var index = newEnd; index < oldEnd; index++) previewNodes.RemoveAt(newEnd);
			for (var index = oldEnd; index < newEnd; index++) previewNodes.Insert(index, nodes[index]);
			return shown.ToArray();
		}
		// Items open their folders in the preview, and so do the folders shown inside them.
		private static bool IsPreviewFolderParent(EditorRow row)
		{
			return row.ViewOnly ? row.CanExpand : CanStyleEditorFolders(row);
		}

		// Adds the inner folders of expanded items as view-only rows, loading them when the sidebar hasn't yet.
		private List<EditorRow> WithPreviewFolders(List<EditorRow> baseRows)
		{
			owner.ClearEditorFolderRows();
			var rows = new List<EditorRow>();
			void Add(EditorRow row)
			{
				rows.Add(row);
				if (!IsPreviewFolderParent(row))
					return;

				// Items start collapsed in the preview unless they are open in the sidebar.
				if (previewFolderParents.Add(row.Key) && PreviewExpansionMode(row) == 0 && !owner.IsEditorItemExpanded(row.Key))
					collapsed.Add(row.Key);
				if (collapsed.Contains(row.Key) || PreviewExpansionMode(row) == 2)
					return;

				// One request per expand: a load that returns without folders (already loading elsewhere,
				// unreadable, or hidden from the sidebar) must not trigger another refresh and request forever.
				if (owner.NeedsEditorFolders(row.Key) && requestedPreviewFolders.Add(row.Key))
					LoadPreviewFolders(row.Key);
				foreach (var child in owner.EditorFolderRows(row.Key, row.Depth + 1, row.ViewOnly ? row.Entry : null))
					Add(child);
			}

			foreach (var row in baseRows)
				Add(row);

			return rows;
		}

		private async void LoadPreviewFolders(string key)
		{
			if (!loadingPreviewFolders.Add(key))
				return;

			try
			{
				await owner.LoadEditorFoldersAsync(key);
				// Already loading elsewhere (for example the sidebar expanding it): wait for that load, up to 5 seconds, instead of asking again.
				for (var attempt = 0; attempt < 20 && !hidden && owner.NeedsEditorFolders(key); attempt++)
					await Task.Delay(250);
			}
			finally
			{
				loadingPreviewFolders.Remove(key);
			}

			if (!hidden)
				QueueRefresh(false);
		}

		// Expanding or collapsing a row changes only the preview, never the sidebar.
		private void TogglePreviewExpansion(string key)
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				if (hidden || refreshing) return;
				var row = renderedRows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
				if (row is null || !row.CanExpand || PreviewExpansionMode(row) != 0) return;
				var collapsing = collapsed.Add(key);
				if (!collapsing) collapsed.Remove(key);
				if (IsPreviewFolderParent(row))
				{
					// Collapsing drops the preview's own folder list, so expanding again lists the folders fresh from disk.
					if (collapsing) owner.ForgetEditorFolders(key);
					requestedPreviewFolders.Remove(key);
				}
				QueueRefresh(false);
			});
		}
		[DynamicWindowsRuntimeCast(typeof(Brush))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private void BuildInspector()
		{
			CommitPendingEdits();
			pendingTextCommits.Clear();
			itemReset = null;
			itemResetKey = null;
			var version = ++inspectorVersion;
			if (inspectorKey != selectedKey) appearanceExpanded = false;
			var offset = inspectorKey == selectedKey ? details.VerticalOffset : 0;
			inspectorKey = selectedKey;
			// A panel opened later starts on this item.
			owner.RememberEditorSelection(selectedKey);
			updateAppearanceApply = null;
			updateGroupIconPreview = null;
			inspector.Children.Clear();
			var row = owner.EditorRows().FirstOrDefault(candidate => candidate.Key == selectedKey);
			if (row is null) return;
			void Edit(Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update) => Change(() => owner.EditSidebarEntry(row.Key, update), false);
			var heading = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
			heading.Inlines.Add(new Run { Text = row.Name });
			if (row.Entry.SourceKey.Length > 0)
				heading.Inlines.Add(new Run
				{
					Text = " " + Strings.SidebarEditorDuplicateSuffix.GetLocalizedResource(),
					Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
					FontWeight = Microsoft.UI.Text.FontWeights.Normal
				});
			inspector.Children.Add(heading);
			if (!row.Available && !row.Removable && !row.Entry.Hidden)
				inspector.Children.Add(new TextBlock { Text = Strings.SidebarEditorUnavailable.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
			var actions = new Grid { ColumnSpacing = 8 };
			actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var visibility = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center
			};
			Check(visibility, Strings.SidebarEditorVisible, !owner.IsEditorEntryHidden(row.Key), value => Edit(entry => entry with { Hidden = !value }));
			actions.Children.Add(visibility);
			var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
			foreach (var direction in new[] { -1, 1 })
			{
				var siblings = owner.EditorRows().Where(candidate => candidate.Parent == row.Parent && !owner.IsEditorSettingsRow(candidate.Key)).ToList();
				var index = siblings.FindIndex(candidate => candidate.Key == row.Key) + direction;
				var button = IconButton(direction < 0 ? "\uE74A" : "\uE74B", direction < 0 ? Strings.SidebarVisualMoveUp : Strings.SidebarVisualMoveDown,
					() => Change(() => owner.MoveEditorRow(row.Key, siblings[index].Key, false, direction > 0)));
				button.IsEnabled = index >= 0 && index < siblings.Count && owner.CanMoveEditorRow(row.Key, siblings[index].Key, false);
				moves.Children.Add(button);
			}
			VerticalSeparator(moves);
			// Separators and gaps have nothing to reset; they only offer Remove.
			if (row.Entry.SourceKey.Length == 0 && !row.Decoration)
			{
				itemReset = IconButton("\uE777", Strings.CustomizationSidebarResetAppearance, () => Change(() => owner.ResetEditorEntry(row.Key)));
				itemResetKey = row.Key;
				itemReset.IsEnabled = !owner.IsEditorEntryDefault(row.Key);
				moves.Children.Add(itemReset);
			}
			if (row.Removable)
			{
				var remove = IconButton("\uE711", Strings.SidebarVisualRemove, () => Change(() => owner.ResetEditorEntry(row.Key, true)));
				CenterIconButton(remove, 14);
				moves.Children.Add(remove);
			}
			SetColumn(moves, 1); actions.Children.Add(moves); inspector.Children.Add(actions);
			if (!row.Decoration)
			{
				Text(inspector, Strings.SidebarVisualName, row.Name, value => Edit(entry => entry with { Label = value.Trim() }), enabled: row.CanEditName);
				if (row.Key.StartsWith("sidebar-shortcut:", StringComparison.Ordinal))
					Text(inspector, Strings.SidebarVisualTarget, row.Entry.Target, value =>
					{
						if (!Path.IsPathFullyQualified(value.Trim()) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return;
						Edit(entry => entry with { Target = value.Trim() });
					}, enabled: row.Entry.SourceKey.Length == 0);
				var size = row.Entry.Size == 3 || row.Entry.TextPt > 0 || row.Entry.IconPt > 0 ? 3 : row.Entry.Size is 1 or 2 ? row.Entry.Size : 0;
				Combo(inspector, Strings.CustomizationSidebarEntrySize,
					[Strings.SidebarEditorDefault, Strings.SidebarEditorMedium, Strings.SidebarEditorLarge, Strings.SidebarEditorCustom], size, value =>
				{
					var current = owner.EditorRows().First(candidate => candidate.Key == row.Key).Entry;
					var points = DefaultPoints(row, current.Size);
					Change(() => owner.EditSidebarEntry(row.Key, entry => entry with { Size = value == 0 ? -1 : value,
						TextPt = value == 3 ? entry.TextPt > 0 ? entry.TextPt : points.Text : 0,
						IconPt = value == 3 ? entry.IconPt > 0 ? entry.IconPt : points.Icon : 0 }));
				});
				if (size == 3)
				{
					var points = DefaultPoints(row);
					var custom = new StackPanel { Spacing = 8 };
					// Legacy headings show no icon, so only their text size is offered.
					Sizes(custom, row.Entry.TextPt > 0 ? row.Entry.TextPt : points.Text, row.Entry.IconPt > 0 ? row.Entry.IconPt : points.Icon, row.Entry.SizesLinked, LegacyHeader(row),
						(field, text, icon) => Edit(entry => field == 0
							? entry with { TextPt = text, IconPt = entry.SizesLinked ? icon : entry.IconPt }
							: entry with { IconPt = icon, TextPt = entry.SizesLinked ? text : entry.TextPt }),
						value => Edit(entry => entry with { SizesLinked = value }));
					// Shown directly under Size, which already says Custom.
					inspector.Children.Add(custom);
				}
				// A group's icon comes right after its size.
				if (row.Key.StartsWith("sidebar-group:", StringComparison.Ordinal)) BuildGroupIconPicker(row);
				var hideIcon = Check(inspector, Strings.CustomizationSidebarHideIcon, LegacyHeader(row) || row.Entry.HideIcon,
					value => Edit(entry => entry with { HideIcon = value }));
				hideIcon.IsEnabled = !LegacyHeader(row);
				if (owner.EditorOptions.SidebarAppearance == 1 && row.CanHidePin)
					Check(inspector, Strings.CustomizationSidebarHidePin, row.Entry.HidePin, value => Edit(entry => entry with { HidePin = value }));
				if (row.CanExpand)
				{
					Combo(inspector, Strings.SidebarEditorExpansion, [Strings.SidebarEditorDefault, Strings.CustomizationSidebarAlwaysExpanded, Strings.CustomizationSidebarAlwaysCollapsed],
						PreviewExpansionMode(row),
						value => Edit(entry => owner.EditorOptions.SidebarAppearance == 1
							? entry with { LegacyExpansion = value } : entry with { HideArrow = value == 2, ExpansionLock = value }));
				}
				Separator(inspector);
				var appearanceBody = new StackPanel { Spacing = 12, Padding = new Thickness(16, 12, 16, 12) };
				var headingText = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 12) };
				headingText.Children.Add(new TextBlock
				{
					Text = Strings.SidebarEditorApplyAppearance.GetLocalizedResource(),
					Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
					TextWrapping = TextWrapping.Wrap
				});
				headingText.Children.Add(new TextBlock
				{
					Text = Strings.SidebarEditorUnifiedDescription.GetLocalizedResource(),
					Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
					Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
					TextWrapping = TextWrapping.Wrap
				});
				var choices = new RadioButtons
				{
					Header = Strings.SidebarEditorApplyTo.GetLocalizedResource(),
					MaxColumns = 1, HorizontalAlignment = HorizontalAlignment.Stretch
				};
				foreach (var resource in new[] { Strings.CustomizationSidebarScopeGroup, Strings.CustomizationSidebarScopeAll })
					choices.Items.Add(new RadioButton
					{
						Content = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
						HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
					});
				choices.SelectedIndex = appearanceScope - 2;
				AutomationProperties.SetName(choices, Strings.SidebarEditorApplyTo.GetLocalizedResource());
				var apply = new Button
				{
					Content = Strings.SidebarEditorApply.GetLocalizedResource(),
					Style = (Style)Application.Current.Resources["AccentButtonStyle"],
					MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right
				};
				var undoAppearance = new Button
				{
					Content = Strings.SidebarEditorUndoAppearance.GetLocalizedResource(),
					IsEnabled = false, MinWidth = 96
				};
				updateAppearanceApply = () =>
				{
					apply.IsEnabled = owner.CanApplyEditorAppearance(row.Key, appearanceScope);
					// Always beside Apply; available once an Apply here can be undone.
					undoAppearance.IsEnabled = CanUndoAppliedAppearance(row.Key);
				};
				choices.SelectionChanged += (_, _) =>
				{
					if (choices.SelectedIndex < 0) return;
					appearanceScope = choices.SelectedIndex + 2;
					updateAppearanceApply?.Invoke();
				};
				apply.Click += (_, _) => ApplyAppearance(row.Key);
				undoAppearance.Click += (_, _) => UndoAppliedAppearance(row.Key);
				appearanceBody.Children.Add(choices);
				var appearanceLayout = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
				appearanceLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
				appearanceLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
				appearanceLayout.Children.Add(appearanceBody);
				var footerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
				footerActions.Children.Add(undoAppearance); footerActions.Children.Add(apply);
				var footer = new Border
				{
					BorderThickness = new Thickness(0, 1, 0, 0),
					BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
					Padding = new Thickness(16), Child = footerActions
				};
				SetRow(footer, 1); appearanceLayout.Children.Add(footer);
				var appearance = new Expander
				{
					Header = headingText, Content = appearanceLayout, IsExpanded = appearanceExpanded, Padding = new Thickness(0),
					HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
				};
				appearance.Expanding += (_, _) => appearanceExpanded = true;
				appearance.Collapsed += (_, _) => appearanceExpanded = false;
				inspector.Children.Add(appearance);
				updateAppearanceApply.Invoke();
				if (CanStyleEditorFolders(row))
					BuildFolderAppearance(row);
			}
			else
			{
				Number(inspector, Strings.CustomizationSidebarGapSize, row.Entry.Gap, 4, 64, value => Edit(entry => entry with { Gap = (int)value }));
			}
			var key = selectedKey;
			DispatcherQueue.TryEnqueue(() => { if (!hidden && key == inspectorKey && version == inspectorVersion) details.ChangeView(null, offset, null, true); });
		}

		private bool CanUndoAppliedAppearance(string key) => appearanceUndo is { } applied && applied.Key == key &&
			applied.After == owner.CaptureEditor() && history.CanUndo(applied.After);

		private void ApplyAppearance(string key)
		{
			CommitPendingEdits();
			var before = owner.CaptureEditor();
			Change(() => owner.ApplyEditorAppearance(key, appearanceScope), false);
			var after = owner.CaptureEditor();
			if (before != after) appearanceUndo = (key, after);
			updateAppearanceApply?.Invoke();
		}

		private void UndoAppliedAppearance(string key)
		{
			CommitPendingEdits();
			if (!CanUndoAppliedAppearance(key)) return;
			appearanceUndo = null;
			Restore(false);
			updateAppearanceApply?.Invoke();
		}

		private void DuplicateSelectedItem()
		{
			CommitPendingEdits();
			Change(() => selectedKey = owner.DuplicateEditorEntry(selectedKey));
		}

		[DynamicWindowsRuntimeCast(typeof(ComboBoxItem))]
		private void BuildGroupIconPicker(EditorRow row)
		{
			var layout = new Grid { ColumnSpacing = 8 };
			layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var icons = new ComboBox { Header = Strings.Icon.GetLocalizedResource(), HorizontalAlignment = HorizontalAlignment.Stretch };
			icons.Items.Add(new ComboBoxItem { Content = Strings.SidebarEditorDefault.GetLocalizedResource(), Tag = "" });
			var selected = 0;
			foreach (var option in EditorGroupIcons)
			{
				var source = "app:" + option.Section;
				var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
				content.Children.Add(new ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(SidebarSectionIcons.For(option.Section)!)), Width = 20, Height = 20 });
				content.Children.Add(new TextBlock { Text = option.Label.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center });
				icons.Items.Add(new ComboBoxItem { Content = content, Tag = source });
				if (row.Entry.IconSource == source) selected = icons.Items.Count - 1;
			}
			// Every imported icon is offered to every group, each with a preview and its file name.
			var imported = owner.EditorImportedIcons().ToList();
			if (row.Entry.IconSource.StartsWith("file:", StringComparison.Ordinal) && !imported.Contains(row.Entry.IconSource, StringComparer.OrdinalIgnoreCase))
				imported.Add(row.Entry.IconSource);
			// Stored icons get an X, shown only while the list is open, that deletes them from the app's data folder.
			var deleteButtons = new List<Button>();
			icons.DropDownOpened += (_, _) => { foreach (var button in deleteButtons) button.Visibility = Visibility.Visible; };
			icons.DropDownClosed += (_, _) => { foreach (var button in deleteButtons) button.Visibility = Visibility.Collapsed; };
			foreach (var source in imported)
			{
				var path = source[5..];
				var content = new Grid { ColumnSpacing = 8 };
				content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
				content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
				var thumbnail = new Image { Width = 20, Height = 20, Stretch = Stretch.Uniform };
				var name = new TextBlock
				{
					Text = Path.GetFileName(path), TextTrimming = TextTrimming.CharacterEllipsis,
					VerticalAlignment = VerticalAlignment.Center
				};
				SetColumn(name, 1); content.Children.Add(thumbnail); content.Children.Add(name);
				if (IsStoredEditorIcon(source))
				{
					// A quiet X: the thin title-bar glyph in secondary text colour, exactly centred in a 24 px square,
					// with no background until hovered, like the close buttons on tabs.
					var delete = new Button
					{
						Content = new FontIcon
						{
							Glyph = "",
							// No fixed box around the glyph, so it sits in the exact centre of the button.
							FontSize = 10,
							HorizontalAlignment = HorizontalAlignment.Center,
							VerticalAlignment = VerticalAlignment.Center
						},
						Width = 24,
						Height = 24,
						Padding = new Thickness(0),
						Margin = new Thickness(0, 0, -4, 0),
						CornerRadius = new CornerRadius(4),
						BorderThickness = new Thickness(0),
						HorizontalContentAlignment = HorizontalAlignment.Center,
						VerticalContentAlignment = VerticalAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center,
						Visibility = Visibility.Collapsed
					};
					var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
					delete.Resources["ButtonBackground"] = transparent;
					delete.Resources["ButtonBackgroundPointerOver"] = Application.Current.Resources["SubtleFillColorSecondaryBrush"];
					delete.Resources["ButtonBackgroundPressed"] = Application.Current.Resources["SubtleFillColorTertiaryBrush"];
					delete.Resources["ButtonBorderBrush"] = transparent;
					delete.Resources["ButtonBorderBrushPointerOver"] = transparent;
					delete.Resources["ButtonBorderBrushPressed"] = transparent;
					delete.Resources["ButtonForeground"] = Application.Current.Resources["TextFillColorSecondaryBrush"];
					delete.Resources["ButtonForegroundPointerOver"] = Application.Current.Resources["TextFillColorPrimaryBrush"];
					delete.Resources["ButtonForegroundPressed"] = Application.Current.Resources["TextFillColorSecondaryBrush"];
					var deleteLabel = Strings.SidebarEditorDeleteIcon.GetLocalizedResource();
					AutomationProperties.SetName(delete, deleteLabel);
					ToolTipService.SetToolTip(delete, deleteLabel);
					delete.Click += (_, _) =>
					{
						icons.IsDropDownOpen = false;
						Change(() => owner.DeleteEditorIcon(source));
					};
					deleteButtons.Add(delete);
					SetColumn(delete, 2); content.Children.Add(delete);
				}
				var custom = new ComboBoxItem
				{
					Content = content, Tag = source, HorizontalContentAlignment = HorizontalAlignment.Stretch
				};
				AutomationProperties.SetName(custom, name.Text);
				ToolTipService.SetToolTip(custom, path);
				icons.Items.Add(custom);
				if (string.Equals(source, row.Entry.IconSource, StringComparison.OrdinalIgnoreCase))
				{
					// The group's own icon follows the loaded sidebar icon.
					updateGroupIconPreview = () => thumbnail.Source = (owner.EditorIcon(row.Key) as ImageIcon)?.Source;
					updateGroupIconPreview.Invoke();
					selected = icons.Items.Count - 1;
				}
				else
					LoadIconPreview(thumbnail, path);
			}
			icons.SelectedIndex = selected;
			icons.SelectionChanged += (_, _) =>
			{
				if (icons.SelectedItem is ComboBoxItem { Tag: string source } && source != row.Entry.IconSource)
					Change(() => owner.EditSidebarEntry(row.Key, entry => entry with { IconSource = source }));
			};
			var browse = new Button { Content = Strings.Browse.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Bottom };
			var error = new TextBlock { Text = Strings.SidebarEditorIconError.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
			browse.Click += async (_, _) =>
			{
				var version = inspectorVersion;
				var handle = HostHandle;
				if (handle == 0) return;
				var service = Ioc.Default.GetRequiredService<ICommonDialogService>();
				if (!service.Open_FileOpenDialog(handle, false, [Strings.AllSupportedFiles.GetLocalizedResource(), "*.ico;*.png;*.bmp;*.jpg;*.jpeg;*.jfif;*.gif"], Environment.SpecialFolder.MyPictures, out var path)) return;
				browse.IsEnabled = false;
				var bitmap = await LoadEditorIconAsync(path);
				if (hidden || selectedKey != row.Key || version != inspectorVersion) return;
				browse.IsEnabled = true;
				error.Visibility = bitmap is null ? Visibility.Visible : Visibility.Collapsed;
				if (bitmap is null) return;
				// The group uses a copy kept in the app's data folder; the original file can then be moved or deleted.
				var stored = await StoreEditorIconAsync(path) ?? path;
				if (hidden || selectedKey != row.Key || version != inspectorVersion) return;
				Change(() => owner.EditSidebarEntry(row.Key, entry => entry with { IconSource = "file:" + stored }));
			};
			// Legacy group headings never show an icon, so choosing one there has no effect.
			if (LegacyHeader(row))
			{
				icons.IsEnabled = false;
				browse.IsEnabled = false;
			}
			SetColumn(browse, 1); layout.Children.Add(icons); layout.Children.Add(browse);
			inspector.Children.Add(layout); inspector.Children.Add(error);
		}
		private async void LoadIconPreview(Image thumbnail, string path)
		{
			var version = inspectorVersion;
			var bitmap = await LoadEditorIconAsync(path);
			if (!hidden && version == inspectorVersion) thumbnail.Source = bitmap;
		}

		// Optional appearance for the folders shown inside an expandable item; the item itself is not affected.
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private void BuildFolderAppearance(EditorRow row)
		{
			var folderKey = EditorFolderKey(row.Key);
			var separate = owner.HasEditorFolderAppearance(row.Key);
			var entry = owner.EditorFolderEntry(row.Key);
			void Edit(Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update) => Change(() => owner.EditSidebarEntry(folderKey, update), false);
			(double Text, double Icon) Points(int size) => SidebarCustomizationStyle.DefaultPoints(size, false, owner.EditorOptions);

			var toggle = new ToggleSwitch
			{
				IsOn = separate
			};
			AutomationProperties.SetName(toggle, Strings.SidebarEditorFolderAppearance.GetLocalizedResource());
			toggle.Toggled += (_, _) =>
			{
				if (toggle.IsOn != owner.HasEditorFolderAppearance(row.Key))
					Change(() => owner.SetEditorFolderAppearance(row.Key, toggle.IsOn));
			};
			inspector.Children.Add(new SettingsCard
			{
				Header = new TextBlock
				{
					Text = Strings.SidebarEditorFolderAppearance.GetLocalizedResource(),
					Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
					TextWrapping = TextWrapping.Wrap
				},
				Description = Strings.SidebarEditorFolderAppearanceDescription.GetLocalizedResource(),
				Content = toggle,
				HorizontalAlignment = HorizontalAlignment.Stretch
			});
			if (!separate)
				return;

			var size = entry.Size == 3 || entry.TextPt > 0 || entry.IconPt > 0 ? 3 : entry.Size is 1 or 2 ? entry.Size : 0;
			// Size and Hide icons share one row; the checkbox lines up with the size box rather than its header.
			var sizeRow = new Grid
			{
				ColumnSpacing = 16
			};
			sizeRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			sizeRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var sizeHost = new StackPanel();
			var iconHost = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Bottom
			};
			SetColumn(iconHost, 1);
			sizeRow.Children.Add(sizeHost);
			sizeRow.Children.Add(iconHost);
			inspector.Children.Add(sizeRow);
			Check(iconHost, Strings.SidebarEditorFolderHideIcons, entry.HideIcon, value => Edit(current => current with { HideIcon = value }));
			Combo(sizeHost, Strings.CustomizationSidebarEntrySize,
				[Strings.SidebarEditorDefault, Strings.SidebarEditorMedium, Strings.SidebarEditorLarge, Strings.SidebarEditorCustom], size, value =>
			{
				var points = Points(owner.EditorFolderEntry(row.Key).Size);
				Change(() => owner.EditSidebarEntry(folderKey, current => current with
				{
					Size = value == 0 ? -1 : value,
					TextPt = value == 3 ? current.TextPt > 0 ? current.TextPt : points.Text : 0,
					IconPt = value == 3 ? current.IconPt > 0 ? current.IconPt : points.Icon : 0
				}));
			});
			if (size == 3)
			{
				var points = Points(entry.Size);
				var custom = new StackPanel
				{
					Spacing = 8
				};
				Sizes(custom, entry.TextPt > 0 ? entry.TextPt : points.Text, entry.IconPt > 0 ? entry.IconPt : points.Icon, entry.SizesLinked, false,
					(field, text, icon) => Edit(current => field == 0
						? current with { TextPt = text, IconPt = current.SizesLinked ? icon : current.IconPt }
						: current with { IconPt = icon, TextPt = current.SizesLinked ? text : current.TextPt }),
					value => Edit(current => current with { SizesLinked = value }));
				inspector.Children.Add(custom);
			}
		}

		private void BuildDefaults()
		{
			defaults.Children.Clear(); var options = owner.EditorOptions;
			void Save(Func<CustomizationPreferences, CustomizationPreferences> update) => Change(() => owner.SaveEditorOptions(update(owner.EditorOptions)), false);
			Separator(defaults);
			var dragEnabled = Check(defaults, Strings.CustomizationSidebarDragEnabled, options.SidebarDragEnabled,
				value => Save(current => current with { SidebarDragEnabled = value }));
			dragEnabled.Content = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new TextBlock { Text = Strings.CustomizationSidebarDragEnabled.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = Strings.CustomizationSidebarDragEnabledNotes.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 }
				}
			};
			AutomationProperties.SetName(dragEnabled, Strings.CustomizationSidebarDragEnabled.GetLocalizedResource());
			AutomationProperties.SetHelpText(dragEnabled, Strings.CustomizationSidebarDragEnabledNotes.GetLocalizedResource());
			var sharpIcons = Check(defaults, Strings.CustomizationSidebarSharpIcons, options.SharpLargeIcons,
				value => Save(current => current with { SharpLargeIcons = value }));
			sharpIcons.Content = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new TextBlock { Text = Strings.CustomizationSidebarSharpIcons.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = Strings.CustomizationSidebarSharpIconsNotes.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 }
				}
			};
			AutomationProperties.SetName(sharpIcons, Strings.CustomizationSidebarSharpIcons.GetLocalizedResource());
			AutomationProperties.SetHelpText(sharpIcons, Strings.CustomizationSidebarSharpIconsNotes.GetLocalizedResource());
			var keepText = Check(defaults, Strings.CustomizationSidebarKeepTextPosition, options.SidebarKeepTextPosition,
				value => Save(current => current with { SidebarKeepTextPosition = value }));
			keepText.Content = new StackPanel
			{
				Spacing = 2,
				Children =
				{
					new TextBlock { Text = Strings.CustomizationSidebarKeepTextPosition.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
					new TextBlock { Text = Strings.CustomizationSidebarKeepTextPositionNotes.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 }
				}
			};
			AutomationProperties.SetName(keepText, Strings.CustomizationSidebarKeepTextPosition.GetLocalizedResource());
			AutomationProperties.SetHelpText(keepText, Strings.CustomizationSidebarKeepTextPositionNotes.GetLocalizedResource());
			Separator(defaults);
			defaults.Children.Add(new TextBlock
			{
				Text = Strings.SidebarEditorCompactMode.GetLocalizedResource(),
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
			});
			Check(defaults, Strings.CustomizationSidebarCompactShowIcons, options.SidebarCompactShowIcons,
				value => Save(current => current with { SidebarCompactShowIcons = value }));
			Check(defaults, Strings.CustomizationSidebarCompactGroupContents, options.SidebarCompactGroupContents,
				value => Save(current => current with { SidebarCompactGroupContents = value }));
			Check(defaults, Strings.SidebarEditorCompactHideGroupIcons, options.SidebarCompactHideGroupIcons,
				value => Save(current => current with { SidebarCompactHideGroupIcons = value }));
			var sizing = new Grid { ColumnSpacing = 8 };
			sizing.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			sizing.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var sizeChoice = new ComboBox { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Stretch };
			foreach (var resource in new[] { Strings.CustomizationSidebarCompactOriginalSize, Strings.CustomizationSidebarCompactLargestSize, Strings.SidebarEditorCustom })
				sizeChoice.Items.Add(resource.GetLocalizedResource());
			sizeChoice.SelectedIndex = options.SidebarCompactIconMode;
			AutomationProperties.SetName(sizeChoice, Strings.CustomizationSidebarCompactIconSize.GetLocalizedResource());
			var customSize = new NumberBox
			{
				Value = options.SidebarCompactIconSize,
				Minimum = 8,
				Maximum = 32,
				Width = 100,
				SmallChange = 1,
				SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
			};
			AutomationProperties.SetName(customSize, Strings.SidebarEditorCompactCustomSize.GetLocalizedResource());
			ToolTipService.SetToolTip(customSize, Strings.SidebarEditorCompactCustomSize.GetLocalizedResource());
			var custom = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Visibility = options.SidebarCompactIconMode == 2 ? Visibility.Visible : Visibility.Collapsed
			};
			custom.Children.Add(customSize);
			custom.Children.Add(new TextBlock { Text = Strings.SidebarEditorPixels.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center });
			SetColumn(custom, 1);
			sizing.Children.Add(sizeChoice); sizing.Children.Add(custom);
			sizeChoice.SelectionChanged += (_, _) =>
			{
				if (sizeChoice.SelectedIndex < 0) return;
				custom.Visibility = sizeChoice.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
				Save(current => current with { SidebarCompactIconMode = sizeChoice.SelectedIndex });
			};
			var updatingCustomSize = false;
			customSize.ValueChanged += (_, _) =>
			{
				if (updatingCustomSize || !double.IsFinite(customSize.Value)) return;
				var pixels = (int)Math.Clamp(Math.Round(customSize.Value), 8, 32);
				updatingCustomSize = true;
				try { customSize.Value = pixels; }
				finally { updatingCustomSize = false; }
				Save(current => current with { SidebarCompactIconSize = pixels });
			};
			defaults.Children.Add(SettingRow(Strings.CustomizationSidebarCompactIconSize, sizing));
		}
		// A label on the left with its control aligned right, used by every choice in the settings flyout.
		private static Grid SettingRow(string resource, FrameworkElement control)
		{
			var row = new Grid { ColumnSpacing = 16 };
			row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			row.Children.Add(new TextBlock
			{
				Text = resource.GetLocalizedResource(),
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center
			});
			control.VerticalAlignment = VerticalAlignment.Center;
			SetColumn(control, 1); row.Children.Add(control);
			return row;
		}
		private int PreviewExpansionMode(EditorRow row) => SidebarCustomizationStyle.ExpansionMode(row.Entry, row.Group, owner.EditorOptions);
		private bool LegacyHeader(EditorRow row) => owner.EditorOptions.SidebarAppearance == 1 && SidebarCustomizationStyle.IsLegacyHeader(row.Group, row.Key);
		private (double Text, double Icon) DefaultPoints(EditorRow row, int? size = null) => SidebarCustomizationStyle.DefaultPoints(size ?? row.Entry.Size,
			row.Key.StartsWith("item:Drives:", StringComparison.Ordinal) || row.Entry.SourceKey.StartsWith("item:Drives:", StringComparison.Ordinal),
			owner.EditorOptions, SidebarCustomizationStyle.IsLegacyHeader(row.Group, row.Key));
		[DynamicWindowsRuntimeCast(typeof(Brush))]
		private static void VerticalSeparator(Panel host) => host.Children.Add(new Border
		{
			Width = 1, Height = 20, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
			Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], IsHitTestVisible = false
		});
		[DynamicWindowsRuntimeCast(typeof(Brush))]
		private static void Separator(StackPanel host) => host.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4),
			Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });

		// textOnly: for rows that show no icon (Legacy headings), only the text size is shown; a linked icon size still follows it.
		private static void Sizes(StackPanel host, double textValue, double iconValue, bool linked, bool textOnly,
			Action<int, double, double> save, Action<bool> saveLink)
		{
			var grid = new Grid { ColumnSpacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
			var text = new NumberBox
			{
				Header = Strings.SidebarEditorTextPoints.GetLocalizedResource(),
				Value = textValue,
				Minimum = 6,
				Maximum = 36,
				SmallChange = 0.5
			};
			var icon = new NumberBox
			{
				Header = Strings.SidebarEditorIconPoints.GetLocalizedResource(),
				Value = iconValue,
				Minimum = 6,
				Maximum = 48,
				SmallChange = 0.5
			};
			var boxes = new[] { text, icon };
			foreach (var box in boxes)
			{
				grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				SetColumn(box, grid.Children.Count);
				grid.Children.Add(box);
			}
			grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var link = new ToggleButton
			{
				IsChecked = linked,
				Content = new FontIcon { Glyph = "\uE71B", FontSize = 14 },
				VerticalAlignment = VerticalAlignment.Bottom,
				Padding = new Thickness(8),
				MinWidth = 32
			};
			var description = Strings.SidebarEditorLinkSizes.GetLocalizedResource();
			AutomationProperties.SetName(link, description);
			ToolTipService.SetToolTip(link, description);
			SetColumn(link, boxes.Length);
			grid.Children.Add(link);
			link.Click += (_, _) => { linked = link.IsChecked == true; saveLink(linked); };
			var updating = false;
			void Changed(int field)
			{
				if (updating || !double.IsFinite(text.Value) || !double.IsFinite(icon.Value)) return;
				updating = true;
				try
				{
					if (linked && field == 0) icon.Value = Math.Clamp(text.Value * iconValue / textValue, 6, 48);
					if (linked && field == 1) text.Value = Math.Clamp(icon.Value * textValue / iconValue, 6, 36);
					textValue = text.Value; iconValue = icon.Value;
					save(field, text.Value, icon.Value);
				}
				finally { updating = false; }
			}
			text.ValueChanged += (_, _) => Changed(0);
			icon.ValueChanged += (_, _) => Changed(1);
			if (textOnly)
			{
				icon.Visibility = link.Visibility = Visibility.Collapsed;
				grid.ColumnDefinitions[1].Width = new GridLength(0);
				grid.ColumnSpacing = 0;
			}
			host.Children.Add(grid);
		}

		// A square 36 px button with its glyph at an even size and no padding, so the glyph sits exactly in the middle on whole pixels.
		[DynamicWindowsRuntimeCast(typeof(FontIcon))]
		private static void CenterIconButton(Button button, double glyphSize)
		{
			if (button.Content is FontIcon icon)
			{
				icon.FontSize = glyphSize;
				icon.Width = icon.Height = glyphSize;
				icon.HorizontalAlignment = HorizontalAlignment.Center;
				icon.VerticalAlignment = VerticalAlignment.Center;
			}
			button.Width = button.Height = 36;
			button.Padding = new Thickness(0);
			button.HorizontalContentAlignment = HorizontalAlignment.Center;
			button.VerticalContentAlignment = VerticalAlignment.Center;
		}
		private static Button IconButton(string glyph, string resource, Action action)
		{
			var button = new Button
			{
				Content = new FontIcon { Glyph = glyph, FontSize = 14 },
				MinWidth = 36,
				MinHeight = 36,
				Padding = new Thickness(8),
				VerticalAlignment = VerticalAlignment.Center
			};
			var label = resource.GetLocalizedResource(); AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label);
			button.Click += (_, _) => action(); return button;
		}
		private static CheckBox Check(StackPanel host, string resource, bool value, Action<bool> save)
		{
			var control = new CheckBox
			{
				Content = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				// Text centred on the box; the standard checkbox sets it 5 px down, which leaves single-line text sitting high.
				Padding = new Thickness(8, 0, 0, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				IsChecked = value
			};
			control.Checked += (_, _) => save(true); control.Unchecked += (_, _) => save(false); host.Children.Add(control);
			return control;
		}
		private void Text(StackPanel host, string resource, string value, Action<string> save, bool enabled = true)
		{
			var control = new TextBox
			{
				Header = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
				Text = value,
				PlaceholderText = resource == Strings.SidebarVisualTarget ? Strings.SidebarVisualTargetPlaceholder.GetLocalizedResource() : "",
				MaxLength = resource == Strings.SidebarVisualTarget ? 32768 : 200,
				IsEnabled = enabled
			};
			void Commit() { if (!control.IsEnabled || control.Text == value) return; value = control.Text; save(value); }
			pendingTextCommits.Add(Commit);
			SettingsValueEditing.Register(control, Commit);
			control.LostFocus += (_, _) => Commit();
			control.KeyDown += (_, args) =>
			{
				if (args.Key == VirtualKey.Enter)
				{
					Commit();
					args.Handled = true;
				}
			};
			host.Children.Add(control);
		}
		private static void Number(StackPanel host, string resource, double value, double minimum, double maximum, Action<double> save)
		{
			var control = new NumberBox
			{
				Header = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
				Value = value,
				Minimum = minimum,
				Maximum = maximum,
				SmallChange = 0.5,
				SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
			};
			control.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue)) save(Math.Clamp(args.NewValue, minimum, maximum)); }; host.Children.Add(control);
		}
		private static void Combo(StackPanel host, string resource, string[] labels, int selected, Action<int> save)
		{
			var control = new ComboBox
			{
				Header = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			foreach (var label in labels) control.Items.Add(label.GetLocalizedResource()); control.SelectedIndex = selected;
			control.SelectionChanged += (_, _) => { if (control.SelectedIndex >= 0) save(control.SelectedIndex); }; host.Children.Add(control);
		}
	}
}
