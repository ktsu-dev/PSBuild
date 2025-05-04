namespace PSBuild.CLI;

using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal class Program
{
	public static async Task<int> Main(string[] args)
	{
		// Setup dependency injection
		var services = new ServiceCollection();

		// Add logging
		services.AddLogging(configure => configure.AddConsole());

		// Add PSBuild services
		services.AddPSBuild();

		// Build service provider
		var serviceProvider = services.BuildServiceProvider();

		// Create root command
		var rootCommand = new RootCommand("PSBuild: A comprehensive build automation tool for .NET applications");

		// Add version command
		var versionCommand = new Command("version", "Manage version information based on git history");

		// Add version get command
		var versionGetCommand = new Command("get", "Get the current version information");
		versionGetCommand.SetHandler(() =>
		{
			string workingDir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Getting version info from repository at: {workingDir}");

			var versionManager = serviceProvider.GetRequiredService<VersionManager>();
			var versionInfo = versionManager.GetVersionInfoFromGit(workingDir);

			Console.WriteLine($"Previous version: {versionInfo.PreviousVersion}");
			Console.WriteLine($"New version: {versionInfo.NewVersion}");
			Console.WriteLine($"Bump type: {versionInfo.BumpType}");
			Console.WriteLine($"Reason: {versionInfo.BumpReason}");
		});

		// Add version set command
		var versionSetCommand = new Command("set", "Set a new version");
		versionSetCommand.AddArgument(new Argument<string>("version", "The version to set"));

		// Use the Action<string> overload explicitly
		versionSetCommand.SetHandler((Action<string>)((string version) =>
		{
			string workingDir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Setting version {version} in repository at: {workingDir}");

			var versionManager = serviceProvider.GetRequiredService<VersionManager>();
			var success = versionManager.NewVersion(workingDir, version);

			if (success)
			{
				Console.WriteLine($"Version set to {version}");
			}
			else
			{
				Console.WriteLine("Failed to set version");
				Environment.ExitCode = 1;
			}
		}), new Argument<string>("version"));

		versionCommand.AddCommand(versionGetCommand);
		versionCommand.AddCommand(versionSetCommand);
		rootCommand.AddCommand(versionCommand);

		// Add build command
		var buildCommand = new Command("build", "Build the .NET solution");
		var configOption = new Option<string>("--configuration", "The build configuration to use") { IsRequired = false };
		configOption.SetDefaultValue("Release");
		buildCommand.AddOption(configOption);

		// Use the Action<string> overload explicitly
		buildCommand.SetHandler((Action<string>)((string configuration) =>
		{
			string workingDir = Directory.GetCurrentDirectory();
			Console.WriteLine($"Building solution in {workingDir} with configuration {configuration}");

			var buildManager = serviceProvider.GetRequiredService<BuildManager>();
			buildManager.InitializeBuildEnvironment();

			Console.WriteLine("Build functionality will be implemented in a future version");
		}), configOption);

		rootCommand.AddCommand(buildCommand);

		// Add init command
		var initCommand = new Command("init", "Initialize a build environment");
		initCommand.SetHandler((Action)(() =>
		{
			var buildManager = serviceProvider.GetRequiredService<BuildManager>();
			buildManager.InitializeBuildEnvironment();
			Console.WriteLine("Build environment initialized");
		}));

		rootCommand.AddCommand(initCommand);

		// Add release command
		var releaseCommand = new Command("release", "Create and publish releases");
		rootCommand.AddCommand(releaseCommand);

		return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
	}
}
