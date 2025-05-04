namespace PSBuild.VersionManagement;

using System.Text;
using System.Text.RegularExpressions;

using LibGit2Sharp;

using Microsoft.Extensions.Logging;

using PSBuild.Utilities;

/// <summary>
/// Manages version information for projects based on git history.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="VersionManager"/> class.
/// </remarks>
/// <param name="logger">The logger.</param>
/// <param name="commandRunner">The command runner.</param>
public partial class VersionManager(ILogger<VersionManager> logger, CommandRunner commandRunner)
{
	private readonly ILogger<VersionManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <summary>
	/// Gets the version information from git history.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <returns>The version information.</returns>
	public VersionInfo GetVersionFromGit(string repoPath)
	{
		_logger.LogInformation("Getting version from git history");

		try
		{
			using var repo = new Repository(repoPath);

			// Get the most recent tag
			var allTags = repo.Tags
				.Select(tag => new { Tag = tag, Commit = tag.PeeledTarget as Commit })
				.Where(x => x.Commit != null)
				.ToList();

			// Sort tags by commit date
			var sortedTags = allTags
				.OrderByDescending(x => x.Commit?.Author.When ?? DateTimeOffset.MinValue)
				.ToList();

			// Get the latest tag with a valid version
			var latestVersionTag = sortedTags
				.FirstOrDefault(x => IsValidVersionTag(x.Tag.FriendlyName));

			// Latest commit reference
			var latestCommit = repo.Head.Tip;
			string shortSha = latestCommit.Sha[..7];

			// Start with a default version
			string version = "0.1.0";
			bool isPreRelease = true;

			if (latestVersionTag != null)
			{
				// Parse version from the tag (remove 'v' prefix if present)
				string tagName = latestVersionTag.Tag.FriendlyName;
				version = tagName.StartsWith("v") ? tagName[1..] : tagName;

				// Check if this commit is exactly at the tag (not a pre-release)
				isPreRelease = latestVersionTag.Commit?.Sha != latestCommit.Sha;

				if (isPreRelease)
				{
					// Count commits since the tag
					int commitsSinceTag = CountCommitsSinceTag(repo, latestVersionTag.Commit, latestCommit);

					// Only increment the base version if there have been commits since the tag
					if (commitsSinceTag > 0)
					{
						version = IncrementVersion(version);
						version = $"{version}-pre.{commitsSinceTag}";
					}
				}
			}
			else
			{
				// No valid version tag found, use commit count as the pre-release number
				int commitCount = repo.Commits.Count();
				version = $"{version}-pre.{commitCount}";
			}

			// Create version info
			var versionInfo = new VersionInfo
			{
				Version = version,
				ShortSha = shortSha,
				FullSha = latestCommit.Sha,
				CommitDate = latestCommit.Author.When,
				CommitMessage = latestCommit.Message?.Trim() ?? "",
				BranchName = repo.Head.FriendlyName,
				IsPreRelease = isPreRelease,
				HasLocalChanges = repo.RetrieveStatus().IsDirty
			};

			_logger.LogInformation($"Determined version: {versionInfo.Version}, IsPreRelease: {versionInfo.IsPreRelease}");

			return versionInfo;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error getting version from git: {ex.Message}");

			// Return default version info
			return new VersionInfo
			{
				Version = "0.1.0-error.0",
				ShortSha = "unknown",
				FullSha = "unknown",
				CommitDate = DateTimeOffset.Now,
				CommitMessage = "Error getting version from git",
				BranchName = "unknown",
				IsPreRelease = true,
				HasLocalChanges = false
			};
		}
	}

