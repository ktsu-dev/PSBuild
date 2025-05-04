// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace PSBuild.BuildManagement;

/// <summary>
/// Contains configuration settings for a build.
/// </summary>
public class BuildConfiguration
{
	/// <summary>
	/// Gets or sets a value indicating whether this is an official build.
	/// </summary>
	public bool IsOfficial { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this is the main branch.
	/// </summary>
	public bool IsMain { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this is a tagged build.
	/// </summary>
	public bool IsTagged { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this build should be released.
	/// </summary>
	public bool ShouldRelease { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to use dotnet-script.
	/// </summary>
	public bool UseDotnetScript { get; set; }

	/// <summary>
	/// Gets or sets the output path for build artifacts.
	/// </summary>
	public string OutputPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the staging path for release artifacts.
	/// </summary>
	public string StagingPath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the pattern to match NuGet packages.
	/// </summary>
	public string PackagePattern { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the pattern to match symbols packages.
	/// </summary>
	public string SymbolsPattern { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the pattern to match application archives.
	/// </summary>
	public string ApplicationPattern { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the build arguments to use for compilation.
	/// </summary>
	public string BuildArgs { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the workspace path.
	/// </summary>
	public string WorkspacePath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the .NET SDK version.
	/// </summary>
	public string DotnetVersion { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the server URL.
	/// </summary>
	public string ServerUrl { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the Git reference (branch/tag).
	/// </summary>
	public string GitRef { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the Git commit SHA.
	/// </summary>
	public string GitSha { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub owner.
	/// </summary>
	public string GitHubOwner { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub repository name.
	/// </summary>
	public string GitHubRepo { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the GitHub token.
	/// </summary>
	public string GithubToken { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the NuGet API key.
	/// </summary>
	public string NuGetApiKey { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the expected owner.
	/// </summary>
	public string ExpectedOwner { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the version being built.
	/// </summary>
	public string Version { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the release hash.
	/// </summary>
	public string ReleaseHash { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the path to the changelog file.
	/// </summary>
	public string ChangelogFile { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the patterns for release assets.
	/// </summary>
	public List<string> AssetPatterns { get; set; } = [];

	/// <summary>
	/// Gets or sets the path to the PowerShell module.
	/// </summary>
	public string ModulePath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the name of the PowerShell module.
	/// </summary>
	public string ModuleName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the list of artifacts created during the build.
	/// </summary>
	public List<string> Artifacts { get; set; } = [];

	/// <summary>
	/// Gets or sets the PowerShell Gallery API key.
	/// </summary>
	public string PSGalleryApiKey { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets a value indicating whether to skip tests.
	/// </summary>
	public bool SkipTests { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to collect code coverage.
	/// </summary>
	public bool CollectCodeCoverage { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to sign artifacts.
	/// </summary>
	public bool SignArtifacts { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to create draft releases.
	/// </summary>
	public bool CreateDraftRelease { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to update the version in files.
	/// </summary>
	public bool UpdateVersionInFiles { get; set; } = true;

	/// <summary>
	/// Gets or sets a value indicating whether to generate a changelog.
	/// </summary>
	public bool GenerateChangelog { get; set; } = true;

	/// <summary>
	/// Gets or sets the parameters for the build process.
	/// </summary>
	public Dictionary<string, string> Parameters { get; set; } = [];

	/// <summary>
	/// Gets or sets the timestamp of the build start.
	/// </summary>
	public DateTimeOffset BuildTimestamp { get; set; } = DateTimeOffset.Now;
}
