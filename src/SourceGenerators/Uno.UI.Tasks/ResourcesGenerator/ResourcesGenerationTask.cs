﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Windows.ApplicationModel.Resources.Core;

namespace Uno.UI.Tasks.ResourcesGenerator;

/// <summary>
/// Parse Resources.resw files and generate the corresponding Localizable.strings (iOS) and Strings.xml (Android) files.
/// </summary>
public class ResourcesGenerationTask_v0 : Task
{
	private const string CommentPattern = @"
	WARNING : THIS FILE HAS BEEN GENERATED BY A TOOL DO NOT UPDATE MANUALLY

	Tool name: {0}
	Source: {1}
";
	[Required]
	public ITaskItem[] Resources { get; set; }

	public string TargetPlatform { get; set; }

	public bool EnableTraceLogging { get; set; }

	public bool IsUnoHead { get; set; }

	[Required]
	public string ProjectName { get; set; }

	[Required]
	public string TargetProjectDirectory { get; set; }

	[Required]
	public string IntermediateOutputPath { get; set; }

	[Required]
	public string DefaultLanguage { get; set; }

	[Output]
	public ITaskItem[] GeneratedFiles { get; set; }

	private string OutputPath => Path.Combine(TargetProjectDirectory, IntermediateOutputPath, "g", "ResourcesGenerator");

	public override bool Execute()
	{
		Log.LogMessage($"Generating resources for platform : {TargetPlatform}");

		try
		{
			GeneratedFiles = Resources
				// TODO: Add support for other resources file names
				.Where(resource => resource.ItemSpec?.EndsWith("resw", StringComparison.Ordinal) ?? false)
				// TODO: Merge duplicates (based on file name and qualifiers)
				.SelectMany(GetResourcesForItem)
				.Where(r => r != null)
				.ToArray();

			return true;
		}
		catch (Exception ex)
		{
			Log.LogError($"Failed to generate resources. Details: {ex.Message}");
		}

		return false;
	}

	private IEnumerable<ITaskItem> GetResourcesForItem(ITaskItem resource)
	{
		TraceLog($"Resources file found : {resource.ItemSpec}");

		var resourceCandidate = ResourceCandidate.Parse(resource.ItemSpec, resource.ItemSpec);

		var language = resourceCandidate.GetQualifierValue("language");
		if (language == null)
		{
			// TODO: Add support for resources without a language qualifier
			TraceLog("No language found, resources ignored");
			yield break;
		}

		TraceLog($"Language found : {language}");

		var resourceFile = resource.ItemSpec;
		var sourceLastWriteTime = new FileInfo(resourceFile).LastWriteTimeUtc;
		var resources = WindowsResourcesReader.Read(resourceFile);
		var comment = string.Format(CultureInfo.InvariantCulture, CommentPattern, this.GetType().Name, resourceFile);

		TraceLog($"{resources.Count} resources found");

		if (Path.GetFileNameWithoutExtension(resource.ItemSpec).Equals("Resources", StringComparison.OrdinalIgnoreCase))
		{
			if (TargetPlatform == "android")
			{
				yield return GenerateAndroidResources(language, sourceLastWriteTime, resources, comment, resource);
			}
			else if (TargetPlatform == "ios")
			{
				yield return GenerateiOSResources(language, sourceLastWriteTime, resources, comment);
			}
		}

		yield return GenerateUnoPRIResources(language, sourceLastWriteTime, resources, comment, resource);
	}