	/// <summary>
	/// Generates a changelog from git history.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <param name="fromTag">The tag to start from (exclusive). If null, starts from the beginning.</param>
	/// <param name="toCommit">The commit to end at (inclusive). If null, ends at HEAD.</param>
	/// <param name="categorize">Whether to categorize commits by type.</param>
	/// <returns>The generated changelog text.</returns>
	public string GenerateChangelog(string repoPath, string? fromTag = null, string? toCommit = null, bool categorize = true)
	{
		_logger.LogInformation("Generating changelog from git history");

		try
		{
			using var repo = new Repository(repoPath);

			// Determine the starting point
			Commit? fromCommit = null;
			if (!string.IsNullOrEmpty(fromTag))
			{
				var tag = repo.Tags.FirstOrDefault(t => t.FriendlyName == fromTag);
				if (tag != null)
				{
					fromCommit = tag.PeeledTarget as Commit;
				}
			}

			// Determine the ending point
			Commit endCommit;
			if (!string.IsNullOrEmpty(toCommit))
			{
				endCommit = repo.Lookup<Commit>(toCommit);
				if (endCommit == null)
				{
					endCommit = repo.Head.Tip;
				}
			}
			else
			{
				endCommit = repo.Head.Tip;
			}

			// Get all commits between fromCommit and endCommit
			var commits = new List<Commit>();
			var current = endCommit;

			while (current != null && (fromCommit == null || current.Sha != fromCommit.Sha))
			{
				commits.Add(current);

				// Move to parent commit
				current = current.Parents.FirstOrDefault();

				// Stop if we've reached the commit before fromCommit
				if (fromCommit != null && current?.Sha == fromCommit.Sha)
				{
					break;
				}
			}

			// Generate the changelog
			var changelog = new StringBuilder();

			if (categorize)
			{
				// Categorize commits
				var features = new List<Commit>();
				var fixes = new List<Commit>();
				var docs = new List<Commit>();
				var refactoring = new List<Commit>();
				var tests = new List<Commit>();
				var other = new List<Commit>();

				foreach (var commit in commits)
				{
					string msg = commit.Message.Split('\n')[0].Trim().ToLower(System.Globalization.CultureInfo.CurrentCulture);

					if (msg.StartsWith("feat") || msg.StartsWith("add"))
					{
						features.Add(commit);
					}
					else if (msg.StartsWith("fix") || msg.Contains("bugfix") || msg.Contains("bug fix"))
					{
						fixes.Add(commit);
					}
					else if (msg.StartsWith("doc") || msg.Contains("documentation"))
					{
						docs.Add(commit);
					}
					else if (msg.StartsWith("refactor") || msg.Contains("refactor"))
					{
						refactoring.Add(commit);
					}
					else if (msg.StartsWith("test") || msg.Contains("test"))
					{
						tests.Add(commit);
					}
					else
					{
						other.Add(commit);
					}
				}

				// Add features section
				if (features.Count > 0)
				{
					changelog.AppendLine("## 🚀 Features");
					foreach (var commit in features)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}

				// Add fixes section
				if (fixes.Count > 0)
				{
					changelog.AppendLine("## 🐛 Bug Fixes");
					foreach (var commit in fixes)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}

				// Add documentation section
				if (docs.Count > 0)
				{
					changelog.AppendLine("## 📚 Documentation");
					foreach (var commit in docs)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}

				// Add refactoring section
				if (refactoring.Count > 0)
				{
					changelog.AppendLine("## 🔨 Refactoring");
					foreach (var commit in refactoring)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}

				// Add tests section
				if (tests.Count > 0)
				{
					changelog.AppendLine("## 🧪 Tests");
					foreach (var commit in tests)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}

				// Add other section
				if (other.Count > 0)
				{
					changelog.AppendLine("## 🔍 Other Changes");
					foreach (var commit in other)
					{
						AppendCommitToChangelog(changelog, commit);
					}

					changelog.AppendLine();
				}
			}
			else
			{
				// Simple list of changes
				changelog.AppendLine("## Changes");
				foreach (var commit in commits)
				{
					AppendCommitToChangelog(changelog, commit);
				}

				changelog.AppendLine();
			}

			return changelog.ToString();
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error generating changelog: {ex.Message}");
			return $"*Error generating changelog: {ex.Message}*";
		}
	}

