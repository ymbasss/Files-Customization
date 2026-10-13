// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using CommunityToolkit.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;
using WinRT;

namespace Files.App.Customization.Columns
{
	internal static partial class ColumnMenuLayout
	{
		private sealed record GlyphOwner(Control Item, bool ReserveGlyph, Control? ReorderRow);
		private static readonly ConditionalWeakTable<Control, GlyphOwner> items = new();

		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutSeparator))]
		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutSubItem))]
		[DynamicWindowsRuntimeCast(typeof(ToggleMenuFlyoutItem))]
		internal static void Configure(IEnumerable<MenuFlyoutItemBase> menuItems)
		{
			var entries = menuItems.ToArray();
			var reorderRow = entries.FirstOrDefault(item => item is MenuFlyoutItem { DataContext: Files.App.Views.Layouts.DetailsLayoutPage.ColumnReorderMenuRow });
			var reserveGlyph = entries.Any(item => item is ToggleMenuFlyoutItem ||
				item is MenuFlyoutItem { Icon: not null } || item is MenuFlyoutSubItem { Icon: not null });
			foreach (var item in entries)
			{
				if (item is MenuFlyoutSeparator) continue;
				item.HorizontalAlignment = HorizontalAlignment.Stretch;
				if (reorderRow is not null) item.MinWidth = 260;
				items.Remove(item);
				items.Add(item, new(item, reserveGlyph, reorderRow));
				item.Loaded -= Item_Loaded;
				item.Loaded += Item_Loaded;
				if (item.IsLoaded) ConfigureItem(item);
				if (item is MenuFlyoutSubItem submenu) Configure(submenu.Items);
			}
		}

		// Puts the items back the way the menu draws them on its own (Column details switched off).
		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutSubItem))]
		[DynamicWindowsRuntimeCast(typeof(ToggleMenuFlyoutItem))]
		internal static void Reset(IEnumerable<MenuFlyoutItemBase> menuItems)
		{
			foreach (var item in menuItems.ToArray())
			{
				if (item is MenuFlyoutSubItem submenu) Reset(submenu.Items);
				if (!items.TryGetValue(item, out _)) continue;
				items.Remove(item);
				item.Loaded -= Item_Loaded;
				item.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
				item.ClearValue(FrameworkElement.MinWidthProperty);
				if (item.FindDescendant<Grid>(element => element.Name == "LayoutRoot") is { } root)
					VisualStateManager.SetCustomVisualStateManager(root, null);
				if (item is ToggleMenuFlyoutItem toggle) VisualStateManager.GoToState(toggle, toggle.IsChecked ? "Checked" : "Unchecked", false);
				else VisualStateManager.GoToState(item, "NoPlaceholder", false);
			}
		}

		[DynamicWindowsRuntimeCast(typeof(Control))]
		private static void Item_Loaded(object sender, RoutedEventArgs args)
		{
			if (sender is Control item) ConfigureItem(item);
		}

		[DynamicWindowsRuntimeCast(typeof(ToggleMenuFlyoutItem))]
		private static void ConfigureItem(Control item)
		{
			if (!items.TryGetValue(item, out var owner) ||
				item.FindDescendant<Grid>(element => element.Name == "LayoutRoot") is not { } root) return;
			if (owner.ReorderRow is { } row && item.DataContext is not Files.App.Views.Layouts.DetailsLayoutPage.ColumnReorderMenuRow)
			{
				row.ApplyTemplate();
				if (row.FindDescendant<Grid>(element => element.Name == "LayoutRoot") is { } rowRoot)
				{
					root.Margin = rowRoot.Margin;
					root.MinWidth = rowRoot.MinWidth;
				}
			}
			var name = item is ToggleMenuFlyoutItem ? "CheckStates" : "CheckPlaceholderStates";
			var group = VisualStateManager.GetVisualStateGroups(root).FirstOrDefault(element => element.Name == name);
			if (group is null) return;
			if (owner.ReserveGlyph) AlignMenuText(item, group);
			VisualStateManager.SetCustomVisualStateManager(root, null);
			if (group.Name == "CheckPlaceholderStates") VisualStateManager.GoToState(item, "NoPlaceholder", false);
			VisualStateManager.SetCustomVisualStateManager(root, new ColumnMenuVisualStateManager(owner));
			VisualStateManager.GoToState(item, GlyphState(group.Name, group.CurrentState?.Name ?? "", owner), false);
		}

		internal static void AlignMenuText(Control item, VisualStateGroup group)
		{
			if (group.Name != "CheckPlaceholderStates") return;
			var check = item.FindAscendant<MenuFlyoutPresenter>()?.FindDescendant<FontIcon>(element => element.Name == "CheckGlyph");
			var width = check is null ? 24 : (check.ActualWidth > 0 ? check.ActualWidth : check.FontSize) + check.Margin.Left + check.Margin.Right;
			var checkRoot = check?.FindAscendant<Grid>(element => element.Name == "LayoutRoot");
			var root = item.FindDescendant<Grid>(element => element.Name == "LayoutRoot");
			if (checkRoot is not null && root is not null)
				width += checkRoot.Margin.Left + checkRoot.BorderThickness.Left + checkRoot.Padding.Left
					- root.Margin.Left - root.BorderThickness.Left - root.Padding.Left;
			width = Math.Max(0, width);
			foreach (var state in group.States)
				if (state.Name is "CheckPlaceholder" or "IconPlaceholder")
					foreach (var setter in state.Setters.OfType<Setter>())
						if (setter.Target?.Path?.Path is "Margin" or "(FrameworkElement.Margin)") setter.Value = new Thickness(width, 0, 0, 0);
		}

		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutItem))]
		[DynamicWindowsRuntimeCast(typeof(MenuFlyoutSubItem))]
		[DynamicWindowsRuntimeCast(typeof(ToggleMenuFlyoutItem))]
		private static string GlyphState(string groupName, string stateName, GlyphOwner owner)
		{
			return groupName switch
			{
				"CheckStates" when owner.Item is ToggleMenuFlyoutItem toggle => toggle.IsChecked ? "Checked" : "Unchecked",
				"CheckPlaceholderStates" when owner.Item is MenuFlyoutItem { Icon: not null } or MenuFlyoutSubItem { Icon: not null } => "IconPlaceholder",
				"CheckPlaceholderStates" => owner.ReserveGlyph ? "CheckPlaceholder" : "NoPlaceholder",
				_ => stateName
			};
		}

		private sealed partial class ColumnMenuVisualStateManager(GlyphOwner owner) : VisualStateManager
		{
			protected override bool GoToStateCore(Control control, FrameworkElement templateRoot, string stateName,
				VisualStateGroup group, VisualState state, bool useTransitions)
			{
				if (group is null || state is null)
					return base.GoToStateCore(control, templateRoot, stateName, group!, state!, useTransitions);
				// Use one native marker slot before hover can measure a different text margin.
				if (owner.ReserveGlyph) AlignMenuText(control, group);
				var glyphName = GlyphState(group.Name, stateName, owner);
				var glyphState = group.States.FirstOrDefault(candidate => candidate.Name == glyphName) ?? state;
				return base.GoToStateCore(control, templateRoot, glyphState.Name, group, glyphState, useTransitions);
			}
		}
	}
}
