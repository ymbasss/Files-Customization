// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace Files.App.Controls
{
	// Rows of a preview (the sidebar customization panel, see SidebarView.Preview.cs) are selected, moved and expanded only within the
	// preview; they never open, drag or expand the sidebar item they show.
	public sealed partial class SidebarItem
	{
		// A preview row is expanded by the preview, not by the sidebar item.
		private bool ItemExpansion => Owner?.IsPreview == true ? (DataContext as FlatSidebarItem)?.PreviewExpanded ?? false : Item?.IsExpanded ?? true;

		// A preview row never expands, loads or collapses the sidebar item it shows.
		private bool KeepsPreviewExpansion()
		{
			if (Owner?.IsPreview != true) return false;
			UpdateExpansionState();
			return true;
		}

		// An item kept expanded or collapsed by its sidebar setting refuses the change, and the row shows the item's state again.
		private bool FollowsLockedExpansion()
		{
			if (!UseItemPresentation || Item is not { } current || current.IsExpanded == IsExpanded) return false;
			IsExpanded = current.IsExpanded;
			return true;
		}

		// Preview rows only move within the preview; they never drag the folder itself.
		private bool TryStartPreviewDrag(DragStartingEventArgs args)
		{
			if (Owner?.IsPreview != true) return false;
			if (Owner.PreviewDragKey(Item) is not { } previewKey)
			{
				args.Cancel = true;
				return true;
			}
			args.Data.SetData(SidebarView.PreviewDataFormat, previewKey);
			args.Data.RequestedOperation = DataPackageOperation.Move;
			return true;
		}

		// Clicking a preview row only selects it.
		private bool TryPreviewClick(PointerUpdateKind pointerUpdateKind)
		{
			if (Owner?.IsPreview != true) return false;
			RaiseItemInvoked(pointerUpdateKind);
			return true;
		}

		// Preview rows: the middle half of a group drops inside it; any other row splits into before and after.
		[DynamicWindowsRuntimeCast(typeof(Grid))]
		private bool TryGetPreviewDropPosition(DragEventArgs args, out SidebarItemDropPosition position)
		{
			position = SidebarItemDropPosition.Center;
			if (Owner?.IsPreview != true || GetTemplateChild("ElementGrid") is not Grid previewGrid) return false;
			var y = args.GetPosition(previewGrid).Y;
			var height = previewGrid.ActualHeight;
			position = (DataContext as FlatSidebarItem)?.PreviewAcceptsInside == true
				? y < height * 0.25 ? SidebarItemDropPosition.Top : y > height * 0.75 ? SidebarItemDropPosition.Bottom : SidebarItemDropPosition.Center
				: y < height / 2 ? SidebarItemDropPosition.Top : SidebarItemDropPosition.Bottom;
			return true;
		}

		// In a preview the panel decides the row's expansion; elsewhere the row expands its item as usual.
		private void ToggleExpandedByUser()
		{
			if (Owner?.IsPreview == true)
			{
				Owner.RaisePreviewExpansionToggled(this);
				return;
			}
			IsExpanded = !IsExpanded;
		}

		// A preview row stops listening to its sidebar item when the preview no longer shows it.
		internal void DetachItemListener() => HookupItemChangeListener(null, null);
	}
}