	private ITaskItem GenerateUnoPRIResources(string language, DateTime sourceLastWriteTime, Dictionary<string, string> resources, string comment, ITaskItem resource)
	{
		string buildBasePath()
		{
			if(resource.GetMetadata("TargetPath") is { Length: > 0 } targetPath)
			{
				return Path.GetDirectoryName(targetPath);
			}
			else if (Path.IsPathRooted(resource.ItemSpec))
			{
				string definingProjectDirectory = resource.GetMetadata("DefiningProjectDirectory");
				if (resource.ItemSpec.StartsWith(definingProjectDirectory, StringComparison.Ordinal))
				{
					return resource.ItemSpec.Replace(definingProjectDirectory, "");
				}
				else if (resource.ItemSpec.StartsWith(TargetProjectDirectory, StringComparison.Ordinal))
				{
					return resource.ItemSpec.Replace(TargetProjectDirectory, "");
				}
				else
				{
					return language;
				}
			}
			else
			{
				return Path.GetDirectoryName(resource.ItemSpec);
			}
		}


		var resourceMapName = Path.GetFileNameWithoutExtension(resource.ItemSpec);
		var logicalTargetPath = Path.Combine(buildBasePath(), resourceMapName + ".upri");
		var actualTargetPath = Path.Combine(OutputPath, logicalTargetPath);

		Directory.CreateDirectory(Path.GetDirectoryName(actualTargetPath));

		var targetLastWriteTime = new FileInfo(actualTargetPath).LastWriteTimeUtc;

		if (sourceLastWriteTime > targetLastWriteTime)
		{
			var libraryName = IsUnoHead ? "" : ProjectName + "/";
			var qualifiedResourceMapName = libraryName + resourceMapName;

			TraceLog($"Writing resources to {actualTargetPath} ({qualifiedResourceMapName})");

			UnoPRIResourcesWriter.Write(qualifiedResourceMapName, language, resources, actualTargetPath, comment);
		}
		else
		{
			TraceLog($"Skipping unmodified file {actualTargetPath}");
		}

		return new TaskItem
		(
			actualTargetPath,
			new Dictionary<string, string>()
			{
				{ "UnoResourceTarget", "Uno" },
				{ "LogicalName", logicalTargetPath.Replace(Path.DirectorySeparatorChar, '.') }
			}
		);
	}

	private ITaskItem GenerateiOSResources(string language, DateTime sourceLastWriteTime, Dictionary<string, string> resources, string comment)
	{
		var logicalTargetPath = Path.Combine($"{language}.lproj", "Localizable.strings"); // this path is required by Xamarin
		var actualTargetPath = Path.Combine(OutputPath, logicalTargetPath);

		var targetLastWriteTime = new FileInfo(actualTargetPath).LastWriteTimeUtc;

		if (sourceLastWriteTime > targetLastWriteTime)
		{
			TraceLog($"Writing resources to {actualTargetPath}");

			iOSResourcesWriter.Write(resources, actualTargetPath, comment);
		}
		else
		{
			TraceLog($"Skipping unmodified file {actualTargetPath}");
		}

		return new TaskItem
		(
			actualTargetPath,
			new Dictionary<string, string>()
			{
				{ "UnoResourceTarget", "iOS" },
				{ "LogicalName", logicalTargetPath }
			}
		);
	}

	private ITaskItem GenerateAndroidResources(string language, DateTime sourceLastWriteTime, Dictionary<string, string> resources, string comment, ITaskItem resource)
	{
		string localizedDirectory;
		if (language == DefaultLanguage)
		{
			// Resources targeting the default application language must go in a directory called "values" (no language extension).
			localizedDirectory = "values";
		}
		else
		{
			// More info about localized resources file structure and codes on Android:
			// https://developer.android.com/guide/topics/resources/providing-resources#AlternativeResources
			var cultureWithRegion = new CultureInfo(language);
			var languageOnly = cultureWithRegion;
			while (languageOnly.Parent != CultureInfo.InvariantCulture)
			{
				languageOnly = languageOnly.Parent;
			}

			localizedDirectory = cultureWithRegion.LCID < 255
				? $"values-{languageOnly.IetfLanguageTag}" // No Region info
				: $"values-b+{languageOnly.IetfLanguageTag}+{cultureWithRegion.LCID}";
		}

		// The file name have to be unique, otherwise it could be overwritten by a file with the same named defined directly in the application's head
		var resourceMapName = Path.GetFileNameWithoutExtension(resource.ItemSpec)?.ToLowerInvariant();
		var logicalTargetPath = Path.Combine(localizedDirectory, $"{resourceMapName}_resw-strings.xml");
		var actualTargetPath = Path.Combine(OutputPath, logicalTargetPath);

		var targetLastWriteTime = new FileInfo(actualTargetPath).LastWriteTimeUtc;

		if (sourceLastWriteTime > targetLastWriteTime)
		{
			TraceLog($"Writing resources to {actualTargetPath}");

			AndroidResourcesWriter.Write(resources, actualTargetPath, comment);
		}
		else
		{
			TraceLog($"Skipping unmodified file {actualTargetPath}");
		}

		return new TaskItem
		(
			actualTargetPath,
			new Dictionary<string, string>()
			{
				{ "UnoResourceTarget", "Android" },
				{ "LogicalName", logicalTargetPath }
			}
		);
	}
	private void TraceLog(string message)
	{
		if (EnableTraceLogging)
		{
			Log.LogMessage(message);
		}
	}
}
