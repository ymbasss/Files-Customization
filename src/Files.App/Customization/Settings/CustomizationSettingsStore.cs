// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Windows.Storage;

namespace Files.App.Services.Settings
{
	[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
	[JsonSerializable(typeof(Dictionary<string, string>))]
	internal sealed partial class CustomizationJsonContext : JsonSerializerContext { }

	internal sealed class CustomizationSettingsStore
	{
		private static readonly Lazy<CustomizationSettingsStore> instance = new(() => new());
		internal static CustomizationSettingsStore Instance => instance.Value;
		// Folders whose columns follow the default layout although they keep their own sort, group or view mode (Column details).
		internal const string ColumnDefaultFoldersKey = "ColumnDefaultFolders";
		// Folders that keep their own column widths: automatic fitting leaves them alone (Column details).
		internal const string ColumnOwnWidthFoldersKey = "ColumnOwnWidthFolders";
		// Sidebar groups the user left closed.
		internal const string SidebarCollapsedGroupsKey = "SidebarCollapsedGroups";
		// The values of the app settings Column details changes, kept while it is switched on or off (see CustomizationSettingsPage.SwitchLinkedSettings).
		internal const string LinkedAppSettingsKey = "LinkedAppSettings";
		private static bool IsKnownKey(string key) =>
			key is nameof(IAppearanceSettingsService.SidebarCustomization) or nameof(IAppearanceSettingsService.CustomizationPreferences) or
				ColumnDefaultFoldersKey or ColumnOwnWidthFoldersKey or SidebarCollapsedGroupsKey or LinkedAppSettingsKey;
		private readonly object gate = new();
		private readonly Dictionary<string, string> values;
		private readonly string path;
		private readonly Timer saveTimer;
		private readonly SemaphoreSlim writer = new(1, 1);
		private long version;
		private long savedVersion;
		private readonly bool readFailed;
		private readonly HashSet<string> dirtyKeys = new(StringComparer.Ordinal);

		private CustomizationSettingsStore()
		{
			path = Path.Combine(ApplicationData.Current.LocalFolder.Path, Constants.LocalSettings.SettingsFolderName, "customization.json");
			// Settings saved under the earlier name move to the new file.
			var earlierPath = Path.Combine(Path.GetDirectoryName(path)!, "custom-features.json");
			try { if (!File.Exists(path) && File.Exists(earlierPath)) File.Move(earlierPath, path); }
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				values = File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), CustomizationJsonContext.Default.DictionaryStringString) ?? [] : [];
			}
			catch (JsonException)
			{
				try { File.Copy(path, path + ".invalid-" + Guid.NewGuid().ToString("N")); }
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { readFailed = true; }
				values = [];
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Leave the unreadable file intact; this session keeps settings in memory only.
				Debug.WriteLine($"Customization settings could not be read: {exception.Message}");
				readFailed = true;
				values = [];
			}
			foreach (var key in values.Where(pair => pair.Value is null).Select(pair => pair.Key).ToArray())
			{
				values.Remove(key);
				version++;
			}
			// The preferences' earlier key and master-switch name.
			if (values.Remove("CustomFeaturePreferences", out var earlier))
			{
				values.TryAdd(nameof(IAppearanceSettingsService.CustomizationPreferences), earlier.Replace("CustomFeaturesEnabled=", "CustomizationEnabled="));
				version++;
			}
			foreach (var key in values.Keys.Where(key => !IsKnownKey(key)).ToArray())
			{
				values.Remove(key);
				version++;
			}
			if (values.TryGetValue(nameof(IAppearanceSettingsService.CustomizationPreferences), out var preferences))
			{
				var retained = CustomizationPreferences.RetainActiveFields(preferences);
				if (retained != preferences) { values[nameof(IAppearanceSettingsService.CustomizationPreferences)] = retained; version++; }
			}
			saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
			if (version > 0) saveTimer.Change(300, Timeout.Infinite);
			AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
		}

		internal bool TryRead(string key, out string value)
		{
			lock (gate) return values.TryGetValue(key, out value!);
		}

		internal string Read(string key, string legacyValue)
		{
			lock (gate)
			{
				if (values.TryGetValue(key, out var value)) return value;
				value = legacyValue;
				values[key] = value;
				dirtyKeys.Add(key);
				version++;
				saveTimer.Change(300, Timeout.Infinite);
				return value;
			}
		}

		internal bool Write(string key, string value)
		{
			lock (gate)
			{
				if (values.TryGetValue(key, out var previous) && previous == value) return false;
				values[key] = value;
				dirtyKeys.Add(key);
				version++;
				saveTimer.Change(300, Timeout.Infinite);
				return true;
			}
		}

		private void Flush()
		{
			if (readFailed) return;
			writer.Wait();
			using var mutex = new Mutex(false, $"Files-{AppLifecycleHelper.AppEnvironment}-Customization");
			var ownsMutex = false;
			try
			{
				try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
				catch (AbandonedMutexException) { ownsMutex = true; }

				Dictionary<string, string> snapshot;
				HashSet<string> changedKeys;
				long capturedVersion;
				lock (gate)
				{
					if (version == savedVersion) return;
					snapshot = new(values);
					changedKeys = new(dirtyKeys, StringComparer.Ordinal);
					capturedVersion = version;
				}

				// Keep settings that another Files process saved for keys this process did not change.
				try
				{
					if (File.Exists(path) && JsonSerializer.Deserialize(File.ReadAllText(path), CustomizationJsonContext.Default.DictionaryStringString) is { } saved)
					{
						foreach (var pair in saved)
						{
							if (pair.Value is not null && !changedKeys.Contains(pair.Key) && IsKnownKey(pair.Key))
								snapshot[pair.Key] = pair.Value;
						}
					}
				}
				catch (JsonException)
				{
				}

				var temporary = path + "." + Environment.ProcessId + ".tmp";
				File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, CustomizationJsonContext.Default.DictionaryStringString));
				File.Move(temporary, path, true);
				lock (gate)
				{
					savedVersion = capturedVersion;
					if (version == capturedVersion)
						dirtyKeys.Clear();
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
				NotSupportedException or InvalidOperationException)
			{
				Debug.WriteLine($"Customization settings could not be saved: {exception.Message}");
			}
			finally
			{
				if (ownsMutex)
					mutex.ReleaseMutex();
				writer.Release();
			}
		}
	}

	internal sealed partial class AppearanceSettingsService
	{
		private string ReadCustomSetting([CallerMemberName] string name = "")
		{
			var store = CustomizationSettingsStore.Instance;
			return store.TryRead(name, out var value) ? value : store.Read(name, Get("", name));
		}
		private void WriteCustomSetting(string value, [CallerMemberName] string name = "")
		{
			if (!CustomizationSettingsStore.Instance.Write(name, value)) return;
			RaiseOnSettingChangedEvent(this, new SettingChangedEventArgs(name, value));
			OnPropertyChanged(name);
		}
		public string SidebarCustomization { get => ReadCustomSetting(); set => WriteCustomSetting(value); }
		public string CustomizationPreferences { get => ReadCustomSetting(); set => WriteCustomSetting(value); }
	}
}
