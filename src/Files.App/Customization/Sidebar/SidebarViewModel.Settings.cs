// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Specialized;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		private async Task HandleCustomSidebarSettingChangedAsync(string settingName)
		{
			if (settingName == nameof(IAppearanceSettingsService.SidebarCustomization))
			{
				void RefreshCustomization()
				{
					if (!SidebarCustomizationEnabled) return;
					var current = ReadCustomization;
					if (lastSidebarStructure is not null && lastSidebarStructure.HasSameStructure(current))
					{
						RefreshVisualAppearanceOnly(true);
						lastSidebarStructure = current;
					}
					else RefreshVisualSidebar();
				}
				if (dispatcherQueue is not null) await dispatcherQueue.EnqueueOrInvokeAsync(RefreshCustomization);
				else RefreshCustomization();
				return;
			}
			void RefreshAppearance()
			{
				var options = CustomizationPreferences.Effective(UserSettingsService.AppearanceSettingsService.CustomizationPreferences);
				var wasOn = lastSidebarAppearance is { } previous && IsSidebarCustomizationOn(previous);
				if (lastSidebarAppearance?.CustomizationEnabled != options.CustomizationEnabled ||
					lastSidebarAppearance?.SidebarEnabled != options.SidebarEnabled)
				{
					SyncSwitchedOffSections();
					RefreshVisualSidebar();
					if (wasOn && !SidebarCustomizationEnabled) RebuildOriginalSidebar();
				}
				RefreshVisualAppearanceOnly();
			}
			if (dispatcherQueue is not null) await dispatcherQueue.EnqueueOrInvokeAsync(RefreshAppearance);
			else RefreshAppearance();
		}

		// While sidebar customization is on, a built-in section switched off with the app's own switch (right-click menu or Settings)
		// stays in the sidebar and is only not drawn, like any hidden item, so the customization panel can show, move and edit it normally.
		// With customization off, switched-off sections are removed exactly as in the original app.
		private bool KeepsSwitchedOffSections => SidebarCustomizationEnabled;

		private bool IsSectionSwitchedOff(INavigationControlItem item)
			=> item is LocationItem { IsHeader: true } header && header.Section != SectionType.Home && !EditorSectionVisible(header.Section);

		// Shows or hides a kept section after its switch changed.
		private void RefreshSectionVisibility(SectionType sectionType)
		{
			if (GetSection(sectionType) is { } section)
				ApplyVisualPreferences(section);
			if (_flatSidebarItems is not null)
				RebuildFlatTree();
			OnPropertyChanged(nameof(SettingsFooterVisibility));
		}

		// Customization was switched off: the sections are built again the way the app builds them at startup, in its own order and with
		// each item's own expansion, as if customization had never been on.
		private void RebuildOriginalSidebar()
		{
			foreach (var item in sidebarItems.ToArray())
			{
				if (item.Children is IEnumerable<INavigationControlItem> children)
					foreach (var child in children.OfType<ExpandableSidebarItemBase>())
						child.StopWatchingSubfoldersAndDescendants();
				sidebarItems.Remove(item);
			}
			_ = CreateItemHomeAsync();
			foreach (var sectionType in SectionOrder.Where(sectionType => sectionType != SectionType.Home))
				ReloadSection(sectionType);
		}

		// Loads a section again from its manager, as the app does at startup.
		private void ReloadSection(SectionType sectionType)
			=> Manager_DataChanged(sectionType, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

		// Turning customization on adds the switched-off sections (hidden); turning it off removes them again.
		private void SyncSwitchedOffSections()
		{
			foreach (var sectionType in SectionOrder)
			{
				if (sectionType == SectionType.Home || EditorSectionVisible(sectionType))
					continue;

				if (KeepsSwitchedOffSections)
				{
					if (GetSection(sectionType) is null)
						ReloadSection(sectionType);
				}
				else if (GetSection(sectionType) is { } section)
					FindVisualSiblings(section)?.Remove(section);
			}
		}
	}
}
