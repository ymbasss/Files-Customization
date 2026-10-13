// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using WinRT;

namespace Files.App.Controls
{
	public sealed partial class SidebarItem
	{
		private Border? wiredBorder;
		private Border? wiredChevron;
		private ItemsRepeater? wiredFlyout;
		private Border? addedSeparator;
		private FrameworkElement? compactFallbackIcon;
		private bool compactIconCentered;
		private bool sidebarAppearanceApplied;
		private bool applyingSidebarTemplate;

		// Sidebar customization is on for this row. While it's off the row is left exactly as the original code makes it.
		private bool IsSidebarCustomized => Item is ISidebarItemAppearanceModel { SidebarCustomized: true };

		private bool UsesLegacyLayout => Item is ISidebarItemAppearanceModel { SidebarCustomized: true, SidebarLegacyAppearance: true } && Owner?.SupportsExpansion != false;

		// The appearance properties of an item that change how its row is drawn.
		private static bool IsSidebarAppearanceProperty(string? name) => name is
			nameof(ISidebarItemAppearanceModel.SidebarCustomized) or nameof(ISidebarItemAppearanceModel.SidebarRowHeight) or
			nameof(ISidebarItemAppearanceModel.SidebarIconSize) or nameof(ISidebarItemAppearanceModel.SidebarFontSize) or
			nameof(ISidebarItemAppearanceModel.SidebarCompactShowIcons) or nameof(ISidebarItemAppearanceModel.SidebarCompactIconMode) or
			nameof(ISidebarItemAppearanceModel.SidebarCompactIconSize) or nameof(ISidebarItemAppearanceModel.SidebarCompactHideGroupIcon) or
			nameof(ISidebarItemAppearanceModel.SidebarArrowVisible) or nameof(ISidebarItemAppearanceModel.SidebarExpansionLock) or
			nameof(ISidebarItemAppearanceModel.SidebarIconVisible) or nameof(ISidebarItemAppearanceModel.SidebarLegacyAppearance) or
			nameof(ISidebarItemAppearanceModel.SidebarLegacyHeader) or nameof(ISidebarItemAppearanceModel.SidebarDecoration) or
			nameof(ISidebarItemAppearanceModel.SidebarHidden) or nameof(ISidebarItemAppearanceModel.SidebarSeparator) or
			nameof(ISidebarItemReorderModel.SidebarDragKey);

		// The children shown in the compact flyout; hidden items are left out while customization is on. The filtered list is a
		// List<object>, which WinUI can take as a source (an array of the app's own item type can't be passed to WinUI).
		private object? SidebarFlyoutChildren()
			=> IsSidebarCustomized && Item?.Children is IEnumerable<ISidebarItemModel> children
				? children.Where(child => child is not ISidebarItemAppearanceModel { SidebarHidden: true }).Cast<object>().ToList()
				: Item?.Children;

		private bool CanDragItem()
			=> Owner?.IsPreview == true
				? Owner.PreviewDragKey(Item) is not null
				: IsSidebarCustomized && Item is ISidebarItemReorderModel { SidebarDragKey: not null } || Item?.Path is string path && Path.IsPathRooted(path);

		// In the compact sidebar an added group (a heading that is also a folder row) opens its items in a flyout, like a built-in
		// section, unless "Show items from expanded groups" lists them under it.
		private bool TryOpenCompactGroupFlyout()
		{
			if (CollapseEnabled || !HasChildren || Item is not { IsLeafWithChildren: true } ||
				Item is not ISidebarItemAppearanceModel { SidebarCustomized: true, SidebarLegacyHeader: true, SidebarCompactGroupContents: false })
				return false;
			SetFlyoutOpen(true);
			return true;
		}

		// With "Drag to rearrange" on, a row is dragged to a new place in the sidebar instead of moving or copying its folder.
		private bool TryStartRearrangeDrag(DragStartingEventArgs args)
		{
			if (!IsSidebarCustomized || Item is not ISidebarItemReorderModel { SidebarDragKey: { } key }) return false;
			args.Data.SetData(SidebarView.ReorderDataFormat, key);
			args.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
			return true;
		}

