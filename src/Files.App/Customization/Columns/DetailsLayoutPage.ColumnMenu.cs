// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Files.App.UserControls;
using Files.App.Customization.Columns;
using WinRT;

namespace Files.App.Views.Layouts
{
	public sealed partial class DetailsLayoutPage
	{
		private readonly List<PersistentColumnMenuItem> columnReorderItems = [];
		private MenuFlyout? columnCustomizationMenu;
		private MenuFlyoutPresenter? columnMenuPresenter;
		private MenuFlyoutPresenter? columnReorderPresenter;
		private UIElement? columnMenuPointerRoot;
		private DispatcherQueueTimer? columnMenuLeaveTimer;
		private PointerEventHandler? columnMenuPointerHandler;
		private KeyEventHandler? columnMenuKeyHandler;

		// While Column details is on, our commands are added to the page's own heading menu, next to 4.3's items, which keep working
		// as they do in 4.3. They are removed again when Column details is off.
		private MenuFlyout? columnMenu;
		private bool columnMenuCustomized;
		private string? originalFitContentText;
		private PersistentColumnMenuItem? ColumnFitWidthItem;
		private PersistentColumnMenuItem? ColumnRestoreDefaultItem;
		private MenuFlyoutSeparator? ColumnReorderSeparator;
		private MenuFlyoutSubItem? ColumnReorderMenu;
		private ControlTemplate? columnReorderItemTemplate;

		private ControlTemplate ColumnReorderItemTemplate
		{
			[DynamicWindowsRuntimeCast(typeof(ControlTemplate))]
			get => columnReorderItemTemplate ??= (ControlTemplate)new ResourceDictionary
			{
				Source = new Uri("ms-appx:///Customization/Columns/ColumnMenuResources.xaml")
			}["ColumnReorderItemTemplate"];
		}

		[DynamicWindowsRuntimeCast(typeof(MenuFlyout))]
		private void UpdateColumnHeaderMenu()
		{
			columnMenu ??= HeaderGrid.ContextFlyout as MenuFlyout;
			if (columnMenu is not { } menu || columnMenuCustomized == ColumnsActive) return;
			menu.Hide();
			if (ColumnsActive) AddColumnMenuItems(menu);
			else RemoveColumnMenuItems(menu);
			columnMenuCustomized = ColumnsActive;
		}

		private void AddColumnMenuItems(MenuFlyout menu)
		{
			if (ColumnFitWidthItem is null)
			{
				// One click fits this view's columns; how folders size them automatically is set in Settings.
				ColumnFitWidthItem = new() { Text = Strings.CustomizationColumnsFit.GetLocalizedResource() };
				ColumnFitWidthItem.Click += (_, _) => FitColumnsToWidthOnce();
				// The default layout is edited in Settings; here the view can only go back to it.
				ColumnRestoreDefaultItem = new() { Text = Strings.CustomizationColumnsRestoreDefault.GetLocalizedResource() };
				ColumnRestoreDefaultItem.Click += RestoreColumnDefaults_Click;
				ColumnReorderSeparator = new();
				ColumnReorderMenu = new() { Text = Strings.CustomizationColumnsReorder.GetLocalizedResource() };
			}
			// 4.3's "Size all columns to fit" stays, with its own handler, under the name the fitting commands share.
			originalFitContentText = AllColumnsToFitMenuflyoutItem.Text;
			AllColumnsToFitMenuflyoutItem.Text = Strings.CustomizationColumnsFitContent.GetLocalizedResource();
			menu.Items.Insert(menu.Items.IndexOf(AllColumnsToFitMenuflyoutItem) + 1, ColumnFitWidthItem);
			SetCurrentColumnsAsDefaultMenuFlyoutItem.Visibility = Visibility.Collapsed;
			menu.Items.Insert(menu.Items.IndexOf(SetCurrentColumnsAsDefaultMenuFlyoutItem) + 1, ColumnRestoreDefaultItem!);
			menu.Items.Add(ColumnReorderSeparator!);
			menu.Items.Add(ColumnReorderMenu!);
			menu.Opening += ColumnCustomizationMenu_Opening;
			menu.Opened += ColumnCustomizationMenu_Opened;
			menu.Closed += ColumnCustomizationMenu_Closed;
		}

		private void RemoveColumnMenuItems(MenuFlyout menu)
		{
			menu.Opening -= ColumnCustomizationMenu_Opening;
			menu.Opened -= ColumnCustomizationMenu_Opened;
			menu.Closed -= ColumnCustomizationMenu_Closed;
			ColumnMenuLayout.Reset(menu.Items);
			foreach (var item in new MenuFlyoutItemBase?[] { ColumnFitWidthItem, ColumnRestoreDefaultItem, ColumnReorderSeparator, ColumnReorderMenu })
				if (item is not null) menu.Items.Remove(item);
			if (originalFitContentText is not null) AllColumnsToFitMenuflyoutItem.Text = originalFitContentText;
			AllColumnsToFitMenuflyoutItem.IsEnabled = true;
			SetCurrentColumnsAsDefaultMenuFlyoutItem.Visibility = Visibility.Visible;
		}

