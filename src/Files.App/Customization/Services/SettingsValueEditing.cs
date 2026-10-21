// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.System;
using WinRT;

namespace Files.App.Helpers
{
	internal static class SettingsValueEditing
	{
		private static readonly ConditionalWeakTable<FrameworkElement, Session> sessions = new();
		private sealed record EditCallbacks(Action Commit, Action? Cancel, bool CommitOnPointer);
		private static readonly ConditionalWeakTable<Control, EditCallbacks> callbacks = new();
		private static readonly ConditionalWeakTable<Control, object> excluded = new();

		// A box that handles Enter and Escape itself, such as a tag's name box, which keeps a name it can't save until it is changed.
		// Clicking elsewhere still finishes its edit like any other box's.
		internal static void Exclude(Control editor) => excluded.AddOrUpdate(editor, new object());

		internal static void Register(Control editor, Action commit, Action? cancel = null, bool commitOnPointer = true)
		{
			callbacks.Remove(editor);
			callbacks.Add(editor, new(commit, cancel, commitOnPointer));
		}

		internal static void Attach(FrameworkElement scope, bool useScopeEvents = false)
			=> sessions.GetValue(scope, element => new Session(element, useScopeEvents));

		private sealed class Session
		{
			private sealed record Edit(Control Control, TextBox Input, string Text, double NumberValue);
			private readonly FrameworkElement scope;
			private readonly bool useScopeEvents;
			private readonly TypedEventHandler<UIElement, GettingFocusEventArgs> gettingFocus;
			private readonly KeyEventHandler previewKeyDown;
			private readonly KeyEventHandler previewKeyUp;
			private readonly PointerEventHandler pointerPressed;
			private UIElement? root;
			private Edit? active;
			private Edit? keyEdit;
			private ComboBox? keyCombo;
			private VirtualKey? finishingKey;

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			internal Session(FrameworkElement scope, bool useScopeEvents)
			{
				this.scope = scope;
				this.useScopeEvents = useScopeEvents;
				scope.IsTabStop = true;
				scope.UseSystemFocusVisuals = false;
				gettingFocus = (_, args) => Capture(args.NewFocusedElement as DependencyObject);
				previewKeyDown = (_, args) => PreviewKeyDown(args);
				previewKeyUp = (_, args) => PreviewKeyUp(args);
				pointerPressed = (_, args) => PointerPressed(args);
				scope.Loaded += (_, _) => Hook();
				scope.Unloaded += (_, _) => Unhook();
				if (scope.IsLoaded) Hook();
			}

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			[DynamicWindowsRuntimeCast(typeof(UIElement))]
			private void Hook()
			{
				var content = useScopeEvents ? scope : scope.XamlRoot?.Content as UIElement;
				if (content is null || ReferenceEquals(root, content)) return;
				Unhook();
				root = content;
				root.GettingFocus += gettingFocus;
				root.AddHandler(UIElement.PreviewKeyDownEvent, previewKeyDown, true);
				root.AddHandler(UIElement.PreviewKeyUpEvent, previewKeyUp, true);
				root.AddHandler(UIElement.PointerPressedEvent, pointerPressed, true);
				Capture(FocusManager.GetFocusedElement(scope.XamlRoot) as DependencyObject);
			}

			private void Unhook()
			{
				if (root is not null)
				{
					root.GettingFocus -= gettingFocus;
					root.RemoveHandler(UIElement.PreviewKeyDownEvent, previewKeyDown);
					root.RemoveHandler(UIElement.PreviewKeyUpEvent, previewKeyUp);
					root.RemoveHandler(UIElement.PointerPressedEvent, pointerPressed);
				}
				root = null;
				active = null;
				ClearKey();
			}

