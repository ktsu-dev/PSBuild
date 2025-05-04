namespace PSBuild;

using Microsoft.Extensions.DependencyInjection;

using PSBuild.BuildManagement;
using PSBuild.ReleaseManagement;
using PSBuild.Utilities;
using PSBuild.VersionManagement;

/// <summary>
/// Provides extension methods for registering PSBuild services with dependency injection.
/// </summary>
public static class ServiceRegistration
{
	/// <summary>
	/// Adds PSBuild services to the specified <see cref="IServiceCollection"/>.
	/// </summary>
	/// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
	/// <returns>The same service collection so that multiple calls can be chained.</returns>
	public static IServiceCollection AddPSBuild(this IServiceCollection services)
	{
		// Register utilities
		services.AddTransient<CommandRunner>();
		services.AddTransient<ModuleManifestGenerator>();

		// Register managers
		services.AddTransient<BuildManager>();
		services.AddTransient<VersionManager>();
		services.AddTransient<ReleaseManager>();

		// Register build workflow
		services.AddTransient<BuildWorkflow>();

		return services;
	}
}
