// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace PSBuild.BuildManagement;

using Microsoft.Extensions.Logging;

using Octokit;

using PSBuild.Utilities;

/// <summary>
/// Manages the build process for .NET applications.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BuildManager"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging messages.</param>
/// <param name="commandRunner">The command runner to use for running external commands.</param>
public class BuildManager(ILogger<BuildManager> logger, CommandRunner commandRunner)
{
	private readonly ILogger<BuildManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly CommandRunner _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));

	/// <summary>
	/// Initializes the build environment with standard settings.
	/// </summary>
	public void InitializeBuildEnvironment()
	{
		_logger.LogInformation("Initializing build environment");

		// Set .NET SDK environment variables
		Environment.SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");
		Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1");
		Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "true");

		_logger.LogInformation("Build environment initialized");
	}

	/// <summary>
	/// Gets the build configuration based on Git status and environment.
	/// </summary>
	/// <param name="options">The options to use for creating the build configuration.</param>
	/// <returns>A build configuration object.</returns>
	public async Task<BuildConfiguration> GetBuildConfigurationAsync(BuildConfigurationOptions options)
	{
		_logger.LogInformation("Creating build configuration");

		ArgumentNullException.ThrowIfNull(options);

		// Determine if this is an official repo (verify owner and ensure it's not a fork)
		var isOfficial = false;
		if (!string.IsNullOrEmpty(options.GithubToken))
		{
			try
			{
				var client = new GitHubClient(new ProductHeaderValue("PSBuild"))
				{
					Credentials = new Credentials(options.GithubToken)
				};

				var repo = await client.Repository.Get(options.GitHubOwner, options.GitHubRepo).ConfigureAwait(false);
				// Consider it official only if it's not a fork AND belongs to the expected owner
				isOfficial = !repo.Fork && repo.Owner.Login == options.ExpectedOwner;
				_logger.LogInformation($"Repository: {repo.FullName}, Is Fork: {repo.Fork}, Owner: {repo.Owner.Login}");
			}
			catch (Exception ex)
			{
				_logger.LogError($"Failed to check repository status: {ex.Message}. Assuming unofficial build.");
			}
		}

		_logger.LogInformation($"Is Official: {isOfficial}");

		// Determine if this is main branch and not tagged
		var isMain = options.GitRef == "refs/heads/main";

		var isTagged = false;
		using (var repo = new LibGit2Sharp.Repository(options.WorkspacePath))
		{
			isTagged = repo.Tags.Any(tag => tag.Target.Sha == options.GitSha);
		}

		var shouldRelease = isMain && !isTagged && isOfficial;

		// Check for .csx files (dotnet-script)
		var useDotnetScript = Directory.GetFiles(options.WorkspacePath, "*.csx", SearchOption.AllDirectories).Length > 0;

		// Setup paths
		var outputPath = Path.Combine(options.WorkspacePath, "output");
		var stagingPath = Path.Combine(options.WorkspacePath, "staging");

		// Setup artifact patterns
		var packagePattern = Path.Combine(stagingPath, "*.nupkg");
		var symbolsPattern = Path.Combine(stagingPath, "*.snupkg");
		var applicationPattern = Path.Combine(stagingPath, "*.zip");

		// Set build arguments
		var buildArgs = useDotnetScript ? "-maxCpuCount:1" : "";

		// Create configuration object
		var config = new BuildConfiguration
		{
			IsOfficial = isOfficial,
			IsMain = isMain,
			IsTagged = isTagged,
			ShouldRelease = shouldRelease,
			UseDotnetScript = useDotnetScript,
			OutputPath = outputPath,
			StagingPath = stagingPath,
			PackagePattern = packagePattern,
			SymbolsPattern = symbolsPattern,
			ApplicationPattern = applicationPattern,
			BuildArgs = buildArgs,
			WorkspacePath = options.WorkspacePath,
			DotnetVersion = GetDotNetSdkVersion(),
			ServerUrl = options.ServerUrl,
			GitRef = options.GitRef,
			GitSha = options.GitSha,
			GitHubOwner = options.GitHubOwner,
			GitHubRepo = options.GitHubRepo,
			GithubToken = options.GithubToken,
			NuGetApiKey = options.NuGetApiKey,
			ExpectedOwner = options.ExpectedOwner,
			Version = "1.0.0-pre.0", // Will be updated by version manager
			ReleaseHash = options.GitSha,
			ChangelogFile = options.ChangelogFile,
			AssetPatterns = [.. options.AssetPatterns]
		};

		_logger.LogInformation("Build configuration created");

		return config;
	}

	/// <summary>
	/// Runs the dotnet restore command to restore dependencies.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectOrSolutionPath">The path to the project or solution file. If null, uses all projects in the workspace.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	public bool DotNetRestore(BuildConfiguration config, string? projectOrSolutionPath = null)
	{
		try
		{
			_logger.LogInformation("Restoring NuGet packages");

			var targetPath = projectOrSolutionPath ?? "";
			var arguments = $"restore {targetPath}";

			var result = _commandRunner.RunCommand("dotnet", arguments, config.WorkspacePath);

			if (!result.Success)
			{
				_logger.LogError($"Failed to restore packages: {result.Error}");
				return false;
			}

			_logger.LogInformation("Packages restored successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error during package restore: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Builds the project or solution.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectOrSolutionPath">The path to the project or solution file. If null, uses all projects in the workspace.</param>
	/// <param name="configuration">The build configuration (Debug, Release, etc.).</param>
	/// <param name="outputPath">The output path for the build. If null, uses the default.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	public bool DotNetBuild(
		BuildConfiguration config,
		string? projectOrSolutionPath = null,
		string configuration = "Release",
		string? outputPath = null)
	{
		try
		{
			_logger.LogInformation($"Building solution in {configuration} configuration");

			var targetPath = projectOrSolutionPath ?? "";
			var output = outputPath ?? config.OutputPath;

			// Create the output directory if it doesn't exist
			if (!string.IsNullOrEmpty(output) && !Directory.Exists(output))
			{
				Directory.CreateDirectory(output);
			}

			var arguments = $"build {targetPath} --configuration {configuration}";

			if (!string.IsNullOrEmpty(output))
			{
				arguments += $" --output {output}";
			}

			if (!string.IsNullOrEmpty(config.BuildArgs))
			{
				arguments += $" {config.BuildArgs}";
			}

			var result = _commandRunner.RunCommand("dotnet", arguments, config.WorkspacePath);

			if (!result.Success)
			{
				_logger.LogError($"Failed to build: {result.Error}");
				return false;
			}

			_logger.LogInformation("Build completed successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error during build: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Runs tests for the specified project or solution.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectOrSolutionPath">The path to the project or solution file. If null, uses all test projects in the workspace.</param>
	/// <param name="configuration">The build configuration (Debug, Release, etc.).</param>
	/// <param name="collectCoverage">Whether to collect code coverage information.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	public bool DotNetTest(
		BuildConfiguration config,
		string? projectOrSolutionPath = null,
		string configuration = "Release",
		bool collectCoverage = true)
	{
		try
		{
			_logger.LogInformation($"Running tests in {configuration} configuration");

			var targetPath = projectOrSolutionPath ?? "";

			var arguments = $"test {targetPath} --configuration {configuration} --no-build";

			if (collectCoverage)
			{
				arguments += " --collect:\"XPlat Code Coverage\"";
			}

			var result = _commandRunner.RunCommand("dotnet", arguments, config.WorkspacePath);

			if (!result.Success)
			{
				_logger.LogError($"Tests failed: {result.Error}");
				return false;
			}

			_logger.LogInformation("Tests completed successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error during testing: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Creates NuGet packages from the specified project.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectPath">The path to the project file.</param>
	/// <param name="configuration">The build configuration (Debug, Release, etc.).</param>
	/// <param name="includeSymbols">Whether to include symbols in the package.</param>
	/// <param name="includeSource">Whether to include source code in the package.</param>
	/// <param name="versionSuffix">The version suffix to append to the package version.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	public bool DotNetPack(
		BuildConfiguration config,
		string projectPath,
		string configuration = "Release",
		bool includeSymbols = true,
		bool includeSource = false,
		string? versionSuffix = null)
	{
		try
		{
			_logger.LogInformation($"Creating NuGet package for {projectPath}");

			// Create the staging directory if it doesn't exist
			if (!string.IsNullOrEmpty(config.StagingPath) && !Directory.Exists(config.StagingPath))
			{
				Directory.CreateDirectory(config.StagingPath);
			}

			var arguments = $"pack {projectPath} --configuration {configuration} --no-build --output {config.StagingPath}";

			if (includeSymbols)
			{
				arguments += " --include-symbols";
			}

			if (includeSource)
			{
				arguments += " --include-source";
			}

			if (!string.IsNullOrEmpty(versionSuffix))
			{
				arguments += $" --version-suffix {versionSuffix}";
			}

			var result = _commandRunner.RunCommand("dotnet", arguments, config.WorkspacePath);

			if (!result.Success)
			{
				_logger.LogError($"Failed to create NuGet package: {result.Error}");
				return false;
			}

			_logger.LogInformation("NuGet package created successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error during package creation: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Publishes the specified project.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectPath">The path to the project file.</param>
	/// <param name="configuration">The build configuration (Debug, Release, etc.).</param>
	/// <param name="runtime">The target runtime identifier.</param>
	/// <param name="framework">The target framework.</param>
	/// <param name="selfContained">Whether to publish as a self-contained application.</param>
	/// <returns>True if the operation was successful, false otherwise.</returns>
	public bool DotNetPublish(
		BuildConfiguration config,
		string projectPath,
		string configuration = "Release",
		string? runtime = null,
		string? framework = null,
		bool? selfContained = null)
	{
		try
		{
			_logger.LogInformation($"Publishing {projectPath}");

			// Create the output directory if it doesn't exist
			if (!string.IsNullOrEmpty(config.OutputPath) && !Directory.Exists(config.OutputPath))
			{
				Directory.CreateDirectory(config.OutputPath);
			}

			var arguments = $"publish {projectPath} --configuration {configuration} --output {config.OutputPath}";

			if (!string.IsNullOrEmpty(runtime))
			{
				arguments += $" --runtime {runtime}";
			}

			if (!string.IsNullOrEmpty(framework))
			{
				arguments += $" --framework {framework}";
			}

			if (selfContained.HasValue)
			{
				arguments += selfContained.Value ? " --self-contained" : " --no-self-contained";
			}

			var result = _commandRunner.RunCommand("dotnet", arguments, config.WorkspacePath);

			if (!result.Success)
			{
				_logger.LogError($"Failed to publish: {result.Error}");
				return false;
			}

			_logger.LogInformation("Publish completed successfully");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error during publish: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Runs a complete build workflow including restore, build, test, and package.
	/// </summary>
	/// <param name="config">The build configuration.</param>
	/// <param name="projectPath">The path to the project or solution file.</param>
	/// <param name="configuration">The build configuration (Debug, Release, etc.).</param>
	/// <param name="skipTests">Whether to skip running tests.</param>
	/// <returns>True if the workflow was successful, false otherwise.</returns>
	public bool InvokeBuildWorkflow(
		BuildConfiguration config,
		string projectPath,
		string configuration = "Release",
		bool skipTests = false)
	{
		_logger.LogInformation("Starting build workflow");

		// First restore packages
		if (!DotNetRestore(config, projectPath))
		{
			return false;
		}

		// Then build
		if (!DotNetBuild(config, projectPath, configuration))
		{
			return false;
		}

		// Run tests if not skipped
		if (!skipTests)
		{
			if (!DotNetTest(config, projectPath, configuration))
			{
				return false;
			}
		}

		// If it's a project (not a solution), also create packages
		if (projectPath.EndsWith(".csproj"))
		{
			if (!DotNetPack(config, projectPath, configuration))
			{
				return false;
			}
		}

		_logger.LogInformation("Build workflow completed successfully");
		return true;
	}

	/// <summary>
	/// Gets the current installed .NET SDK version.
	/// </summary>
	/// <returns>The .NET SDK version string.</returns>
	private string GetDotNetSdkVersion()
	{
		try
		{
			var result = _commandRunner.RunCommand("dotnet", "--version");
			return result.Output.Trim();
		}
		catch (Exception ex)
		{
			_logger.LogWarning($"Failed to get .NET SDK version: {ex.Message}");
			return "unknown";
		}
	}
}

/// <summary>
/// Options for creating a build configuration.
/// </summary>
public class BuildConfigurationOptions
{
	/// <summary>
	/// Gets or sets the server URL to use for the build.
	/// </summary>
	public string ServerUrl { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the Git reference (branch/tag) being built.
	/// </summary>
	public string GitRef { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the Git commit SHA being built.
	/// </summary>
	public string GitSha { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub owner of the repository.
	/// </summary>
	public string GitHubOwner { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub repository name.
	/// </summary>
	public string GitHubRepo { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub token for API operations.
	/// </summary>
	public string GithubToken { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the NuGet API key for package publishing.
	/// </summary>
	public string NuGetApiKey { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the path to the workspace/repository root.
	/// </summary>
	public string WorkspacePath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the expected owner/organization of the official repository.
	/// </summary>
	public string ExpectedOwner { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the path to the changelog file.
	/// </summary>
	public string ChangelogFile { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the array of glob patterns for release assets.
	/// </summary>
	public string[] AssetPatterns { get; set; } = [];
}
