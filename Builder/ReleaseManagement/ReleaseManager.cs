namespace PSBuild.ReleaseManagement;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

using Octokit;

using PSBuild.BuildManagement;
using PSBuild.Utilities;

/// <summary>
/// Manages the release process for .NET applications.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ReleaseManager"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging messages.</param>
/// <param name="commandRunner">The command runner to use for running external commands.</param>
/// <param name="loggerFactory">The logger factory to create loggers for dependencies.</param>
public partial class ReleaseManager(
	ILogger<ReleaseManager> logger,
	CommandRunner commandRunner,
	ILoggerFactory loggerFactory)
{
	private readonly ILogger<ReleaseManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly CommandRunner _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
	private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

	/// <summary>
	/// Publishes NuGet packages to a NuGet repository.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="packagePatterns">The patterns to match NuGet packages to publish.</param>
	/// <param name="nugetApiKey">The NuGet API key to use for publishing.</param>
	/// <param name="nugetSource">The NuGet source to publish to.</param>
	/// <returns>A task representing the asynchronous operation, with a value indicating whether the operation succeeded.</returns>
	public async Task<bool> PublishNuGetPackagesAsync(
		BuildConfiguration config,
		string[] packagePatterns,
		string nugetApiKey,
		string nugetSource = "https://api.nuget.org/v3/index.json")
	{
		_logger.LogInformation($"Publishing NuGet packages to {nugetSource}");

		if (string.IsNullOrEmpty(nugetApiKey))
		{
			_logger.LogError("NuGet API key is required");
			return false;
		}

		// Verify that we have at least one package to publish
		var packages = new List<string>();
		foreach (string pattern in packagePatterns)
		{
			string[] matches = Directory.GetFiles(Path.GetDirectoryName(pattern) ?? config.WorkspacePath, Path.GetFileName(pattern));
			packages.AddRange(matches);
		}

		if (packages.Count == 0)
		{
			_logger.LogError("No packages found to publish");
			return false;
		}

		// Publish each package
		foreach (string package in packages)
		{
			_logger.LogInformation($"Publishing {Path.GetFileName(package)}");

			try
			{
				// Execute the dotnet nuget push command
				var result = _commandRunner.RunCommand(
					"dotnet",
					$"nuget push \"{package}\" --api-key {nugetApiKey} --source {nugetSource}",
					config.WorkspacePath);

				if (result.ExitCode != 0)
				{
					_logger.LogError($"Failed to publish {package}: {result.Error}");
					return false;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error publishing {package}: {ex.Message}");
				return false;
			}
		}

		_logger.LogInformation($"Successfully published {packages.Count} packages");
		return true;
	}

	/// <summary>
	/// Creates a GitHub release with assets.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="tagName">The tag name for the release.</param>
	/// <param name="releaseNotes">The release notes.</param>
	/// <param name="assets">The assets to include in the release.</param>
	/// <returns>A task representing the asynchronous operation, with the new release if successful or null if it failed.</returns>
	public async Task<Release?> CreateGitHubReleaseAsync(
		BuildConfiguration config,
		string tagName,
		string releaseNotes,
		string[] assets)
	{
		_logger.LogInformation($"Creating GitHub release {tagName}");

		if (string.IsNullOrEmpty(config.GithubToken))
		{
			_logger.LogError("GitHub token is required");
			return null;
		}

		try
		{
			// Create GitHub client
			var client = new GitHubClient(new ProductHeaderValue("PSBuild"))
			{
				Credentials = new Credentials(config.GithubToken)
			};

			// Check if release already exists
			try
			{
				var existingRelease = await client.Repository.Release.Get(config.GitHubOwner, config.GitHubRepo, tagName).ConfigureAwait(false);
				_logger.LogInformation($"Release {tagName} already exists, skipping");
				return existingRelease;
			}
			catch (NotFoundException)
			{
				// Release doesn't exist, which is what we want
			}

			// Create the release
			var newRelease = new NewRelease(tagName)
			{
				Name = $"Release {tagName}",
				Body = releaseNotes,
				Draft = false,
				Prerelease = tagName.Contains('-')
			};

			var release = await client.Repository.Release.Create(config.GitHubOwner, config.GitHubRepo, newRelease).ConfigureAwait(false);
			_logger.LogInformation($"Created release {release.Name}");

			// Upload assets
			foreach (string assetPattern in assets)
			{
				string[] assetFiles = Directory.GetFiles(Path.GetDirectoryName(assetPattern) ?? config.WorkspacePath, Path.GetFileName(assetPattern));
				foreach (string assetFile in assetFiles)
				{
					_logger.LogInformation($"Uploading asset {Path.GetFileName(assetFile)}");

					using var stream = File.OpenRead(assetFile);
					string contentType = GetContentType(assetFile);
					var assetUpload = new ReleaseAssetUpload
					{
						FileName = Path.GetFileName(assetFile),
						ContentType = contentType,
						RawData = stream
					};

					await client.Repository.Release.UploadAsset(release, assetUpload).ConfigureAwait(false);
				}
			}

			_logger.LogInformation($"Release created successfully at {release.HtmlUrl}");
			return release;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating GitHub release: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Creates a signed ZIP package of a PowerShell module.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="modulePath">The path to the module to package.</param>
	/// <param name="outputPath">The path to place the output ZIP file.</param>
	/// <param name="version">The version of the module.</param>
	/// <param name="signPackage">Whether to create a signature file for the package.</param>
	/// <returns>The path to the created package, or null if the operation failed.</returns>
	public string? CreateModuleZipPackage(
		BuildConfiguration config,
		string modulePath,
		string outputPath,
		string version,
		bool signPackage = true)
	{
		_logger.LogInformation($"Creating ZIP package for module at {modulePath}");

		if (!Directory.Exists(modulePath))
		{
			_logger.LogError($"Module directory does not exist: {modulePath}");
			return null;
		}

		try
		{
			// Create output directory if it doesn't exist
			if (!Directory.Exists(outputPath))
			{
				Directory.CreateDirectory(outputPath);
			}

			// Determine module name from directory name
			string moduleName = Path.GetFileName(modulePath);
			string zipFilePath = Path.Combine(outputPath, $"{moduleName}-{version}.zip");

			// Delete the ZIP file if it already exists
			if (File.Exists(zipFilePath))
			{
				File.Delete(zipFilePath);
			}

			// Create the ZIP file
			ZipFile.CreateFromDirectory(modulePath, zipFilePath, CompressionLevel.Optimal, false);

			_logger.LogInformation($"Created ZIP package at {zipFilePath}");

			// Sign the package if requested
			if (signPackage)
			{
				string signatureFilePath = $"{zipFilePath}.signature";

				// Compute hash of the ZIP file
				string fileHash = ComputeFileHash(zipFilePath);

				// Write hash to signature file
				File.WriteAllText(signatureFilePath, fileHash);

				_logger.LogInformation($"Created signature file at {signatureFilePath}");
			}

			return zipFilePath;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating ZIP package: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Publishes a PowerShell module to the PowerShell Gallery.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="modulePath">The path to the module to publish.</param>
	/// <param name="apiKey">The API key to use for publishing.</param>
	/// <returns>A value indicating whether the operation succeeded.</returns>
	public bool PublishToPowerShellGallery(
		BuildConfiguration config,
		string modulePath,
		string apiKey)
	{
		_logger.LogInformation($"Publishing module to PowerShell Gallery: {Path.GetFileName(modulePath)}");

		if (string.IsNullOrEmpty(apiKey))
		{
			_logger.LogError("PowerShell Gallery API key is required");
			return false;
		}

		if (!Directory.Exists(modulePath))
		{
			_logger.LogError($"Module directory does not exist: {modulePath}");
			return false;
		}

		try
		{
			// Use PowerShellGet's Publish-Module command to publish to PSGallery
			string psCommand = $"Publish-Module -Path '{modulePath}' -NuGetApiKey '{apiKey}' -Verbose";

			// Execute the PowerShell command
			var result = _commandRunner.RunCommand(
				"powershell",
				$"-Command \"{psCommand}\"",
				config.WorkspacePath);

			if (result.ExitCode != 0)
			{
				_logger.LogError($"Failed to publish module: {result.Error}");
				return false;
			}

			_logger.LogInformation("Module published successfully to PowerShell Gallery");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error publishing to PowerShell Gallery: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Creates release artifacts for a PowerShell module.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="modulePath">The path to the module.</param>
	/// <param name="version">The version of the module.</param>
	/// <param name="includeZip">Whether to create a ZIP package.</param>
	/// <returns>A list of paths to the created artifacts.</returns>
	public List<string> CreateReleaseArtifacts(
		BuildConfiguration config,
		string modulePath,
		string version,
		bool includeZip = true)
	{
		_logger.LogInformation($"Creating release artifacts for module at {modulePath}");

		var artifacts = new List<string>();

		// Create staging directory if it doesn't exist
		if (!Directory.Exists(config.StagingPath))
		{
			Directory.CreateDirectory(config.StagingPath);
		}

		try
		{
			// Create ZIP package if requested
			if (includeZip)
			{
				string? zipPath = CreateModuleZipPackage(config, modulePath, config.StagingPath, version);
				if (zipPath != null)
				{
					artifacts.Add(zipPath);

					// Also add the signature file if it exists
					string signatureFilePath = $"{zipPath}.signature";
					if (File.Exists(signatureFilePath))
					{
						artifacts.Add(signatureFilePath);
					}
				}
			}

			// Create module manifest verification report
			string manifestReport = VerifyModuleManifest(modulePath);
			string reportPath = Path.Combine(config.StagingPath, $"{Path.GetFileName(modulePath)}-verification.txt");
			File.WriteAllText(reportPath, manifestReport);
			artifacts.Add(reportPath);

			// Create a README if it doesn't exist
			string readmePath = Path.Combine(modulePath, "README.md");
			if (!File.Exists(readmePath))
			{
				string readmeContent = GenerateDefaultReadme(Path.GetFileName(modulePath), version);
				File.WriteAllText(readmePath, readmeContent);
			}

			_logger.LogInformation($"Created {artifacts.Count} release artifacts");
			return artifacts;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating release artifacts: {ex.Message}");
			return artifacts;
		}
	}

	/// <summary>
	/// Extracts release notes from a changelog file.
	/// </summary>
	/// <param name="config">The build configuration to use.</param>
	/// <param name="version">The version to extract release notes for.</param>
	/// <param name="changelogPath">The path to the changelog file. If null, uses CHANGELOG.md in the repository root.</param>
	/// <returns>The extracted release notes, or a default message if not found.</returns>
	public string ExtractReleaseNotes(
		BuildConfiguration config,
		string version,
		string? changelogPath = null)
	{
		_logger.LogInformation($"Extracting release notes for version {version}");

		changelogPath ??= Path.Combine(config.WorkspacePath, "CHANGELOG.md");

		if (!File.Exists(changelogPath))
		{
			_logger.LogWarning($"Changelog file not found at {changelogPath}");
			return $"Release {version}";
		}

		try
		{
			string content = File.ReadAllText(changelogPath);

			// Remove 'v' prefix if present in the version
			if (version.StartsWith("v"))
			{
				version = version[1..];
			}

			// Look for version heading pattern (# x.y.z or ## x.y.z)
			string versionHeadingPattern = $@"(^|\n)#+\s+({version}|v{version}).*?(\n#+\s+|$)";
			var match = Regex.Match(content, versionHeadingPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

			if (match.Success)
			{
				// Extract all text between this heading and the next heading
				string sectionText = match.Value;

				// Remove the heading itself
				string notes = MyRegex().Replace(sectionText, "\n").Trim();

				// Remove the next heading if it was captured
				notes = Regex.Replace(notes, @"\n#+\s+.*?$", "").Trim();

				if (!string.IsNullOrWhiteSpace(notes))
				{
					return notes;
				}
			}

			_logger.LogWarning($"No release notes found for version {version} in changelog");
			return $"Release {version}";
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error extracting release notes: {ex.Message}");
			return $"Release {version}";
		}
	}

	/// <summary>
	/// Verifies a PowerShell module manifest.
	/// </summary>
	/// <param name="modulePath">The path to the module.</param>
	/// <returns>A verification report.</returns>
	private string VerifyModuleManifest(string modulePath)
	{
		_logger.LogInformation($"Verifying module manifest for {Path.GetFileName(modulePath)}");

		string[] psd1Files = Directory.GetFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly);
		if (psd1Files.Length == 0)
		{
			return "ERROR: No module manifest found";
		}

		string manifestPath = psd1Files[0];
		string moduleName = Path.GetFileNameWithoutExtension(manifestPath);

		var result = _commandRunner.RunCommand(
			"powershell",
			$"-Command \"Test-ModuleManifest -Path '{manifestPath}' | Format-List\"");

		if (result.ExitCode != 0)
		{
			return $"ERROR: Failed to verify module manifest: {result.Error}";
		}

		var sb = new StringBuilder();
		sb.AppendLine($"Module Manifest Verification: {moduleName}");
		sb.AppendLine("------------------------------------------------");
		sb.AppendLine(result.Output);

		// Check for required files
		if (!File.Exists(Path.Combine(modulePath, $"{moduleName}.psm1")))
		{
			sb.AppendLine("WARNING: Module .psm1 file not found");
		}

		if (!File.Exists(Path.Combine(modulePath, "LICENSE.txt")) && !File.Exists(Path.Combine(modulePath, "LICENSE")))
		{
			sb.AppendLine("WARNING: License file not found");
		}

		if (!File.Exists(Path.Combine(modulePath, "README.md")))
		{
			sb.AppendLine("WARNING: README.md file not found");
		}

		return sb.ToString();
	}

	/// <summary>
	/// Generates a default README for a module.
	/// </summary>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="version">The version of the module.</param>
	/// <returns>The generated README content.</returns>
	private static string GenerateDefaultReadme(string moduleName, string version)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"# {moduleName}");
		sb.AppendLine();
		sb.AppendLine($"Version: {version}");
		sb.AppendLine();
		sb.AppendLine("## Description");
		sb.AppendLine();
		sb.AppendLine($"{moduleName} is a PowerShell module that provides utilities for [description].");
		sb.AppendLine();
		sb.AppendLine("## Installation");
		sb.AppendLine();
		sb.AppendLine("```powershell");
		sb.AppendLine($"Install-Module -Name {moduleName} -Scope CurrentUser");
		sb.AppendLine("```");
		sb.AppendLine();
		sb.AppendLine("## Usage");
		sb.AppendLine();
		sb.AppendLine("```powershell");
		sb.AppendLine($"Import-Module {moduleName}");
		sb.AppendLine("# Example commands");
		sb.AppendLine("```");
		sb.AppendLine();
		sb.AppendLine("## License");
		sb.AppendLine();
		sb.AppendLine("See the LICENSE file for details.");

		return sb.ToString();
	}

	/// <summary>
	/// Computes a hash for a file.
	/// </summary>
	/// <param name="filePath">The path to the file.</param>
	/// <returns>The computed hash as a hexadecimal string.</returns>
	private static string ComputeFileHash(string filePath)
	{
		using var sha256 = SHA256.Create();
		using var stream = File.OpenRead(filePath);
		byte[] hashBytes = sha256.ComputeHash(stream);
		return BitConverter.ToString(hashBytes).Replace("-", "");
	}

	private static string GetContentType(string fileName)
	{
		string extension = Path.GetExtension(fileName).ToLowerInvariant();
		return extension switch
		{
			".nupkg" => "application/octet-stream",
			".snupkg" => "application/octet-stream",
			".zip" => "application/zip",
			".exe" => "application/octet-stream",
			".dll" => "application/octet-stream",
			".json" => "application/json",
			".xml" => "application/xml",
			".txt" => "text/plain",
			".md" => "text/markdown",
			_ => "application/octet-stream"
		};
	}

	/// <summary>
	/// Creates a new PowerShell module with the specified parameters.
	/// </summary>
	/// <param name="modulePath">The target directory where the module will be created.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="moduleVersion">The version of the module.</param>
	/// <param name="description">A description of the module.</param>
	/// <param name="author">The author of the module.</param>
	/// <param name="companyName">The company that created the module.</param>
	/// <param name="functions">A dictionary of function names and their content.</param>
	/// <param name="projectUri">The URI to the project's repository.</param>
	/// <param name="licenseUri">The URI to the module's license.</param>
	/// <param name="tags">Tags for the module to aid in discovery.</param>
	/// <returns>The path to the created module directory, or null if the operation failed.</returns>
	public string? CreatePowerShellModule(
		string modulePath,
		string moduleName,
		string moduleVersion,
		string description,
		string author = "",
		string companyName = "",
		Dictionary<string, string>? functions = null,
		string? projectUri = null,
		string? licenseUri = null,
		IEnumerable<string>? tags = null)
	{
		try
		{
			_logger.LogInformation($"Creating PowerShell module {moduleName} v{moduleVersion}");

			// Resolve the full module path
			string fullModulePath = Path.GetFullPath(modulePath);
			if (!Directory.Exists(fullModulePath))
			{
				Directory.CreateDirectory(fullModulePath);
			}

			// Get the module manifest generator
			var manifestGenerator = new ModuleManifestGenerator(
				_loggerFactory.CreateLogger<ModuleManifestGenerator>(),
				_commandRunner);

			// Create the module structure
			manifestGenerator.CreateModuleStructure(fullModulePath, moduleName, moduleVersion, description);

			// Create the module manifest
			var functionsToExport = functions?.Keys.ToList() ?? [];
			manifestGenerator.CreateModuleManifest(
				fullModulePath,
				moduleName,
				moduleVersion,
				description,
				author,
				companyName,
				functionsToExport: functionsToExport,
				projectUri: projectUri,
				licenseUri: licenseUri,
				tags: tags
			);

			// Create the .psm1 file
			manifestGenerator.CreateModulePsm1(fullModulePath, moduleName);

			// Add functions if provided
			if (functions != null)
			{
				foreach (var function in functions)
				{
					manifestGenerator.AddFunction(fullModulePath, function.Key, function.Value, true);
				}
			}

			// Create a README.md file if it doesn't exist
			string readmePath = Path.Combine(fullModulePath, "README.md");
			if (!File.Exists(readmePath))
			{
				File.WriteAllText(readmePath, GenerateDefaultReadme(moduleName, moduleVersion));
			}

			_logger.LogInformation($"PowerShell module {moduleName} created successfully at {fullModulePath}");
			return fullModulePath;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating PowerShell module: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Creates a PowerShell function file to add to a module.
	/// </summary>
	/// <param name="modulePath">The path to the module directory.</param>
	/// <param name="functionName">The name of the function to create.</param>
	/// <param name="description">A description of the function.</param>
	/// <param name="parameters">A dictionary of parameter names and their descriptions.</param>
	/// <param name="isPublic">Whether the function should be public (exported) or private.</param>
	/// <returns>The content of the created function.</returns>
	public string CreatePowerShellFunction(
		string modulePath,
		string functionName,
		string description,
		Dictionary<string, string>? parameters = null,
		bool isPublic = true)
	{
		try
		{
			_logger.LogInformation($"Creating PowerShell function {functionName}");

			// Get the module manifest generator
			var manifestGenerator = new ModuleManifestGenerator(
				_loggerFactory.CreateLogger<ModuleManifestGenerator>(),
				_commandRunner);

			// Create function content from template
			string functionContent = ModuleManifestGenerator.CreateFunctionTemplate(
				functionName,
				description,
				parameters
			);

			// Add the function to the module
			string functionPath = manifestGenerator.AddFunction(
				modulePath,
				functionName,
				functionContent,
				isPublic
			);

			_logger.LogInformation($"PowerShell function {functionName} created at {functionPath}");
			return functionContent;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating PowerShell function: {ex.Message}");
			throw;
		}
	}

	[GeneratedRegex(@"(^|\n)#+\s+.*?\n")]
	private static partial Regex MyRegex();

	public string? CreatePowerShellModule(string modulePath, string moduleName, string moduleVersion, string description, string author = "", string companyName = "", Dictionary<string, string>? functions = null, Uri projectUri = null, string? licenseUri = null, IEnumerable<string>? tags = null)
	{
		throw new NotImplementedException();
	}
}
