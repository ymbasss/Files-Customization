// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.CompilerServices;
using Windows.System;
using WinRT;

namespace Files.App.Views.Layouts
{
	public sealed partial class DetailsLayoutPage
	{
		private sealed class ColumnRowState
		{
			public UIElement[] Children = [];
			public int[]? Order;
			public bool Applying;
			public EventHandler<object>? LayoutChanged;
		}
		private sealed record ColumnCell(int Index);
		private readonly ConditionalWeakTable<StackPanel, ColumnRowState> originalColumnRows = new();
		private readonly ConditionalWeakTable<FrameworkElement, ColumnCell> columnCells = new();
		private readonly List<WeakReference<StackPanel>> columnRows = [];
		private readonly Dictionary<FrameworkElement, int> originalHeaderPositions = [];
		private ColumnDefinition[]? originalHeaderDefinitions;
		private Grid? columnHeaderGrid;
		private int columnRowsSincePrune;
		private bool applyingCustomColumnOrder;
		private bool customColumnOrderPending;

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		private void ColumnHeader_Loaded(object sender, RoutedEventArgs args)
		{
			if (!ColumnsActive || sender is not Grid grid) return;
			if (columnHeaderGrid != grid)
			{
				columnHeaderGrid = grid;
				originalHeaderDefinitions = grid.ColumnDefinitions.ToArray();
				originalHeaderPositions.Clear();
				foreach (var child in grid.Children)
				{
					if (child is not FrameworkElement element) continue;
					originalHeaderPositions[element] = Grid.GetColumn(element);
					if (element is Files.App.Controls.GridSplitter splitter)
					{
						splitter.PreviewKeyDown -= GridSplitter_PreviewKeyDown;
						splitter.PreviewKeyDown += GridSplitter_PreviewKeyDown;
					}
				}
			}
			ApplyCustomColumnOrder();
		}

		private void GridSplitter_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (ColumnsActive && e.Key is VirtualKey.Left or VirtualKey.Right) manualColumnResize = true;
		}

		// The splitter's column before Reorder columns moved it.
		private int ColumnSplitterIndex(Files.App.Controls.GridSplitter splitter)
			=> ColumnsActive ? OriginalSplitterColumn(splitter) : Grid.GetColumn(splitter);

		[DynamicWindowsRuntimeCast(typeof(ListViewItem))]
		[DynamicWindowsRuntimeCast(typeof(UserControl))]
		[DynamicWindowsRuntimeCast(typeof(StackPanel))]
		private void RememberRealizedColumnRows()
		{
			if (FileList.ItemsPanelRoot is not { } panel) return;
			foreach (var child in panel.Children)
				if (child is ListViewItem { ContentTemplateRoot: UserControl { Content: StackPanel row } }) RememberColumnRow(row);
		}

		private void ForgetColumnRows()
		{
			foreach (var reference in columnRows)
				if (reference.TryGetTarget(out var row) && originalColumnRows.TryGetValue(row, out var state))
				{
					row.LayoutUpdated -= state.LayoutChanged;
					row.Unloaded -= ColumnRow_Unloaded;
				}
		}

		private void RememberColumnRow(StackPanel panel)
		{
			if (!ColumnsActive) return;
			if (!originalColumnRows.TryGetValue(panel, out var state))
			{
				state = new ColumnRowState();
				state.LayoutChanged = (_, _) =>
				{
					if (!applyingCustomColumnOrder) ApplyColumnRow(panel, CustomizationColumnOrder.Parse(CustomOptions.ColumnOrder));
				};
				originalColumnRows.Add(panel, state);
				columnRows.Add(new(panel));
				if (++columnRowsSincePrune >= 128)
				{
					columnRows.RemoveAll(reference => !reference.TryGetTarget(out _));
					columnRowsSincePrune = 0;
				}
			}
			panel.LayoutUpdated -= state.LayoutChanged;
			panel.LayoutUpdated += state.LayoutChanged;
			panel.Unloaded -= ColumnRow_Unloaded;
			panel.Unloaded += ColumnRow_Unloaded;
			if (applyingCustomColumnOrder) customColumnOrderPending = true;
			else ApplyColumnRow(panel, CustomizationColumnOrder.Parse(CustomOptions.ColumnOrder));
		}
		[DynamicWindowsRuntimeCast(typeof(StackPanel))]
		private void ColumnRow_Unloaded(object sender, RoutedEventArgs args)
		{
			if (sender is StackPanel panel && originalColumnRows.TryGetValue(panel, out var state)) panel.LayoutUpdated -= state.LayoutChanged;
		}
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private int ColumnCellIndex(UIElement child)
		{
			if (child is not FrameworkElement element) return -1;
			if (columnCells.TryGetValue(element, out var cell)) return cell.Index;
			var path = element.GetBindingExpression(FrameworkElement.WidthProperty)?.ParentBinding.Path?.Path;
			if (path is null || !path.StartsWith("ColumnsViewModel.", StringComparison.Ordinal)) return -1;
			var property = path.Split('.')[1];
			var index = Array.FindIndex(CustomizationColumnOrder.Keys, key => property == key + "Column");
			if (index >= 0) columnCells.Add(element, new(index));
			return index;
		}

