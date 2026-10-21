// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using System.Runtime.CompilerServices;
using WinRT;

namespace Files.App.Helpers
{
	// Theme brushes for properties set in code. Application.Current.Resources gives the brushes of Windows' theme, which can differ
	// from Files' own theme (light text on a light page), so the property is bound to a hidden element in the given panel that holds
	// the brush as a theme resource: it follows the panel's theme, also when the theme changes.
	internal static class CustomizationThemeBrushes
	{
		private static readonly ConditionalWeakTable<Panel, Dictionary<string, Border>> sources = new();

		[DynamicWindowsRuntimeCast(typeof(Border))]
		internal static T Bind<T>(Panel host, T target, DependencyProperty property, string key) where T : DependencyObject
		{
			var keys = sources.GetOrCreateValue(host);
			if (!keys.TryGetValue(key, out var source))
			{
				source = (Border)XamlReader.Load(
					$"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Visibility=\"Collapsed\" Background=\"{{ThemeResource {key}}}\" />");
				host.Children.Add(source);
				keys[key] = source;
			}
			BindingOperations.SetBinding(target, property, new Binding { Source = source, Path = new PropertyPath(nameof(Border.Background)) });
			return target;
		}
	}
}
