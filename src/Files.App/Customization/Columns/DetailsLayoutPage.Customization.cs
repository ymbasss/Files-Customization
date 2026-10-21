// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;


namespace Files.App.Views.Layouts
{
	public sealed partial class DetailsLayoutPage
	{
		private bool customColumnsReady;
		private readonly Dictionary<DetailsLayoutColumnItem, double> customColumnWidths = new(ReferenceEqualityComparer.Instance);
		private bool applyingCustomColumnFit;
		private bool manualColumnResize;
		private ScrollViewer? customFitScroller;
		private double customFitChromeAdjustment;
		private int customFitVerificationRemaining;
		private double lastCustomFitWidth = -1;
		// False after a manual resize or a fitting command in this view: the view stops following automatic fitting to the width.
		private bool? fitColumnsToWidthOverride;
		// The view's widths were changed here (a resize or a fitting command), so they no longer follow the default.
		private bool viewWidthsChanged;
		// Each shown column's content width; fitting to the width starts from these and shares the rest of the view equally.
		private Dictionary<DetailsLayoutColumnItem, double>? contentColumnWidths;
		private DispatcherQueueTimer? contentFitTimer;
		private DispatcherQueueTimer? autoFitSaveTimer;
		// The content was measured for the current columns and items. Until then automatic fitting waits, so the columns change once.
		private bool contentMeasured;
		// The folder this view shows, as 4.3 keys its saved layouts.
		private string? columnsFolderPath;
		private string? columnsFolderId;

		// The default layout's columns (shown or not, and widths unless the view keeps its own) from Settings.
		private void ApplyDefaultColumns(bool widths = true)
		{
			var defaults = ColumnsOf(new LayoutPreferencesItem().ColumnsViewModel);
			for (int index = 0; index < CustomColumns.Length; index++)
			{
				CustomColumns[index].UserCollapsed = defaults[index].UserCollapsed;
				if (widths) CustomColumns[index].UserLengthPixels = defaults[index].UserLengthPixels;
			}
		}

		// Restore to default: a folder's own layout is removed when it holds nothing but columns, so the folder follows the default
		// layout from then on, also after it changes. A layout that also keeps its own sort, group or view mode stays; that folder
		// is listed instead and gets the default columns each time it opens, then fits them automatically if set.
		private void ForgetFolderColumnLayout()
		{
			if (columnsFolderPath is not { } path || path.StartsWith("tag:", StringComparison.Ordinal) ||
				UserSettingsService.LayoutSettingsService.SyncFolderPreferencesAcrossDirectories || FolderSettings?.LayoutPreferencesItem is not { } current)
				return;
			var folderLayout = DefaultFolderLayout(path);
			folderLayout.ColumnsViewModel = current.ColumnsViewModel;
			// Automatic fitting saves the layout again, so that folder is listed instead.
			if (folderLayout.Equals(current) && !AutoFitToWidth && !AutoSizeToContent)
			{
				LayoutPreferencesManager.GetDatabaseManagerInstance().SetPreferences(path, Win32Helper.GetFolderFRN(path), null);
				SetFollowsDefaultColumns(false);
			}
			else SetFollowsDefaultColumns(true);
		}

		// The layout 4.3 gives a folder without one of its own (as LayoutPreferencesManager.GetDefaultLayoutPreferences does).
		private static LayoutPreferencesItem DefaultFolderLayout(string path)
		{
			if (path == Constants.UserEnvironmentPaths.DownloadsPath)
				return new() { DirectoryGroupOption = GroupOption.DateCreated, DirectoryGroupDirection = SortDirection.Descending, DirectoryGroupByDateUnit = GroupByDateUnit.Year };
			if (LibraryManager.IsLibraryPath(path))
				return new() { DirectoryGroupOption = GroupOption.FolderPath };
			return new();
		}

