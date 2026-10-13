// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;
using WinRT;

namespace Files.App.Helpers
{
	public static class CustomShortcutArrow
	{
		// The shortcut badge of an item in a folder view.
		public static readonly DependencyProperty ItemPathProperty = DependencyProperty.RegisterAttached(
			"ItemPath", typeof(string), typeof(CustomShortcutArrow), new PropertyMetadata(null, Changed));
		public static string? GetItemPath(DependencyObject element) => (string?)element.GetValue(ItemPathProperty);
		public static void SetItemPath(DependencyObject element, string? value) => element.SetValue(ItemPathProperty, value);

		// The shortcut badge of an item in the search suggestions under the address bar.
		public static readonly DependencyProperty SuggestionPathProperty = DependencyProperty.RegisterAttached(
			"SuggestionPath", typeof(string), typeof(CustomShortcutArrow), new PropertyMetadata(null, Changed));
		public static string? GetSuggestionPath(DependencyObject element) => (string?)element.GetValue(SuggestionPathProperty);
		public static void SetSuggestionPath(DependencyObject element, string? value) => element.SetValue(SuggestionPathProperty, value);

		// Shortcut badges seen so far. While the feature is off they are only remembered, never changed.
		private static readonly ConditionalWeakTable<FrameworkElement, object> Badges = new();
		private static IAppearanceSettingsService? settings;
		private static bool applied;

		private static IAppearanceSettingsService Settings
		{
			get
			{
				if (settings is null)
				{
					settings = Ioc.Default.GetRequiredService<IUserSettingsService>().AppearanceSettingsService;
					settings.PropertyChanged += SettingsChanged;
				}
				return settings;
			}
		}

		private static CustomizationPreferences Options => CustomizationPreferences.Effective(Settings.CustomizationPreferences);

		[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
		private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
		{
			if (sender is not FrameworkElement element) return;
			Badges.AddOrUpdate(element, Badges);
			var options = Options;
			if (options.ShortcutArrowMode != 0) Apply(element, options);
		}

		private static void SettingsChanged(object? sender, PropertyChangedEventArgs args)
		{
			if (args.PropertyName != nameof(IAppearanceSettingsService.CustomizationPreferences)) return;
			var options = Options;
			// Nothing to restore while the feature stays off.
			if (options.ShortcutArrowMode == 0 && !applied) return;
			foreach (var (element, _) in Badges)
				element.DispatcherQueue.TryEnqueue(() =>
				{
					if (options.ShortcutArrowMode != 0) Apply(element, options);
					else element.ClearValue(UIElement.OpacityProperty);
				});
			applied = options.ShortcutArrowMode != 0;
		}

		private static void Apply(FrameworkElement element, CustomizationPreferences options)
		{
			applied = true;
			var suggestion = GetSuggestionPath(element);
			var hide = (suggestion is null || options.ShortcutArrowSearchSuggestions) &&
				CustomShortcutArrowPolicy.HideArrow(options.ShortcutArrowMode, suggestion ?? GetItemPath(element),
					Constants.UserEnvironmentPaths.DesktopPath, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
					options.ShortcutArrowDesktopSubfolders);
			if (hide) element.Opacity = 0;
			else element.ClearValue(UIElement.OpacityProperty);
		}
	}
}
