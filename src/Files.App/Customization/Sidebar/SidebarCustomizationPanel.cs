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
		private CustomizationChangeHistory<EditorSnapshot> history => owner.EditorHistory;
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
		// As wide as the Spacing steppers and the compact Icon size box below it, so the right-hand controls line up.
		private readonly ComboBox appearanceChoice = new()
		{
			Width = 132,
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
		// A tag renamed in the Tag name box, until the sidebar has saved it under its new name.
		private string? renamingTagKey;
		// A size being typed or stepped: applied at once, saved as one Undo step when it is confirmed (Enter or clicking away).
		private EditorSnapshot? liveBefore;
		private EditorSnapshot? localChangeSnapshot;
		private int appearanceScope = 3;
		private bool appearanceExpanded;
		private bool foldersExpanded = true;
		private Action? updateAppearanceApply;
		private Action? updateGroupIconPreview;

		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		internal SidebarCustomizationPanel(SidebarViewModel owner)
		{
			this.owner = owner;
			// The Segmented control's style isn't loaded app-wide; the status bar loads it the same way.
			Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///CommunityToolkit.WinUI.Controls.Segmented/Segmented/Segmented.xaml") });
			Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
			SettingsValueEditing.Attach(this);
			Margin = new Thickness(0);
			RowDefinitions.Add(new() { Height = GridLength.Auto });
			RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
			RowDefinitions.Add(new() { Height = GridLength.Auto });
			// No background of its own: Settings already sits on the app's file area, so the panel shows the same background as the rest
			// of its Settings section.
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
			Raised(appearanceChoice);
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
				MaxHeight = 2000,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			};
			var settingsFlyout = new Flyout
			{
				Content = settingsScroll,
				Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
				// The default acrylic background, with the rounded corners of the app's other flyouts.
				FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
				{
					Setters =
					{
						new Setter(Control.PaddingProperty, new Thickness(0)),
						new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity),
						new Setter(Control.CornerRadiusProperty, new CornerRadius(8))
					}
				}
			};
			settingsFlyout.Opening += (_, _) =>
			{
				CommitPendingEdits();
				settingsScroll.Width = Math.Min(400, Math.Max(200, ActualWidth - 48));
				settingsScroll.MaxHeight = Math.Max(160, ActualHeight - dragArea.ActualHeight - 48);
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
			owner.EditorEntriesRenamed += OwnerEntriesRenamed;
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
			owner.EditorEntriesRenamed -= OwnerEntriesRenamed;
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
		// The sidebar saved items under new keys by itself (the history already follows them): so do the selection and what this
		// panel last drew, so that isn't taken for an outside change. The details are rebuilt only when the selected item moved.
		private void OwnerEntriesRenamed(IReadOnlyList<(string From, string To)> renames)
		{
			string Renamed(string key) => renames.Aggregate(key, (current, rename) => string.Equals(current, rename.From, StringComparison.OrdinalIgnoreCase) ? rename.To : current);
			EditorSnapshot Map(EditorSnapshot snapshot) => snapshot with { Sidebar = SidebarViewModel.RenameEditorEntries(snapshot.Sidebar, renames) };
			if (lastRendered is { } rendered) lastRendered = Map(rendered);
			if (localChangeSnapshot is { } local) localChangeSnapshot = Map(local);
			if (liveBefore is { } live) liveBefore = Map(live);
			var moved = selectedKey is not null && Renamed(selectedKey) != selectedKey;
			if (moved)
			{
				selectedKey = Renamed(selectedKey!);
				if (inspectorKey is not null) inspectorKey = Renamed(inspectorKey);
				renamingTagKey = null;
			}
			QueueRefresh(moved);
		}
		private void OwnerIconsChanged(object? sender, EventArgs args)
		{
			updateGroupIconPreview?.Invoke();
			QueueRefresh(false);
		}
		private void Change(Action action, bool rebuildInspector = true)
		{
			CommitLive();
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
			renamingTagKey = null;
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
			CommitLive();
		}
		// A live edit: shown at once (preview and sidebar), not yet a step in the history.
		private void ChangeLive(Action action)
		{
			liveBefore ??= owner.CaptureEditor();
			changeDepth++;
			try { action(); }
			finally { changeDepth--; }
			localChangeSnapshot = owner.CaptureEditor();
			QueueRefresh(false);
		}
		// The live edit becomes one Undo step, back to how things were before it started.
		private void CommitLive()
		{
			if (liveBefore is not { } before) return;
			liveBefore = null;
			history.Record(before, owner.CaptureEditor());
			QueueRefresh(false);
		}
		// Escape: the live edit is dropped, and everything is back to how it was before it started. No Undo step.
		private void CancelLive()
		{
			if (liveBefore is not { } before) return;
			liveBefore = null;
			changeDepth++;
			try { owner.RestoreEditor(before); }
			finally { changeDepth--; }
			localChangeSnapshot = owner.CaptureEditor();
			QueueRefresh(true);
		}
		private void Restore(bool forward)
		{
			CommitPendingEdits();
			var current = owner.CaptureEditor();
			var detailsBefore = owner.EditorDetailsState(selectedKey);
			if (!(forward ? history.Redo(current, out var restored) : history.Undo(current, out restored))) return;
			// The panel's own change, so the sidebar refreshing after it isn't taken for a change made elsewhere.
			changeDepth++;
			try { owner.RestoreEditor(restored); }
			finally { changeDepth--; }
			localChangeSnapshot = owner.CaptureEditor();
			// The details are rebuilt only when what they show changed, so an open Unify Appearance otherwise stays as it is. Options
			// aren't part of Undo, so they never need rebuilding here.
			QueueRefresh(restored.Sections != current.Sections || owner.EditorDetailsState(selectedKey) != detailsBefore);
		}
		private void Refresh(bool rebuildInspector = true, bool rebuildDefaults = false)
		{
			var previousSelection = selectedKey;
			var waitingForTag = false;
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
				{
					// A tag renamed in the Tag name box is missing until the sidebar saves it under its new name; the details wait for it.
					if (selectedKey is not null && selectedKey == renamingTagKey) waitingForTag = true;
					else selectedKey = shown.FirstOrDefault(row => !row.ViewOnly)?.Key;
				}
				ShowPreviewSelection();
				// After layout (low priority), so the list has measured its new rows and the position isn't cut short.
				DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
				{
					if (!hidden && version == renderVersion && Math.Abs(preview.VerticalScrollOffset - offset) > 0.5) preview.ScrollToVerticalOffset(offset);
				});
				undo.IsEnabled = liveBefore is not null || history.CanUndo(owner.CaptureEditor()); redo.IsEnabled = history.CanRedo(owner.CaptureEditor());
				// Reset buttons are greyed out when there is nothing to reset.
				resetAll.IsEnabled = !owner.IsSidebarEditorDefault();
				duplicate.IsEnabled = baseRows.FirstOrDefault(candidate => candidate.Key == selectedKey) is { } selected && owner.CanDuplicateEditorRow(selected);
			}
			finally { refreshing = false; }
			if (!waitingForTag && (rebuildInspector || previousSelection != selectedKey)) BuildInspector();
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
			// The same gaps as the sidebar: the section spacing after an expanded section, and Legacy's 2 px insets at both ends of a section.
			var legacy = owner.EditorOptions.SidebarAppearance == 1;
			var sectionGap = owner.EditorOptions.SidebarSectionSpacing;
			var previousExpanded = false;
			var legacySection = false;
			for (int index = 0; index < nodes.Count; index++)
			{
				var node = nodes[index];
				if (node.Depth == 0)
				{
					node.HasExpandedPredecessor = previousExpanded;
					previousExpanded = node.Item.Children is null || (shown[index].CanExpand ? node.PreviewExpanded : node.Item.IsExpanded);
					legacySection = node.Item is ISidebarItemAppearanceModel { SidebarLook.LegacyHeader: true };
				}
				var first = legacy && legacySection && node.Depth == 1 && index > 0 && nodes[index - 1].Depth == 0;
				var last = legacy && legacySection && node.Depth > 0 && (index + 1 == nodes.Count || nodes[index + 1].Depth == 0);
				node.SetLegacySectionInsets(first, last, sectionGap);
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
			// Rebuilding the same item keeps the details as tall as they were until the new content is laid out, so the scroll
			// position doesn't jump.
			if (offset > 0) inspector.MinHeight = inspector.ActualHeight;
			inspector.Children.Clear();
			var row = owner.EditorRows().FirstOrDefault(candidate => candidate.Key == selectedKey);
			if (row is null) { inspector.MinHeight = 0; return; }
			void Edit(Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update) => Change(() => owner.EditSidebarEntry(row.Key, update), false);
			void EditLive(Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update) => ChangeLive(() => owner.EditSidebarEntry(row.Key, update));
			// The item's own name: a label only changes the name box. A tag's name changes when the tag is renamed; a duplicate shows
			// the name of what it copies, and groups have only their label.
			var titleText = row.Decoration || row.OriginalName.Length == 0 ? row.Name : row.OriginalName;
			var titleStyle = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
			var title = new TextBlock { Style = titleStyle, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
			title.Inlines.Add(new Run { Text = titleText });
			// "Duplicate" and "Currently unavailable" follow the name on its baseline, in normal size and the same darker grey. Each
			// stays in one piece when the line wraps.
			void HeadingNote(string resource)
			{
				title.Inlines.Add(new Run { Text = "   " });
				title.Inlines.Add(Themed(new Run
				{
					Text = resource.GetLocalizedResource().Replace(' ', '\u00A0'), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Normal
				}, TextElement.ForegroundProperty, "TextFillColorTertiaryBrush"));
			}
			if (row.Entry.SourceKey.Length > 0) HeadingNote(Strings.SidebarEditorDuplicateSuffix);
			if (!row.Available && !row.Removable && !row.Entry.Hidden || owner.IsEditorMissingTagCopy(row)) HeadingNote(Strings.SidebarEditorUnavailable);
			// The name alone, measured to decide whether the buttons still fit beside it.
			var nameProbe = new TextBlock { Text = titleText, Style = titleStyle };
			// One line: the name on the left; Show in Sidebar, Move up and down, Reset and Remove on the right.
			var actions = new Grid { ColumnSpacing = 8 };
			actions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			actions.Children.Add(title);
			var moves = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
			var visibility = EyeToggle(Strings.SidebarEditorVisible, !owner.IsEditorEntryHidden(row.Key), value => Edit(entry => entry with { Hidden = !value }), eye: false);
			visibility.MinHeight = 36;
			moves.Children.Add(visibility);
			VerticalSeparator(moves);
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
			// When the line is too long, the notes wrap under the name first; the buttons move under the name, as one group, only when
			// the name and the buttons don't fit either.
			actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
			actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
			actions.SizeChanged += (_, args) =>
			{
				var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
				nameProbe.Measure(unbounded); moves.Measure(unbounded);
				var buttonsBelow = nameProbe.DesiredSize.Width + actions.ColumnSpacing + moves.DesiredSize.Width > args.NewSize.Width;
				SetRow(moves, buttonsBelow ? 1 : 0); SetColumn(moves, buttonsBelow ? 0 : 1); Grid.SetColumnSpan(moves, buttonsBelow ? 2 : 1);
				actions.RowSpacing = buttonsBelow ? 8 : 0;
			};
			if (!row.Decoration)
			{
				// [Icon], then "Label" (a tag's "Tag name") joined to its box, the divider on the same line as every other row's. A tag's
				// box renames the tag through Files' own tag settings, with a note saying so; a duplicated tag is a sidebar shortcut, so
				// its box is only a label.
				var tag = owner.IsEditorTag(row.Key);
				var labelRow = new Grid { ColumnSpacing = IconGap };
				labelRow.ColumnDefinitions.Add(new() { Width = new GridLength(IconButtonWidth) });
				labelRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				var showIcon = EyeToggle(Strings.SidebarEditorShowIcon, !LegacyHeader(row) && !row.Entry.HideIcon, value => Edit(entry => entry with { HideIcon = !value }));
				showIcon.IsEnabled = !LegacyHeader(row);
				showIcon.Width = IconButtonWidth;
				showIcon.Padding = new Thickness(8, 4, 8, 4);
				showIcon.VerticalAlignment = VerticalAlignment.Top;
				ToolTipService.SetToolTip(showIcon, Strings.SidebarEditorIconTooltip.GetLocalizedResource());
				AutomationProperties.SetHelpText(showIcon, Strings.SidebarEditorIconTooltip.GetLocalizedResource());
				labelRow.Children.Add(showIcon);
				var labelHost = new StackPanel { Spacing = 4 };
				SetColumn(labelHost, 1);
				if (tag)
				{
					labelHost.Children.Add(Labeled(Strings.SidebarEditorTagName, TagNameBox(row), ShortLabelWidth));
					// Under the box's text.
					labelHost.Children.Add(new TextBlock
					{
						Text = Strings.SidebarEditorTagRenameNote.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7,
						Margin = new Thickness(ShortLabelWidth + 13, 0, 0, 0)
					});
				}
				else
					Text(labelHost, Strings.SidebarVisualName, row.Name, value => Edit(entry => entry with { Label = value.Trim() }), enabled: row.CanEditName, labelWidth: ShortLabelWidth);
				labelRow.Children.Add(labelHost);
				inspector.Children.Add(labelRow);
				var size = SizeChoice(row.Entry);
				Combo(inspector, Strings.CustomizationSidebarEntrySize, SizeChoices, size,
					value => Change(() => owner.EditSidebarEntry(row.Key, entry => WithSizeChoice(entry, value, DefaultPoints(row, entry.Size)))));
				// Shown directly under Size, which already says Custom. Legacy headings show no icon, so only their text size is offered.
				if (size == 3) CustomSizes(inspector, row.Entry, DefaultPoints(row), LegacyHeader(row), EditLive, Edit);
				// A group's icon comes right after its size.
				if (row.Key.StartsWith("sidebar-group:", StringComparison.Ordinal)) BuildGroupIconPicker(row);
				if (owner.EditorOptions.SidebarAppearance == 1 && row.CanHidePin)
					Check(inspector, Strings.CustomizationSidebarHidePin, row.Entry.HidePin, value => Edit(entry => entry with { HidePin = value }));
				if (row.CanExpand)
				{
					Combo(inspector, Strings.SidebarEditorExpansion, [Strings.SidebarEditorDefault, Strings.CustomizationSidebarAlwaysExpanded, Strings.CustomizationSidebarAlwaysCollapsed],
						PreviewExpansionMode(row),
						// Rebuilds the details: Always collapsed switches off Style inner folders separately.
						value => Change(() => owner.EditSidebarEntry(row.Key, entry => owner.EditorOptions.SidebarAppearance == 1
							? entry with { LegacyExpansion = value } : entry with { HideArrow = value == 2, ExpansionLock = value })));
				}
				Separator(inspector);
				var appearanceBody = new StackPanel { Spacing = 12, Padding = new Thickness(16, 12, 16, 12) };
				// Where to apply (a two-part switch, the chosen scope described under it) and what to copy (toggle chips), then Undo
				// changes and Apply. Every row in the scope takes the copied settings it can use.
				TextBlock UnifyLabel(string resource) => Themed(new TextBlock
				{
					Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
				}, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
				// Entire sidebar (3), or Customize (4): only the sections chosen with the chips below the switch.
				var scopeValues = new[] { 3, 4 };
				// No note under Entire sidebar.
				var scopeHints = new[] { null, Strings.SidebarEditorScopeCustomHint };
				var scopeSwitch = new Segmented { HorizontalAlignment = HorizontalAlignment.Stretch };
				scopeSwitch.Items.Add(new SegmentedItem { Content = Strings.CustomizationSidebarScopeAll.GetLocalizedResource() });
				scopeSwitch.Items.Add(new SegmentedItem { Content = Strings.CustomizationSidebarScopeCustom.GetLocalizedResource() });
				scopeSwitch.SelectedIndex = Math.Max(0, Array.IndexOf(scopeValues, appearanceScope));
				AutomationProperties.SetName(scopeSwitch, Strings.SidebarEditorApplyTo.GetLocalizedResource());
				var scopeHint = Themed(new TextBlock
				{
					Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
				}, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
				var scopePanel = new StackPanel();
				scopePanel.Children.Add(UnifyLabel(Strings.SidebarEditorApplyTo));
				scopePanel.Children.Add(scopeSwitch);
				scopePanel.Children.Add(scopeHint);
				// Customize: a chip for each top-level row (a section with its contents, or a single item such as Home). The first
				// time, the row holding the source item is chosen.
				var sectionChips = new CommunityToolkit.WinUI.Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
				var chosenSections = owner.EditorUnifySections;
				if (chosenSections.Count == 0) chosenSections.Add(owner.EditorTopLevelKey(row.Key));
				void SectionChip(string key, string label)
				{
					var chip = new ToggleButton { Content = label, IsChecked = chosenSections.Contains(key) };
					chip.Checked += (_, _) => { chosenSections.Add(key); updateAppearanceApply?.Invoke(); };
					chip.Unchecked += (_, _) => { chosenSections.Remove(key); updateAppearanceApply?.Invoke(); };
					sectionChips.Children.Add(chip);
				}
				foreach (var topLevel in owner.EditorTopLevelRows())
					SectionChip(topLevel.Key, topLevel.Name);
				scopePanel.Children.Add(sectionChips);
				appearanceBody.Children.Add(scopePanel);

				// What is copied; kept by the sidebar for every item and panel.
				var chips = new CommunityToolkit.WinUI.Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
				void UnifyChip(string resource, bool value, Func<UnifyChoice, bool, UnifyChoice> update)
				{
					var chip = new ToggleButton { Content = resource.GetLocalizedResource(), IsChecked = value };
					chip.Checked += (_, _) => { owner.EditorUnify = update(owner.EditorUnify, true); updateAppearanceApply?.Invoke(); };
					chip.Unchecked += (_, _) => { owner.EditorUnify = update(owner.EditorUnify, false); updateAppearanceApply?.Invoke(); };
					chips.Children.Add(chip);
				}
				var unify = owner.EditorUnify;
				UnifyChip(Strings.CustomizationSidebarEntrySize, unify.Size, (choice, isOn) => choice with { Size = isOn });
				UnifyChip(Strings.SidebarEditorUnifyIcon, unify.Icon, (choice, isOn) => choice with { Icon = isOn });
				UnifyChip(Strings.SidebarEditorUnifyArrow, unify.Arrow, (choice, isOn) => choice with { Arrow = isOn });
				// Pins are shown only in the Legacy layout.
				if (owner.EditorOptions.SidebarAppearance == 1)
					UnifyChip(Strings.SidebarEditorUnifyPin, unify.Pin, (choice, isOn) => choice with { Pin = isOn });
				var copyPanel = new StackPanel();
				copyPanel.Children.Add(UnifyLabel(Strings.SidebarEditorUnifyCopy));
				copyPanel.Children.Add(chips);
				appearanceBody.Children.Add(copyPanel);

				var apply = new Button
				{
					Content = Strings.SidebarEditorApply.GetLocalizedResource(),
					Style = (Style)Application.Current.Resources["AccentButtonStyle"],
					MinWidth = 96
				};
				// The panel's own Undo reverses an Apply.
				updateAppearanceApply = () =>
				{
					apply.IsEnabled = owner.CanApplyEditorAppearance(row.Key, appearanceScope);
					var hint = scopeHints[Math.Max(0, Array.IndexOf(scopeValues, appearanceScope))];
					scopeHint.Text = hint?.GetLocalizedResource() ?? string.Empty;
					scopeHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
					sectionChips.Visibility = appearanceScope == 4 ? Visibility.Visible : Visibility.Collapsed;
				};
				scopeSwitch.SelectionChanged += (_, _) =>
				{
					if (scopeSwitch.SelectedIndex < 0) return;
					appearanceScope = scopeValues[scopeSwitch.SelectedIndex];
					updateAppearanceApply?.Invoke();
				};
				apply.Click += (_, _) => ApplyAppearance(row.Key);
				// The open part in the header's colour (see HeaderColoured).
				var appearanceLayout = Themed(new Grid { HorizontalAlignment = HorizontalAlignment.Stretch }, Panel.BackgroundProperty, HeaderColour);
				appearanceLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
				appearanceLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
				appearanceLayout.Children.Add(appearanceBody);
				var footerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
				footerActions.Children.Add(apply);
				var footer = Themed(new Border
				{
					BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16), Child = footerActions
				}, Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
				SetRow(footer, 1); appearanceLayout.Children.Add(footer);
				// The app's own settings expander, so its name and note look like every other setting's (and like the inner folders' card).
				var appearance = new SettingsExpander
				{
					Header = Strings.SidebarEditorApplyAppearance.GetLocalizedResource(), Description = Strings.SidebarEditorUnifiedDescription.GetLocalizedResource(),
					ItemsFooter = appearanceLayout, IsExpanded = appearanceExpanded, HorizontalAlignment = HorizontalAlignment.Stretch
				};
				HeaderColoured(appearance);
				Raised(appearance);
				appearance.RegisterPropertyChangedCallback(SettingsExpander.IsExpandedProperty, (_, _) => appearanceExpanded = appearance.IsExpanded);
				inspector.Children.Add(appearance);
				updateAppearanceApply.Invoke();
				if (CanStyleEditorFolders(row))
					BuildFolderAppearance(row);
			}
			else
			{
				Number(inspector, Strings.CustomizationSidebarGapSize, row.Entry.Gap, 4, 64, value => EditLive(entry => entry with { Gap = (int)value }));
			}
			var key = selectedKey;
			DispatcherQueue.TryEnqueue(() =>
			{
				if (!hidden && key == inspectorKey && version == inspectorVersion) details.ChangeView(null, offset, null, true);
				inspector.MinHeight = 0;
			});
		}

		private void ApplyAppearance(string key)
		{
			CommitPendingEdits();
			Change(() => owner.ApplyEditorAppearance(key, appearanceScope), false);
			updateAppearanceApply?.Invoke();
		}

		private void DuplicateSelectedItem()
		{
			CommitPendingEdits();
			Change(() => selectedKey = owner.DuplicateEditorEntry(selectedKey));
		}

		[DynamicWindowsRuntimeCast(typeof(ComboBoxItem))]
		[DynamicWindowsRuntimeCast(typeof(ImageIcon))]
		private void BuildGroupIconPicker(EditorRow row)
		{
			var layout = new Grid { ColumnSpacing = 8 };
			layout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var icons = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
			AutomationProperties.SetName(icons, Strings.Icon.GetLocalizedResource());
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
					delete.Resources["ButtonBorderBrush"] = transparent;
					delete.Resources["ButtonBorderBrushPointerOver"] = transparent;
					delete.Resources["ButtonBorderBrushPressed"] = transparent;
					// Secondary text colour until hovered, following Files' theme.
					Themed(delete, Control.ForegroundProperty, "TextFillColorSecondaryBrush");
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
			var browse = new Button { Content = Strings.Browse.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center };
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
			SetColumn(browse, 1); layout.Children.Add(Labeled(Strings.Icon, icons)); layout.Children.Add(Raised(browse));
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
			void EditLive(Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry> update) => ChangeLive(() => owner.EditSidebarEntry(folderKey, update));
			(double Text, double Icon) Points(int size) => SidebarCustomizationStyle.DefaultPoints(size, owner.EditorOptions);

			// An item that is always collapsed never shows its inner folders: the switch shows off and is greyed out.
			var alwaysCollapsed = PreviewExpansionMode(row) == 2;
			var active = separate && !alwaysCollapsed;
			var toggle = new ToggleSwitch
			{
				IsOn = active,
				IsEnabled = !alwaysCollapsed
			};
			AutomationProperties.SetName(toggle, Strings.SidebarEditorFolderAppearance.GetLocalizedResource());
			if (alwaysCollapsed) AutomationProperties.SetHelpText(toggle, Strings.SidebarEditorFolderCollapsedNote.GetLocalizedResource());
			toggle.Toggled += (_, _) =>
			{
				if (toggle.IsOn != owner.HasEditorFolderAppearance(row.Key))
					Change(() => owner.SetEditorFolderAppearance(row.Key, toggle.IsOn));
			};
			// The same Icon button and Size box as the item's, inside the card and greyed out while it is off.
			var body = new StackPanel { Spacing = 8, Padding = new Thickness(16, 12, 16, 12) };
			var size = SizeChoice(entry);
			// Wider than the item's Icon button, which fits the text "Icon" but not "Icons"; the divider stays where it was.
			const double folderIconWidth = IconButtonWidth + 14;
			var sizeRow = new Grid { ColumnSpacing = IconGap };
			sizeRow.ColumnDefinitions.Add(new() { Width = new GridLength(folderIconWidth) });
			sizeRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			var icons = EyeToggle(Strings.SidebarEditorFolderShowIcons, !entry.HideIcon, value => Edit(current => current with { HideIcon = !value }));
			icons.Width = folderIconWidth;
			icons.Padding = new Thickness(8, 4, 8, 4);
			ToolTipService.SetToolTip(icons, Strings.SidebarEditorFolderIconTooltip.GetLocalizedResource());
			AutomationProperties.SetHelpText(icons, Strings.SidebarEditorFolderIconTooltip.GetLocalizedResource());
			sizeRow.Children.Add(icons);
			var sizeHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			SetColumn(sizeHost, 1); sizeRow.Children.Add(sizeHost);
			Combo(sizeHost, Strings.CustomizationSidebarEntrySize, SizeChoices, size,
				value => Change(() => owner.EditSidebarEntry(folderKey, current => WithSizeChoice(current, value, Points(current.Size)))),
				LabelColumnWidth - folderIconWidth - IconGap);
			body.Children.Add(sizeRow);
			if (size == 3) CustomSizes(body, entry, Points(entry.Size), false, EditLive, Edit);
			// The app's own settings expander, like Unify Appearance, with its switch on the right.
			var folders = new SettingsExpander
			{
				Header = Strings.SidebarEditorFolderAppearance.GetLocalizedResource(),
				// While the item is always collapsed, a second line says why the switch is off.
				Description = alwaysCollapsed
					? new StackPanel
					{
						Children =
						{
							new TextBlock { Text = Strings.SidebarEditorFolderAppearanceDescription.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
							new TextBlock { Text = Strings.SidebarEditorFolderCollapsedNote.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap }
						}
					}
					: Strings.SidebarEditorFolderAppearanceDescription.GetLocalizedResource(),
				Content = toggle, IsExpanded = foldersExpanded,
				// A StackPanel can't be disabled, so a ContentControl around it turns everything inside off and greys it out.
				// In the header's colour, which stays full while the settings inside are greyed out.
				ItemsFooter = Themed(new Border
				{
					Child = new ContentControl
					{
						Content = body, IsEnabled = active, Opacity = active ? 1 : 0.5,
						HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
					}
				}, Border.BackgroundProperty, HeaderColour), HorizontalAlignment = HorizontalAlignment.Stretch
			};
			HeaderColoured(folders);
			Raised(folders);
			folders.RegisterPropertyChangedCallback(SettingsExpander.IsExpandedProperty, (_, _) => foldersExpanded = folders.IsExpanded);
			inspector.Children.Add(folders);
		}

		[DynamicWindowsRuntimeCast(typeof(Brush))]
		private void BuildDefaults()
		{
			defaults.Children.Clear(); var options = owner.EditorOptions;
			void Save(Func<CustomizationPreferences, CustomizationPreferences> update) => Change(() => owner.SaveEditorOptions(update(owner.EditorOptions)), false);
			Separator(defaults);
			// Spacing: the gap between row highlights (the 4 px built in plus the setting), and the gap before a section that follows
			// an expanded one. Each row is its name, an info icon with the default, and a - value + stepper.
			defaults.Children.Add(new TextBlock { Text = Strings.SidebarEditorSpacing.GetLocalizedResource(), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
			// setter, when given, receives a way to show another value without saving it.
			Grid SpacingRow(StackPanel host, string resource, int value, int minimum, int maximum, int fallback, Func<CustomizationPreferences, int, CustomizationPreferences> save,
				Action<Action<int>>? setter = null)
			{
				var label = resource.GetLocalizedResource();
				var defaultText = string.Format(Strings.SidebarEditorSpacingDefault.GetLocalizedResource(), fallback);
				// The default, as a small outlined tag beside the name.
				var tag = Themed(new Border
				{
					BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
					Child = Themed(new TextBlock { Text = defaultText, FontSize = 12 }, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush")
				}, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
				// One outlined control: - | value | +, with thin dividers, the value centred.
				var number = new TextBlock { Text = value.ToString(), HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
				AutomationProperties.SetName(number, label); AutomationProperties.SetHelpText(number, defaultText);
				// Held down, a button keeps stepping: after 0.4 s, every 60 ms.
				RepeatButton Step(string glyph, string name, CornerRadius corners)
				{
					var button = new RepeatButton
					{
						Delay = 400, Interval = 60,
						Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Width = 36, Height = 32, Padding = new Thickness(0),
						BorderThickness = new Thickness(0), CornerRadius = corners, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
						HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
					};
					var text = name.GetLocalizedResource(); AutomationProperties.SetName(button, text + " " + label); ToolTipService.SetToolTip(button, text);
					return button;
				}
				var decrease = Step("\uE738", Strings.SidebarEditorDecrease, new CornerRadius(3, 0, 0, 3));
				var increase = Step("\uE710", Strings.SidebarEditorIncrease, new CornerRadius(0, 3, 3, 0));
				void Show() { number.Text = value.ToString(); decrease.IsEnabled = value > minimum; increase.IsEnabled = value < maximum; }
				setter?.Invoke(next => { value = Math.Clamp(next, minimum, maximum); Show(); });
				void StepBy(int direction)
				{
					var next = Math.Clamp(value + direction, minimum, maximum);
					if (next == value) return;
					value = next; Show();
					Save(current => save(current, next));
				}
				decrease.Click += (_, _) => StepBy(-1);
				increase.Click += (_, _) => StepBy(1);
				Show();
				var stepper = new Grid { Height = 32 };
				foreach (var width in new[] { 36d, 1, 56, 1, 36 }) stepper.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
				Border Divider(int column) { var line = Themed(new Border(), Border.BackgroundProperty, "ControlStrokeColorDefaultBrush"); SetColumn(line, column); return line; }
				SetColumn(number, 2); SetColumn(increase, 4);
				stepper.Children.Add(decrease); stepper.Children.Add(Divider(1)); stepper.Children.Add(number); stepper.Children.Add(Divider(3)); stepper.Children.Add(increase);
				var outline = Raised(Themed(Themed(new Border
				{
					BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Child = stepper
				}, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush"), Border.BackgroundProperty, "ControlFillColorDefaultBrush"));
				var row = new Grid { ColumnSpacing = 8 };
				row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
				row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
					Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, tag } });
				SetColumn(outline, 1); row.Children.Add(outline);
				host.Children.Add(row);
				return row;
			}
			SpacingRow(defaults, Strings.SidebarEditorSpacingSections, options.SidebarSectionSpacing, 0, SidebarCustomizationStyle.MaxSectionSpacing,
				SidebarCustomizationStyle.DefaultSectionSpacing, (current, value) => current with { SidebarSectionSpacing = value });
			SpacingRow(defaults, Strings.SidebarEditorSpacingItems, EditorBaseRowGap + options.SidebarItemSpacing,
				EditorBaseRowGap + SidebarCustomizationStyle.MinItemSpacing, EditorBaseRowGap + SidebarCustomizationStyle.MaxItemSpacing, EditorBaseRowGap,
				(current, value) => current with { SidebarItemSpacing = value - EditorBaseRowGap });
			Separator(defaults);
			var checkboxSettings = new StackPanel { Spacing = 6 };
			defaults.Children.Add(checkboxSettings);
			void CheckSetting(string resource, string notes, bool value, Action<bool> save)
			{
				var setting = new StackPanel();
				var control = Check(setting, resource, value, save);
				var note = new TextBlock
				{
					Text = notes.GetLocalizedResource(),
					TextWrapping = TextWrapping.Wrap,
					FontSize = 12,
					Opacity = 0.7,
					Margin = new Thickness(28, -4, 0, 0)
				};
				note.Tapped += (_, args) =>
				{
					if (!control.IsEnabled) return;
					control.Focus(FocusState.Pointer);
					control.IsChecked = control.IsChecked != true;
					args.Handled = true;
				};
				setting.Children.Add(note);
				AutomationProperties.SetName(control, resource.GetLocalizedResource());
				AutomationProperties.SetHelpText(control, notes.GetLocalizedResource());
				checkboxSettings.Children.Add(setting);
			}
			CheckSetting(Strings.CustomizationSidebarDragEnabled, Strings.CustomizationSidebarDragEnabledNotes, options.SidebarDragEnabled,
				value => Save(current => current with { SidebarDragEnabled = value }));
			CheckSetting(Strings.CustomizationSidebarSharpIcons, Strings.CustomizationSidebarSharpIconsNotes, options.SharpLargeIcons,
				value => Save(current => current with { SharpLargeIcons = value }));
			CheckSetting(Strings.CustomizationSidebarKeepTextPosition, Strings.CustomizationSidebarKeepTextPositionNotes, options.SidebarKeepTextPosition,
				value => Save(current => current with { SidebarKeepTextPosition = value }));
			// A lighter panel sets Compact mode apart from the settings above.
			var compact = new StackPanel { Spacing = 6 };
			compact.Children.Add(new TextBlock
			{
				Text = Strings.SidebarEditorCompactMode.GetLocalizedResource(),
				FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
			});
			Check(compact, Strings.CustomizationSidebarCompactShowIcons, options.SidebarCompactShowIcons,
				value => Save(current => current with { SidebarCompactShowIcons = value }));
			Check(compact, Strings.CustomizationSidebarCompactGroupContents, options.SidebarCompactGroupContents,
				value => Save(current => current with { SidebarCompactGroupContents = value }));
			Check(compact, Strings.SidebarEditorCompactHideGroupIcons, options.SidebarCompactHideGroupIcons,
				value => Save(current => current with { SidebarCompactHideGroupIcons = value }));
			SpacingRow(compact, Strings.SidebarEditorCompactSpacing, EditorBaseRowGap + options.SidebarCompactItemSpacing,
				EditorBaseRowGap + SidebarCustomizationStyle.MinItemSpacing, EditorBaseRowGap + SidebarCustomizationStyle.MaxItemSpacing, EditorBaseRowGap,
				(current, value) => current with { SidebarCompactItemSpacing = value - EditorBaseRowGap });
			// As wide as the Spacing stepper above and flush with its right edge. Custom adds a row under it with the same stepper as
			// Spacing (8 to 32 px), shown only while Custom is chosen.
			var sizeChoice = Raised(new ComboBox { Width = 132, HorizontalAlignment = HorizontalAlignment.Right });
			foreach (var resource in new[] { Strings.CustomizationSidebarCompactOriginalSize, Strings.CustomizationSidebarCompactLargestSize, Strings.SidebarEditorCustom })
				sizeChoice.Items.Add(resource.GetLocalizedResource());
			sizeChoice.SelectedIndex = options.SidebarCompactIconMode;
			AutomationProperties.SetName(sizeChoice, Strings.CustomizationSidebarCompactIconSize.GetLocalizedResource());
			compact.Children.Add(SettingRow(Strings.CustomizationSidebarCompactIconSize, sizeChoice));
			// The size each choice draws: Original (16 px) is the custom row's default, Largest is 24 px.
			int ShownPixels(int mode) => (options with { SidebarCompactIconMode = mode }).CompactIconPixels;
			Action<int>? showCustomSize = null;
			var customSize = SpacingRow(compact, Strings.SidebarEditorCompactCustomSize, Math.Clamp(options.SidebarCompactIconSize, 8, 32), 8, 32, ShownPixels(0),
				(current, value) => current with { SidebarCompactIconSize = value, SidebarCompactIconSizeChosen = true }, setter => showCustomSize = setter);
			customSize.Visibility = options.SidebarCompactIconMode == 2 ? Visibility.Visible : Visibility.Collapsed;
			sizeChoice.SelectionChanged += (_, _) =>
			{
				if (sizeChoice.SelectedIndex < 0) return;
				customSize.Visibility = sizeChoice.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
				var current = owner.EditorOptions;
				if (sizeChoice.SelectedIndex == 2 && current.SidebarCompactIconMode != 2)
				{
					// Until the user picks a size, Custom starts at the size shown so far: 16 px from Original, 24 px from Largest.
					// After that it comes back with the user's last size.
					var pixels = current.SidebarCompactIconSizeChosen ? Math.Clamp(current.SidebarCompactIconSize, 8, 32) : ShownPixels(current.SidebarCompactIconMode);
					showCustomSize?.Invoke(pixels);
					Save(saved => saved with { SidebarCompactIconMode = 2, SidebarCompactIconSize = pixels });
				}
				else Save(saved => saved with { SidebarCompactIconMode = sizeChoice.SelectedIndex });
			};
			// A subtle fill with a card's outline, visible on the flyout in both themes (the near-white control fill disappears on
			// the light flyout).
			defaults.Children.Add(Raised(Themed(Themed(new Border
			{
				CornerRadius = new CornerRadius(4),
				BorderThickness = new Thickness(1),
				Margin = new Thickness(0, 16, 0, 0),
				Padding = new Thickness(12),
				Child = compact
			}, Border.BackgroundProperty, "SubtleFillColorSecondaryBrush"), Border.BorderBrushProperty, "CardStrokeColorDefaultBrush")));
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
		// A brush that follows Files' theme for a property set in code (see CustomizationThemeBrushes).
		private T Themed<T>(T target, DependencyProperty property, string key) where T : DependencyObject
			=> CustomizationThemeBrushes.Bind(this, target, property, key);
		private bool LegacyHeader(EditorRow row) => owner.EditorOptions.SidebarAppearance == 1 && SidebarCustomizationStyle.IsLegacyHeader(row.Group, row.Key);
		private (double Text, double Icon) DefaultPoints(EditorRow row, int? size = null) => SidebarCustomizationStyle.DefaultPoints(size ?? row.Entry.Size,
			owner.EditorOptions, SidebarCustomizationStyle.IsLegacyHeader(row.Group, row.Key));
		private void VerticalSeparator(Panel host) => host.Children.Add(Themed(new Border
		{
			Width = 1, Height = 20, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
		}, Border.BackgroundProperty, "DividerStrokeColorDefaultBrush"));
		private void Separator(StackPanel host)
			=> host.Children.Add(Themed(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4) }, Border.BackgroundProperty, "DividerStrokeColorDefaultBrush"));

		// The Size choices, which one an entry shows, and the entry after one is picked: Custom keeps its sizes, or starts at those of
		// the choice it replaces. The same for an item and for its inner folders.
		private static readonly string[] SizeChoices = [Strings.SidebarEditorDefault, Strings.SidebarEditorMedium, Strings.SidebarEditorLarge, Strings.SidebarEditorCustom];
		private static int SizeChoice(SidebarCustomizationState.Entry entry)
			=> entry.Size == 3 || entry.TextPt > 0 || entry.IconPt > 0 ? 3 : entry.Size is 1 or 2 ? entry.Size : 0;
		private static SidebarCustomizationState.Entry WithSizeChoice(SidebarCustomizationState.Entry entry, int choice, (double Text, double Icon) points) => entry with
		{
			Size = choice == 0 ? -1 : choice,
			TextPt = choice == 3 ? entry.TextPt > 0 ? entry.TextPt : points.Text : 0,
			IconPt = choice == 3 ? entry.IconPt > 0 ? entry.IconPt : points.Icon : 0
		};
		// Custom's Text (pt) and Icon (pt) boxes for an entry, applied live while typed, linked or not.
		private void CustomSizes(StackPanel host, SidebarCustomizationState.Entry entry, (double Text, double Icon) points, bool textOnly,
			Action<Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry>> editLive,
			Action<Func<SidebarCustomizationState.Entry, SidebarCustomizationState.Entry>> edit)
			=> Sizes(host, entry.TextPt > 0 ? entry.TextPt : points.Text, entry.IconPt > 0 ? entry.IconPt : points.Icon, entry.SizesLinked, textOnly,
				(field, text, icon) => editLive(current => field == 0
					? current with { TextPt = text, IconPt = current.SizesLinked ? icon : current.IconPt }
					: current with { IconPt = icon, TextPt = current.SizesLinked ? text : current.TextPt }),
				value => edit(current => current with { SizesLinked = value }));
		// textOnly: for rows that show no icon (Legacy headings), only the text size is shown; a linked icon size still follows it.
		private void Sizes(StackPanel host, double textValue, double iconValue, bool linked, bool textOnly,
			Action<int, double, double> save, Action<bool> saveLink)
		{
			// [Text (pt) | box] [Icon (pt) | box] [link], each name joined to its box like the other settings.
			var grid = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
			var text = new NumberBox { Value = textValue, Minimum = 6, Maximum = 36, SmallChange = 0.5 };
			var icon = new NumberBox { Value = iconValue, Minimum = 6, Maximum = 48, SmallChange = 0.5 };
			AutomationProperties.SetName(text, Strings.SidebarEditorTextPoints.GetLocalizedResource());
			AutomationProperties.SetName(icon, Strings.SidebarEditorIconPoints.GetLocalizedResource());
			var boxes = new[] { text, icon };
			var textField = Labeled(Strings.SidebarEditorTextPoints, text, SizeLabelWidth);
			var iconField = Labeled(Strings.SidebarEditorIconPoints, icon, SizeLabelWidth);
			foreach (var field in new[] { textField, iconField })
			{
				grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				SetColumn(field, grid.Children.Count);
				grid.Children.Add(field);
			}
			grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			var link = Raised(new ToggleButton
			{
				IsChecked = linked,
				Content = new FontIcon { Glyph = "\uE71B", FontSize = 14 },
				VerticalAlignment = VerticalAlignment.Center,
				Padding = new Thickness(0),
				Width = 32,
				Height = 32,
				MinWidth = 32
			});
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
			// Digits typed apply at once; the box being typed in keeps its text, only the linked one follows.
			void Typed(int field, double typed)
			{
				if (updating || typed < 6 || typed > (field == 0 ? 36 : 48)) return;
				updating = true;
				try
				{
					var textNow = field == 0 ? typed : text.Value;
					var iconNow = field == 1 ? typed : icon.Value;
					if (linked && field == 0) icon.Value = iconNow = Math.Clamp(typed * iconValue / textValue, 6, 48);
					if (linked && field == 1) text.Value = textNow = Math.Clamp(typed * textValue / iconValue, 6, 36);
					save(field, textNow, iconNow);
				}
				finally { updating = false; }
			}
			LiveTyping(text, typed => Typed(0, typed));
			LiveTyping(icon, typed => Typed(1, typed));
			foreach (var box in boxes)
			{
				box.LostFocus += (_, _) => CommitLive();
				SettingsValueEditing.Register(box, CommitLive, CancelLive);
			}
			if (textOnly)
			{
				iconField.Visibility = link.Visibility = Visibility.Collapsed;
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
		// A tag's name box, the same name as in Settings > Tags: Enter, or leaving the box, renames the tag there. A name Settings > Tags
		// wouldn't save, an empty one included, shows "Invalid name" inside the box at its right end; Enter then does nothing, as in
		// Settings > Tags, and leaving the box (or Escape) puts the tag's name back.
		[DynamicWindowsRuntimeCast(typeof(Brush))]
		[DynamicWindowsRuntimeCast(typeof(FontFamily))]
		private Grid TagNameBox(EditorRow row)
		{
			var name = row.OriginalName;
			var box = new TextBox
			{
				Text = name, MaxLength = 200, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0, 3, 3, 0),
				Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
			};
			AutomationProperties.SetName(box, Strings.SidebarEditorTagName.GetLocalizedResource());
			// Settings > Tags' error icon and wording, small and in red at the box's right end, with the naming rules in its tooltip.
			object Resource(string key) => Application.Current.Resources[key];
			var symbols = (FontFamily)Resource("SymbolThemeFontFamily");
			var icon = new Grid { VerticalAlignment = VerticalAlignment.Center };
			var iconBackground = Themed(new TextBlock { Text = (string)Resource("InfoBarIconBackgroundGlyph"), FontFamily = symbols, FontSize = 14 },
				TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
			AutomationProperties.SetAccessibilityView(iconBackground, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
			icon.Children.Add(iconBackground);
			icon.Children.Add(Themed(new TextBlock { Text = (string)Resource("InfoBarErrorIconGlyph"), FontFamily = symbols, FontSize = 14 },
				TextBlock.ForegroundProperty, "InfoBarInformationalSeverityIconForeground"));
			var invalid = new StackPanel
			{
				Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Visibility = Visibility.Collapsed,
				Children =
				{
					icon,
					Themed(new TextBlock { Text = Strings.InvalidTagNameWarning.GetLocalizedResource(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
						TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush")
				}
			};
			ToolTipService.SetToolTip(invalid, Strings.TagNameRules.GetLocalizedResource());
			// The box's own padding, put back when the warning hides.
			Thickness? basePadding = null;
			void ShowInvalid(bool show)
			{
				invalid.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
				AutomationProperties.SetHelpText(box, show ? Strings.InvalidTagNameWarning.GetLocalizedResource() : "");
				// The box's clear button would sit under the warning.
				if (FindTemplatePart(box, "DeleteButton") is { } clear)
				{
					clear.Opacity = show ? 0 : 1;
					clear.IsHitTestVisible = !show;
				}
				if (!show && basePadding is { } original) box.Padding = original;
			}
			box.TextChanged += (_, _) => ShowInvalid(!owner.IsEditorTagNameValid(row.Key, box.Text));
			// While the warning shows, the typed text stops before it.
			invalid.SizeChanged += (_, args) =>
			{
				if (invalid.Visibility != Visibility.Visible) return;
				var padding = basePadding ??= box.Padding;
				box.Padding = new Thickness(padding.Left, padding.Top, args.NewSize.Width + 16, padding.Bottom);
			};
			void Commit(bool keepTyping)
			{
				if (box.Text == name) return;
				if (owner.IsEditorTagNameValid(row.Key, box.Text))
				{
					// Renamed in Files at once, as in Settings > Tags; Undo doesn't reverse it.
					if (!owner.RenameEditorTag(row.Key, box.Text)) return;
					name = box.Text;
					renamingTagKey = row.Key;
					QueueRefresh(false);
				}
				else if (!keepTyping) box.Text = name;
			}
			// The panel's shared Enter and Escape handling would save or put the name back at once, so this box handles them itself;
			// clicking elsewhere still finishes it like any other box.
			SettingsValueEditing.Exclude(box);
			SettingsValueEditing.Register(box, () => Commit(false));
			pendingTextCommits.Add(() => Commit(false));
			box.LostFocus += (_, _) => Commit(false);
			box.KeyDown += (_, args) =>
			{
				// Escape, or Enter with a name that was saved, leaves the box; Enter with a name it can't use keeps typing.
				if (args.Key == VirtualKey.Enter) { Commit(true); if (box.Text == name) Focus(FocusState.Programmatic); }
				else if (args.Key == VirtualKey.Escape) { box.Text = name; Focus(FocusState.Programmatic); }
				else return;
				args.Handled = true;
			};
			return new Grid { Children = { box, invalid } };
		}
		// Digits typed in a number box apply at once, before Enter (only while it has focus, so its own updates aren't taken as typing).
		[DynamicWindowsRuntimeCast(typeof(TextBox))]
		private static void LiveTyping(NumberBox box, Action<double> apply)
		{
			TextBox? input = null;
			box.Loaded += (_, _) =>
			{
				if (input is not null || FindTemplatePart(box, "InputBox") is not TextBox found) return;
				input = found;
				input.TextChanged += (_, _) =>
				{
					if (input.FocusState != FocusState.Unfocused &&
						double.TryParse(input.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var typed) &&
						double.IsFinite(typed))
						apply(typed);
				};
			};
		}
		// A small shadow under a control (depth 3), in light mode only; dark mode shows none. It follows theme changes.
		private static readonly ThemeShadow smallShadow = new();
		private static T Raised<T>(T element) where T : FrameworkElement
		{
			void Apply()
			{
				var light = element.ActualTheme == ElementTheme.Light;
				element.Shadow = light ? smallShadow : null;
				element.Translation = new System.Numerics.Vector3(0, 0, light ? 3 : 0);
			}
			element.ActualThemeChanged += (_, _) => Apply();
			// The theme is only known once the control is shown.
			element.Loaded += (_, _) => Apply();
			Apply();
			return element;
		}
		// A named part of a control's template, such as a text box's clear button.
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private static FrameworkElement? FindTemplatePart(DependencyObject parent, string partName)
		{
			for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
			{
				var child = VisualTreeHelper.GetChild(parent, index);
				if (child is FrameworkElement element && element.Name == partName) return element;
				if (FindTemplatePart(child, partName) is { } found) return found;
			}
			return null;
		}
		// An eye drawn with its lid closed: the lid's curve and three lashes, 16 px.
		private static PathIcon ClosedEye()
		{
			static PathFigure Shape(Point start, params PathSegment[] segments)
			{
				var figure = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
				foreach (var segment in segments) figure.Segments.Add(segment);
				return figure;
			}
			static LineSegment Line(double x, double y) => new() { Point = new Point(x, y) };
			static QuadraticBezierSegment Curve(double cx, double cy, double x, double y) => new() { Point1 = new Point(cx, cy), Point2 = new Point(x, y) };
			var geometry = new PathGeometry();
			geometry.Figures.Add(Shape(new Point(1, 6.5), Curve(8, 13.5, 15, 6.5), Line(15, 8), Curve(8, 15, 1, 8)));
			geometry.Figures.Add(Shape(new Point(3, 9.6), Line(4.2, 10.2), Line(2.9, 12.4), Line(1.7, 11.8)));
			geometry.Figures.Add(Shape(new Point(7.3, 11.4), Line(8.7, 11.4), Line(8.7, 14.4), Line(7.3, 14.4)));
			geometry.Figures.Add(Shape(new Point(11.8, 10.2), Line(13, 9.6), Line(14.3, 11.8), Line(13.1, 12.4)));
			return new PathIcon { Data = geometry, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
		}
		// A 32 px toggle with its text and, unless eye is false, an eye that is shut while it is off.
		private static ToggleButton EyeToggle(string resource, bool isOn, Action<bool> save, bool eye = true)
		{
			var label = resource.GetLocalizedResource();
			var open = new FontIcon { Glyph = "\uE890", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Visibility = isOn ? Visibility.Visible : Visibility.Collapsed };
			var shut = ClosedEye();
			shut.Visibility = isOn ? Visibility.Collapsed : Visibility.Visible;
			var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
			if (eye) content.Children.Add(new Grid { Children = { open, shut } });
			content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
			var toggle = new ToggleButton
			{
				Content = content,
				IsChecked = isOn, MinWidth = 0, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center,
				HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
			};
			AutomationProperties.SetName(toggle, label);
			toggle.Click += (_, _) =>
			{
				var on = toggle.IsChecked == true;
				open.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
				shut.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
				save(on);
			};
			return Raised(toggle);
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
			button.Click += (_, _) => action(); return Raised(button);
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
		private void Text(StackPanel host, string resource, string value, Action<string> save, bool enabled = true, double labelWidth = LabelColumnWidth)
		{
			var control = new TextBox
			{
				Text = value,
				MaxLength = 200,
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
			AutomationProperties.SetName(control, resource.GetLocalizedResource());
			host.Children.Add(Labeled(resource, control, labelWidth));
		}
		// A whole number of pixels (separator and gap heights): the arrows step by 1, and a typed decimal is rounded.
		private void Number(StackPanel host, string resource, double value, double minimum, double maximum, Action<double> save)
		{
			var control = new NumberBox
			{
				Value = value,
				Minimum = minimum,
				Maximum = maximum,
				SmallChange = 1,
				SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
			};
			static double Whole(double number) => Math.Round(number, MidpointRounding.AwayFromZero);
			AutomationProperties.SetName(control, resource.GetLocalizedResource());
			control.ValueChanged += (_, args) =>
			{
				if (!double.IsFinite(args.NewValue)) return;
				var whole = Math.Clamp(Whole(args.NewValue), minimum, maximum);
				// The box shows the rounded number too.
				if (whole != args.NewValue) control.Value = whole;
				save(whole);
			};
			// Shown at once while typed or stepped; one Undo step when confirmed, none after Escape.
			LiveTyping(control, typed => { if (typed >= minimum && typed <= maximum) save(Whole(typed)); });
			control.LostFocus += (_, _) => CommitLive();
			SettingsValueEditing.Register(control, CommitLive, CancelLive);
			host.Children.Add(Labeled(resource, control));
		}
		private void Combo(StackPanel host, string resource, string[] labels, int selected, Action<int> save, double labelWidth = LabelColumnWidth)
		{
			var control = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
			AutomationProperties.SetName(control, resource.GetLocalizedResource());
			foreach (var label in labels) control.Items.Add(label.GetLocalizedResource()); control.SelectedIndex = selected;
			control.SelectionChanged += (_, _) => { if (control.SelectedIndex >= 0) save(control.SelectedIndex); };
			host.Children.Add(Labeled(resource, control, labelWidth));
		}
		// A setting's name joined to its box as one control: the name on a slightly darker part, a divider, then the box. Every
		// row's divider sits on one line: rows that start with the Icon button give the name part what the button doesn't take.
		private const double LabelColumnWidth = 176;
		private const double IconButtonWidth = 80;
		private const double IconGap = 8;
		private const double ShortLabelWidth = LabelColumnWidth - IconButtonWidth - IconGap;
		// The name part of the custom Text (pt) and Icon (pt) boxes, which share a row.
		private const double SizeLabelWidth = 84;
		[DynamicWindowsRuntimeCast(typeof(Control))]
		[DynamicWindowsRuntimeCast(typeof(TextBox))]
		private Border Labeled(string resource, FrameworkElement control, double labelWidth = LabelColumnWidth)
		{
			var grid = new Grid();
			grid.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth - 1) });
			grid.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
			grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			var name = Themed(new TextBlock
			{
				Text = resource.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
				// Always one line, even where a text style turns wrapping on.
				TextWrapping = TextWrapping.NoWrap
			}, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
			ToolTipService.SetToolTip(name, resource.GetLocalizedResource());
			grid.Children.Add(Themed(new Border { CornerRadius = new CornerRadius(3, 0, 0, 3), Padding = new Thickness(10, 0, 6, 0), Child = name },
				// A faint dark wash, so the name part is darker than the box in light and dark alike.
				Border.BackgroundProperty, "ControlAltFillColorSecondaryBrush"));
			var divider = Themed(new Border(), Border.BackgroundProperty, "ControlStrokeColorDefaultBrush");
			SetColumn(divider, 1); grid.Children.Add(divider);
			// The box drops its own frame; the joined frame around both is its frame.
			// Its text is centred in the frame on the same line as the name.
			if (control is Control box)
			{
				box.BorderThickness = new Thickness(0);
				box.CornerRadius = new CornerRadius(0, 3, 3, 0);
				box.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
				box.MinHeight = 0;
				box.VerticalContentAlignment = VerticalAlignment.Center;
			}
			// A text box always draws its text at the top, so its padding places the text: 6 px above and 5 below the 19 px line fill
			// the frame's 30 px, the same place as the centred name.
			if (FindTextBox(control) is { } text) { text.MinHeight = 0; text.Padding = new Thickness(12, 6, 8, 5); }
			// A number box draws its text in a text box inside its template, which gets the same once it is shown.
			if (control is NumberBox number)
				number.Loaded += (_, _) =>
				{
					if (FindTemplatePart(number, "InputBox") is not TextBox input) return;
					input.MinHeight = 0;
					input.Padding = new Thickness(12, 6, 8, 5);
				};
			control.HorizontalAlignment = HorizontalAlignment.Stretch;
			control.VerticalAlignment = VerticalAlignment.Stretch;
			SetColumn(control, 2); grid.Children.Add(control);
			return Raised(Themed(Themed(new Border
			{
				BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), MinHeight = 32, Child = grid
			}, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush"), Border.BackgroundProperty, "ControlFillColorDefaultBrush"));
		}
		// The text box in a joined box: the box itself, or the one inside a tag's name box (with its warning).
		private static TextBox? FindTextBox(FrameworkElement control)
			=> control as TextBox ?? (control as Panel)?.Children.OfType<TextBox>().FirstOrDefault();
		// A settings card's open part in its header's colour: the card's own content colour is cleared, and the part placed in it is
		// painted with the header's brush, following Files' theme.
		private const string HeaderColour = "CardBackgroundFillColorDefaultBrush";
		private static void HeaderColoured(SettingsExpander card)
			=> card.Resources["ExpanderContentBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
	}
}