		// Folder lists in customization.json, matched like 4.3 finds a folder's layout: by path, or by ID after the folder was renamed
		// or moved. One holds folders that follow the default columns, the other folders that keep their own column widths.
		private sealed record ListedColumnFolder(string? Id, string Path);
		private static List<ListedColumnFolder> ReadColumnFolders(string key)
		{
			if (!CustomizationSettingsStore.Instance.TryRead(key, out var value)) return [];
			return value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
			{
				var split = line.IndexOf('|');
				// An ID saved without its drive (a plain number) isn't used; that folder is found by its path and gets a full ID.
				return split < 0 ? new ListedColumnFolder(null, line) :
					new ListedColumnFolder(line[..split].Contains(':') ? line[..split] : null, line[(split + 1)..]);
			}).ToList();
		}
		private static void WriteColumnFolders(string key, List<ListedColumnFolder> folders)
			=> CustomizationSettingsStore.Instance.Write(key, string.Join('\n', folders.Select(folder => folder.Id + "|" + folder.Path)));
		private bool IsThisFolder(ListedColumnFolder folder)
			=> string.Equals(folder.Path, columnsFolderPath, StringComparison.OrdinalIgnoreCase) || columnsFolderId is not null && folder.Id == columnsFolderId;

		// A file ID is only unique on its own drive (every NTFS drive's root folder has the same one), so the ID also holds the
		// drive's serial number.
		private static string? ColumnFolderId(string path)
		{
			try
			{
				return global::Windows.Win32.PInvoke.GetVolumeInformation(SystemIO.Path.GetPathRoot(path) ?? path, [], out var serial, out _, out _, []) &&
					Win32Helper.GetFolderFRN(path) is { } frn ? serial.ToString("X8") + ":" + frn : null;
			}
			catch (Exception) { return null; }
		}

		// Whether this folder is listed; a folder found by its FRN under a new path keeps its place under that path.
		private bool IsColumnFolderListed(string key)
		{
			if (columnsFolderPath is not { } path) return false;
			var folders = ReadColumnFolders(key);
			if (folders.Count == 0) return false;
			columnsFolderId ??= ColumnFolderId(path);
			var index = folders.FindIndex(IsThisFolder);
			if (index < 0) return false;
			if (!string.Equals(folders[index].Path, path, StringComparison.OrdinalIgnoreCase) || folders[index].Id != columnsFolderId)
			{
				folders[index] = new(columnsFolderId, path);
				WriteColumnFolders(key, folders);
			}
			return true;
		}

		private void SetColumnFolderListed(string key, bool listed)
		{
			if (columnsFolderPath is not { } path) return;
			var folders = ReadColumnFolders(key);
			if (!listed && folders.Count == 0) return;
			columnsFolderId ??= ColumnFolderId(path);
			var removed = folders.RemoveAll(IsThisFolder) > 0;
			if (listed) folders.Add(new(columnsFolderId, path));
			if (listed || removed) WriteColumnFolders(key, folders);
		}

		private bool FollowsDefaultColumns() => IsColumnFolderListed(CustomizationSettingsStore.ColumnDefaultFoldersKey);
		// Changing a listed folder's columns gives it its own columns again.
		private void SetFollowsDefaultColumns(bool follows) => SetColumnFolderListed(CustomizationSettingsStore.ColumnDefaultFoldersKey, follows);

		// A folder whose columns were fitted or resized here keeps those widths: automatic fitting leaves it alone until Restore to
		// default. With "Sync layout and sorting preferences across directories" on, folders have no widths of their own.
		private bool KeepsOwnColumnWidths()
			=> !UserSettingsService.LayoutSettingsService.SyncFolderPreferencesAcrossDirectories && IsColumnFolderListed(CustomizationSettingsStore.ColumnOwnWidthFoldersKey);
		private void SetKeepsOwnColumnWidths(bool keeps)
		{
			// With the app's default sizing nothing is recorded, as in 4.3.
			if (keeps && UsesAppSizing) return;
			ownColumnWidths = keeps && !UserSettingsService.LayoutSettingsService.SyncFolderPreferencesAcrossDirectories;
			SetColumnFolderListed(CustomizationSettingsStore.ColumnOwnWidthFoldersKey, ownColumnWidths);
		}

