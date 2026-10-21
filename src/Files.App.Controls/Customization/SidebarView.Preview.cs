// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using WinRT;

namespace Files.App.Controls
{
	public sealed partial class SidebarView
	{
		// Data format of rows dragged inside a preview; the real sidebar ignores it.
		public const string PreviewDataFormat = "Files.Sidebar.PreviewItem";

		private Func<ISidebarItemModel, string?>? previewDragKey;

		// A preview draws the real sidebar rows for the sidebar customization panel. Clicking a row only selects it, hidden rows
		// stay visible (the panel dims them), and expanding a row changes only the preview, never the sidebar item itself.
		public bool IsPreview { get; private set; }

		// Raised when a row's arrow (or keyboard or double-tap) asks to expand or collapse it in the preview.
		public event EventHandler<ISidebarItemModel>? PreviewExpansionToggled;

		// dragKey returns the key a row is dragged with, or null for rows that can't be moved.
		// rightInset is the space after the rows; the scroll bar stays at the pane's edge.
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		public void EnablePreview(Func<ISidebarItemModel, string?> dragKey, double rightInset = 4)
		{
			MenuItemsHost.Margin = MenuItemsHost.Margin with { Right = rightInset };
			IsPreview = true;
			SupportsExpansion = true;
			CanResizePane = false;
			// A preview's rows and scroll bar fill the whole space it is given; the sidebar's width and 480 px limit don't apply.
			PaneColumnDefinition.MaxWidth = double.PositiveInfinity;
			PaneColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
			// The content column (unused in a preview) would otherwise share the space.
			if (Content is Grid root && root.ColumnDefinitions.Count == 3)
				root.ColumnDefinitions[2].Width = new GridLength(0);
			previewDragKey = dragKey;
			// Rows leaving the preview stop listening to their sidebar item, so a closed panel isn't kept alive by the sidebar.
			MenuItemsHost.ElementClearing += (_, args) =>
			{
				if (args.Element is SidebarItem item)
					item.DetachItemListener();
			};
		}

		internal string? PreviewDragKey(ISidebarItemModel? item)
			=> item is null ? null : previewDragKey?.Invoke(item);

		internal void RaisePreviewExpansionToggled(SidebarItem item)
		{
			if (item.Item is { } model)
				PreviewExpansionToggled?.Invoke(this, model);
		}

		// The Legacy look keeps each row's arrow at the pane's right edge, so its rows are as wide as the pane and long names end
		// with "…" instead of scrolling sideways. Otherwise the pane scrolls sideways, as the original sidebar does.
		// A row changed height: the list lays its rows out again.
		internal void InvalidateRowLayout() => MenuItemsHost.InvalidateMeasure();

		internal void FitRowsToPane(bool fit)
		{
			MenuItemHostScrollViewer.HorizontalScrollMode = fit ? ScrollMode.Disabled : ScrollMode.Enabled;
			MenuItemHostScrollViewer.HorizontalScrollBarVisibility = fit ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Visible;
		}
	}
}
