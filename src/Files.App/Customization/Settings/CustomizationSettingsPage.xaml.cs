// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI.Controls;
using Files.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace Files.App.Views.Settings
{
	public sealed partial class CustomizationSettingsPage : Page
	{
		private IAppearanceSettingsService Settings => Ioc.Default.GetRequiredService<IUserSettingsService>().AppearanceSettingsService;
		private CustomizationPreferences Options => CustomizationPreferences.Parse(Settings.CustomizationPreferences);
		private readonly List<Action> updateControls = [];
		private bool updatingControls;
		private bool resetConfirmationOpen;
		// Where Settings shows the sidebar panel, the only place it opens. Several open Settings tabs share its settings, Undo/Redo, selection and expanded rows.
		private readonly Border sidebarPanelHost = new()
		{
			Height = 640,
			CornerRadius = new CornerRadius(4)
		};

		public CustomizationSettingsPage()
		{
			InitializeComponent();
			BuildSettings();
			Loaded += (_, _) => { Settings.PropertyChanged += SettingsChanged; SidebarOwner.PropertyChanged += SidebarChanged; LayoutSettings.PropertyChanged += LayoutSettingsChanged; RefreshControls(); };
			Unloaded += (_, _) => { Settings.PropertyChanged -= SettingsChanged; SidebarOwner.PropertyChanged -= SidebarChanged; LayoutSettings.PropertyChanged -= LayoutSettingsChanged; };
		}
		private static ILayoutSettingsService LayoutSettings => Ioc.Default.GetRequiredService<IUserSettingsService>().LayoutSettingsService;
		// The default columns also change in Settings > Layout and, while Column details is off, from a column heading's menu (Set as default), so Column details follows them.
		private void LayoutSettingsChanged(object? sender, PropertyChangedEventArgs args) => DispatcherQueue.TryEnqueue(RefreshControls);

		// Every column, in CustomizationColumnOrder.Keys order: its heading, where it appears when that isn't every folder,
		// and whether it is shown before anything was changed.
		private sealed record DefaultColumn(string Heading, string? Note, Func<ILayoutSettingsService, bool> Shown, Action<ILayoutSettingsService, bool>? SetShown, bool DefaultShown);
		private static readonly DefaultColumn[] DefaultColumns =
		[
			new(Strings.Name, null, _ => true, null, true),
			new(Strings.GitStatus, Strings.CustomizationColumnsOnlyGit, s => s.ShowGitStatusColumn, (s, v) => s.ShowGitStatusColumn = v, false),
			new(Strings.DateCommitted, Strings.CustomizationColumnsOnlyGit, s => s.ShowGitLastCommitDateColumn, (s, v) => s.ShowGitLastCommitDateColumn = v, false),
			new(Strings.CommitMessage, Strings.CustomizationColumnsOnlyGit, s => s.ShowGitLastCommitMessageColumn, (s, v) => s.ShowGitLastCommitMessageColumn = v, false),
			new(Strings.Author, Strings.CustomizationColumnsOnlyGit, s => s.ShowGitCommitAuthorColumn, (s, v) => s.ShowGitCommitAuthorColumn = v, false),
			new(Strings.CommitSha, Strings.CustomizationColumnsOnlyGit, s => s.ShowGitLastCommitShaColumn, (s, v) => s.ShowGitLastCommitShaColumn = v, false),
			new(Strings.Tag, null, s => s.ShowFileTagColumn, (s, v) => s.ShowFileTagColumn = v, true),
			new(Strings.Path, Strings.CustomizationColumnsOnlySearch, s => s.ShowPathColumn, (s, v) => s.ShowPathColumn = v, true),
			new(Strings.OriginalPath, Strings.CustomizationColumnsOnlyRecycleBin, s => s.ShowOriginalPathColumn, (s, v) => s.ShowOriginalPathColumn = v, true),
			new(Strings.DateDeleted, Strings.CustomizationColumnsOnlyRecycleBin, s => s.ShowDateDeletedColumn, (s, v) => s.ShowDateDeletedColumn = v, true),
			new(Strings.DateModifiedLowerCase, null, s => s.ShowDateColumn, (s, v) => s.ShowDateColumn = v, true),
			new(Strings.DateCreated, null, s => s.ShowDateCreatedColumn, (s, v) => s.ShowDateCreatedColumn = v, false),
			new(Strings.Type, null, s => s.ShowTypeColumn, (s, v) => s.ShowTypeColumn = v, true),
			new(Strings.Size, null, s => s.ShowSizeColumn, (s, v) => s.ShowSizeColumn = v, true),
			new(Strings.Status, Strings.CustomizationColumnsOnlyCloud, s => s.ShowSyncStatusColumn, (s, v) => s.ShowSyncStatusColumn = v, true)
		];
		private int[] ColumnOrder => CustomizationColumnOrder.Parse(Options.ColumnOrder);
		// What Undo and Redo in Column details step through: which columns are shown, their order and the view options.
		private sealed record ColumnDefaults(string Order, bool SizeToContent, bool FitToWidth, int Shown);
		// Kept until the app closes, like the sidebar panel's history.
		private static readonly CustomizationChangeHistory<ColumnDefaults> columnHistory = new();
		private static ColumnDefaults FactoryColumns => new(CustomizationColumnOrder.Serialize(Enumerable.Range(0, DefaultColumns.Length)), false, false,
			Enumerable.Range(0, DefaultColumns.Length).Where(index => DefaultColumns[index].DefaultShown).Sum(index => 1 << index));

		private ColumnDefaults CaptureColumns()
		{
			var layout = LayoutSettings;
			var options = Options;
			var shown = Enumerable.Range(0, DefaultColumns.Length).Where(index => DefaultColumns[index].Shown(layout)).Sum(index => 1 << index);
			return new(CustomizationColumnOrder.Serialize(CustomizationColumnOrder.Parse(options.ColumnOrder)), layout.AutoSizeColumnsInDetailsLayout,
				options.FitColumnsToWidth, shown);
		}
		private void ApplyColumns(ColumnDefaults columns)
		{
			var layout = LayoutSettings;
			for (int index = 0; index < DefaultColumns.Length; index++)
			{
				var shown = (columns.Shown & 1 << index) != 0;
				if (DefaultColumns[index].SetShown is { } setShown && DefaultColumns[index].Shown(layout) != shown) setShown(layout, shown);
			}
			if (layout.AutoSizeColumnsInDetailsLayout != columns.SizeToContent) layout.AutoSizeColumnsInDetailsLayout = columns.SizeToContent;
			Save(options => options with { ColumnOrder = columns.Order, FitColumnsToWidth = columns.FitToWidth });
		}
		// Every change made in Column details can be undone.
		private void ChangeColumns(Action change)
		{
			if (updatingControls) return;
			var before = CaptureColumns();
			change();
			columnHistory.Record(before, CaptureColumns());
			RefreshControls();
		}

		// The default layout of the details view, the same one "Set as default" saves: how columns are sized,
		// and every column, shown or not, in its order. Columns that appear only in some folders keep their place here too.
		[DynamicWindowsRuntimeCast(typeof(Brush))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private void BuildColumnSettings(SettingsExpander section)
		{
			bool Editable() => Enabled(options => options.ColumnsEnabled);


			// Top row: how columns are sized when a folder opens on the left, and Undo, Redo and Reset to default at the far right.
			// Fitting to content and fitting to the width undo each other, so only one is chosen.
			var sizing = new ComboBox { MinWidth = 280, VerticalAlignment = VerticalAlignment.Center };
			sizing.Items.Add(Strings.CustomizationColumnsAppDefault.GetLocalizedResource());
			// Fitting to content is the app's own setting: a small link icon at the right of the open list says so.
			var layoutHint = new FontIcon
			{
				Glyph = "",
				FontSize = 12,
				Visibility = Visibility.Collapsed,
				VerticalAlignment = VerticalAlignment.Center
			};
			Themed(layoutHint, IconElement.ForegroundProperty, "TextFillColorSecondaryBrush");
			ToolTipService.SetToolTip(layoutHint, Strings.CustomizationColumnsAppSettingsNote.GetLocalizedResource());
			var contentSizing = new Grid { ColumnSpacing = 8 };
			contentSizing.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			contentSizing.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			contentSizing.Children.Add(new TextBlock { Text = Strings.AutoSizeColumnsInDetailsLayout.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center });
			Grid.SetColumn(layoutHint, 1);
			contentSizing.Children.Add(layoutHint);
			sizing.Items.Add(new ComboBoxItem { Content = contentSizing, HorizontalContentAlignment = HorizontalAlignment.Stretch });
			sizing.DropDownOpened += (_, _) => layoutHint.Visibility = Visibility.Visible;
			sizing.DropDownClosed += (_, _) => layoutHint.Visibility = Visibility.Collapsed;
			sizing.Items.Add(Strings.CustomizationColumnsLive.GetLocalizedResource());
			AutomationProperties.SetName(sizing, Strings.CustomizationColumnsSizing.GetLocalizedResource());
			sizing.SelectionChanged += (_, _) => ChangeColumns(() =>
			{
				var mode = sizing.SelectedIndex;
				if (mode < 0) return;
				// Fitting to content is the app's own setting (Settings > Layout).
				LayoutSettings.AutoSizeColumnsInDetailsLayout = mode == 1;
				Save(options => options with { FitColumnsToWidth = mode == 2 });
			});
			// In a narrow page the label, the dropdown and Options wrap onto more lines instead of being cut off.
			var view = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
			view.Children.Add(new TextBlock { Text = Strings.CustomizationColumnsSizing.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center });
			view.Children.Add(sizing);
			// The app's own setting (Settings > Layout) that decides whether folders keep their own layouts; changing it here changes it there.
			var syncFolders = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
			AutomationProperties.SetName(syncFolders, Strings.SyncFolderPreferencesAcrossDirectories.GetLocalizedResource());
			syncFolders.Toggled += (_, _) =>
			{
				if (!updatingControls) LayoutSettings.SyncFolderPreferencesAcrossDirectories = syncFolders.IsOn;
			};
			var syncRow = new Grid { ColumnSpacing = 12 };
			syncRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			syncRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			syncRow.Children.Add(new TextBlock { Text = Strings.SyncFolderPreferencesAcrossDirectories.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
			Grid.SetColumn(syncFolders, 1);
			syncRow.Children.Add(syncFolders);
			var appSettings = new DropDownButton
			{
				Content = Strings.CustomizationColumnsAppSettings.GetLocalizedResource(),
				Height = 32,
				Padding = new Thickness(12, 0, 12, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Flyout = new Flyout
				{
					Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
					Content = new StackPanel
					{
						Width = 320,
						Spacing = 8,
						Children =
						{
							syncRow,
							Themed(new TextBlock
							{
								Text = Strings.CustomizationColumnsAppSettingsNote.GetLocalizedResource(),
								Style = (Style)Application.Current.Resources["App.Theme.CaptionTextBlockStyle"],
								TextWrapping = TextWrapping.Wrap
							}, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush")
						}
					}
				}
			};
			view.Children.Add(appSettings);
			updateControls.Add(() =>
			{
				syncFolders.IsOn = LayoutSettings.SyncFolderPreferencesAcrossDirectories;
				appSettings.IsEnabled = Editable();
			});

			var reset = new Button { Content = Strings.CustomizationResetDefaults.GetLocalizedResource(), Height = 32, Padding = new Thickness(12, 0, 12, 0) };
			reset.Click += async (_, _) =>
			{
				if (await ConfirmResetAsync(reset, Strings.CustomizationColumnsResetConfirm)) ChangeColumns(() => ApplyColumns(FactoryColumns));
			};
			Button HistoryButton(string glyph, string resource, bool forward)
			{
				var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Width = 32, Height = 32, Padding = new Thickness(0) };
				AutomationProperties.SetName(button, resource.GetLocalizedResource());
				ToolTipService.SetToolTip(button, resource.GetLocalizedResource());
				button.Click += (_, _) =>
				{
					var current = CaptureColumns();
					if (forward ? columnHistory.Redo(current, out var restored) : columnHistory.Undo(current, out restored)) ApplyColumns(restored);
					RefreshControls();
				};
				return button;
			}
			var undo = HistoryButton("\uE7A7", Strings.Undo, false);
			var redo = HistoryButton("\uE7A6", Strings.Redo, true);
			var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
			actions.Children.Add(undo);
			actions.Children.Add(redo);
			actions.Children.Add(Themed(new Border
			{
				Width = 1, Height = 20, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
			}, Border.BackgroundProperty, "DividerStrokeColorDefaultBrush"));
			actions.Children.Add(reset);

			var top = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
			top.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
			top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
			top.RowDefinitions.Add(new() { Height = GridLength.Auto });
			top.RowDefinitions.Add(new() { Height = GridLength.Auto });
			Grid.SetColumn(actions, 1);
			top.Children.Add(view);
			top.Children.Add(actions);
			// When the row is too narrow for both, Undo, Redo and Reset go to their own line below.
			top.SizeChanged += (_, args) =>
			{
				var unlimited = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
				view.Measure(unlimited);
				actions.Measure(unlimited);
				var stacked = args.NewSize.Width < view.DesiredSize.Width + actions.DesiredSize.Width + top.ColumnSpacing;
				Grid.SetRow(actions, stacked ? 1 : 0);
				Grid.SetColumn(actions, stacked ? 0 : 1);
				top.RowSpacing = stacked ? 8 : 0;
			};
			// The row is the card's content laid out full width (like the Columns list below), so the buttons sit at the far right.
			section.Items.Add(SectionWidthCard(new SettingsCard
			{
				ContentAlignment = CommunityToolkit.WinUI.Controls.ContentAlignment.Vertical,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Content = top,
				HorizontalAlignment = HorizontalAlignment.Stretch
			}));
			updateControls.Add(() =>
			{
				sizing.SelectedIndex = Options.FitColumnsToWidth ? 2 : LayoutSettings.AutoSizeColumnsInDetailsLayout ? 1 : 0;
				sizing.IsEnabled = Editable();
			});

			// Columns: every column in its order, each with its own switch. Rows are dragged to reorder; a line shows where the
			// column will go, like dragging in the sidebar, and the rows keep their size while dragging.
			// The rows' 8 px padding is taken back here, so checkboxes line up with the text above and grips with the buttons.
			var list = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(-8, 6, -8, 0) };
			var rows = new FrameworkElement[DefaultColumns.Length];
			var boxes = new CheckBox[DefaultColumns.Length];
			int? draggedColumn = null;
			for (int index = 0; index < DefaultColumns.Length; index++)
			{
				var column = DefaultColumns[index];
				var columnIndex = index;
				var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
				label.Children.Add(new TextBlock { Text = column.Heading.GetLocalizedResource(), VerticalAlignment = VerticalAlignment.Center });
				if (column.Note is { } note)
					label.Children.Add(Themed(new TextBlock
					{
						Text = note.GetLocalizedResource(),
						Style = (Style)Application.Current.Resources["App.Theme.CaptionTextBlockStyle"],
						VerticalAlignment = VerticalAlignment.Center
					}, TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush"));
				var box = CenteredCheckBox(label);
				AutomationProperties.SetName(box, column.Heading.GetLocalizedResource());
				if (column.SetShown is { } setShown)
					box.Click += (_, _) => ChangeColumns(() => setShown(LayoutSettings, box.IsChecked == true));
				// The system gripper glyph at its 16 px design size, in a whole-pixel 20 px box so its thin bars land on pixels; the
				// secondary text colour dims it without opacity, which would soften it.
				var grip = Themed(new FontIcon
				{
					Glyph = "",
					FontSize = 16,
					Width = 20,
					Height = 20,
					VerticalAlignment = VerticalAlignment.Center
				}, IconElement.ForegroundProperty, "TextFillColorSecondaryBrush");
				ToolTipService.SetToolTip(grip, Strings.CustomizationColumnsDragToReorder.GetLocalizedResource());
				// Insertion lines above and below the row; drawn over it, so showing them doesn't move anything.
				var lineAbove = Themed(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false },
					Border.BackgroundProperty, "AccentFillColorDefaultBrush");
				var lineBelow = Themed(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false },
					Border.BackgroundProperty, "AccentFillColorDefaultBrush");
				// Rows highlight on hover, like list items, to show they can be dragged.
				var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
				var row = new Grid { MinHeight = 36, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(4), Background = transparent, AllowDrop = true };
				row.PointerEntered += (_, _) => Themed(row, Panel.BackgroundProperty, "SubtleFillColorSecondaryBrush");
				row.PointerExited += (_, _) => row.Background = transparent;
				row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
				Grid.SetColumn(grip, 1);
				Grid.SetColumnSpan(lineAbove, 2);
				Grid.SetColumnSpan(lineBelow, 2);
				row.Children.Add(box);
				row.Children.Add(grip);
				row.Children.Add(lineAbove);
				row.Children.Add(lineBelow);
				bool Below(DragEventArgs args) => args.GetPosition(row).Y > row.ActualHeight / 2;
				void ClearLine() => lineAbove.Visibility = lineBelow.Visibility = Visibility.Collapsed;
				row.DragStarting += (_, args) =>
				{
					draggedColumn = columnIndex;
					args.Data.SetText(column.Heading);
					args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
				};
				row.DropCompleted += (_, _) => draggedColumn = null;
				row.DragOver += (_, args) =>
				{
					if (draggedColumn is not { } dragged || dragged == columnIndex) { args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; ClearLine(); return; }
					args.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
					args.DragUIOverride.IsCaptionVisible = false;
					var below = Below(args);
					lineAbove.Visibility = below ? Visibility.Collapsed : Visibility.Visible;
					lineBelow.Visibility = below ? Visibility.Visible : Visibility.Collapsed;
				};
				row.DragLeave += (_, _) => ClearLine();
				row.Drop += (_, args) =>
				{
					ClearLine();
					if (draggedColumn is not { } dragged || dragged == columnIndex) return;
					var order = ColumnOrder.ToList();
					order.Remove(dragged);
					var at = order.IndexOf(columnIndex) + (Below(args) ? 1 : 0);
					order.Insert(at, dragged);
					draggedColumn = null;
					if (!order.SequenceEqual(ColumnOrder))
						ChangeColumns(() => Save(options => options with { ColumnOrder = CustomizationColumnOrder.Serialize(order) }));
				};
				rows[index] = row;
				boxes[index] = box;
			}
			section.Items.Add(SectionWidthCard(new SettingsCard
			{
				Header = Strings.Columns.GetLocalizedResource(),
				Description = Strings.CustomizationColumnsOrderNotes.GetLocalizedResource(),
				ContentAlignment = CommunityToolkit.WinUI.Controls.ContentAlignment.Vertical,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Content = list,
				HorizontalAlignment = HorizontalAlignment.Stretch
			}));
			updateControls.Add(() =>
			{
				var layout = LayoutSettings;
				var editable = Editable();
				var order = ColumnOrder;
				if (!list.Children.Select(row => Array.IndexOf(rows, row)).SequenceEqual(order))
				{
					list.Children.Clear();
					foreach (var index in order) list.Children.Add(rows[index]);
				}
				for (int index = 0; index < DefaultColumns.Length; index++)
				{
					boxes[index].IsChecked = DefaultColumns[index].Shown(layout);
					boxes[index].IsEnabled = editable && DefaultColumns[index].SetShown is not null;
					rows[index].CanDrag = editable;
				}
				list.Opacity = editable ? 1 : 0.5;
			});

			updateControls.Add(() =>
			{
				var editable = Editable();
				var current = CaptureColumns();
				reset.IsEnabled = editable && !resetConfirmationOpen && current != FactoryColumns;
				undo.IsEnabled = editable && columnHistory.CanUndo(current);
				redo.IsEnabled = editable && columnHistory.CanRedo(current);
			});
		}
		// A brush that follows Files' theme for a property set in code (see CustomizationThemeBrushes).
		private T Themed<T>(T target, DependencyProperty property, string key) where T : DependencyObject
			=> CustomizationThemeBrushes.Bind(SettingsHost, target, property, key);
		private static Files.App.ViewModels.UserControls.SidebarViewModel SidebarOwner => Ioc.Default.GetRequiredService<Files.App.ViewModels.UserControls.SidebarViewModel>();
		private void SettingsChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName is nameof(IAppearanceSettingsService.CustomizationPreferences) or nameof(IAppearanceSettingsService.SidebarCustomization))
				DispatcherQueue.TryEnqueue(RefreshControls);
		}
		// The sidebar raises this after every layout change, including section visibility, so Reset to default stays current.
		private void SidebarChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName == nameof(Files.App.ViewModels.UserControls.SidebarViewModel.SettingsFooterVisibility))
				DispatcherQueue.TryEnqueue(RefreshControls);
		}
		private void RefreshControls()
		{
			if (updatingControls) return;
			updatingControls = true;
			try { foreach (var update in updateControls) update(); }
			finally { updatingControls = false; }
		}
		private void Save(Func<CustomizationPreferences, CustomizationPreferences> update)
		{
			if (updatingControls) return;
			var before = Options;
			var after = update(before);
			Settings.CustomizationPreferences = after.Serialize();
			SwitchLinkedSettings(before.ColumnsEnabled, after.ColumnsEnabled);
			RefreshControls();
		}

		// Column details changes app settings (Settings > Layout): fitting to content, syncing folder preferences and the default
		// layout's columns. Switching it off gives them back the values they had when it was switched on, and switching it on again
		// brings back its own values. Saved as "U" and the user's values while it is on, "F" and its own values while it is off.
		private static void SwitchLinkedSettings(bool wasOn, bool isOn)
		{
			if (wasOn == isOn) return;
			var layout = LayoutSettings;
			var linked = DefaultColumns.Where(column => column.SetShown is not null).Select(column => (Get: column.Shown, Set: column.SetShown!))
				.Prepend((s => s.SyncFolderPreferencesAcrossDirectories, (s, value) => s.SyncFolderPreferencesAcrossDirectories = value))
				.Prepend((s => s.AutoSizeColumnsInDetailsLayout, (s, value) => s.AutoSizeColumnsInDetailsLayout = value))
				.ToArray();
			var current = string.Concat(linked.Select(setting => setting.Get(layout) ? '1' : '0'));
			var store = CustomizationSettingsStore.Instance;
			var hasSaved = store.TryRead(CustomizationSettingsStore.LinkedAppSettingsKey, out var saved) && saved.Length == linked.Length + 1;
			if (hasSaved && saved[0] == (isOn ? 'F' : 'U'))
				for (int index = 0; index < linked.Length; index++)
					if (linked[index].Get(layout) != (saved[index + 1] == '1')) linked[index].Set(layout, saved[index + 1] == '1');
			if (isOn || hasSaved && saved[0] == 'U')
				store.Write(CustomizationSettingsStore.LinkedAppSettingsKey, (isOn ? "U" : "F") + current);
		}
		private bool Enabled(Func<CustomizationPreferences, bool> flag) => flag(Options);
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private void BuildSettings()
		{
			SettingsHost.Children.Add(new TextBlock
			{
				Text = Strings.Customization.GetLocalizedResource(),
				Style = (Style)Application.Current.Resources["App.Theme.SubtitleTextBlockStyle"],
				Padding = new Thickness(0, 0, 0, 12)
			});

			var shortcuts = Section(Strings.CustomizationShortcutArrowsHeading, "App.ThemedIcons.Shortcut");
			HeaderSwitch(shortcuts, options => options.ShortcutBadgesEnabled, (options, value) => options with
			{
				ShortcutBadgesEnabled = value,
				ShortcutArrowMode = value ? Math.Clamp(options.ShortcutArrowMode, 1, 2) : options.ShortcutArrowMode
			});
			shortcuts.Description = Strings.CustomizationShortcutArrowsNotes.GetLocalizedResource();
			// Desktop only can include the folders inside the Desktop; hiding badges everywhere includes them anyway.
			var desktopFolders = new CheckBox { MinWidth = 0 };
			AutomationProperties.SetName(desktopFolders, Strings.CustomizationShortcutArrowsDesktopFolders.GetLocalizedResource());
			desktopFolders.Click += (_, _) => Save(options => options with { ShortcutArrowDesktopSubfolders = desktopFolders.IsChecked == true });
			var suggestions = new CheckBox { MinWidth = 0 };
			AutomationProperties.SetName(suggestions, Strings.CustomizationShortcutArrowsSuggestions.GetLocalizedResource());
			suggestions.Click += (_, _) => Save(options => options with { ShortcutArrowSearchSuggestions = suggestions.IsChecked == true });
			CheckBox ShortcutOptionRow(string resource, CheckBox control)
			{
				control.Content = new TextBlock { Text = resource.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap };
				control.MinHeight = 32;
				control.VerticalAlignment = VerticalAlignment.Center;
				control.VerticalContentAlignment = VerticalAlignment.Center;
				control.HorizontalAlignment = HorizontalAlignment.Stretch;
				control.Padding = new Thickness(8, 0, 0, 0);
				return control;
			}
			var shortcutOptionsFlyout = new Flyout
			{
				Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
				Content = new StackPanel
				{
					Width = 360,
					Spacing = 8,
					Children =
					{
						ShortcutOptionRow(Strings.CustomizationShortcutArrowsDesktopFolders, desktopFolders),
						ShortcutOptionRow(Strings.CustomizationShortcutArrowsSuggestions, suggestions)
					}
				}
			};
			var shortcutOptions = new Button
			{
				Content = new FontIcon { Glyph = "\uE713", FontSize = 16 },
				Width = 32,
				Height = 32,
				Padding = new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center
			};
			shortcutOptions.Click += (_, _) =>
			{
				var rightEdge = shortcuts.TransformToVisual(shortcutOptions)
					.TransformPoint(new Windows.Foundation.Point(shortcuts.ActualWidth, 0)).X;
				shortcutOptionsFlyout.ShowAt(shortcutOptions, new FlyoutShowOptions
				{
					Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
					Position = new Windows.Foundation.Point(rightEdge, shortcutOptions.ActualHeight),
					ExclusionRect = new Windows.Foundation.Rect(0, 0, rightEdge, shortcutOptions.ActualHeight)
				});
			};
			AutomationProperties.SetName(shortcutOptions, Strings.Settings.GetLocalizedResource());
			ToolTipService.SetToolTip(shortcutOptions, Strings.Settings.GetLocalizedResource());
			Choice(shortcuts, Strings.CustomizationShortcutArrowVisibility, [Strings.CustomizationShortcutArrowsDesktop, Strings.CustomizationShortcutArrowsAll],
				() => Options.ShortcutArrowMode == 2 ? 1 : 0,
				value => Save(options => options with { ShortcutArrowMode = value + 1 }),
				() => Enabled(options => options.ShortcutBadgesEnabled), shortcutOptions);
			updateControls.Add(() =>
			{
				var enabled = Enabled(options => options.ShortcutBadgesEnabled);
				shortcutOptions.IsEnabled = enabled;
				var everywhere = Options.ShortcutArrowMode == 2;
				desktopFolders.IsChecked = everywhere || Options.ShortcutArrowDesktopSubfolders;
				desktopFolders.IsEnabled = enabled && !everywhere;
				suggestions.IsChecked = Options.ShortcutArrowSearchSuggestions;
				suggestions.IsEnabled = enabled;
			});

			var sidebar = Section(Strings.CustomizationSidebarHeading, "App.ThemedIcons.PanelLeft");
			HeaderSwitch(sidebar, options => options.SidebarEnabled, (options, value) => options with { SidebarEnabled = value });
			sidebar.Description = Strings.CustomizationSidebarEditorDescription.GetLocalizedResource();
			// The sidebar panel is the section's content and belongs to this page: it works while the page is shown and draws its
			// preview just after (so opening Settings isn't held up), so expanding the section just reveals it and animates like every other section.
			sidebarPanelHost.Child = new Files.App.ViewModels.UserControls.SidebarCustomizationPanel(SidebarOwner);
			sidebar.ItemsFooter = sidebarPanelHost;
			updateControls.Add(() =>
			{
				// While the Sidebar feature is off the panel is greyed out and read-only, like the other sections' settings,
				// because changes would not show in the sidebar.
				var featureOn = Enabled(options => options.SidebarEnabled);
				sidebarPanelHost.Opacity = featureOn ? 1 : 0.5;
				sidebarPanelHost.IsHitTestVisible = featureOn;
			});

			var columns = Section(Strings.CustomizationColumnsHeading, "App.ThemedIcons.Settings.General.EditColumns");
			HeaderSwitch(columns, options => options.ColumnsEnabled, (options, value) => options with { ColumnsEnabled = value });
			columns.Description = Strings.CustomizationColumnsMenuNotes.GetLocalizedResource();
			BuildColumnSettings(columns);

			FrameworkElement[] sections = [sidebar, columns, shortcuts];
			foreach (var section in sections) SettingsHost.Children.Remove(section);
			foreach (var section in sections) SettingsHost.Children.Add(section);
			RefreshControls();
		}

		private SettingsExpander Section(string resource, string icon)
		{
			var section = new SettingsExpander
			{
				Header = resource.GetLocalizedResource(),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HeaderIcon = SectionIcon(icon)
			};
			SettingsHost.Children.Add(section);
			return section;
		}
		[DynamicWindowsRuntimeCast(typeof(Geometry))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private static IconElement SectionIcon(string icon)
		{
			var style = (Style)Application.Current.Resources[icon];
			var data = style.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == ThemedIcon.OutlineIconDataProperty)?.Value as string;
			return string.IsNullOrEmpty(data)
				? new SymbolIcon(Symbol.Setting)
				: new PathIcon { Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data) };
		}
		private void HeaderSwitch(SettingsExpander section, Func<CustomizationPreferences, bool> flag,
			Func<CustomizationPreferences, bool, CustomizationPreferences> update)
		{
			var control = new ToggleSwitch();
			AutomationProperties.SetName(control, section.Header?.ToString() ?? "");
			control.Toggled += (_, _) => Save(options => update(options, control.IsOn));
			section.Content = control;
			updateControls.Add(() => { control.IsOn = Enabled(flag); });
		}
		private static SettingsCard Card(string resource, object content) => AlignWithCards(new()
		{
			Header = resource.GetLocalizedResource(),
			Content = content,
			HorizontalAlignment = HorizontalAlignment.Stretch
		});
		// Rows inside a section keep room for the section's arrow on the right; trimming it to a normal card's 16 px
		// lines their controls up with the section's own switch.
		private static SettingsCard AlignWithCards(SettingsCard card)
		{
			card.Loaded += (_, _) => card.Padding = card.Padding with { Right = 16 };
			return card;
		}
		// A checkbox whose text is centred on the box. The standard one sets its text 5 px down for wrapping text,
		// which leaves single-line text sitting high.
		private static CheckBox CenteredCheckBox(object content) => new()
		{
			Content = content,
			MinWidth = 0,
			Padding = new Thickness(8, 0, 0, 0),
			VerticalContentAlignment = VerticalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		// Column details' rows use the section's own 16 px edges, lining up with its icon on the left and its arrow on the right.
		private static SettingsCard SectionWidthCard(SettingsCard card)
		{
			card.Loaded += (_, _) => card.Padding = card.Padding with { Left = 16, Right = 16 };
			return card;
		}
		private void Choice(SettingsExpander section, string resource, string[] labels, Func<int> value, Action<int> save, Func<bool> enabled, FrameworkElement? trailingControl = null)
		{
			var control = new ComboBox { MinWidth = 180, MaxWidth = 280 };
			foreach (var label in labels) control.Items.Add(label.GetLocalizedResource());
			AutomationProperties.SetName(control, resource.GetLocalizedResource());
			control.SelectionChanged += (_, _) => { if (!updatingControls && control.SelectedIndex >= 0) save(control.SelectedIndex); };
			var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
			controls.Children.Add(control);
			if (trailingControl is not null) controls.Children.Add(trailingControl);
			section.Items.Add(Card(resource, controls));
			updateControls.Add(() => { control.SelectedIndex = value(); control.IsEnabled = enabled(); });
		}
		// A small confirmation under the Reset button instead of a full dialog.
		[DynamicWindowsRuntimeCast(typeof(Style))]
		private async Task<bool> ConfirmResetAsync(FrameworkElement anchor, string message)
		{
			if (resetConfirmationOpen) return false;
			resetConfirmationOpen = true;
			RefreshControls();
			try
			{
				var result = new TaskCompletionSource<bool>();
				var reset = new Button
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
				var buttons = new Grid { ColumnSpacing = 8 };
				buttons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				buttons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
				Grid.SetColumn(cancel, 1);
				buttons.Children.Add(reset);
				buttons.Children.Add(cancel);
				var flyout = new Flyout
				{
					Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
					Content = new StackPanel
					{
						Width = 280,
						Spacing = 16,
						Children =
						{
							new TextBlock { Text = message.GetLocalizedResource(), TextWrapping = TextWrapping.Wrap },
							buttons
						}
					}
				};
				reset.Click += (_, _) => { result.TrySetResult(true); flyout.Hide(); };
				cancel.Click += (_, _) => flyout.Hide();
				flyout.Opened += (_, _) => cancel.Focus(FocusState.Programmatic);
				flyout.Closed += (_, _) => result.TrySetResult(false);
				flyout.ShowAt(anchor);
				return await result.Task && IsLoaded;
			}
			finally
			{
				resetConfirmationOpen = false;
				RefreshControls();
			}
		}
	}
}