		// After a column is shown or hidden here.
		private void CustomColumnsShownChanged()
		{
			if (!ColumnsActive) return;
			SetFollowsDefaultColumns(false);
			QueueCustomColumnFit(true);
			RefreshColumnMenuState();
			RefreshColumnReorderMenu();
		}


		// The default "Automatically fit columns to content" (the app's own setting); fitting to the width turns it off.
		private bool AutoSizeToContent => !ownColumnWidths && !CustomOptions.FitColumnsToWidth && UserSettingsService.LayoutSettingsService.AutoSizeColumnsInDetailsLayout;
		// This folder keeps its own column widths (see KeepsOwnColumnWidths).
		private bool ownColumnWidths;
		// Automatically fit columns to width (also while the window is resized) for this view.
		private bool AutoFitToWidth => CustomOptions.FitColumnsToWidth;
		// Column sizing is "App's default": no automatic fitting, and folders show their saved layouts as in 4.3.
		// Read from Settings, not CustomOptions, which turns fitting off for a folder that keeps its own widths.
		private bool UsesAppSizing => !CurrentOptions.FitColumnsToWidth &&
			!UserSettingsService.LayoutSettingsService.AutoSizeColumnsInDetailsLayout;

		// Whether this view sizes columns to their content after loading; null while Column details is off (the original setting decides).
		private bool? CustomAutoSizeToContent => ColumnsActive ? AutoSizeToContent : null;