		[DynamicWindowsRuntimeCast(typeof(MenuFlyout))]
		private void ColumnCustomizationMenu_Opening(object? sender, object args)
		{
			columnCustomizationMenu = sender as MenuFlyout;
			RefreshColumnMenuState(true);
			RefreshColumnReorderMenu();
			// Plain commands line their text up with the checkmark items (4.3's own items included, while Column details is on).
			if (sender is MenuFlyout menu) ColumnMenuLayout.Configure(menu.Items);
		}
		[DynamicWindowsRuntimeCast(typeof(MenuFlyout))]
		[DynamicWindowsRuntimeCast(typeof(UIElement))]
		private void ColumnCustomizationMenu_Opened(object? sender, object args)
		{
			ClearColumnMenuHover();
			if (sender is not MenuFlyout menu ||
				menu.Items.FirstOrDefault()?.FindAscendant<MenuFlyoutPresenter>() is not { } presenter) return;
			columnMenuPointerHandler ??= ColumnMenu_PointerChanged;
			columnMenuKeyHandler ??= ColumnMenu_KeyDown;
			columnMenuPresenter = presenter;
			AttachColumnMenuHover(presenter);
			if (columnReorderItems.FirstOrDefault(row => row.IsLoaded) is { } loadedRow) ColumnReorderItem_Loaded(loadedRow, new RoutedEventArgs());
			if (XamlRoot?.Content is UIElement root)
			{
				columnMenuPointerRoot = root;
				root.AddHandler(UIElement.PointerMovedEvent, columnMenuPointerHandler, true);
			}
			Unloaded += ColumnMenu_Unloaded;
		}
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private void ColumnReorderItem_Loaded(object sender, RoutedEventArgs args)
		{
			if (columnMenuPresenter is null || sender is not FrameworkElement item ||
				item.FindAscendant<MenuFlyoutPresenter>() is not { } presenter || presenter == columnReorderPresenter) return;
			DetachColumnMenuHover(columnReorderPresenter);
			columnReorderPresenter = presenter;
			AttachColumnMenuHover(presenter);
		}
		private void AttachColumnMenuHover(MenuFlyoutPresenter presenter)
		{
			presenter.AddHandler(UIElement.PointerEnteredEvent, columnMenuPointerHandler!, true);
			presenter.AddHandler(UIElement.PointerMovedEvent, columnMenuPointerHandler!, true);
			presenter.AddHandler(UIElement.PointerExitedEvent, columnMenuPointerHandler!, true);
			presenter.AddHandler(UIElement.KeyDownEvent, columnMenuKeyHandler!, true);
		}
		private void DetachColumnMenuHover(MenuFlyoutPresenter? presenter)
		{
			if (presenter is null) return;
			presenter.RemoveHandler(UIElement.PointerEnteredEvent, columnMenuPointerHandler!);
			presenter.RemoveHandler(UIElement.PointerMovedEvent, columnMenuPointerHandler!);
			presenter.RemoveHandler(UIElement.PointerExitedEvent, columnMenuPointerHandler!);
			presenter.RemoveHandler(UIElement.KeyDownEvent, columnMenuKeyHandler!);
		}
		private void ColumnMenu_PointerChanged(object sender, PointerRoutedEventArgs args)
		{
			if (columnCustomizationMenu?.IsOpen != true || args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
			if (ColumnMenuContainsPointer(columnMenuPresenter, args) || ColumnMenuContainsPointer(columnReorderPresenter, args))
			{
				columnMenuLeaveTimer?.Stop();
				return;
			}
			if (columnMenuLeaveTimer is null)
			{
				columnMenuLeaveTimer = DispatcherQueue.CreateTimer();
				// Allow crossing the gap between the parent menu and its submenu.
				columnMenuLeaveTimer.Interval = TimeSpan.FromMilliseconds(300);
				columnMenuLeaveTimer.IsRepeating = false;
				columnMenuLeaveTimer.Tick += ColumnMenuLeaveTimer_Tick;
			}
			if (!columnMenuLeaveTimer.IsRunning) columnMenuLeaveTimer.Start();
		}
		private static bool ColumnMenuContainsPointer(MenuFlyoutPresenter? presenter, PointerRoutedEventArgs args)
		{
			if (presenter?.IsLoaded != true) return false;
			var point = args.GetCurrentPoint(presenter).Position;
			return point.X >= 0 && point.Y >= 0 && point.X < presenter.ActualWidth && point.Y < presenter.ActualHeight;
		}
		private void ColumnMenu_KeyDown(object sender, KeyRoutedEventArgs args) => columnMenuLeaveTimer?.Stop();
		private void ColumnMenuLeaveTimer_Tick(DispatcherQueueTimer sender, object args)
		{
			if (ReferenceEquals(sender, columnMenuLeaveTimer)) columnCustomizationMenu?.Hide();
		}
		private void ColumnCustomizationMenu_Closed(object? sender, object args) => ClearColumnMenuHover();
		private void ColumnMenu_Unloaded(object sender, RoutedEventArgs args)
		{
			columnCustomizationMenu?.Hide();
			ClearColumnMenuHover();
		}
		private void ClearColumnMenuHover()
		{
			if (columnMenuLeaveTimer is { } timer)
			{
				timer.Stop();
				timer.Tick -= ColumnMenuLeaveTimer_Tick;
				columnMenuLeaveTimer = null;
			}
			DetachColumnMenuHover(columnMenuPresenter);
			DetachColumnMenuHover(columnReorderPresenter);
			columnMenuPresenter = columnReorderPresenter = null;
			if (columnMenuPointerRoot is { } root)
			{
				root.RemoveHandler(UIElement.PointerMovedEvent, columnMenuPointerHandler!);
				columnMenuPointerRoot = null;
			}
			Unloaded -= ColumnMenu_Unloaded;
		}
		private void RefreshColumnMenuState(bool opening = false)
		{
			if (!columnMenuCustomized || !opening && columnCustomizationMenu?.IsOpen != true) return;
			// Greyed out while the columns already fit that way, or the view already shows the default layout.
			AllColumnsToFitMenuflyoutItem.IsEnabled = !ColumnsMatchContentFit();
			ColumnFitWidthItem!.IsEnabled = !ColumnsMatchWidthFit();
			// With the app's default sizing it restores only the column order (see RestoreColumnDefaults_Click).
			ColumnRestoreDefaultItem!.Text = (UsesAppSizing ? Strings.CustomizationColumnsRestoreOrder : Strings.CustomizationColumnsRestoreDefault).GetLocalizedResource();
			ColumnRestoreDefaultItem.IsEnabled = UsesAppSizing ? !ColumnOrderMatchesSaved() : !ColumnsMatchSavedDefaults();
		}
		// Restore to default: this view goes back to the default columns, widths and order from Settings, fitted the default way, and the
		// folder follows the default columns from then on (see ForgetFolderColumnLayout).
		private void RestoreColumnDefaults_Click(object sender, RoutedEventArgs args)
		{
			// With the app's default sizing the columns and widths are the folder's own, as in 4.3; only the order is restored.
			if (UsesAppSizing)
			{
				columnOrderOverride = null;
				ApplyCustomColumnOrder();
				RefreshColumnReorderMenu();
				RefreshColumnMenuState();
				return;
			}
			ApplyDefaultColumns();
			columnOrderOverride = null;
			// The folder no longer keeps its own widths, so automatic fitting applies to it again.
			SetKeepsOwnColumnWidths(false);
			fitColumnsToWidthOverride = null;
			manualColumnResize = viewWidthsChanged = false;
			customColumnWidths.Clear();
			fittedColumnLayout = null;
			lastCustomFitWidth = -1;
			customFitVerificationRemaining = 0;
			ApplyCustomColumnOrder();
			// Saves this folder's columns and updates the view, as showing or hiding a column does; that also fits the columns to the
			// width, in one step once the restored columns are laid out.
			ToggleMenuFlyoutItem_Click(sender, args);
			ForgetFolderColumnLayout();
			if (!AutoFitToWidth) AutoFitColumnsIfEnabled();
		}
		private void RefreshColumnReorderMenu()
		{
			if (!columnMenuCustomized || ColumnReorderMenu is null) return;
			var order = CustomColumnOrder.Parse(CustomOptions.ColumnOrder);
			var available = order.Where(index => !CustomColumns[index].IsHidden && !CustomColumns[index].UserCollapsed)
				.Select(index => (Index: index, Label: (originalHeaderPositions
					.FirstOrDefault(pair => pair.Key is DataGridHeader && pair.Value == (index + 1) * 2).Key as DataGridHeader)?.Header as string))
				.Where(column => column.Label is not null).ToArray();
			bool addedRows = false;
			for (int position = 0; position < available.Length; position++)
			{
				if (position == columnReorderItems.Count)
				{
					var item = new PersistentColumnMenuItem
					{
						DataContext = new ColumnReorderMenuRow(this),
						Template = ColumnReorderItemTemplate
					};
					// The rows tell the menu where the submenu is, so pointer tracking covers it too.
					item.Loaded += ColumnReorderItem_Loaded;
					columnReorderItems.Add(item);
					ColumnReorderMenu.Items.Insert(position, item);
					addedRows = true;
				}
				var menuItem = columnReorderItems[position];
				var column = available[position];
				menuItem.Text = column.Label!;
				menuItem.Visibility = Visibility.Visible;
				((ColumnReorderMenuRow)menuItem.DataContext).UpdateColumn(column.Index, column.Label!, position, available.Length);
			}
			for (int position = available.Length; position < columnReorderItems.Count; position++)
			{
				columnReorderItems[position].Visibility = Visibility.Collapsed;
				((ColumnReorderMenuRow)columnReorderItems[position].DataContext).UpdateColumn(-1, "", -1, 0);
			}
			if (addedRows) ColumnMenuLayout.Configure(ColumnReorderMenu.Items);
		}
		// The Move up and Move down buttons bind to these in ColumnMenuResources.xaml.
		[WinRT.GeneratedBindableCustomProperty([nameof(MoveUp), nameof(MoveDown), nameof(MoveUpName), nameof(MoveDownName)], [])]
		public sealed partial class ColumnReorderMenuRow : ObservableObject
		{
			private bool canMoveUp;
			private bool canMoveDown;
			private int columnIndex = -1;
			private string moveUpName = "";
			private string moveDownName = "";
			public RelayCommand MoveUp { get; }
			public RelayCommand MoveDown { get; }
			public string MoveUpName => moveUpName;
			public string MoveDownName => moveDownName;
			internal ColumnReorderMenuRow(DetailsLayoutPage owner)
			{
				MoveUp = new(() => owner.QueueColumnMove(columnIndex, -1), () => canMoveUp);
				MoveDown = new(() => owner.QueueColumnMove(columnIndex, 1), () => canMoveDown);
			}
			internal void UpdateColumn(int index, string label, int position, int count)
			{
				columnIndex = index;
				SetProperty(ref moveUpName, label + " · " + Strings.SidebarVisualMoveUp.GetLocalizedResource(), nameof(MoveUpName));
				SetProperty(ref moveDownName, label + " · " + Strings.SidebarVisualMoveDown.GetLocalizedResource(), nameof(MoveDownName));
				var moveUp = position > 0;
				var moveDown = position >= 0 && position < count - 1;
				if (canMoveUp != moveUp) { canMoveUp = moveUp; MoveUp.NotifyCanExecuteChanged(); }
				if (canMoveDown != moveDown) { canMoveDown = moveDown; MoveDown.NotifyCanExecuteChanged(); }
			}
		}
		// The view shows the default layout: default columns and order, and default widths unless automatic fitting sized them.
		private bool ColumnsMatchSavedDefaults()
		{
			if (fitColumnsToWidthOverride is not null || !ColumnOrderMatchesSaved()) return false;
			var fittedAutomatically = (CustomOptions.FitColumnsToWidth || AutoSizeToContent) && !viewWidthsChanged;
			var defaults = ColumnsOf(new LayoutPreferencesItem().ColumnsViewModel);
			for (int index = 0; index < CustomColumns.Length; index++)
			{
				var column = CustomColumns[index];
				if (column.IsHidden) continue;
				if (column.UserCollapsed != defaults[index].UserCollapsed) return false;
				if (!column.UserCollapsed && !fittedAutomatically && Math.Abs(column.UserLengthPixels - defaults[index].UserLengthPixels) > 0.5) return false;
			}
			return true;
		}
		// The view shows the default column order from Settings.
		private bool ColumnOrderMatchesSaved()
			=> CustomColumnOrder.Parse(CustomOptions.ColumnOrder).SequenceEqual(CustomColumnOrder.Parse(
				CustomizationPreferences.Parse(UserSettingsService.AppearanceSettingsService.CustomizationPreferences).ColumnOrder));
		private void QueueColumnMove(int index, int direction)
		{
			// Finish the button's input event before changing its command or enabled state.
			DispatcherQueue.TryEnqueue(() => MoveCustomColumn(index, direction));
		}
		private void MoveCustomColumn(int index, int direction)
		{
			if (!customColumnsReady || !CustomOptions.ColumnsEnabled || !CustomOptions.CustomizationEnabled ||
				(uint)index >= (uint)CustomColumns.Length || direction is not (-1 or 1)) return;
			// Reordering changes this view only, like resizing; Restore to default undoes it.
			var order = CustomColumnOrder.MoveShown(CustomColumnOrder.Parse(CustomOptions.ColumnOrder), index, direction,
				column => !CustomColumns[column].IsHidden && !CustomColumns[column].UserCollapsed);
			columnOrderOverride = CustomColumnOrder.Serialize(order);
			ApplyCustomColumnOrder();
			RefreshColumnReorderMenu();
			RefreshColumnMenuState();
		}
	}
}
