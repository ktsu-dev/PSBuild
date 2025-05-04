// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace PSBuild.BuildManagement;

using Microsoft.Extensions.Logging;

using PSBuild.ReleaseManagement;
using PSBuild.Utilities;
using PSBuild.VersionManagement;

/// <summary>
/// Coordinates the different managers to execute common build and release workflows.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BuildWorkflow"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging messages.</param>
/// <param name="buildManager">The build manager to use for build operations.</param>
/// <param name="versionManager">The version manager to use for version operations.</param>
/// <param name="releaseManager">The release manager to use for release operations.</param>
/// <param name="commandRunner">The command runner to use for running external commands.</param>
public class BuildWorkflow(
	ILogger<BuildWorkflow> logger,
	BuildManager buildManager,
	VersionManager versionManager,
	ReleaseManager releaseManager,
	CommandRunner commandRunner)
{
	private readonly ILogger<BuildWorkflow> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly BuildManager _buildManager = buildManager ?? throw new ArgumentNullException(nameof(buildManager));
	private readonly VersionManager _versionManager = versionManager ?? throw new ArgumentNullException(nameof(versionManager));
	private readonly ReleaseManager _releaseManager = releaseManager ?? throw new ArgumentNullException(nameof(releaseManager));
	private readonly CommandRunner _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));

	/// <summary>
	/// Initializes the build environment.
	/// </summary>
	/// <param name="workspacePath">The workspace path to use for the build.</param>
	/// <returns>A build configuration with default settings.</returns>
	public BuildConfiguration InitializeBuildEnvironment(string workspacePath)
	{
		_logger.LogInformation("Initializing build environment");

		// Initialize the build environment with default settings
		_buildManager.InitializeBuildEnvironment();

		// Get the current version from Git
		var versionInfo = _versionManager.GetVersionFromGit(workspacePath);

		// Create build configuration options with default settings
		var options = new BuildConfigurationOptions
		{
			WorkspacePath = workspacePath,
			GitSha = versionInfo.FullSha,
			GitRef = $"refs/heads/{versionInfo.BranchName}",
			AssetPatterns = ["*.nupkg", "*.zip", "*.txt"]
		};

		// Create and return build configuration
		return _buildManager.GetBuildConfigurationAsync(options).GetAwaiter().GetResult();
	}

	/// <summary>
	/// Runs a full build workflow for a PowerShell module.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="skipTests">Whether to skip running tests.</param>
	/// <returns>True if the build was successful, false otherwise.</returns>
	public bool RunPowerShellModuleBuild(BuildConfiguration config, string moduleName, bool skipTests = false)
	{
		_logger.LogInformation($"Starting PowerShell module build workflow for {moduleName}");

		try
		{
			// Update the version in all relevant files
			var versionInfo = _versionManager.GetVersionFromGit(config.WorkspacePath);
			config.Version = versionInfo.Version;

			_logger.LogInformation($"Building version {config.Version}");

			// Update version in the module manifest and other files
			if (!_versionManager.UpdateVersionInFiles(config.WorkspacePath, config.Version))
			{
				_logger.LogError("Failed to update version in files");
				return false;
			}

			// Ensure output and staging directories exist
			Directory.CreateDirectory(config.OutputPath);
			Directory.CreateDirectory(config.StagingPath);

			// Run tests if not skipped
			if (!skipTests)
			{
				_logger.LogInformation("Running Pester tests");

				// Get test directory
				var testDir = Path.Combine(config.WorkspacePath, "Tests");
				if (Directory.Exists(testDir))
				{
					var testResult = _commandRunner.RunCommand(
						"powershell",
						$"-Command \"Import-Module Pester; Invoke-Pester -Path '{testDir}' -PassThru | ConvertTo-Json\"",
						config.WorkspacePath);

					if (testResult.ExitCode != 0)
					{
						_logger.LogError($"Tests failed: {testResult.Error}");
						return false;
					}

					_logger.LogInformation("Tests completed successfully");
				}
				else
				{
					_logger.LogWarning("No test directory found, skipping tests");
				}
			}

			// Create release artifacts
			var modulePath = Path.Combine(config.WorkspacePath, moduleName);
			var artifacts = _releaseManager.CreateReleaseArtifacts(config, modulePath, config.Version);

			if (artifacts.Count == 0)
			{
				_logger.LogError("Failed to create release artifacts");
				return false;
			}

			_logger.LogInformation($"Created {artifacts.Count} release artifacts");

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"PowerShell module build failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Runs a full build workflow for a .NET project or solution.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="projectPath">The path to the project or solution file.</param>
	/// <param name="configuration">The build configuration to use (Debug, Release, etc.).</param>
	/// <param name="skipTests">Whether to skip running tests.</param>
	/// <returns>True if the build was successful, false otherwise.</returns>
	public bool RunDotNetBuild(
		BuildConfiguration config,
		string projectPath,
		string configuration = "Release",
		bool skipTests = false)
	{
		_logger.LogInformation($"Starting .NET build workflow for {projectPath}");

		try
		{
			// Update the version in all relevant files
			var versionInfo = _versionManager.GetVersionFromGit(config.WorkspacePath);
			config.Version = versionInfo.Version;

			_logger.LogInformation($"Building version {config.Version}");

			// Update version in project files
			if (!_versionManager.UpdateVersionInFiles(config.WorkspacePath, config.Version))
			{
				_logger.LogError("Failed to update version in files");
				return false;
			}

			// Run the build workflow
			if (!_buildManager.InvokeBuildWorkflow(config, projectPath, configuration, skipTests))
			{
				_logger.LogError("Build workflow failed");
				return false;
			}

			_logger.LogInformation("Build workflow completed successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($".NET build failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Publishes a release to GitHub and NuGet.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="tagVersion">The tag/version to release.</param>
	/// <param name="publishToNuGet">Whether to publish to NuGet.</param>
	/// <param name="publishToPSGallery">Whether to publish to PowerShell Gallery.</param>
	/// <returns>A task representing the asynchronous operation, with a value indicating whether it was successful.</returns>
	public async Task<bool> PublishReleaseAsync(
		BuildConfiguration config,
		string tagVersion,
		bool publishToNuGet = true,
		bool publishToPSGallery = false)
	{
		_logger.LogInformation($"Publishing release {tagVersion}");

		try
		{
			// Skip if this is not an official build and should release
			if (!config.ShouldRelease)
			{
				_logger.LogInformation("Skipping release as this is not an official releasable build");
				return true;
			}

			// Extract release notes from changelog
			var releaseNotes = _releaseManager.ExtractReleaseNotes(config, tagVersion);

			// Create GitHub release if token is available
			if (!string.IsNullOrEmpty(config.GithubToken))
			{
				// Find all assets to include in the release
				string[] assetPatterns = [.. config.AssetPatterns.Select(pattern =>
					Path.Combine(config.StagingPath, pattern))];

				var release = await _releaseManager.CreateGitHubReleaseAsync(
					config,
					tagVersion.StartsWith("v") ? tagVersion : $"v{tagVersion}",
					releaseNotes,
					assetPatterns).ConfigureAwait(false);

				if (release == null)
				{
					_logger.LogError("Failed to create GitHub release");
					return false;
				}

				_logger.LogInformation($"Created GitHub release at {release.HtmlUrl}");
			}
			else
			{
				_logger.LogWarning("No GitHub token available, skipping GitHub release");
			}

			// Publish to NuGet if requested
			if (publishToNuGet && !string.IsNullOrEmpty(config.NuGetApiKey))
			{
				var nugetPackages = Directory.GetFiles(config.StagingPath, "*.nupkg");
				if (nugetPackages.Length > 0)
				{
					var success = await _releaseManager.PublishNuGetPackagesAsync(
						config,
						[Path.Combine(config.StagingPath, "*.nupkg")],
						config.NuGetApiKey).ConfigureAwait(false);

					if (!success)
					{
						_logger.LogError("Failed to publish to NuGet");
						return false;
					}

					_logger.LogInformation("Published to NuGet successfully");
				}
				else
				{
					_logger.LogWarning("No NuGet packages found to publish");
				}
			}

			// Publish to PowerShell Gallery if requested
			if (publishToPSGallery && !string.IsNullOrEmpty(config.NuGetApiKey))
			{
				// Find PowerShell module directories
				var psd1Files = Directory.GetFiles(config.WorkspacePath, "*.psd1", SearchOption.AllDirectories);
				foreach (var psd1File in psd1Files)
				{
					var modulePath = Path.GetDirectoryName(psd1File);
					if (modulePath != null)
					{
						var success = _releaseManager.PublishToPowerShellGallery(
							config,
							modulePath,
							config.NuGetApiKey);

						if (!success)
						{
							_logger.LogError($"Failed to publish module {Path.GetFileName(modulePath)} to PowerShell Gallery");
							return false;
						}

						_logger.LogInformation($"Published module {Path.GetFileName(modulePath)} to PowerShell Gallery");
					}
				}
			}

			_logger.LogInformation("Release published successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Failed to publish release: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Increments the version and tags the repository.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="part">The version part to increment (0=major, 1=minor, 2=patch).</param>
	/// <param name="preRelease">The pre-release suffix to use.</param>
	/// <param name="createTag">Whether to create a Git tag for the new version.</param>
	/// <returns>The new version information.</returns>
	public VersionInfo IncrementVersion(
		BuildConfiguration config,
		int part = 2,
		string? preRelease = null,
		bool createTag = false)
	{
		_logger.LogInformation($"Incrementing version (part: {part})");

		try
		{
			// Increment the version
			var newVersion = _versionManager.IncrementVersionPart(
				config.WorkspacePath,
				part,
				preRelease,
				true,
				createTag);

			_logger.LogInformation($"Version incremented to {newVersion.Version}");

			// Update the config version
			config.Version = newVersion.Version;

			return newVersion;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Failed to increment version: {ex.Message}");
			throw;
		}
	}
}
