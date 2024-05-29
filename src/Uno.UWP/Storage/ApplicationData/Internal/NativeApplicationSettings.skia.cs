﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#nullable enable

using Uno.Foundation.Logging;
using Windows.Storage;

namespace Uno.Storage;

partial class NativeApplicationSettings
{
	private static partial bool SupportsLocality() => true;

	private readonly Dictionary<string, string> _values = new();
	private string _folderPath;
	private string _filePath;

	partial void InitializePlatform()
	{
		var settingsFolderPath = ApplicationData.Current.GetSettingsFolderPath();

		_folderPath = settingsFolderPath;
		_filePath = Path.Combine(settingsFolderPath, $"{_locality}.dat");
		ReadFromFile();
	}

	public string? this[string key]
	{
		get
		{
			if (_values.TryGetValue(key, out var value))
			{
				return DataTypeSerializer.Deserialize(value);
			}

			return null;
		}
		set
		{
			if (value != null)
			{
				_values[key] = DataTypeSerializer.Serialize(value);
			}
			else
			{
				Remove(key);
			}

			WriteToFile();
		}
	}

	private void ReadFromFile()
	{
		try
		{

			if (File.Exists(_filePath))
			{
				using (var reader = new BinaryReader(File.OpenRead(_filePath)))
				{
					var count = reader.ReadInt32();

					if (this.Log().IsEnabled(LogLevel.Debug))
					{
						this.Log().Debug($"Reading {count} settings values");
					}

					for (int i = 0; i < count; i++)
					{
						var key = reader.ReadString();
						var value = reader.ReadString();

						_values[key] = value;
					}
				}
			}
			else
			{
				if (this.Log().IsEnabled(LogLevel.Debug))
				{
					this.Log().Debug($"File {_filePath} does not exist, skipping reading settings");
				}
			}
		}
		catch (Exception e)
		{
			if (this.Log().IsEnabled(LogLevel.Error))
			{
				this.Log().Error($"Failed to read settings from {_filePath}", e);
			}
		}
	}

	private void WriteToFile()
	{
		try
		{
			Directory.CreateDirectory(_folderPath);

			if (this.Log().IsEnabled(LogLevel.Debug))
			{
				this.Log().Debug($"Writing {_values.Count} settings to {_filePath}");
			}

			using (var writer = new BinaryWriter(File.OpenWrite(_filePath)))
			{
				writer.Write(_values.Count);

				foreach (var pair in _values)
				{
					writer.Write(pair.Key);
					writer.Write(pair.Value ?? "");
				}
			}
		}
		catch (Exception e)
		{
			if (this.Log().IsEnabled(LogLevel.Error))
			{
				this.Log().Error($"Failed to write settings to {_filePath}", e);
			}
		}
	}

	public ICollection<string> Keys
		=> _values.Keys;

	public ICollection<object> Values
		=> _values.Values.Select(DataTypeSerializer.Deserialize).ToList();

	public int Count
		=> _values.Count;

	public void Add(string key, object value)
	{
		if (ContainsKey(key))
		{
			throw new ArgumentException("An item with the same key has already been added.");
		}
		if (value != null)
		{
			_values.Add(key, DataTypeSerializer.Serialize(value));
			WriteToFile();
		}
	}

	public void Add(KeyValuePair<string, object> item)
		=> Add(item.Key, item.Value);

	public void Clear()
	{
		_values.Clear();
		WriteToFile();
	}

	public bool Contains(KeyValuePair<string, object> item)
		=> throw new NotSupportedException();

	public bool ContainsKey(string key)
		=> _values.ContainsKey(key);

	public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
		=> throw new NotSupportedException();

	public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
		=> _values.Select(v => new KeyValuePair<string, object>(v.Key, v.Value)).GetEnumerator();

	public bool Remove(string key)
	{
		var ret = _values.Remove(key);

		WriteToFile();

		return ret;
	}

	public bool TryGetValue(string key, out object? value)
	{
		if (_values.TryGetValue(key, out var innervalue))
		{
			value = DataTypeSerializer.Deserialize(innervalue);
			return true;
		}

		value = null;
		return false;
	}
}
