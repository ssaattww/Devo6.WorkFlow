using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// .csx workflow の実行と validation を行う command-line entry point を提供します。
/// </summary>
public static class Program
{
    /// <summary>
    /// command-line interface を実行して process exit code を返します。
    /// </summary>
    /// <param name="args">command-line 引数。</param>
    /// <returns>成功時は 0。command、validation、または workflow 失敗時は 0 以外。</returns>
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

    /// <summary>
    /// validation 結果を標準出力または標準 error に表示します。
    /// </summary>
    /// <param name="validationResult">表示する validation 結果。</param>
    /// <returns>validation 成功時は 0。失敗時は 1。</returns>
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

    /// <summary>
    /// command-line 引数を CLI command に変換します。
    /// </summary>
    /// <param name="args">parse 対象の command-line 引数。</param>
    /// <param name="command">parse できた CLI command。</param>
    /// <param name="errorMessage">parse に失敗した場合の error message。</param>
    /// <returns>parse に成功した場合は true。</returns>
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

    /// <summary>
    /// option の直後にある値を読み取ります。
    /// </summary>
    /// <param name="args">command-line 引数。</param>
    /// <param name="index">現在の option index。成功時は値の index に進みます。</param>
    /// <param name="value">読み取った option 値。</param>
    /// <returns>値を読み取れた場合は true。</returns>
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

    /// <summary>
    /// entry path を基準に config path を絶対 path へ解決します。
    /// </summary>
    /// <param name="entryPath">workflow entry の path。</param>
    /// <param name="configPath">指定された config path。</param>
    /// <returns>解決済み config path。未指定の場合は空文字列。</returns>
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

    /// <summary>
    /// parse 済み CLI command と option を保持します。
    /// </summary>
    /// <param name="Name">実行する command 名。</param>
    /// <param name="EntryPath">workflow entry .csx file path。</param>
    /// <param name="EntryName">明示指定された entry Step 名。</param>
    /// <param name="ConfigPath">指定された config file path。</param>
    /// <param name="Settings">--set option で指定された override 設定。</param>
    private sealed record CliCommand(
        string Name,
        string EntryPath,
        string? EntryName,
        string ConfigPath,
        IReadOnlyDictionary<string, string> Settings);
}