			[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
			private static DependencyObject? Parent(DependencyObject element)
				=> VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

			private static bool Contains(DependencyObject parent, DependencyObject? element)
			{
				for (; element is not null; element = Parent(element))
					if (ReferenceEquals(element, parent)) return true;
				return false;
			}

			[DynamicWindowsRuntimeCast(typeof(AutoSuggestBox))]
			[DynamicWindowsRuntimeCast(typeof(NumberBox))]
			[DynamicWindowsRuntimeCast(typeof(TextBox))]
			private Control? Editor(DependencyObject? element, out TextBox? input)
			{
				input = null;
				if (!Contains(scope, element)) return null;
				for (; element is not null && element != scope; element = Parent(element))
				{
					if (element is AutoSuggestBox) return null;
					if (element is NumberBox number && number.IsEnabled)
					{
						input ??= FindInput(number);
						return input is null ? null : number;
					}
					if (element is TextBox text) input ??= text;
				}
				return input is { IsReadOnly: false, IsEnabled: true, AcceptsReturn: false } ? input : null;
			}

			[DynamicWindowsRuntimeCast(typeof(TextBox))]
			private static TextBox? FindInput(DependencyObject element)
			{
				if (element is TextBox text) return text;
				for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
					if (FindInput(VisualTreeHelper.GetChild(element, index)) is { } input) return input;
				return null;
			}

			[DynamicWindowsRuntimeCast(typeof(ComboBox))]
			private ComboBox? Combo(DependencyObject? element)
			{
				if (!Contains(scope, element)) return null;
				for (; element is not null && element != scope; element = Parent(element))
					if (element is ComboBox combo) return combo;
				return null;
			}

			[DynamicWindowsRuntimeCast(typeof(NumberBox))]
			private Edit Start(Control control, TextBox input)
				=> new(control, input, input.Text, control is NumberBox number ? number.Value : double.NaN);

			private void Capture(DependencyObject? element)
			{
				var control = Editor(element, out var input);
				if (control is null) { active = null; return; }
				if (!ReferenceEquals(active?.Control, control)) active = Start(control, input!);
			}

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			private void PreviewKeyDown(KeyRoutedEventArgs args)
			{
				if (args.Key is not (VirtualKey.Enter or VirtualKey.Escape)) return;
				if (finishingKey == args.Key && args.KeyStatus.WasKeyDown)
				{
					if (keyEdit is not null || keyCombo is not null && !ReferenceEquals(keyCombo, Combo(args.OriginalSource as DependencyObject))) args.Handled = true;
					return;
				}
				ClearKey();
				var control = Editor(args.OriginalSource as DependencyObject, out var input);
				if (control is not null && excluded.TryGetValue(control, out _)) return;
				if (control is not null)
				{
					keyEdit = ReferenceEquals(active?.Control, control) ? active : Start(control, input!);
					finishingKey = args.Key;
					args.Handled = true;
					Finish(keyEdit!, args.Key == VirtualKey.Escape, true);
				}
				else if (args.Key == VirtualKey.Enter && Combo(args.OriginalSource as DependencyObject) is { } combo)
				{
					keyCombo = combo;
					finishingKey = args.Key;
				}
			}

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			private void PreviewKeyUp(KeyRoutedEventArgs args)
			{
				if (args.Key != finishingKey) return;
				// A saved value can rebuild the inspector before key-up. Keep that release off the next control.
				if (keyEdit is not null ||
					keyCombo is not null && !ReferenceEquals(keyCombo, Combo(FocusManager.GetFocusedElement(scope.XamlRoot) as DependencyObject)))
					args.Handled = true;
				ClearKey();
			}

			private void ClearKey()
			{
				finishingKey = null;
				keyEdit = null;
				keyCombo = null;
			}

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			private void PointerPressed(PointerRoutedEventArgs args)
			{
				var control = Editor(FocusManager.GetFocusedElement(scope.XamlRoot) as DependencyObject, out var input);
				if (control is null || Contains(control, args.OriginalSource as DependencyObject)) return;
				var edit = ReferenceEquals(active?.Control, control) ? active! : Start(control, input!);
				Finish(edit, false, false);
			}

			[DynamicWindowsRuntimeCast(typeof(DependencyObject))]
			[DynamicWindowsRuntimeCast(typeof(NumberBox))]
			[DynamicWindowsRuntimeCast(typeof(UIElement))]
			private void Finish(Edit edit, bool cancel, bool keyboard)
			{
				if (edit.Control is NumberBox number)
				{
					if (cancel) { number.Value = edit.NumberValue; number.Text = edit.Text; }
					else number.Text = edit.Input.Text; // Use NumberBox's formatter, expression parser and range validation.
				}
				else if (cancel) edit.Input.Text = edit.Text;
				else edit.Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
				if (callbacks.TryGetValue(edit.Control, out var actions))
				{
					if (cancel) actions.Cancel?.Invoke();
					else if (keyboard || actions.CommitOnPointer) actions.Commit();
				}
				edit.Input.Select(edit.Input.Text.Length, 0);
				if (keyboard || ReferenceEquals(Editor(FocusManager.GetFocusedElement(scope.XamlRoot) as DependencyObject, out _), edit.Control))
				{
					if (!scope.Focus(FocusState.Programmatic))
						(FocusManager.FindFirstFocusableElement(scope) as UIElement)?.Focus(FocusState.Programmatic);
				}
			}
		}
	}
}
