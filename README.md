# Builder

A comprehensive .NET library and CLI tool for automating the build, test, package, and release process for .NET applications using Git-based versioning.

This is a .NET port of the original PowerShell Builder module.

## Features

- Semantic versioning based on git history and commit messages
- Automatic version calculation from commit analysis
- Metadata file generation and management
- Comprehensive build, test, and package pipeline
- NuGet package creation and publishing
- GitHub release creation with assets
- Proper line ending handling based on git config

## Installation

### Via .NET CLI

```bash
dotnet tool install --global Builder.CLI
```

### Manual

1. Clone the repository
2. Build the solution: `dotnet build -c Release`
3. Install the CLI tool: `dotnet tool install --global --add-source ./src/Builder.CLI/bin/Release Builder.CLI`

## Usage

### Command Line

```bash
# Get version information from git history
builder version get

# Set a specific version
builder version set 1.2.3

# Initialize build environment
builder init

# Build a project
builder build --configuration Release
```

### API

```csharp
// Add Builder to your services
services.AddBuilder();

// Use the build manager
var buildManager = serviceProvider.GetRequiredService<BuildManager>();
buildManager.InitializeBuildEnvironment();

// Use the version manager
var versionManager = serviceProvider.GetRequiredService<VersionManager>();
var versionInfo = versionManager.GetVersionInfoFromGit("/path/to/repo");
```

## Build Configuration

The `BuildManager.GetBuildConfigurationAsync` method returns a configuration object with the following key properties:

| Property | Description |
|----------|-------------|
| IsOfficial | Whether this is an official repository build |
| IsMain | Whether building from main branch |
| IsTagged | Whether the current commit is tagged |
| ShouldRelease | Whether a release should be created |
| UseDotnetScript | Whether .NET script files are present |
| OutputPath | Path for build outputs |
| StagingPath | Path for staging artifacts |
| PackagePattern | Pattern for NuGet packages |
| SymbolsPattern | Pattern for symbol packages |
| ApplicationPattern | Pattern for application archives |
| Version | Current version number |
| ReleaseHash | Hash of the release commit |

## Version Control

### Version Tags

Commits can include the following tags to control version increments:

| Tag | Description | Example |
|-----|-------------|---------|
| [major] | Triggers a major version increment | 2.0.0 |
| [minor] | Triggers a minor version increment | 1.2.0 |
| [patch] | Triggers a patch version increment | 1.1.2 |
| [pre] | Creates/increments pre-release version | 1.1.2-pre.1 |

### Automatic Version Calculation

The library analyzes commit history to determine appropriate version increments:

1. Checks for explicit version tags in commit messages
2. Analyzes code changes vs. documentation changes
3. Considers the scope and impact of changes
4. Maintains semantic versioning principles

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes with appropriate version tags
4. Create a pull request

## License

MIT License - See LICENSE.md for details
