namespace PSBuild.Utilities;

using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.Logging;

/// <summary>
/// Handles running external commands and capturing their output.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CommandRunner"/> class.
/// </remarks>
/// <param name="logger">The logger to use for logging messages.</param>
public class CommandRunner(ILogger<CommandRunner> logger)
{
	private readonly ILogger<CommandRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

	/// <summary>
	/// Runs an external command and captures its output.
	/// </summary>
	/// <param name="fileName">The name of the executable to run.</param>
	/// <param name="arguments">The arguments to pass to the executable.</param>
	/// <param name="workingDirectory">The working directory to use when running the command.</param>
	/// <returns>A CommandResult containing the exit code, standard output, and standard error.</returns>
	public CommandResult RunCommand(string fileName, string arguments, string? workingDirectory = null)
	{
		_logger.LogDebug($"Running command: {fileName} {arguments}");

		var startInfo = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = arguments,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		if (!string.IsNullOrEmpty(workingDirectory))
		{
			startInfo.WorkingDirectory = workingDirectory;
		}

		var outputBuilder = new StringBuilder();
		var errorBuilder = new StringBuilder();

		using var process = new Process { StartInfo = startInfo };
		process.OutputDataReceived += (sender, e) =>
		{
			if (e.Data != null)
			{
				outputBuilder.AppendLine(e.Data);
				_logger.LogDebug($"[Output] {e.Data}");
			}
		};

		process.ErrorDataReceived += (sender, e) =>
		{
			if (e.Data != null)
			{
				errorBuilder.AppendLine(e.Data);
				_logger.LogDebug($"[Error] {e.Data}");
			}
		};

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		var result = new CommandResult
		{
			ExitCode = process.ExitCode,
			Output = outputBuilder.ToString(),
			Error = errorBuilder.ToString()
		};

		_logger.LogDebug($"Command exit code: {result.ExitCode}");

		return result;
	}

	/// <summary>
	/// Runs an external command and throws an exception if it fails.
	/// </summary>
	/// <param name="fileName">The name of the executable to run.</param>
	/// <param name="arguments">The arguments to pass to the executable.</param>
	/// <param name="workingDirectory">The working directory to use when running the command.</param>
	/// <returns>A CommandResult containing the exit code, standard output, and standard error.</returns>
	/// <exception cref="CommandFailedException">Thrown when the command exits with a non-zero code.</exception>
	public CommandResult RunCommandAndThrow(string fileName, string arguments, string? workingDirectory = null)
	{
		var result = RunCommand(fileName, arguments, workingDirectory);

		return result.ExitCode != 0
			?             throw new CommandFailedException(
				$"Command '{fileName} {arguments}' failed with exit code {result.ExitCode}",
				result.ExitCode,
				result.Output,
				result.Error)
			: result;
	}
}

/// <summary>
/// Contains the result of running a command.
/// </summary>
public class CommandResult
{
	/// <summary>
	/// Gets or sets the exit code of the command.
	/// </summary>
	public int ExitCode { get; set; }

	/// <summary>
	/// Gets or sets the standard output of the command.
	/// </summary>
	public string Output { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the standard error of the command.
	/// </summary>
	public string Error { get; set; } = string.Empty;

	/// <summary>
	/// Gets a value indicating whether the command succeeded.
	/// </summary>
	public bool Success => ExitCode == 0;
}

/// <summary>
/// Exception thrown when a command fails.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CommandFailedException"/> class.
/// </remarks>
/// <param name="message">The exception message.</param>
/// <param name="exitCode">The exit code of the command.</param>
/// <param name="output">The standard output of the command.</param>
/// <param name="error">The standard error of the command.</param>
public class CommandFailedException(string message, int exitCode, string output, string error) : Exception(message)
{

	/// <summary>
	/// Gets the exit code of the command.
	/// </summary>
	public int ExitCode { get; } = exitCode;

	/// <summary>
	/// Gets the standard output of the command.
	/// </summary>
	public string Output { get; } = output;

	/// <summary>
	/// Gets the standard error of the command.
	/// </summary>
	public string Error { get; } = error;
	public CommandFailedException()
	{
	}
}
