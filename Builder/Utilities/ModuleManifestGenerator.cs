// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace PSBuild.Utilities;

using System.Text;

using Microsoft.Extensions.Logging;

/// <summary>
/// Utility for generating PowerShell module manifests.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ModuleManifestGenerator"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging messages.</param>
/// <param name="commandRunner">The command runner to use for running external commands.</param>
public class ModuleManifestGenerator(ILogger<ModuleManifestGenerator> logger, CommandRunner commandRunner)
{
	private readonly ILogger<ModuleManifestGenerator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly CommandRunner _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));

	/// <summary>
	/// Creates a new PowerShell module manifest.
	/// </summary>
	/// <param name="modulePath">The path to the module directory.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="moduleVersion">The version of the module.</param>
	/// <param name="description">A description of the module.</param>
	/// <param name="author">The author of the module.</param>
	/// <param name="companyName">The company that created the module.</param>
	/// <param name="copyright">The copyright notice for the module.</param>
	/// <param name="requiredModules">A list of modules that this module depends on.</param>
	/// <param name="functionsToExport">A list of functions to export from the module.</param>
	/// <param name="cmdletsToExport">A list of cmdlets to export from the module.</param>
	/// <param name="variablesToExport">A list of variables to export from the module.</param>
	/// <param name="aliasesToExport">A list of aliases to export from the module.</param>
	/// <param name="projectUri">The URI to the project's repository.</param>
	/// <param name="licenseUri">The URI to the module's license.</param>
	/// <param name="tags">Tags for the module to aid in discovery.</param>
	/// <returns>The path to the created module manifest file.</returns>
	public string CreateModuleManifest(
		string modulePath,
		string moduleName,
		string moduleVersion,
		string description,
		string author = "",
		string companyName = "",
		string copyright = "",
		IEnumerable<string>? requiredModules = null,
		IEnumerable<string>? functionsToExport = null,
		IEnumerable<string>? cmdletsToExport = null,
		IEnumerable<string>? variablesToExport = null,
		IEnumerable<string>? aliasesToExport = null,
		string? projectUri = null,
		string? licenseUri = null,
		IEnumerable<string>? tags = null)
	{
		_logger.LogInformation($"Creating module manifest for {moduleName} v{moduleVersion}");

		if (!Directory.Exists(modulePath))
		{
			_logger.LogInformation($"Creating module directory: {modulePath}");
			Directory.CreateDirectory(modulePath);
		}

		// Generate default values if not provided
		var year = DateTime.Now.Year;
		author = string.IsNullOrEmpty(author) ? "PSBuild Authors" : author;
		companyName = string.IsNullOrEmpty(companyName) ? author : companyName;
		copyright = string.IsNullOrEmpty(copyright) ? $"(c) {year} {author}. All rights reserved." : copyright;

		// Build the PowerShell command to create the manifest
		var sbCommand = new StringBuilder();
		sbCommand.AppendLine("$manifest = @{");
		sbCommand.AppendLine($"    RootModule = '{moduleName}.psm1'");
		sbCommand.AppendLine($"    ModuleVersion = '{moduleVersion}'");
		sbCommand.AppendLine($"    Description = '{EscapePSString(description)}'");
		sbCommand.AppendLine($"    Author = '{EscapePSString(author)}'");
		sbCommand.AppendLine($"    CompanyName = '{EscapePSString(companyName)}'");
		sbCommand.AppendLine($"    Copyright = '{EscapePSString(copyright)}'");
		sbCommand.AppendLine($"    PowerShellVersion = '5.1'");

		// Add required modules if specified
		if (requiredModules != null && IsNotEmpty(requiredModules))
		{
			var modules = string.Join("', '", requiredModules);
			sbCommand.AppendLine($"    RequiredModules = @('{modules}')");
		}

		// Add exported functions if specified
		if (functionsToExport != null && IsNotEmpty(functionsToExport))
		{
			var functions = string.Join("', '", functionsToExport);
			sbCommand.AppendLine($"    FunctionsToExport = @('{functions}')");
		}
		else
		{
			sbCommand.AppendLine("    FunctionsToExport = @()");
		}

		// Add exported cmdlets if specified
		if (cmdletsToExport != null && IsNotEmpty(cmdletsToExport))
		{
			var cmdlets = string.Join("', '", cmdletsToExport);
			sbCommand.AppendLine($"    CmdletsToExport = @('{cmdlets}')");
		}
		else
		{
			sbCommand.AppendLine("    CmdletsToExport = @()");
		}

		// Add exported variables if specified
		if (variablesToExport != null && IsNotEmpty(variablesToExport))
		{
			var variables = string.Join("', '", variablesToExport);
			sbCommand.AppendLine($"    VariablesToExport = @('{variables}')");
		}
		else
		{
			sbCommand.AppendLine("    VariablesToExport = @()");
		}

		// Add exported aliases if specified
		if (aliasesToExport != null && IsNotEmpty(aliasesToExport))
		{
			var aliases = string.Join("', '", aliasesToExport);
			sbCommand.AppendLine($"    AliasesToExport = @('{aliases}')");
		}
		else
		{
			sbCommand.AppendLine("    AliasesToExport = @()");
		}

		// Add private data if URIs or tags are specified
		if (projectUri != null || licenseUri != null || (tags != null && IsNotEmpty(tags)))
		{
			sbCommand.AppendLine("    PrivateData = @{");
			sbCommand.AppendLine("        PSData = @{");

			if (tags != null && IsNotEmpty(tags))
			{
				var tagList = string.Join("', '", tags);
				sbCommand.AppendLine($"            Tags = @('{tagList}')");
			}

			if (projectUri != null)
			{
				sbCommand.AppendLine($"            ProjectUri = '{projectUri}'");
			}

			if (licenseUri != null)
			{
				sbCommand.AppendLine($"            LicenseUri = '{licenseUri}'");
			}

			sbCommand.AppendLine("        }");
			sbCommand.AppendLine("    }");
		}

		sbCommand.AppendLine("}");
		sbCommand.AppendLine($"New-ModuleManifest -Path '{Path.Combine(modulePath, $"{moduleName}.psd1")}' @manifest");

		// Execute the PowerShell command to create the manifest
		var result = _commandRunner.RunCommand(
			"powershell",
			$"-Command \"{sbCommand.ToString().Replace("\"", "`\"")}\"");

		if (result.ExitCode != 0)
		{
			_logger.LogError($"Failed to create module manifest: {result.Error}");
			throw new InvalidOperationException($"Failed to create module manifest: {result.Error}");
		}

		var manifestPath = Path.Combine(modulePath, $"{moduleName}.psd1");
		_logger.LogInformation($"Module manifest created at {manifestPath}");

		return manifestPath;
	}

	/// <summary>
	/// Creates a default .psm1 file for the module.
	/// </summary>
	/// <param name="modulePath">The path to the module directory.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <returns>The path to the created .psm1 file.</returns>
	public string CreateModulePsm1(string modulePath, string moduleName)
	{
		_logger.LogInformation($"Creating .psm1 file for {moduleName}");

		var psm1Path = Path.Combine(modulePath, $"{moduleName}.psm1");

		// Create a basic .psm1 file
		var sbContent = new StringBuilder();
		sbContent.AppendLine("# Module script file for the " + moduleName + " module");
		sbContent.AppendLine();
		sbContent.AppendLine("# Dot source any functions in the private folder");
		sbContent.AppendLine("$privateFunctions = Get-ChildItem -Path $PSScriptRoot\\Private\\*.ps1 -ErrorAction SilentlyContinue");
		sbContent.AppendLine("foreach ($function in $privateFunctions) {");
		sbContent.AppendLine("    . $function.FullName");
		sbContent.AppendLine("}");
		sbContent.AppendLine();
		sbContent.AppendLine("# Dot source any functions in the public folder and export them");
		sbContent.AppendLine("$publicFunctions = Get-ChildItem -Path $PSScriptRoot\\Public\\*.ps1 -ErrorAction SilentlyContinue");
		sbContent.AppendLine("foreach ($function in $publicFunctions) {");
		sbContent.AppendLine("    . $function.FullName");
		sbContent.AppendLine("    Export-ModuleMember -Function $function.BaseName");
		sbContent.AppendLine("}");
		sbContent.AppendLine();
		sbContent.AppendLine("# Create alias to any of the functions");
		sbContent.AppendLine("# New-Alias -Name 'Alias' -Value 'Function'");
		sbContent.AppendLine("# Export-ModuleMember -Alias 'Alias'");

		File.WriteAllText(psm1Path, sbContent.ToString());

		// Create public and private directories
		Directory.CreateDirectory(Path.Combine(modulePath, "Public"));
		Directory.CreateDirectory(Path.Combine(modulePath, "Private"));

		_logger.LogInformation($"Module .psm1 file created at {psm1Path}");

		return psm1Path;
	}

	/// <summary>
	/// Creates the folder structure for a PowerShell module.
	/// </summary>
	/// <param name="modulePath">The path to the module directory.</param>
	/// <param name="moduleName">The name of the module.</param>
	/// <param name="moduleVersion">The version of the module.</param>
	/// <param name="description">A description of the module.</param>
	/// <returns>The path to the created module directory.</returns>
	public string CreateModuleStructure(
		string modulePath,
		string moduleName,
		string moduleVersion,
		string description)
	{
		_logger.LogInformation($"Creating module structure for {moduleName}");

		// Create the module directory
		if (!Directory.Exists(modulePath))
		{
			Directory.CreateDirectory(modulePath);
		}

		// Create the .psm1 file
		CreateModulePsm1(modulePath, moduleName);

		// Create the module manifest
		CreateModuleManifest(
			modulePath,
			moduleName,
			moduleVersion,
			description);

		// Create a basic README.md file
		var readmePath = Path.Combine(modulePath, "README.md");
		var readmeContent = $"# {moduleName}\n\n{description}\n\n## Installation\n\n```powershell\nInstall-Module -Name {moduleName}\n```\n\n## Usage\n\n```powershell\nImport-Module {moduleName}\n```\n";
		File.WriteAllText(readmePath, readmeContent);

		// Create a basic license file
		var licensePath = Path.Combine(modulePath, "LICENSE.txt");
		var licenseContent = $"MIT License\n\nCopyright (c) {DateTime.Now.Year}\n\nPermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\nThe above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.\n\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.";
		File.WriteAllText(licensePath, licenseContent);

		_logger.LogInformation($"Module structure created at {modulePath}");

		return modulePath;
	}

	/// <summary>
	/// Adds a new function to a PowerShell module.
	/// </summary>
	/// <param name="modulePath">The path to the module directory.</param>
	/// <param name="functionName">The name of the function to add.</param>
	/// <param name="functionContent">The content of the function.</param>
	/// <param name="isPublic">Whether the function is public or private.</param>
	/// <returns>The path to the created function file.</returns>
	public string AddFunction(
		string modulePath,
		string functionName,
		string functionContent,
		bool isPublic = true)
	{
		var folderName = isPublic ? "Public" : "Private";
		var functionPath = Path.Combine(modulePath, folderName, $"{functionName}.ps1");

		_logger.LogInformation($"Adding {folderName.ToLower(System.Globalization.CultureInfo.CurrentCulture)} function {functionName} to module");

		// Create the directory if it doesn't exist
		Directory.CreateDirectory(Path.Combine(modulePath, folderName));

		// Write the function file
		File.WriteAllText(functionPath, functionContent);

		_logger.LogInformation($"Function added at {functionPath}");

		return functionPath;
	}

	/// <summary>
	/// Creates a template for a PowerShell function.
	/// </summary>
	/// <param name="functionName">The name of the function.</param>
	/// <param name="description">A description of the function.</param>
	/// <param name="parameters">A dictionary of parameter names and types.</param>
	/// <param name="returnType">The type that the function returns.</param>
	/// <returns>A string containing the function template.</returns>
	public static string CreateFunctionTemplate(
		string functionName,
		string description,
		Dictionary<string, string>? parameters = null,
		string returnType = "void")
	{
		var sb = new StringBuilder();

		// Add function header
		sb.AppendLine("<#");
		sb.AppendLine($".SYNOPSIS");
		sb.AppendLine($"    {description}");
		sb.AppendLine();
		sb.AppendLine($".DESCRIPTION");
		sb.AppendLine($"    {description}");
		sb.AppendLine();

		// Add parameter documentation
		if (parameters != null)
		{
			foreach (var param in parameters)
			{
				sb.AppendLine($".PARAMETER {param.Key}");
				sb.AppendLine($"    The {param.Key.ToLower(System.Globalization.CultureInfo.CurrentCulture)} to use.");
				sb.AppendLine();
			}
		}

		sb.AppendLine($".EXAMPLE");
		sb.AppendLine($"    {functionName}");
		sb.AppendLine();

		sb.AppendLine("#>");
		sb.AppendLine($"function {functionName} {{");

		// Add parameters
		if (parameters != null && parameters.Count > 0)
		{
			sb.AppendLine("    [CmdletBinding()]");
			sb.AppendLine("    param(");

			var i = 0;
			foreach (var param in parameters)
			{
				var isLast = i == parameters.Count - 1;
				sb.AppendLine($"        [{param.Value}]");
				sb.Append($"        ${param.Key}");

				if (!isLast)
				{
					sb.AppendLine(",");
				}
				else
				{
					sb.AppendLine();
				}

				i++;
			}

			sb.AppendLine("    )");
		}
		else
		{
			sb.AppendLine("    [CmdletBinding()]");
			sb.AppendLine("    param()");
		}

		// Add function body
		sb.AppendLine();
		sb.AppendLine("    begin {");
		sb.AppendLine("        Write-Verbose \"Starting $($MyInvocation.MyCommand)\"");
		sb.AppendLine("    }");
		sb.AppendLine();
		sb.AppendLine("    process {");
		sb.AppendLine("        # Add your function logic here");
		sb.AppendLine("    }");
		sb.AppendLine();
		sb.AppendLine("    end {");
		sb.AppendLine("        Write-Verbose \"Ending $($MyInvocation.MyCommand)\"");
		sb.AppendLine("    }");
		sb.AppendLine("}");

		return sb.ToString();
	}

	private static string EscapePSString(string value) => value.Replace("'", "''");

	private static bool IsNotEmpty<T>(IEnumerable<T> collection) => collection.GetEnumerator().MoveNext();

	/// <inheritdoc/>
	public string CreateModuleManifest(string modulePath, string moduleName, string moduleVersion, string description, string author = "", string companyName = "", string copyright = "", IEnumerable<string>? requiredModules = null, IEnumerable<string>? functionsToExport = null, IEnumerable<string>? cmdletsToExport = null, IEnumerable<string>? variablesToExport = null, IEnumerable<string>? aliasesToExport = null, Uri? projectUri = null, string? licenseUri = null, IEnumerable<string>? tags = null) => throw new NotImplementedException();

	public string CreateModuleManifest(string modulePath, string moduleName, string moduleVersion, string description, string author = "", string companyName = "", string copyright = "", IEnumerable<string>? requiredModules = null, IEnumerable<string>? functionsToExport = null, IEnumerable<string>? cmdletsToExport = null, IEnumerable<string>? variablesToExport = null, IEnumerable<string>? aliasesToExport = null, string? projectUri = null, Uri licenseUri = null, IEnumerable<string>? tags = null)
	{
		throw new NotImplementedException();
	}
}