		private void ApplyColumnRow(StackPanel panel, int[] order)
		{
			if (!originalColumnRows.TryGetValue(panel, out var state) || state.Applying) return;
			// By value: tabs with different column orders keep replacing the parser's single cached array.
			if (state.Order?.SequenceEqual(order) == true && state.Children.SequenceEqual(panel.Children)) return;
			state.Applying = true;
			try
			{
				// Deferred Git and Recycle Bin cells can be absent or added to a recycled row.
				var cells = panel.Children.Select((child, position) => (Child: child, Position: position, Index: ColumnCellIndex(child)))
					.Where(cell => cell.Index >= 0).ToArray();
				var desired = cells.OrderBy(cell => Array.IndexOf(order, cell.Index)).ToArray();
				for (int position = 0; position < desired.Length; position++)
				{
					var child = desired[position].Child;
					var destination = cells[position].Position;
					var currentPosition = panel.Children.IndexOf(child);
					if (currentPosition < 0 || currentPosition == destination) continue;
					panel.Children.RemoveAt(currentPosition);
					panel.Children.Insert(destination, child);
				}
				state.Children = panel.Children.ToArray();
				state.Order = order;
			}
			finally { state.Applying = false; }
		}

		private void ApplyCustomColumnOrder()
		{
			if (applyingCustomColumnOrder)
			{
				customColumnOrderPending = true;
				return;
			}
			applyingCustomColumnOrder = true;
			try
			{
				var order = CustomizationColumnOrder.Parse(CustomOptions.ColumnOrder);
				if (columnHeaderGrid is { } grid && originalHeaderDefinitions is { Length: 33 } definitions)
				{
					var desired = new List<ColumnDefinition> { definitions[0], definitions[1] };
					foreach (var index in order)
					{
						desired.Add(definitions[(index + 1) * 2]);
						desired.Add(definitions[(index + 1) * 2 + 1]);
					}
					desired.Add(definitions[^1]);
					if (!grid.ColumnDefinitions.SequenceEqual(desired))
					{
						for (int position = 0; position < desired.Count; position++)
						{
							var definition = desired[position];
							var currentPosition = grid.ColumnDefinitions.IndexOf(definition);
							if (currentPosition < 0 || currentPosition == position) continue;
							grid.ColumnDefinitions.RemoveAt(currentPosition);
							grid.ColumnDefinitions.Insert(position, definition);
						}
					}
					foreach (var pair in originalHeaderPositions.ToArray())
					{
						var original = pair.Value;
						if (original >= 2 && original < 32)
							Grid.SetColumn(pair.Key, (Array.IndexOf(order, original / 2 - 1) + 1) * 2 + original % 2);
					}
				}
				columnRows.RemoveAll(reference => !reference.TryGetTarget(out _));
				foreach (var reference in columnRows.ToArray())
					if (reference.TryGetTarget(out var row)) ApplyColumnRow(row, order);
			}
			finally
			{
				applyingCustomColumnOrder = false;
				if (customColumnOrderPending)
				{
					customColumnOrderPending = false;
					DispatcherQueue.TryEnqueue(() => { if (customColumnsReady) ApplyCustomColumnOrder(); });
				}
			}
		}

		private int OriginalSplitterColumn(FrameworkElement splitter) =>
			originalHeaderPositions.TryGetValue(splitter, out var original) ? original : Grid.GetColumn(splitter);
	}
}