	/// <summary>
	/// Updates the version in all relevant files.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <param name="version">The new version to set.</param>
	/// <param name="generateChangelog">Whether to generate and update the changelog.</param>
	/// <returns>True if successful, false otherwise.</returns>
	public bool UpdateVersionInFiles(string repoPath, string version, bool generateChangelog = true)
	{
		_logger.LogInformation($"Updating version to {version} in files");

		try
		{
			// Update all .csproj files
			string[] csprojFiles = Directory.GetFiles(repoPath, "*.csproj", SearchOption.AllDirectories);
			foreach (string csprojFile in csprojFiles)
			{
				UpdateVersionInCsprojFile(csprojFile, version);
			}

			// Update PowerShell module manifest if present
			string[] psd1Files = Directory.GetFiles(repoPath, "*.psd1", SearchOption.AllDirectories);
			foreach (string psd1File in psd1Files)
			{
				UpdateVersionInPsd1File(psd1File, version);
			}

			// Update AssemblyInfo.cs files if present
			string[] assemblyInfoFiles = Directory.GetFiles(repoPath, "AssemblyInfo.cs", SearchOption.AllDirectories);
			foreach (string assemblyInfoFile in assemblyInfoFiles)
			{
				UpdateVersionInAssemblyInfoFile(assemblyInfoFile, version);
			}

			// Generate and update changelog if requested
			if (generateChangelog)
			{
				UpdateChangelog(repoPath, version);
			}

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error updating version in files: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Tags the current commit with the specified version.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <param name="version">The version to tag with.</param>
	/// <param name="message">The tag message.</param>
	/// <returns>True if successful, false otherwise.</returns>
	public bool TagVersion(string repoPath, string version, string message = "")
	{
		_logger.LogInformation($"Tagging commit with version {version}");

		try
		{
			// Make sure version has a 'v' prefix
			string tagName = version.StartsWith("v") ? version : $"v{version}";

			// If no message provided, create a default one
			if (string.IsNullOrEmpty(message))
			{
				message = $"Version {version}";
			}

			using var repo = new Repository(repoPath);

			// Get the current commit
			var commit = repo.Head.Tip;

			// Create the tag
			var tag = repo.Tags.Add(tagName, commit, new Signature("PSBuild", "psbuild@example.com", DateTimeOffset.Now), message);

			_logger.LogInformation($"Created tag {tagName} on commit {commit.Sha[..7]}");

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error creating tag: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Creates a new version by incrementing the specified part (major, minor, patch) of the current version.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <param name="part">The version part to increment (0=major, 1=minor, 2=patch).</param>
	/// <param name="preRelease">The optional pre-release suffix to add.</param>
	/// <param name="updateFiles">Whether to update the version in files.</param>
	/// <param name="tagVersion">Whether to tag the commit with the new version.</param>
	/// <returns>The new version information.</returns>
	public VersionInfo IncrementVersionPart(
		string repoPath,
		int part = 2,
		string? preRelease = null,
		bool updateFiles = true,
		bool tagVersion = false)
	{
		_logger.LogInformation($"Incrementing version part {part}");

		try
		{
			// Get current version
			var currentVersion = GetVersionFromGit(repoPath);

			// Parse the base version (without pre-release part)
			string baseVersion = currentVersion.Version.Split('-')[0];

			// Split into major.minor.patch
			int[] versionParts = baseVersion.Split('.').Select(int.Parse).ToArray();

			// Ensure we have at least 3 parts
			if (versionParts.Length < 3)
			{
				int[] newParts = new int[3];
				Array.Copy(versionParts, newParts, versionParts.Length);
				versionParts = newParts;
			}

			// Increment the specified part
			if (part is >= 0 and < 3)
			{
				versionParts[part]++;

				// Reset lower parts to 0
				for (int i = part + 1; i < 3; i++)
				{
					versionParts[i] = 0;
				}
			}

			// Construct the new version string
			string newVersion = $"{versionParts[0]}.{versionParts[1]}.{versionParts[2]}";

			// Add pre-release suffix if provided
			if (!string.IsNullOrEmpty(preRelease))
			{
				newVersion = $"{newVersion}-{preRelease}";
			}

			// Update the version info
			currentVersion.Version = newVersion;
			currentVersion.IsPreRelease = !string.IsNullOrEmpty(preRelease);

			// Update files if requested
			if (updateFiles)
			{
				UpdateVersionInFiles(repoPath, newVersion);
			}

			// Tag the version if requested
			if (tagVersion)
			{
				TagVersion(repoPath, newVersion);
			}

			_logger.LogInformation($"Incremented version to {newVersion}");

			return currentVersion;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error incrementing version: {ex.Message}");

			// Return default version info with error indicator
			return new VersionInfo
			{
				Version = "0.1.0-error.0",
				ShortSha = "unknown",
				FullSha = "unknown",
				CommitDate = DateTimeOffset.Now,
				CommitMessage = $"Error incrementing version: {ex.Message}",
				BranchName = "unknown",
				IsPreRelease = true,
				HasLocalChanges = false
			};
		}
	}

	/// <summary>
	/// Updates the changelog file with the latest changes.
	/// </summary>
	/// <param name="repoPath">The repository path.</param>
	/// <param name="version">The version to update the changelog for.</param>
	private void UpdateChangelog(string repoPath, string version)
	{
		// Default changelog file path
		string changelogPath = Path.Combine(repoPath, "CHANGELOG.md");

		// Get the previous tag to generate changes since then
		string? previousTag = null;
		using (var repo = new Repository(repoPath))
		{
			var tags = repo.Tags
				.Select(tag => new { Tag = tag, Commit = tag.PeeledTarget as Commit })
				.Where(x => x.Commit != null && IsValidVersionTag(x.Tag.FriendlyName))
				.OrderByDescending(x => x.Commit?.Author.When ?? DateTimeOffset.MinValue)
				.ToList();

			if (tags.Count > 0)
			{
				previousTag = tags[0].Tag.FriendlyName;
			}
		}

		// Generate changelog for the new version
		string changes = GenerateChangelog(repoPath, previousTag);

		// Create or update the changelog file
		if (File.Exists(changelogPath))
		{
			string existingContent = File.ReadAllText(changelogPath);
			string header = $"# {version} ({DateTime.Now:yyyy-MM-dd})";
			string newChangelog = $"{header}\n\n{changes}\n\n{existingContent}";
			File.WriteAllText(changelogPath, newChangelog);
		}
		else
		{
			string header = "# Changelog\n\n";
			string versionHeader = $"# {version} ({DateTime.Now:yyyy-MM-dd})";
			string newChangelog = $"{header}{versionHeader}\n\n{changes}\n";
			File.WriteAllText(changelogPath, newChangelog);
		}

		_logger.LogInformation($"Updated changelog at {changelogPath}");
	}

	/// <summary>
	/// Updates the version in a .csproj file.
	/// </summary>
	/// <param name="csprojPath">The path to the .csproj file.</param>
	/// <param name="version">The new version.</param>
	private void UpdateVersionInCsprojFile(string csprojPath, string version)
	{
		_logger.LogInformation($"Updating version in {csprojPath}");

		string content = File.ReadAllText(csprojPath);

		// Update Version property
		content = MyRegex().Replace(content, $"<Version>{version}</Version>");

		// Update VersionPrefix if present
		content = Regex.Replace(content,
			@"<VersionPrefix>.*?</VersionPrefix>",
			$"<VersionPrefix>{version.Split('-')[0]}</VersionPrefix>");

		// Update VersionSuffix if present and if version has a suffix
		if (version.Contains('-'))
		{
			string suffix = version[(version.IndexOf('-') + 1)..];
			content = Regex.Replace(content,
				@"<VersionSuffix>.*?</VersionSuffix>",
				$"<VersionSuffix>{suffix}</VersionSuffix>");
		}

		// Update AssemblyVersion and FileVersion
		string assemblyVersion = version.Split('-')[0];
		content = Regex.Replace(content,
			@"<AssemblyVersion>.*?</AssemblyVersion>",
			$"<AssemblyVersion>{assemblyVersion}</AssemblyVersion>");

		content = Regex.Replace(content,
			@"<FileVersion>.*?</FileVersion>",
			$"<FileVersion>{assemblyVersion}</FileVersion>");

		// Write updated content
		File.WriteAllText(csprojPath, content);
	}

	/// <summary>
	/// Updates the version in a PowerShell module manifest (.psd1) file.
	/// </summary>
	/// <param name="psd1Path">The path to the .psd1 file.</param>
	/// <param name="version">The new version.</param>
	private void UpdateVersionInPsd1File(string psd1Path, string version)
	{
		_logger.LogInformation($"Updating version in {psd1Path}");

		string content = File.ReadAllText(psd1Path);

		// Extract the base version without any pre-release suffix
		string baseVersion = version.Split('-')[0];

		// Update ModuleVersion
		content = Regex.Replace(content,
			@"ModuleVersion\s*=\s*['\""].*?['\""]",
			$"ModuleVersion = '{baseVersion}'");

		// Write updated content
		File.WriteAllText(psd1Path, content);
	}

	/// <summary>
	/// Updates the version in an AssemblyInfo.cs file.
	/// </summary>
	/// <param name="assemblyInfoPath">The path to the AssemblyInfo.cs file.</param>
	/// <param name="version">The new version.</param>
	private void UpdateVersionInAssemblyInfoFile(string assemblyInfoPath, string version)
	{
		_logger.LogInformation($"Updating version in {assemblyInfoPath}");

		string content = File.ReadAllText(assemblyInfoPath);

		// Extract the base version without any pre-release suffix
		string baseVersion = version.Split('-')[0];

		// Update AssemblyVersion
		content = Regex.Replace(content,
			@"\[assembly: AssemblyVersion\("".*?""\)\]",
			$"[assembly: AssemblyVersion(\"{baseVersion}\")]");

		// Update AssemblyFileVersion
		content = Regex.Replace(content,
			@"\[assembly: AssemblyFileVersion\("".*?""\)\]",
			$"[assembly: AssemblyFileVersion(\"{baseVersion}\")]");

		// Update AssemblyInformationalVersion (can include the pre-release suffix)
		content = Regex.Replace(content,
			@"\[assembly: AssemblyInformationalVersion\("".*?""\)\]",
			$"[assembly: AssemblyInformationalVersion(\"{version}\")]");

		// Write updated content
		File.WriteAllText(assemblyInfoPath, content);
	}

	/// <summary>
	/// Appends a commit to the changelog.
	/// </summary>
	/// <param name="changelog">The changelog StringBuilder.</param>
	/// <param name="commit">The commit to append.</param>
	private static void AppendCommitToChangelog(StringBuilder changelog, Commit commit)
	{
		// Get the first line of the commit message for the changelog
		string message = commit.Message.Split('\n')[0].Trim();
		string shortSha = commit.Sha[..7];

		// Replace common prefixes like "feat:", "fix:", etc.
		message = Regex.Replace(message, @"^(feat|fix|docs|style|refactor|test|chore|ci|build|perf)(\([\w-]+\))?:\s*", "");

		// Capitalize first letter
		if (message.Length > 0)
		{
			message = char.ToUpper(message[0]) + message[1..];
		}

		// Add the entry to the changelog
		changelog.AppendLine($"- {message} ({shortSha})");
	}

	/// <summary>
	/// Determines if a tag name is a valid version tag.
	/// </summary>
	/// <param name="tagName">The tag name to check.</param>
	/// <returns>True if it's a valid version tag, false otherwise.</returns>
	private static bool IsValidVersionTag(string tagName)
	{
		// Remove 'v' prefix if present
		if (tagName.StartsWith("v"))
		{
			tagName = tagName[1..];
		}

		// Check if it matches a semantic version pattern
		return Regex.IsMatch(tagName, @"^\d+\.\d+\.\d+(-[\w.-]+)?$");
	}

	/// <summary>
	/// Counts the number of commits between two commits.
	/// </summary>
	/// <param name="repo">The repository.</param>
	/// <param name="fromCommit">The starting commit.</param>
	/// <param name="toCommit">The ending commit.</param>
	/// <returns>The number of commits between the two commits.</returns>
	private static int CountCommitsSinceTag(Repository repo, Commit fromCommit, Commit toCommit)
	{
		int count = 0;
		var filter = new CommitFilter
		{
			ExcludeReachableFrom = fromCommit,
			IncludeReachableFrom = toCommit
		};

		// Count commits in the filtered range
		foreach (var commit in repo.Commits.QueryBy(filter))
		{
			count++;
		}

		return count;
	}

	/// <summary>
	/// Increments a semantic version by incrementing the patch version.
	/// </summary>
	/// <param name="version">The version to increment.</param>
	/// <returns>The incremented version.</returns>
	private static string IncrementVersion(string version)
	{
		// Only use the base version, without any pre-release suffix
		string baseVersion = version.Split('-')[0];

		// Split into major.minor.patch
		int[] versionParts = baseVersion.Split('.').Select(int.Parse).ToArray();

		// Ensure we have at least 3 parts
		if (versionParts.Length < 3)
		{
			int[] newParts = new int[3];
			Array.Copy(versionParts, newParts, versionParts.Length);
			versionParts = newParts;
		}

		// Increment patch version
		versionParts[2]++;

		// Return new version
		return $"{versionParts[0]}.{versionParts[1]}.{versionParts[2]}";
	}

	/// <summary>
	/// Updates the version in a PowerShell module manifest and related files.
	/// </summary>
	/// <param name="modulePath">The path to the PowerShell module.</param>
	/// <param name="version">The new version to set.</param>
	/// <param name="updateChangelog">Whether to update the changelog.</param>
	/// <returns>True if successful, false otherwise.</returns>
	public bool UpdatePowerShellModuleVersion(string modulePath, string version, bool updateChangelog = true)
	{
		try
		{
			_logger.LogInformation($"Updating PowerShell module version to {version}");

			if (!Directory.Exists(modulePath))
			{
				_logger.LogError($"Module directory not found: {modulePath}");
				return false;
			}

			// Get module name from directory name
			string moduleName = Path.GetFileName(modulePath);

			// Update module manifest (.psd1) file
			string psd1Path = Path.Combine(modulePath, $"{moduleName}.psd1");
			if (File.Exists(psd1Path))
			{
				UpdateVersionInPsd1File(psd1Path, version);
				_logger.LogInformation($"Updated version in module manifest: {psd1Path}");
			}
			else
			{
				_logger.LogWarning($"Module manifest not found: {psd1Path}");
			}

			// Update PSBuild.psd1 file if it exists at the repo root
			string repoRoot = Directory.GetParent(modulePath)?.FullName ?? modulePath;
			string rootPsd1Path = Path.Combine(repoRoot, "PSBuild.psd1");
			if (File.Exists(rootPsd1Path))
			{
				UpdateVersionInPsd1File(rootPsd1Path, version);
				_logger.LogInformation($"Updated version in root module manifest: {rootPsd1Path}");
			}

			// Update any PowerShell data files (.psd1) in the module directory
			foreach (string psd1File in Directory.GetFiles(modulePath, "*.psd1", SearchOption.AllDirectories))
			{
				if (psd1File != psd1Path && psd1File != rootPsd1Path)
				{
					UpdateVersionInPsd1File(psd1File, version);
					_logger.LogInformation($"Updated version in: {psd1File}");
				}
			}

			// Update version in module script (.psm1) files if they contain version information
			foreach (string psmFile in Directory.GetFiles(modulePath, "*.psm1", SearchOption.AllDirectories))
			{
				UpdateVersionInPsmFile(psmFile, version);
				_logger.LogInformation($"Updated version in module script: {psmFile}");
			}

			// Update changelog if requested
			if (updateChangelog)
			{
				// Check for a CHANGELOG.md in the module directory or parent directory
				string changelogPath = Path.Combine(modulePath, "CHANGELOG.md");
				if (!File.Exists(changelogPath))
				{
					changelogPath = Path.Combine(repoRoot, "CHANGELOG.md");
				}

				if (File.Exists(changelogPath))
				{
					UpdateChangelogWithModuleChanges(changelogPath, moduleName, version);
					_logger.LogInformation($"Updated changelog: {changelogPath}");
				}
				else
				{
					_logger.LogWarning("Changelog file not found");
				}
			}

			_logger.LogInformation($"PowerShell module version updated to {version}");
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError($"Error updating PowerShell module version: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Updates version information in a PowerShell module script (.psm1) file.
	/// </summary>
	/// <param name="psmFilePath">The path to the .psm1 file.</param>
	/// <param name="version">The new version.</param>
	private static void UpdateVersionInPsmFile(string psmFilePath, string version)
	{
		if (!File.Exists(psmFilePath))
		{
			return;
		}

		string content = File.ReadAllText(psmFilePath);

		// Look for version declarations in comments or variables
		// First look for version in comments
		var commentPattern = new Regex(@"#\s*Version:\s*([0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?)");
		content = commentPattern.Replace(content, $"# Version: {version}");

		// Then look for version in variable assignments with single quotes
		var singleQuotePattern = new Regex(@"Version\s*=\s*'([0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?)'");
		content = singleQuotePattern.Replace(content, $"Version = '{version}'");

		// Then look for version in variable assignments with double quotes
		var doubleQuotePattern = new Regex(@"Version\s*=\s*""([0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?)""");
		content = doubleQuotePattern.Replace(content, $"Version = \"{version}\"");

		File.WriteAllText(psmFilePath, content);
	}

	/// <summary>
	/// Updates a PowerShell module changelog with version information.
	/// </summary>
	/// <param name="changelogPath">The path to the changelog file.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="version">The new version.</param>
	private void UpdateChangelogWithModuleChanges(string changelogPath, string moduleName, string version)
	{
		if (!File.Exists(changelogPath))
		{
			return;
		}

		string content = File.ReadAllText(changelogPath);
		var sb = new StringBuilder();

		// Add new version header if it doesn't exist
		string versionHeader = $"## {version}";
		string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
		string fullHeader = $"## {version} - {dateStr}";

		if (!content.Contains(versionHeader))
		{
			// Create new entry at the top
			sb.AppendLine($"# Changelog for {moduleName}");
			sb.AppendLine();
			sb.AppendLine(fullHeader);
			sb.AppendLine();

			// Get relevant commits for this version
			string repoPath = Path.GetDirectoryName(Path.GetDirectoryName(changelogPath)) ?? "";
			string changelog = "";

			try
			{
				if (Directory.Exists(Path.Combine(repoPath, ".git")))
				{
					changelog = GenerateChangelog(repoPath, null, null, true);
				}
			}
			catch (Exception ex)
			{
				_logger.LogWarning($"Failed to generate changelog from git: {ex.Message}");
			}

			// Add placeholder if no changes are found
			if (string.IsNullOrEmpty(changelog))
			{
				sb.AppendLine("### Changes");
				sb.AppendLine();
				sb.AppendLine("- Updated module version");
				sb.AppendLine();
			}
			else
			{
				sb.Append(changelog);
				sb.AppendLine();
			}

			// Add the existing content
			sb.Append(content);

			// Write the updated content
			File.WriteAllText(changelogPath, sb.ToString());
		}
	}

	[GeneratedRegex(@"<Version>.*?</Version>")]
	private static partial Regex MyRegex();
}

/// <summary>
/// Contains version information.
/// </summary>
public class VersionInfo
{
	/// <summary>
	/// Gets or sets the version string.
	/// </summary>
	public string Version { get; set; } = "0.1.0";

	/// <summary>
	/// Gets or sets the short Git SHA.
	/// </summary>
	public string ShortSha { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the full Git SHA.
	/// </summary>
	public string FullSha { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the commit date.
	/// </summary>
	public DateTimeOffset CommitDate { get; set; }

	/// <summary>
	/// Gets or sets the commit message.
	/// </summary>
	public string CommitMessage { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the branch name.
	/// </summary>
	public string BranchName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether this is a pre-release version.
	/// </summary>
	public bool IsPreRelease { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the repository has local changes.
	/// </summary>
	public bool HasLocalChanges { get; set; }
}
