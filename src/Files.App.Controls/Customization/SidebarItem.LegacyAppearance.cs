// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Markup;
using WinRT;

namespace Files.App.Controls
{
	// The Legacy look is drawn with the original template, changed in code once per template: the arrow moves to the end of the row
	// as an up and down arrow, group headings are grey, and icons sit further in. Going back to the Default look takes a fresh copy
	// of the template (see ResetTemplateForDefaultLayout).
	public sealed partial class SidebarItem
	{
		private bool legacyLayoutApplied;
		private AnimatedIcon? legacyChevron;
		private Style? legacyHeaderStyle;
		private bool? legacyHeaderText;
		private Thickness legacyTextMargin;

		private const string LegacyChevronXaml =
			"<AnimatedIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
			"xmlns:animatedvisuals=\"using:Microsoft.UI.Xaml.Controls.AnimatedVisuals\" Width=\"12\" Height=\"12\" Margin=\"0,0,8,0\" " +
			"HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" IsHitTestVisible=\"False\" AnimatedIcon.State=\"NormalOff\" " +
			"AutomationProperties.AccessibilityView=\"Raw\" Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" Visibility=\"Collapsed\">" +
			"<animatedvisuals:AnimatedChevronUpDownSmallVisualSource />" +
			"<AnimatedIcon.FallbackIconSource><FontIconSource FontFamily=\"Segoe Fluent Icons,Segoe MDL2 Assets\" FontSize=\"12\" Glyph=\"&#xE70D;\" IsTextScaleFactorEnabled=\"False\" /></AnimatedIcon.FallbackIconSource>" +
			"</AnimatedIcon>";

		private const string LegacyHeaderStyleXaml =
			"<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"TextBlock\">" +
			"<Setter Property=\"Foreground\" Value=\"{ThemeResource TextFillColorTertiaryBrush}\" /></Style>";

		[DynamicWindowsRuntimeCast(typeof(AnimatedIcon))]
		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(ColumnDefinition))]
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		private void ApplyLegacyLayout()
		{
			if (legacyLayoutApplied || GetTemplateChild("ElementGrid") is not Grid grid || GetTemplateChild("ChevronContainer") is not Border chevron)
				return;
			legacyLayoutApplied = true;
			// The arrow's column moves from before the icon to the end of the row.
			if (GetTemplateChild("ChevronColumnDefinition") is ColumnDefinition leadingColumn)
				leadingColumn.Width = new GridLength(0);
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			var last = grid.ColumnDefinitions.Count - 1;
			Grid.SetColumn(chevron, last);
			if (GetTemplateChild("DragOnTopIndicator") is FrameworkElement dropIndicator)
				Grid.SetColumnSpan(dropIndicator, grid.ColumnDefinitions.Count);
			// The original arrow stays, driven by the template's states, but isn't drawn; the up and down arrow follows it.
			if (GetTemplateChild("ExpandCollapseChevron") is FrameworkElement originalArrow)
				originalArrow.Opacity = 0;
			legacyChevron = (AnimatedIcon)XamlReader.Load(LegacyChevronXaml);
			Grid.SetColumn(legacyChevron, last);
			// The arrow animates through the template's own arrow states (pressed, released, expanded, collapsed), which the original
			// code switches on every press, release and expansion. Its first state is set before it is shown, so a new row's arrow
			// starts in place instead of turning.
			var iconStates = GetTemplateChild("RootPanel") is FrameworkElement root
				? VisualStateManager.GetVisualStateGroups(root).FirstOrDefault(group => group.Name == "ExpansionIconStates") : null;
			SetLegacyChevronState(iconStates?.CurrentState?.Name);
			grid.Children.Add(legacyChevron);
			if (iconStates is not null)
				iconStates.CurrentStateChanged += (_, args) => SetLegacyChevronState(args.NewState?.Name);
		}

		private void SetLegacyChevronState(string? arrowState)
		{
			if (legacyChevron is null) return;
			AnimatedIcon.SetState(legacyChevron, arrowState switch
			{
				"ExpandedIconNormal" => "NormalOn",
				"ExpandedIconPressed" => "PressedOn",
				"CollapsedIconNormal" => "NormalOff",
				"CollapsedIconPressed" => "PressedOff",
				_ => IsExpanded ? "NormalOn" : "NormalOff"
			});
		}

		[DynamicWindowsRuntimeCast(typeof(Border))]
		[DynamicWindowsRuntimeCast(typeof(ContentPresenter))]
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Style))]
		[DynamicWindowsRuntimeCast(typeof(TextBlock))]
		private void UpdateLegacySidebarLayout(SidebarLook look, bool compact)
		{
			var header = look.LegacyHeader && (!compact || IsInFlyout);
			if (GetTemplateChild("ItemDecoratorPresenter") is ContentPresenter decorator)
			{
				// Compact rows show only their icon, so the pin after a pinned folder is left out there too.
				if (header || compact && !IsInFlyout) decorator.Visibility = Visibility.Collapsed;
				else decorator.ClearValue(VisibilityProperty);
				// Rows are as wide as the pane in this look, so an empty decorator slot takes no space and long names run to the edge.
				decorator.Width = Decorator is null ? 0 : 28;
			}
			if (GetTemplateChild("IconPresenter") is ContentPresenter icon)
			{
				icon.Margin = new Thickness(8, 0, 0, 0);
				if (header) icon.Visibility = look.IconVisible ? Visibility.Visible : Visibility.Collapsed;
			}
			if (GetTemplateChild("ItemNameTextBlock") is TextBlock text)
			{
				var margin = header ? new Thickness(8, 0, 12, 0) : new Thickness(look.IconVisible || KeepsIconSpace(look, compact) ? 12 : 8, 0, 12, 2);
				text.Margin = margin;
				text.FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
				text.LineHeight = text.FontSize <= 14 ? 16 : text.FontSize * 1.35;
				if (header) text.Style = legacyHeaderStyle ??= (Style)XamlReader.Load(LegacyHeaderStyleXaml);
				else text.ClearValue(FrameworkElement.StyleProperty);
				SetLegacyHeadingStates(header, margin);
			}
			if (legacyChevron is not null && GetTemplateChild("ChevronContainer") is Border chevron && GetTemplateChild("ExpandCollapseChevron") is FrameworkElement arrow)
			{
				legacyChevron.Visibility = chevron.Visibility == Visibility.Visible && arrow.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
				legacyChevron.Opacity = chevron.Opacity;
			}
		}

		// The original group-heading states make every group's text semibold with the heading gap. In the Legacy look only headings are
		// semibold, so those states get the row's own weight and gap; the states are entered again to show the change.
		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		[DynamicWindowsRuntimeCast(typeof(Setter))]
		private void SetLegacyHeadingStates(bool header, Thickness margin)
		{
			if (legacyHeaderText == header && legacyTextMargin == margin || GetTemplateChild("RootPanel") is not FrameworkElement root) return;
			legacyHeaderText = header;
			legacyTextMargin = margin;
			foreach (var group in VisualStateManager.GetVisualStateGroups(root).Where(group => group.Name == "ExpansionStates"))
				foreach (var state in group.States)
					// Only the text's weight and margin are set in these states.
					foreach (var setter in state.Setters.OfType<Setter>())
					{
						if (setter.Target?.Path?.Path is "FontWeight" or "(TextBlock.FontWeight)")
							setter.Value = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
						else if (setter.Target?.Path?.Path is "Margin" or "(FrameworkElement.Margin)")
							setter.Value = margin;
					}
			VisualStateManager.GoToState(this, "LeafWithChildren", false);
			UpdateExpansionState();
		}
	}
}
