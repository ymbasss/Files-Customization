// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Files.App.Customization.Columns
{
	public sealed partial class PersistentColumnMenuItem : MenuFlyoutItem
	{
		private readonly ColumnMenuActivation activation;
		public new event RoutedEventHandler? Click;
		public PersistentColumnMenuItem() { DefaultStyleKey = typeof(MenuFlyoutItem); activation = new(this, Invoke); }
		internal void Invoke()
		{
			if (!IsEnabled) return;
			Click?.Invoke(this, new RoutedEventArgs());
			if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
		}
		protected override void OnPointerPressed(PointerRoutedEventArgs args) => activation.Press(args);
		protected override void OnPointerReleased(PointerRoutedEventArgs args) => activation.Release(args);
		protected override void OnPointerCanceled(PointerRoutedEventArgs args) => activation.Cancel();
		protected override void OnPointerCaptureLost(PointerRoutedEventArgs args) => activation.Cancel();
		protected override void OnKeyDown(KeyRoutedEventArgs args) { if (!activation.KeyDown(args)) base.OnKeyDown(args); }
		protected override void OnKeyUp(KeyRoutedEventArgs args) { if (!activation.KeyUp(args)) base.OnKeyUp(args); }
		protected override AutomationPeer OnCreateAutomationPeer() => new PersistentColumnMenuItemPeer(this);
	}

	internal sealed class ColumnMenuActivation(Control owner, Action invoke)
	{
		private bool pressed;
		private bool keyboardPressed;
		internal void Press(PointerRoutedEventArgs args)
		{
			if (args.Handled || !owner.IsEnabled || !args.GetCurrentPoint(owner).Properties.IsLeftButtonPressed) return;
			pressed = true;
			owner.Focus(FocusState.Pointer);
			owner.CapturePointer(args.Pointer);
			VisualStateManager.GoToState(owner, "Pressed", true);
			args.Handled = true;
		}
		internal void Release(PointerRoutedEventArgs args)
		{
			if (!pressed) return;
			var point = args.GetCurrentPoint(owner).Position;
			var activate = point.X >= 0 && point.Y >= 0 && point.X < owner.ActualWidth && point.Y < owner.ActualHeight;
			pressed = keyboardPressed = false;
			owner.ReleasePointerCapture(args.Pointer); args.Handled = true;
			// Like native menu items, the item stays highlighted while the pointer is still on it.
			VisualStateManager.GoToState(owner, activate ? "PointerOver" : "Normal", true);
			if (activate) invoke();
		}
		// Only ends this item's own press. A button inside the item (Move up/down) releasing its capture is not a cancel,
		// so the item keeps its hover highlight.
		internal void Cancel()
		{
			if (!pressed && !keyboardPressed) return;
			pressed = keyboardPressed = false;
			VisualStateManager.GoToState(owner, "Normal", true);
		}
		internal bool KeyDown(KeyRoutedEventArgs args)
		{
			if (args.Handled || args.Key is not (VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA)) return false;
			keyboardPressed = true; args.Handled = true;
			VisualStateManager.GoToState(owner, "Pressed", true); return true;
		}
		internal bool KeyUp(KeyRoutedEventArgs args)
		{
			if (args.Handled || args.Key is not (VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA)) return false;
			var activate = keyboardPressed; Cancel(); args.Handled = true;
			if (activate) invoke(); return true;
		}
	}

	internal sealed partial class PersistentColumnMenuItemPeer(PersistentColumnMenuItem owner) : MenuFlyoutItemAutomationPeer(owner), IInvokeProvider
	{
		protected override object GetPatternCore(PatternInterface patternInterface)
			=> patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);
		void IInvokeProvider.Invoke() => owner.DispatcherQueue.TryEnqueue(owner.Invoke);
	}
}
