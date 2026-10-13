// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Files.App.Controls;
using System.Collections.Specialized;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		// Hooks called from the original flat sidebar list (SidebarViewModel.FlatTree.cs). Each one does nothing while customization is off.

		// In the compact sidebar, a group's contents are listed under it when "Show items from expanded groups" is on.
		private bool ShowsCompactGroupContents(ISidebarItemModel parent)
			=> SidebarCustomizationEnabled && IsCompactDisplayMode && parent is INavigationControlItem navigation && IsCustomizationGroup(navigation) &&
				CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences).SidebarCompactGroupContents;

		// Icons are sized for the current mode, so they are loaded again when switching to or from compact.
		private void RefreshVisualIconsForDisplayMode()
		{
			if (SidebarCustomizationEnabled)
				RefreshVisualAppearanceOnly(true);
		}

		// An item's new children collection may already hold children whose Add notifications were missed.
		private void ReconcileVisualChildren(ISidebarItemModel item)
		{
			if (SidebarCustomizationEnabled && dispatcherQueue is not null)
				dispatcherQueue.EnqueueOrInvokeAsync(() => HandleChildCollectionChangedAsync(item, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)));
		}

		private void UpdateLegacySectionInsets()
		{
			if (_flatSidebarItems is null) return;
			var legacy = SidebarCustomizationEnabled && !IsCompactDisplayMode &&
				CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences).SidebarAppearance == 1;
			bool section = false;
			for (int index = 0; index < _flatSidebarItems.Count; index++)
			{
				var node = _flatSidebarItems[index];
				if (node.Depth == 0) section = node.Item is ISidebarItemAppearanceModel { SidebarLegacyHeader: true };
				// The reference nested ChildrenPresenter has a 2px vertical margin at each end.
				var first = legacy && section && node.Depth == 1 && index > 0 && _flatSidebarItems[index - 1].Depth == 0;
				var last = legacy && section && node.Depth > 0 && (index + 1 == _flatSidebarItems.Count || _flatSidebarItems[index + 1].Depth == 0);
				node.SetLegacySectionInsets(first, last);
			}
		}
	}
}
