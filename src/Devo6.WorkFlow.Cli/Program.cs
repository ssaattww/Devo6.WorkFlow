using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// Provides the command-line entry point for running and validating .csx workflows.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the command-line interface and returns the process exit code.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero on success; non-zero on command, validation, or workflow failure.</returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Devo6.WorkFlow CLI");
            Console.WriteLine("Usage: engine run|validate <entry.csx> [--entry Name] [--config path] [--set key=value]");

            return 0;
        }

        if (!TryParse(args, out CliCommand command, out string errorMessage))
        {
            Console.Error.WriteLine(errorMessage);

            return 2;
        }

        string entryPath = Path.GetFullPath(command.EntryPath);
        string configPath = ResolveConfigPath(entryPath, command.ConfigPath);
        var loader = new CsxEntryLoader();

        if (command.Name == "validate")
        {
            WorkflowValidationResult validationResult = loader.Validate(
                entryPath,
                command.EntryName,
                new CsxValidationOptions
                {
                    ConfigPaths = string.IsNullOrEmpty(configPath) ? [] : [configPath],
                });

            return PrintValidationResult(validationResult);
        }

        WorkflowResult result = loader.Execute(
            entryPath,
            command.EntryName,
            new WorkflowExecutionOptions(engineArguments: new EngineArguments
            {
                EntryPath = entryPath,
                ConfigPath = configPath,
                Settings = command.Settings,
            }));

        if (result.Succeeded)
        {
            Console.WriteLine($"Succeeded: {result.EntryName}");

            return 0;
        }

        Console.Error.WriteLine($"{result.ErrorCode}: {result.ErrorMessage}");

        return 1;
    }

    private static int PrintValidationResult(WorkflowValidationResult validationResult)
    {
        if (validationResult.Succeeded)
        {
            Console.WriteLine("Validation succeeded.");

            return 0;
        }

        foreach (ValidationError error in validationResult.Errors)
        {
            Console.Error.WriteLine($"{error.Code}: {error.Path}: {error.Message}");
        }

        return 1;
    }

    private static bool TryParse(string[] args, out CliCommand command, out string errorMessage)
    {
        command = new CliCommand("", "", null, "", new Dictionary<string, string>());
        errorMessage = "";

        if (args.Length < 2)
        {
            errorMessage = "A command and entry .csx path are required.";

            return false;
        }

        string commandName = args[0];
        if (commandName is not ("run" or "validate"))
        {
            errorMessage = $"Unknown command: {commandName}";

            return false;
        }

        string entryPath = args[1];
        string? entryName = null;
        string configPath = "";
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--entry":
                    if (!TryReadValue(args, ref i, out entryName))
                    {
                        errorMessage = "--entry requires a value.";
                        return false;
                    }

                    break;
                case "--config":
                    if (!TryReadValue(args, ref i, out configPath))
                    {
                        errorMessage = "--config requires a value.";
                        return false;
                    }

                    break;
                case "--set":
                    if (!TryReadValue(args, ref i, out string setting))
                    {
                        errorMessage = "--set requires a key=value value.";
                        return false;
                    }

                    int separatorIndex = setting.IndexOf('=', StringComparison.Ordinal);
                    if (separatorIndex <= 0)
                    {
                        errorMessage = "--set requires a key=value value.";
                        return false;
                    }

                    settings[setting[..separatorIndex]] = setting[(separatorIndex + 1)..];
                    break;
                default:
                    errorMessage = $"Unknown option: {args[i]}";
                    return false;
            }
        }

        command = new CliCommand(commandName, entryPath, entryName, configPath, settings);

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        value = "";

        if (index + 1 >= args.Length)
        {
            return false;
        }

        index++;
        value = args[index];

        return true;
    }

    private static string ResolveConfigPath(string entryPath, string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return "";
        }

        if (Path.IsPathRooted(configPath))
        {
            return Path.GetFullPath(configPath);
        }

        string entryDirectory = Path.GetDirectoryName(Path.GetFullPath(entryPath)) ?? Directory.GetCurrentDirectory();

        return Path.GetFullPath(Path.Combine(entryDirectory, configPath));
    }

    private sealed record CliCommand(
        string Name,
        string EntryPath,
        string? EntryName,
        string ConfigPath,
        IReadOnlyDictionary<string, string> Settings);
}
