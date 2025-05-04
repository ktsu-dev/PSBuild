// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace PSBuild.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using PSBuild.BuildManagement;
using PSBuild.ReleaseManagement;
using PSBuild.Utilities;
using PSBuild.VersionManagement;

/// <summary>
/// Service locator for getting service instances.
/// </summary>
public static class ServiceLocator
{
	private static IServiceProvider? _serviceProvider;

	/// <summary>
	/// Initializes the service provider.
	/// </summary>
	public static void Initialize()
	{
		if (_serviceProvider != null)
		{
			return;
		}

		var services = new ServiceCollection();

		// Add logging
		services.AddLogging(configure =>
		{
			configure.AddConsole();
			configure.SetMinimumLevel(LogLevel.Information);
		});

		// Add utility services
		services.AddSingleton<CommandRunner>();
		services.AddSingleton<ModuleManifestGenerator>();

		// Add manager services
		services.AddSingleton<BuildManager>();
		services.AddSingleton<VersionManager>();
		services.AddSingleton<ReleaseManager>();

		// Add workflow service
		services.AddSingleton<BuildWorkflow>();

		_serviceProvider = services.BuildServiceProvider();
	}

	/// <summary>
	/// Gets the BuildManager service.
	/// </summary>
	/// <returns>The BuildManager instance.</returns>
	public static BuildManager GetBuildManager()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<BuildManager>();
	}

	/// <summary>
	/// Gets the VersionManager service.
	/// </summary>
	/// <returns>The VersionManager instance.</returns>
	public static VersionManager GetVersionManager()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<VersionManager>();
	}

	/// <summary>
	/// Gets the ReleaseManager service.
	/// </summary>
	/// <returns>The ReleaseManager instance.</returns>
	public static ReleaseManager GetReleaseManager()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<ReleaseManager>();
	}

	/// <summary>
	/// Gets the BuildWorkflow service.
	/// </summary>
	/// <returns>The BuildWorkflow instance.</returns>
	public static BuildWorkflow GetBuildWorkflow()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<BuildWorkflow>();
	}

	/// <summary>
	/// Gets the CommandRunner service.
	/// </summary>
	/// <returns>The CommandRunner instance.</returns>
	public static CommandRunner GetCommandRunner()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<CommandRunner>();
	}

	/// <summary>
	/// Gets the ModuleManifestGenerator service.
	/// </summary>
	/// <returns>The ModuleManifestGenerator instance.</returns>
	public static ModuleManifestGenerator GetModuleManifestGenerator()
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<ModuleManifestGenerator>();
	}

	/// <summary>
	/// Gets a service of the specified type.
	/// </summary>
	/// <typeparam name="T">The type of service to get.</typeparam>
	/// <returns>The service instance.</returns>
	public static T GetService<T>() where T : class
	{
		EnsureInitialized();
		return _serviceProvider!.GetRequiredService<T>();
	}

	private static void EnsureInitialized()
	{
		if (_serviceProvider == null)
		{
			Initialize();
		}
	}
}