		// "Fit all columns to width" in the menu: fits the columns once and saves them to this folder's layout, like "Fit all columns
		// to content"; how folders are sized automatically doesn't change.
		private void FitColumnsToWidthOnce()
		{
			if (!ColumnsActive) return;
			FitColumnsToWidthFromContent();
			viewWidthsChanged = true;
			// The folder now has its own column widths, which automatic fitting leaves alone.
			SetFollowsDefaultColumns(false);
			SetKeepsOwnColumnWidths(true);
			fitColumnsToWidthOverride = false;
			if (FolderSettings is { } folderSettings) folderSettings.ColumnsViewModel = ColumnsViewModel;
			RefreshColumnMenuState();
		}
		// Fits the columns to their content, then shares the rest of the view equally between them; also used by Restore to default.
		private void FitColumnsToWidthFromContent()
		{
			manualColumnResize = false;
			customColumnWidths.Clear();
			fittedColumnLayout = null;
			MeasureContentColumnWidths();
			FitColumnsToWidth(RootGrid.ActualWidth);
			if (!AutoFitToWidth) customColumnWidths.Clear();
		}
		// This view's column order after Reorder columns; it stays for the tab until Restore to default.
		private string? columnOrderOverride;
		// Column details is on. While it's off the view runs the original code only: the original heading menu, original
		// column order and widths, and none of the handlers below.
		private bool ColumnsActive => CurrentOptions.ColumnsEnabled;
		private CustomizationPreferences CurrentOptions => CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences);
		private CustomizationPreferences CustomOptions
		{
			get
			{
				var options = CurrentOptions;
				// Also drops this view's overrides: switching off restores the original order before they are cleared.
				if (!options.ColumnsEnabled)
					return options;
				var fit = fitColumnsToWidthOverride ?? options.FitColumnsToWidth;
				return options with { FitColumnsToWidth = fit, ColumnOrder = columnOrderOverride ?? options.ColumnOrder };
			}
		}
		private DetailsLayoutColumnItem[]? customColumns;
		private DetailsLayoutColumnItem[] CustomColumns => customColumns ??= ColumnsOf(ColumnsViewModel);
		// A layout's columns, in CustomizationColumnOrder.Keys order.
		private static DetailsLayoutColumnItem[] ColumnsOf(ColumnsViewModel columns) =>
		[
			columns.NameColumn, columns.GitStatusColumn, columns.GitLastCommitDateColumn,
			columns.GitLastCommitMessageColumn, columns.GitCommitAuthorColumn, columns.GitLastCommitShaColumn,
			columns.TagColumn, columns.PathColumn, columns.OriginalPathColumn, columns.DateDeletedColumn,
			columns.DateModifiedColumn, columns.DateCreatedColumn, columns.ItemTypeColumn,
			columns.SizeColumn, columns.StatusColumn
		];

		private sealed record FittedColumnLayout(double Width, double Scale, int[] Columns, double[] Preferred, double[] Minimum, double[] Maximum, double[] Sizes);
		private FittedColumnLayout? fittedColumnLayout;

		// The width the shown columns share: the view less the icon column, the splitters and the row's fixed parts.
		private double AvailableColumnWidth(double viewport, int splitters, double scale)
			=> Math.Max(0, viewport - ColumnsViewModel.IconColumn.Length.Value - splitters - 52 - customFitChromeAdjustment - 1 / scale);

		// Whether the columns already have the widths "Fit all columns to width" would give them now, whether this view fitted them or
		// they were saved that way.
		private bool ColumnsMatchWidthFit()
		{
			var indices = Enumerable.Range(0, CustomColumns.Length).Where(index => CustomColumns[index].Visibility == Visibility.Visible).ToArray();
			if (manualColumnResize || indices.Length == 0 || ContentScroller is { ScrollableWidth: > 0.5 }) return false;
			var scale = XamlRoot?.RasterizationScale ?? 1;
			var viewport = ContentScroller is { ViewportWidth: > 0 } scroller ? scroller.ViewportWidth : RootGrid.ActualWidth;
			if (!double.IsFinite(viewport) || viewport <= 0) return false;
			var available = AvailableColumnWidth(viewport, indices.Count(index => CustomColumns[index].IsResizable) * 12, scale);
			var hasItems = FileList.Items.Any();
			var preferred = indices.Select(index => hasItems ? ContentColumnWidth(index) : CustomColumns[index].UserLengthPixels).ToArray();
			var minimum = indices.Select(index => CustomColumns[index].NormalMinLength).ToArray();
			var sizes = ColumnWidthFitter.Fit(available, preferred, minimum, indices.Select(index => CustomColumns[index].NormalMaxLength).ToArray());
			for (int position = 0; position < indices.Length; position++)
				if (Math.Abs(CustomColumns[indices[position]].UserLengthPixels - Math.Max(minimum[position], Math.Floor(sizes[position] * scale) / scale)) > 1)
					return false;
			return true;
		}

		// Whether every shown column is as wide as "Fit all columns to content" makes it, also in a view that opened that way (its
		// saved widths). That command leaves a column with nothing to show as it is, so any width counts for it.
		private bool ColumnsMatchContentFit()
		{
			if (manualColumnResize || !FileList.Items.Any()) return false;
			var shown = false;
			for (int index = 0; index < CustomColumns.Length; index++)
			{
				if (CustomColumns[index].Visibility != Visibility.Visible) continue;
				shown = true;
				if (MeasuredColumnWidth(index) is { } width && Math.Abs(CustomColumns[index].UserLengthPixels - width) > 0.5) return false;
			}
			return shown;
		}

		protected override Size MeasureOverride(Size availableSize)
		{
			if (customColumnsReady && contentMeasured && !manualColumnResize && CustomOptions.FitColumnsToWidth && double.IsFinite(availableSize.Width) && availableSize.Width > 0)
			{
				FitColumnsToWidth(availableSize.Width);
			}
			return base.MeasureOverride(availableSize);
		}

		public void FitColumnsToCurrentWidth()
		{
			if (!contentMeasured) MeasureContentColumnWidths();
			FitColumnsToWidth(RootGrid.ActualWidth);
		}

		// Measures each shown column's content the way "Fit all columns to content" does, without changing the columns or saving the
		// folder's layout. Without items the current widths are used.
		private void MeasureContentColumnWidths()
		{
			contentColumnWidths = null;
			contentMeasured = true;
			if (!FileList.Items.Any()) return;
			var widths = new Dictionary<DetailsLayoutColumnItem, double>(ReferenceEqualityComparer.Instance);
			for (int index = 0; index < CustomColumns.Length; index++)
				if (CustomColumns[index].Visibility == Visibility.Visible) widths[CustomColumns[index]] = ContentColumnWidth(index);
			contentColumnWidths = widths;
		}

		// The width ResizeColumnToFit gives a column (index in CustomColumns), measured the same way. A column with nothing to
		// show (such as Tag without tags) gets its minimum.
		private double ContentColumnWidth(int index) => MeasuredColumnWidth(index) ?? CustomColumns[index].NormalMinLength;

		// The same, or null for a column with nothing to show, which ResizeColumnToFit leaves as it is.
		private double? MeasuredColumnWidth(int index)
		{
			var column = CustomColumns[index];
			var columnToResize = index + 2;
			var items = FileList.Items.Cast<ListedItem>();
			var maxItemLength = columnToResize switch
			{
				2 => items.Select(x => x.Name?.Length ?? 0).Max(),
				4 => items.Select(x => (x as IGitItem)?.GitLastCommitDateHumanized?.Length ?? 0).Max(),
				5 => items.Select(x => (x as IGitItem)?.GitLastCommitMessage?.Length ?? 0).Max(),
				6 => items.Select(x => (x as IGitItem)?.GitLastCommitAuthor?.Length ?? 0).Max(),
				7 => items.Select(x => (x as IGitItem)?.GitLastCommitSha?.Length ?? 0).Max(),
				8 => items.Select(x => x.FileTagsUI?.Sum(tag => tag?.Name?.Length ?? 0) ?? 0).Max(),
				9 => items.Select(x => x.ItemPath?.Length ?? 0).Max(),
				10 => items.Select(x => (x as RecycleBinItem)?.ItemOriginalPath?.Length ?? 0).Max(),
				11 => items.Select(x => (x as RecycleBinItem)?.ItemDateDeleted?.Length ?? 0).Max(),
				12 => items.Select(x => x.ItemDateModified?.Length ?? 0).Max(),
				13 => items.Select(x => x.ItemDateCreated?.Length ?? 0).Max(),
				14 => items.Select(x => x.ItemType?.Length ?? 0).Max(),
				15 => items.Select(x => x.FileSize?.Length ?? 0).Max(),
				_ => 20
			};
			var width = maxItemLength == 0 ? 0 : MeasureColumnEstimate(columnToResize, 5, maxItemLength);
			if (width <= 1) return null;
			if (columnToResize == 2) width += 20;
			// 36 for the sort icon and padding, as in ResizeColumnToFit.
			return Math.Min(Math.Max(width, column.NormalMinLength) + 36, column.NormalMaxLength);
		}

		// Automatic fitting while Column details is on: to the width, or to the content. Both save the folder's layout like 4.3's own
		// automatic fit; a folder that follows the default columns stays listed.
		// From the original automatic fit: true when Column details is on and fits the columns itself.
		private bool QueueCustomAutoFit()
		{
			if (CustomAutoSizeToContent is not { } toContent) return false;
			QueueCustomAutoFit(toContent);
			return true;
		}

		private void CustomColumnsUnloaded()
		{
			DetachCustomColumnFitScroller();
			UserSettingsService.AppearanceSettingsService.PropertyChanged -= CustomizationSettingsChanged;
		}

		private void QueueCustomAutoFit(bool toContent)
		{
			if (!toContent)
			{
				QueueContentWidthFit();
				return;
			}
			contentFitTimer ??= DispatcherQueue.CreateTimer();
			contentFitTimer.Debounce(() =>
			{
				if (!customColumnsReady || !AutoSizeToContent) return;
				MeasureContentColumnWidths();
				if (contentColumnWidths is not { } widths) return;
				foreach (var (column, width) in widths) column.UserLengthPixels = width;
				if (FolderSettings is { } folderSettings) folderSettings.ColumnsViewModel = ColumnsViewModel;
				RefreshColumnMenuState();
			}, TimeSpan.FromMilliseconds(250));
		}

		// After items load or change, a view that fits to its width automatically starts again from the new content.
		private void QueueContentWidthFit()
		{
			if (!AutoFitToWidth || manualColumnResize) return;
			// The columns keep their widths while items keep arriving, then fit once.
			contentColumnWidths = null;
			contentMeasured = false;
			contentFitTimer ??= DispatcherQueue.CreateTimer();
			contentFitTimer.Debounce(() =>
			{
				if (!customColumnsReady || !AutoFitToWidth || manualColumnResize) return;
				FitColumnsToCurrentWidth();
			}, TimeSpan.FromMilliseconds(250));
		}

		private void FitColumnsToWidth(double viewWidth, bool verify = true)
		{
			if (applyingCustomColumnFit || manualColumnResize || !double.IsFinite(viewWidth) || viewWidth <= 0) return;
			var indices = Enumerable.Range(0, CustomColumns.Length).Where(index => CustomColumns[index].Visibility == Visibility.Visible).ToArray();
			var visible = indices.Select(index => CustomColumns[index]).ToArray();
			if (visible.Length == 0) return;
			foreach (var column in CustomColumns) customColumnWidths.TryAdd(column, column.UserLengthPixels);
			var scale = XamlRoot?.RasterizationScale ?? 1;
			var viewport = ContentScroller is { ViewportWidth: > 0 } scroller && Math.Abs(RootGrid.ActualWidth - viewWidth) < 1 ? scroller.ViewportWidth : viewWidth;
			var available = AvailableColumnWidth(viewport, visible.Count(column => column.IsResizable) * 12, scale);
			var preferred = visible.Select(column => contentColumnWidths is { } content && content.TryGetValue(column, out var width) ? width : customColumnWidths[column]).ToArray();
			var minimum = visible.Select(column => column.NormalMinLength).ToArray();
			var maximum = visible.Select(column => column.NormalMaxLength).ToArray();
			var cached = fittedColumnLayout;
			if (cached is null || cached.Width != available || cached.Scale != scale ||
				!cached.Columns.SequenceEqual(indices) || !cached.Preferred.SequenceEqual(preferred) ||
				!cached.Minimum.SequenceEqual(minimum) || !cached.Maximum.SequenceEqual(maximum))
			{
				var sizes = ColumnWidthFitter.Fit(available, preferred, minimum, maximum);
				for (int index = 0; index < sizes.Length; index++) sizes[index] = Math.Max(minimum[index], Math.Floor(sizes[index] * scale) / scale);
				fittedColumnLayout = cached = new(available, scale, indices, preferred, minimum, maximum, sizes);
			}
			var changed = lastCustomFitWidth != viewWidth;
			applyingCustomColumnFit = true;
			try
			{
				for (int i = 0; i < visible.Length; i++)
					if (Math.Abs(visible[i].UserLengthPixels - cached.Sizes[i]) > 0.001) { visible[i].UserLengthPixels = cached.Sizes[i]; changed = true; }
			}
			finally { applyingCustomColumnFit = false; }
			lastCustomFitWidth = viewWidth;
			if (verify && changed) customFitVerificationRemaining = 3;
			if (!changed) return;
			if (AutoFitToWidth) QueueAutoFitSave();
			// Called from MeasureOverride too: only a real change refreshes the open menu.
			RefreshColumnMenuState();
		}
		// Automatic fitting to the width saves the folder's layout once the columns settle, as 4.3's automatic fit to content does;
		// the folder fits and saves again each time it opens.
		private void QueueAutoFitSave()
		{
			autoFitSaveTimer ??= DispatcherQueue.CreateTimer();
			autoFitSaveTimer.Debounce(() =>
			{
				if (customColumnsReady && AutoFitToWidth && !manualColumnResize && FolderSettings is { } folderSettings)
					folderSettings.ColumnsViewModel = ColumnsViewModel;
			}, TimeSpan.FromMilliseconds(500));
		}
		private void CustomColumnsNavigatedTo(NavigationArguments? args)
		{
			columnsFolderPath = args is { IsSearchResultPage: false } ? args.NavPathParam.TrimPath() : null;
			columnsFolderId = null;
			// A folder restored to default while keeping its own sort or view mode shows the current default columns; with the app's
			// default sizing it keeps its saved widths, as in 4.3.
			if (ColumnsActive && FollowsDefaultColumns() && FolderSettings is { } folderSettings)
			{
				ApplyDefaultColumns(widths: !UsesAppSizing);
				folderSettings.ColumnsViewModel = ColumnsViewModel;
			}
			// Each folder opens with the default layout; changes made in the previous one don't carry over. A folder with its own column
			// widths keeps them: automatic fitting is paused for it.
			ownColumnWidths = ColumnsActive && KeepsOwnColumnWidths();
			fitColumnsToWidthOverride = ownColumnWidths ? false : null;
			viewWidthsChanged = false;
			contentColumnWidths = null;
			contentMeasured = false;
			customColumnsReady = true;
			HeaderGrid.Loaded -= ColumnHeader_Loaded;
			HeaderGrid.Loaded += ColumnHeader_Loaded;
			lastCustomColumnOptions = CustomOptions;
			UpdateColumnHeaderMenu();
			AttachCustomColumnFitScroller();
		}
		private void CustomColumnsNavigatingFrom()
		{
			customColumnsReady = false;
			autoFitSaveTimer?.Stop();
			DetachCustomColumnFitScroller();
			RestoreCustomColumnWidths();
		}
		// Switching Column details on or off while the view is open.
		private void ApplyColumnsActive()
		{
			UpdateColumnHeaderMenu();
			if (ColumnsActive)
			{
				if (HeaderGrid.IsLoaded) ColumnHeader_Loaded(HeaderGrid, new RoutedEventArgs());
				RememberRealizedColumnRows();
				AttachCustomColumnFitScroller();
				ownColumnWidths = KeepsOwnColumnWidths();
				if (ownColumnWidths) fitColumnsToWidthOverride = false;
				QueueCustomColumnFit(true);
				return;
			}
			// Back to the original order first, then stop following the rows.
			ApplyCustomColumnOrder();
			ForgetColumnRows();
			DetachCustomColumnFitScroller();
			RestoreCustomColumnWidths();
			fitColumnsToWidthOverride = null;
			ownColumnWidths = false;
			viewWidthsChanged = false;
			columnOrderOverride = null;
			manualColumnResize = false;
		}
		private void AttachCustomColumnFitScroller()
		{
			if (!ColumnsActive) return;
			if (customFitScroller == ContentScroller) return;
			DetachCustomColumnFitScroller();
			customFitScroller = ContentScroller;
			if (customFitScroller is not null) customFitScroller.LayoutUpdated += VerifyCustomColumnFit;
		}
		private void DetachCustomColumnFitScroller()
		{
			if (customFitScroller is not null) customFitScroller.LayoutUpdated -= VerifyCustomColumnFit;
			customFitScroller = null;
		}
		// Runs on every layout pass anywhere in the window, so it must stay cheap: the menu's state (which measures every column's
		// content) is refreshed by the commands and fits that change it, not here.
		private void VerifyCustomColumnFit(object? sender, object args)
		{
			if (customFitVerificationRemaining <= 0 || !customColumnsReady || !CustomOptions.FitColumnsToWidth || manualColumnResize ||
				ContentScroller is not { ViewportWidth: > 0 } scroller)
				return;
			customFitVerificationRemaining--;
			var minimum = CustomColumns.Where(column => column.Visibility == Visibility.Visible).Sum(column => column.NormalMinLength + (column.IsResizable ? 12 : 0));
			if (minimum + ColumnsViewModel.IconColumn.Length.Value + 52 > scroller.ViewportWidth) { customFitVerificationRemaining = 0; return; }
			if (scroller.ScrollableWidth > 0.001)
			{
				customFitChromeAdjustment += scroller.ScrollableWidth + 1 / (XamlRoot?.RasterizationScale ?? 1);
				FitColumnsToWidth(RootGrid.ActualWidth, false);
			}
			else customFitVerificationRemaining = 0;
		}
		private void MarkCustomColumnWidthsChanged()
		{
			if (!ColumnsActive) return;
			SetFollowsDefaultColumns(false);
			SetKeepsOwnColumnWidths(true);
			viewWidthsChanged = true;
			contentColumnWidths = null;
			customColumnWidths.Clear(); lastCustomFitWidth = -1; fittedColumnLayout = null;
			customFitVerificationRemaining = 0; manualColumnResize = false;
			fitColumnsToWidthOverride = false;
			lastCustomColumnOptions = CustomOptions;
			RefreshColumnMenuState();
		}

		private void QueueCustomColumnFit(bool force = false)
		{
			var options = CustomOptions;
			if (!customColumnsReady || manualColumnResize || !options.FitColumnsToWidth || !force && Math.Abs(lastCustomFitWidth - RootGrid.ActualWidth) < 1) return;
			// A forced fit follows a change in the columns or items: their content is measured again once they are laid out,
			// then the columns fit in one step.
			if (force)
			{
				contentColumnWidths = null;
				contentMeasured = false;
				DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
				{
					if (customColumnsReady && !manualColumnResize && CustomOptions.FitColumnsToWidth) FitColumnsToCurrentWidth();
				});
				return;
			}
			FitColumnsToCurrentWidth();
		}
		private CustomizationPreferences? lastCustomColumnOptions;
		private void CustomizationSettingsChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName != nameof(IAppearanceSettingsService.CustomizationPreferences)) return;
			var options = CustomOptions;
			var previous = lastCustomColumnOptions;
			if (previous is not null &&
				options.ColumnOrder == previous.ColumnOrder &&
				options.FitColumnsToWidth == previous.FitColumnsToWidth &&
				options.ColumnsEnabled == previous.ColumnsEnabled)
				return;
			var fittingChanged = previous is null || options.FitColumnsToWidth != previous.FitColumnsToWidth;
			var activeChanged = previous is null || previous.ColumnsEnabled != options.ColumnsEnabled;
			lastCustomColumnOptions = options;
			DispatcherQueue.TryEnqueue(() =>
			{
				if (!customColumnsReady) return;
				if (activeChanged)
				{
					ApplyColumnsActive();
					return;
				}
				if (!ColumnsActive) return;
				ApplyCustomColumnOrder();
				if (fittingChanged && !CustomOptions.FitColumnsToWidth)
				{
					RestoreCustomColumnWidths();
				}
				else if (fittingChanged)
				{
					_autoFitColumnsTimer?.Stop();
					QueueCustomColumnFit(true);
				}
				RefreshColumnMenuState();
				RefreshColumnReorderMenu();
			});
		}
		private void RestoreCustomColumnWidths()
		{
			foreach (var pair in customColumnWidths) pair.Key.UserLengthPixels = pair.Value;
			customColumnWidths.Clear(); lastCustomFitWidth = -1; fittedColumnLayout = null; customFitVerificationRemaining = 0;
		}
	}
}