		// From OnApplyTemplate. The template can't be reset while it is being applied, so that waits for the dispatcher.
		private void UpdateItemAppearanceOnTemplate()
		{
			applyingSidebarTemplate = true;
			try { UpdateItemAppearance(); }
			finally { applyingSidebarTemplate = false; }
		}

		// Leaving the Compact state can restore its earlier margins and widths after this pass, so the row is laid out once more afterwards.
		private void UpdateItemAppearanceAfterDisplayMode()
		{
			UpdateItemAppearance();
			if (IsSidebarCustomized)
				DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateItemAppearance);
		}

		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(ContentPresenter))]
		[DynamicWindowsRuntimeCast(typeof(FontIcon))]
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		private void UpdateItemAppearance()
		{
			if (ResetTemplateForDefaultLayout()) return;
			if (Item is not ISidebarItemAppearanceModel { SidebarCustomized: true } appearance)
			{
				RestoreOriginalAppearance();
				return;
			}
			sidebarAppearanceApplied = true;
			if (UsesLegacyLayout) ApplyLegacyLayout();
			CanDrag = CanDragItem();
			CenterCompactIcon();
			// A preview shows hidden rows too; the customization panel dims them.
			Visibility = appearance.SidebarHidden && Owner?.IsPreview != true ? Visibility.Collapsed : Visibility.Visible;
			var decoration = appearance.SidebarDecoration;
			// Separators and gaps never use the Legacy look, so only real rows decide.
			if (!decoration) Owner?.FitRowsToPane(UsesLegacyLayout);
			// In the sidebar, separators and gaps are only drawn: no hover, click or focus. The preview can still select them.
			var interactive = !decoration || Owner?.IsPreview == true;
			IsHitTestVisible = interactive;
			if (interactive) ClearValue(IsTabStopProperty);
			else IsTabStop = false;
			var compact = DisplayMode == SidebarDisplayMode.Compact || Owner?.DisplayMode == SidebarDisplayMode.Compact;
			var showCompactIcon = compact && appearance.SidebarCompactShowIcons;
			var iconSize = compact ? appearance.SidebarCompactIconMode switch { 1 => 24, 2 => Math.Clamp(appearance.SidebarCompactIconSize, 8, 32), _ => 16 } : appearance.SidebarIconSize;
			if (GetTemplateChild("RootPanel") is Grid root)
				root.MaxWidth = compact && !IsInFlyout ? 48 : double.PositiveInfinity;
			UpdateItemIndent();
			if (compact && Icon is null && (HasChildren || IsGroupHeader))
			{
				compactFallbackIcon = new FontIcon { Glyph = "" };
				Icon = compactFallbackIcon;
			}
			else if (!compact && Icon == compactFallbackIcon && compactFallbackIcon is not null)
			{
				Icon = (Item as ISidebarItemPresentationModel)?.IconElement;
				compactFallbackIcon = null;
			}
			if (GetTemplateChild("ElementBorder") is Border border)
				border.Height = compact && !decoration ? iconSize + 16 : appearance.SidebarRowHeight;
			var presenter = GetTemplateChild("IconPresenter") as ContentPresenter;
			if (presenter is not null)
			{
				presenter.Width = iconSize;
				presenter.Height = iconSize;
				// Hidden for separators and gaps, for groups in compact mode with "Hide group icons", and when the row's icon is switched off.
				var hideGroupIcon = compact && IsGroupHeader && appearance.SidebarCompactHideGroupIcon;
				var iconSwitchedOff = !showCompactIcon && !appearance.SidebarIconVisible;
				presenter.Visibility = KeepsIconSpace(appearance, compact) || !(decoration || hideGroupIcon || iconSwitchedOff) ? Visibility.Visible : Visibility.Collapsed;
			}
			if (Icon is FrameworkElement iconElement)
			{
				iconElement.Width = iconSize;
				iconElement.Height = iconSize;
				iconElement.Opacity = KeepsIconSpace(appearance, compact) ? 0 : 1;
			}
			if (Icon is FontIcon fontIcon)
				fontIcon.FontSize = iconSize;
			if (GetTemplateChild("ItemNameTextBlock") is TextBlock textBlock)
			{
				textBlock.FontSize = appearance.SidebarFontSize;
				textBlock.LineHeight = textBlock.FontSize * 1.35;
				textBlock.Visibility = decoration || compact && !IsInFlyout ? Visibility.Collapsed : Visibility.Visible;
			}
			if (GetTemplateChild("ChevronContainer") is Border chevron)
			{
				chevron.Visibility = compact && !IsInFlyout || !HasChildren || !appearance.SidebarArrowVisible || decoration ? Visibility.Collapsed : Visibility.Visible;
				chevron.IsHitTestVisible = appearance.SidebarExpansionLock == 0;
				chevron.Opacity = appearance.SidebarExpansionLock == 0 ? 1 : 0.4;
			}
			UpdateCustomizationSeparator(appearance.SidebarSeparator);
			if (UsesLegacyLayout) UpdateLegacySidebarLayout(appearance, compact);
			else UpdateDefaultSidebarLayout(appearance, compact);
			if (presenter is not null)
			{
				var iconOnly = compact && !IsInFlyout;
				Grid.SetColumn(presenter, iconOnly ? 0 : 3);
				Grid.SetColumnSpan(presenter, iconOnly ? Math.Max(1, (GetTemplateChild("ElementGrid") as Grid)?.ColumnDefinitions.Count ?? 6) : 1);
				if (iconOnly) presenter.Margin = new Thickness(0);
			}
		}

		private void UpdateItemIndent()
		{
			var compact = DisplayMode == SidebarDisplayMode.Compact || Owner?.DisplayMode == SidebarDisplayMode.Compact;
			IndentWidth = !IsSidebarCustomized ? NestingLevel * 16d : compact && !IsInFlyout ? 0 : Item is ISidebarItemAppearanceModel { SidebarLegacyAppearance: true }
				? Math.Max(0, NestingLevel - 1) * 16d : NestingLevel * 16d;
		}

		// A row changed by the Legacy look goes back to the Default look with a fresh copy of the original template.
		private bool ResetTemplateForDefaultLayout()
		{
			if (!legacyLayoutApplied || UsesLegacyLayout) return false;
			if (applyingSidebarTemplate)
			{
				DispatcherQueue?.TryEnqueue(UpdateItemAppearance);
				return false;
			}
			legacyLayoutApplied = false;
			Template = null;
			ClearValue(TemplateProperty);
			ApplyTemplate();
			return true;
		}

		// Customization was switched off: undo what it set on this row and take a fresh copy of the original template.
		private void RestoreOriginalAppearance()
		{
			if (!sidebarAppearanceApplied) return;
			sidebarAppearanceApplied = false;
			Owner?.FitRowsToPane(false);
			ClearValue(VisibilityProperty);
			ClearValue(IsHitTestVisibleProperty);
			ClearValue(IsTabStopProperty);
			compactFallbackIcon = null;
			if (UseItemPresentation) Icon = (Item as ISidebarItemPresentationModel)?.IconElement;
			IndentWidth = NestingLevel * 16d;
			CanDrag = Item?.Path is string path && Path.IsPathRooted(path);
			// A template being applied right now is already a fresh copy.
			if (applyingSidebarTemplate) return;
			legacyLayoutApplied = false;
			Template = null;
			ClearValue(TemplateProperty);
		}

		// Separator rows draw a line added to the original template.
		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		private void UpdateCustomizationSeparator(bool show)
		{
			var separator = addedSeparator;
			if (separator is null)
			{
				if (!show || GetTemplateChild("ElementGrid") is not Grid grid) return;
				separator = addedSeparator = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
					"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Height=\"1\" VerticalAlignment=\"Center\" IsHitTestVisible=\"False\" Background=\"{ThemeResource DividerStrokeColorDefaultBrush}\" />");
				Grid.SetColumnSpan(separator, Math.Max(1, grid.ColumnDefinitions.Count));
				grid.Children.Insert(0, separator);
			}
			separator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		}

		// Compact icons are centred across the row in code, so the original Compact state's left icon margin is dropped.
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Setter))]
		private void CenterCompactIcon()
		{
			if (compactIconCentered || GetTemplateChild("RootPanel") is not FrameworkElement root) return;
			compactIconCentered = true;
			foreach (var group in VisualStateManager.GetVisualStateGroups(root))
				foreach (var state in group.States.Where(state => state.Name == "Compact"))
					foreach (var setter in state.Setters.OfType<Setter>())
						if (setter.Target?.Path?.Path is "Margin" or "(FrameworkElement.Margin)") setter.Value = new Thickness(0);
		}

		private void ReleaseSidebarTemplateParts()
		{
			if (wiredBorder is not null)
			{
				wiredBorder.PointerEntered -= ItemBorder_PointerEntered;
				wiredBorder.PointerExited -= ItemBorder_PointerExited;
				wiredBorder.PointerCanceled -= ItemBorder_PointerCanceled;
				wiredBorder.PointerPressed -= ItemBorder_PointerPressed;
				wiredBorder.ContextRequested -= ItemBorder_ContextRequested;
				wiredBorder.DoubleTapped -= ItemBorder_DoubleTapped;
				wiredBorder.DragLeave -= ItemBorder_DragLeave;
				wiredBorder.DragOver -= ItemBorder_DragOver;
				wiredBorder.Drop -= ItemBorder_Drop;
			}
			if (wiredChevron is not null) wiredChevron.PointerPressed -= ChevronContainer_PointerPressed;
			if (wiredFlyout is not null) wiredFlyout.ElementPrepared -= FlyoutChildrenPresenter_ElementPrepared;
			wiredBorder = wiredChevron = null;
			wiredFlyout = null;
			addedSeparator = null;
			compactIconCentered = false;
			legacyLayoutApplied = false;
			legacyChevron = null;
			legacyHeaderText = null;
			isTemplateWired = false;
		}

		// With "Keep text in place", a hidden icon keeps its slot (drawn transparent) so the text stays where it is with the icon shown.
		// Legacy group headings have no icon by design, so they never reserve one.
		private bool KeepsIconSpace(ISidebarItemAppearanceModel appearance, bool compact)
			=> appearance.SidebarKeepTextPosition && !appearance.SidebarIconVisible && !appearance.SidebarDecoration
				&& (!compact || IsInFlyout) && !(UsesLegacyLayout && appearance.SidebarLegacyHeader);

		[DynamicWindowsRuntimeCast(typeof(ColumnDefinition))]
		[DynamicWindowsRuntimeCast(typeof(ContentPresenter))]
		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		private void UpdateDefaultSidebarLayout(ISidebarItemAppearanceModel appearance, bool compact)
		{
			if (GetTemplateChild("ChevronColumnDefinition") is ColumnDefinition leadingColumn)
				leadingColumn.Width = new GridLength(compact && !IsInFlyout || Owner?.SupportsExpansion == false ? 0 : 16);
			if (GetTemplateChild("IconPresenter") is ContentPresenter icon)
				icon.Margin = new Thickness(compact && !IsInFlyout || Owner?.SupportsExpansion == false ? 8 : 4, 0, 0, 0);
			if (GetTemplateChild("ItemNameTextBlock") is TextBlock text)
			{
				var iconSlot = appearance.SidebarIconVisible || KeepsIconSpace(appearance, compact);
				var textInset = iconSlot ? 12d : 4d;
				// Added groups use the same heading text as built-in groups (Drives, Network, ...): semibold with the heading gap.
				var heading = appearance.SidebarLegacyHeader && !appearance.SidebarDecoration;
				if (heading) textInset = 8;
				text.FontWeight = heading ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
				text.Margin = new Thickness(textInset, 0, 12, heading ? 0 : 2);
			}
		}
	}
}
