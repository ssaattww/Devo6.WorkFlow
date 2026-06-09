using Devo6.WorkFlow.Abstractions;
using Devo6.WorkFlow.Engine;
using Microsoft.Extensions.Logging;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.RepresentationModel;

namespace Devo6.WorkFlow.Cli;

/// <summary>
/// .csx workflow の実行と validation を行う command-line entry point を提供します。
/// </summary>
public static class Program
{
    /// <summary>
    /// ヘルプ表示で参照するエンジン既定 YAML の相対パス。
    /// </summary>
    private const string EngineDefaultsRelativePath = "config/engine.defaults.yaml";

    /// <summary>
    /// command-line interface を実行して process exit code を返します。
    /// </summary>
    /// <param name="args">command-line 引数。</param>
    /// <returns>成功時は 0。command、validation、または workflow 失敗時は 0 以外。</returns>
    public static int Main(string[] args)
    {
        return Run(args);
    }

    /// <summary>
    /// command-line interface を実行して process exit code を返します。
    /// </summary>
    /// <param name="args">command-line 引数。</param>
    /// <param name="nuGetDependencyGraphProvider">NuGet 依存 graph provider。null の場合は既定 provider を使います。</param>
    /// <returns>成功時は 0。command、validation、または workflow 失敗時は 0 以外。</returns>
    internal static int Run(string[] args, ICsxNuGetDependencyGraphProvider? nuGetDependencyGraphProvider = null)
    {
        if (args.Length == 0 || IsHelpCommand(args))
        {
            PrintHelp();

            return 0;
        }

        if (!TryParse(args, out CliCommand command, out string errorMessage))
        {
            Console.Error.WriteLine(errorMessage);

            return 2;
        }

        string entryPath = Path.GetFullPath(command.EntryPath);
        string workflowConfigPath = ResolveConfigPath(entryPath, command.WorkflowConfigPath);
        string engineConfigPath = ResolveConfigPath(entryPath, command.EngineConfigPath);
        string effectiveEngineConfigPath = string.IsNullOrWhiteSpace(command.EngineConfigPath)
            ? GetEngineDefaultsPath()
            : engineConfigPath;
        var loader = new CsxEntryLoader(new CsxEntryLoaderOptions
        {
            AllowedNuGetReferences = command.AllowedNuGetReferences,
            RequireNuGetLock = command.RequireNuGetLock,
            NuGetDependencyGraphProvider = nuGetDependencyGraphProvider,
        });

        if (command.Name == "validate")
        {
            WorkflowValidationResult validationResult = loader.Validate(
            entryPath,
            command.EntryName,
            new CsxValidationOptions
            {
                ConfigPaths = CollectValidationConfigPaths(
                    string.IsNullOrWhiteSpace(workflowConfigPath) ? null : workflowConfigPath,
                    string.IsNullOrWhiteSpace(engineConfigPath) ? null : engineConfigPath),
            });

            return PrintValidationResult(validationResult);
        }

        var baseExecutionOptions = new WorkflowExecutionOptions(
            engineArguments: new EngineArguments
            {
                EntryPath = entryPath,
                WorkflowConfigPath = workflowConfigPath,
                EngineConfigPath = engineConfigPath,
                WorkflowSettings = command.WorkflowSettings,
                EngineSettings = command.EngineSettings,
            });
        WorkflowExecutionOptions? executionOptions = ApplyEngineConfigToExecutionOptions(
            baseExecutionOptions,
            GetEngineDefaultsPath(),
            effectiveEngineConfigPath,
            command.EngineSettings,
            out string failureCode,
            out string failureMessage);

        if (executionOptions is null)
        {
            Console.Error.WriteLine($"{failureCode}: {failureMessage}");

            return 1;
        }

        WorkflowResult result = loader.Execute(
            entryPath,
            command.EntryName,
            executionOptions);

        if (result.Succeeded)
        {
            Console.WriteLine($"Succeeded: {result.EntryName}");

            return 0;
        }

        Console.Error.WriteLine($"{result.ErrorCode}: {result.ErrorMessage}");

        return 1;
    }

    /// <summary>
    /// ヘルプ表示を標準出力へ出力します。
    /// </summary>
    private static void PrintHelp()
    {
        Console.WriteLine("Devo6.WorkFlow CLI");
        Console.WriteLine("Usage: engine run|validate <entry.csx> [--entry Name] [--workflow-config path] [--workflow-set key=value] [--wset key=value] [--engine-config path] [--engine-set key=value] [--eset key=value] [--allow-nuget PackageId,Version] [--locked]");
        Console.WriteLine($"Engine defaults: {GetEngineDefaultsPath()}");
    }

    /// <summary>
    /// ヘルプ表示コマンドかどうかを判定します。
    /// </summary>
    /// <param name="args">CLI 引数。</param>
    /// <returns>help コマンドなら true。</returns>
    private static bool IsHelpCommand(string[] args)
    {
        return args.Length == 1 && args[0].Equals("help", StringComparison.Ordinal);
    }

    /// <summary>
    /// エンジン既定 YAML の実行時解決済み完全パスを取得します。
    /// </summary>
    /// <returns>実行時解決済み完全パス。</returns>
    private static string GetEngineDefaultsPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, EngineDefaultsRelativePath));
    }

    /// <summary>
    /// engine config を読み込み、WorkflowExecutionOptions の StepTimeout / Retry / LoggerFactory を反映します。
    /// </summary>
    /// <param name="executionOptions">反映先の option。</param>
    /// <param name="defaultsConfigPath">既定 engine config YAML。</param>
    /// <param name="engineConfigPath">`--engine-config` 解決済み path。</param>
    /// <param name="engineSettings">--engine-set / --eset の設定。</param>
    /// <param name="failureCode">失敗時の error code。</param>
    /// <param name="failureMessage">失敗時の詳細。</param>
    /// <returns>失敗時は null、成功時は反映済み options。</returns>
    private static WorkflowExecutionOptions? ApplyEngineConfigToExecutionOptions(
        WorkflowExecutionOptions executionOptions,
        string defaultsConfigPath,
        string engineConfigPath,
        IReadOnlyDictionary<string, string> engineSettings,
        out string failureCode,
        out string failureMessage)
    {
        failureCode = WorkflowErrorCodes.ConfigLoadFailed;
        failureMessage = "";
        var loggingOptions = new EngineLoggingOptions();

        if (!ApplyEngineConfigFile(
            defaultsConfigPath,
            executionOptions,
            loggingOptions,
            out failureCode,
            out failureMessage))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(engineConfigPath)
            && !string.Equals(engineConfigPath, defaultsConfigPath, StringComparison.Ordinal))
        {
            if (!ApplyEngineConfigFile(
                engineConfigPath,
                executionOptions,
                loggingOptions,
                out failureCode,
                out failureMessage))
            {
                return null;
            }
        }

        foreach (KeyValuePair<string, string> setting in engineSettings)
        {
            if (!ApplyEngineSetting(
                setting.Key,
                setting.Value,
                executionOptions,
                loggingOptions,
                out failureCode,
                out failureMessage))
            {
                return null;
            }
        }

        ILoggerFactory? loggerFactory = CreateLoggerFactory(loggingOptions, executionOptions.EngineArguments);

        return new WorkflowExecutionOptions(loggerFactory, executionOptions.EngineArguments)
        {
            StepTimeout = executionOptions.StepTimeout,
            Retry = executionOptions.Retry,
        };
    }

    /// <summary>
    /// engine config ファイルを 2 つの対象設定に展開して反映します。
    /// </summary>
    /// <param name="configPath">読込対象 YAML path。</param>
    /// <param name="executionOptions">反映先の option。</param>
    /// <param name="failureCode">失敗時の error code。</param>
    /// <param name="failureMessage">失敗時の詳細。</param>
    /// <returns>読み込み成功なら true。</returns>
    private static bool ApplyEngineConfigFile(
        string configPath,
        WorkflowExecutionOptions executionOptions,
        EngineLoggingOptions loggingOptions,
        out string failureCode,
        out string failureMessage)
    {
        failureCode = WorkflowErrorCodes.ConfigLoadFailed;
        failureMessage = "";

        if (!File.Exists(configPath))
        {
            failureCode = WorkflowErrorCodes.ConfigNotFound;
            failureMessage = $"Engine config file was not found: {configPath}";

            return false;
        }

        try
        {
            string configText = File.ReadAllText(configPath);
            if (!TryValidateEngineConfigPaths(configText, out string unsupportedConfigPath, out string configPathErrorMessage))
            {
                failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                failureMessage = $"Unsupported engine config path in {configPath}: {unsupportedConfigPath}. {configPathErrorMessage}";
                return false;
            }

            using var fileReader = new StringReader(configText);
            EngineConfigDto? config = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<EngineConfigDto>(fileReader);

            if (config?.Timeout is not null)
            {
                if (!TryReadTimeoutValue(config.Timeout.StepTimeout, out TimeSpan? timeout, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;

                    return false;
                }

                executionOptions.StepTimeout = timeout;
            }

            if (config?.Retry is not null)
            {
                if (config.Retry.MaxAttempts is not null)
                {
                    if (!int.TryParse(config.Retry.MaxAttempts, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxAttempts))
                    {
                        failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                        failureMessage = $"Retry.MaxAttempts in {configPath} is invalid: {config.Retry.MaxAttempts}";

                        return false;
                    }

                    executionOptions.Retry = new RetryOptions { MaxAttempts = maxAttempts };
                }
            }

            if (config?.Logging is not null)
            {
                if (config.Logging.Console is not null)
                {
                    if (config.Logging.Console.Enabled is not null)
                    {
                        loggingOptions.ConsoleEnabled = config.Logging.Console.Enabled.Value;
                    }

                    if (!string.IsNullOrWhiteSpace(config.Logging.Console.Format))
                    {
                        if (!TryReadLoggingFormat(config.Logging.Console.Format, out EngineLoggingFormat consoleFormat, out failureMessage))
                        {
                            failureCode = WorkflowErrorCodes.ConfigLoadFailed;

                            return false;
                        }

                        loggingOptions.ConsoleFormat = consoleFormat;
                    }
                }

                if (config.Logging.File is not null)
                {
                    if (config.Logging.File.Enabled is not null)
                    {
                        loggingOptions.FileEnabled = config.Logging.File.Enabled.Value;
                    }

                    if (config.Logging.File.Directory is not null)
                    {
                        loggingOptions.FileDirectory = config.Logging.File.Directory;
                    }

                    if (config.Logging.File.NameFormat is not null)
                    {
                        loggingOptions.FileNameFormat = config.Logging.File.NameFormat;
                    }

                    if (!string.IsNullOrWhiteSpace(config.Logging.File.Format))
                    {
                        if (!TryReadLoggingFormat(config.Logging.File.Format, out EngineLoggingFormat fileFormat, out failureMessage))
                        {
                            failureCode = WorkflowErrorCodes.ConfigLoadFailed;

                            return false;
                        }

                        loggingOptions.FileFormat = fileFormat;
                    }
                }
            }

            return true;
        }
        catch (YamlException exception)
        {
            failureCode = WorkflowErrorCodes.ConfigLoadFailed;
            failureMessage = $"Engine config could not be loaded from {configPath}. {exception.Message}";

            return false;
        }
        catch (Exception exception)
        {
            failureCode = WorkflowErrorCodes.ConfigLoadFailed;
            failureMessage = $"Engine config could not be loaded from {configPath}. {exception.Message}";

            return false;
        }
    }

    /// <summary>
    /// engine config YAML の既知セクションとキーのみを許可し、未知 path は失敗します。
    /// </summary>
    /// <param name="configText">YAML テキスト。</param>
    /// <param name="unsupportedPath">未対応の path。</param>
    /// <param name="errorMessage">失敗理由。</param>
    /// <returns>検証成功なら true。</returns>
    private static bool TryValidateEngineConfigPaths(
        string configText,
        out string unsupportedPath,
        out string errorMessage)
    {
        unsupportedPath = "";
        errorMessage = "";

        try
        {
            var yamlStream = new YamlStream();
            using var reader = new StringReader(configText);
            yamlStream.Load(reader);
            if (yamlStream.Documents.Count == 0)
            {
                return true;
            }

            if (yamlStream.Documents[0].RootNode is not YamlMappingNode rootNode)
            {
                return true;
            }

            if (!TryValidateMappingKeys(rootNode, "", ["Timeout", "Retry", "Logging"], "Unsupported engine config section.", out unsupportedPath, out errorMessage))
            {
                return false;
            }

            if (TryReadNodeValue(rootNode, "Timeout", out YamlNode? timeoutNode))
            {
                if (timeoutNode is not YamlMappingNode timeoutSection)
                {
                    unsupportedPath = "Timeout";
                    errorMessage = "Timeout section must be a mapping.";
                    return false;
                }

                if (!TryValidateMappingKeys(timeoutSection, "Timeout", ["StepTimeout"], "Unsupported timeout key.", out unsupportedPath, out errorMessage))
                {
                    return false;
                }
            }

            if (TryReadNodeValue(rootNode, "Retry", out YamlNode? retryNode))
            {
                if (retryNode is not YamlMappingNode retrySection)
                {
                    unsupportedPath = "Retry";
                    errorMessage = "Retry section must be a mapping.";
                    return false;
                }

                if (!TryValidateMappingKeys(retrySection, "Retry", ["MaxAttempts"], "Unsupported retry key.", out unsupportedPath, out errorMessage))
                {
                    return false;
                }
            }

            if (!TryReadNodeValue(rootNode, "Logging", out YamlNode? loggingNode))
            {
                return true;
            }

            if (loggingNode is not YamlMappingNode loggingSection)
            {
                unsupportedPath = "Logging";
                errorMessage = "Logging section must be a mapping.";
                return false;
            }

            if (!TryValidateMappingKeys(loggingSection, "Logging", ["Console", "File"], "Unsupported logging section.", out unsupportedPath, out errorMessage))
            {
                return false;
            }

            if (TryReadNodeValue(loggingSection, "Console", out YamlNode? consoleNode))
            {
                if (consoleNode is not YamlMappingNode consoleSection)
                {
                    unsupportedPath = "Logging.Console";
                    errorMessage = "Logging.Console section must be a mapping.";
                    return false;
                }

                if (!TryValidateMappingKeys(consoleSection, "Logging.Console", ["Enabled", "Format"], "Unsupported logging key.", out unsupportedPath, out errorMessage))
                {
                    return false;
                }
            }

            if (TryReadNodeValue(loggingSection, "File", out YamlNode? fileNode))
            {
                if (fileNode is not YamlMappingNode fileSection)
                {
                    unsupportedPath = "Logging.File";
                    errorMessage = "Logging.File section must be a mapping.";
                    return false;
                }

                if (!TryValidateMappingKeys(fileSection, "Logging.File", ["Enabled", "Directory", "NameFormat", "Format"], "Unsupported logging key.", out unsupportedPath, out errorMessage))
                {
                    return false;
                }
            }

            return true;
        }
        catch (YamlException exception)
        {
            unsupportedPath = "YAML";
            errorMessage = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// YAML mapping 配下のキーが許可リストに入っていることを検証します。
    /// </summary>
    /// <param name="mappingNode">検証対象 mapping ノード。</param>
    /// <param name="pathPrefix">対象パスの接頭辞。root の場合は空文字。</param>
    /// <param name="allowedKeys">許可キー一覧。</param>
    /// <param name="unsupportedKeyMessage">未知キーを見つけた場合の説明。</param>
    /// <param name="unsupportedPath">未対応の path。</param>
    /// <param name="errorMessage">失敗理由。</param>
    /// <returns>検証成功なら true。</returns>
    private static bool TryValidateMappingKeys(
        YamlMappingNode mappingNode,
        string pathPrefix,
        string[] allowedKeys,
        string unsupportedKeyMessage,
        out string unsupportedPath,
        out string errorMessage)
    {
        unsupportedPath = "";
        errorMessage = "";
        foreach (KeyValuePair<YamlNode, YamlNode> child in mappingNode.Children)
        {
            string key = GetNodeScalarValue(child.Key);
            if (key == "")
            {
                continue;
            }

            if (!allowedKeys.Any(allowedKey => string.Equals(allowedKey, key, StringComparison.OrdinalIgnoreCase)))
            {
                unsupportedPath = string.IsNullOrEmpty(pathPrefix) ? key : $"{pathPrefix}.{key}";
                errorMessage = unsupportedKeyMessage;
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// YAML mapping node から指定キーの値を取得します。
    /// </summary>
    /// <param name="node">検索対象の mapping node。</param>
    /// <param name="key">検索する scalar key。</param>
    /// <param name="value">見つかった YAML node。存在しない場合は null。</param>
    /// <returns>指定キーが見つかった場合は true。</returns>
    private static bool TryReadNodeValue(YamlMappingNode node, string key, out YamlNode? value)
    {
        foreach (KeyValuePair<YamlNode, YamlNode> pair in node.Children)
        {
            if (GetNodeScalarValue(pair.Key) == key)
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// YAML node が scalar の場合に文字列値を取得し、それ以外は空文字を返します。
    /// </summary>
    /// <param name="node">値を読む YAML node。</param>
    /// <returns>scalar node の文字列値。値がない場合または scalar でない場合は空文字。</returns>
    private static string GetNodeScalarValue(YamlNode node)
    {
        if (node is YamlScalarNode scalarNode)
        {
            return scalarNode.Value ?? "";
        }

        return "";
    }

    /// <summary>
    /// `--engine-set` / `--eset` の既知パスを反映します。
    /// </summary>
    /// <param name="settingKey">設定キー。</param>
    /// <param name="settingValue">設定値。</param>
    /// <param name="executionOptions">反映先 options。</param>
    /// <param name="failureCode">失敗時の error code。</param>
    /// <param name="failureMessage">失敗時の詳細。</param>
    /// <returns>反映成功なら true。</returns>
    private static bool ApplyEngineSetting(
        string settingKey,
        string settingValue,
        WorkflowExecutionOptions executionOptions,
        EngineLoggingOptions loggingOptions,
        out string failureCode,
        out string failureMessage)
    {
        failureCode = WorkflowErrorCodes.ConfigLoadFailed;
        failureMessage = "";

        switch (settingKey)
        {
            case "Timeout.StepTimeout":
                if (!TryReadTimeoutValue(settingValue, out TimeSpan? timeout, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;

                    return false;
                }

                executionOptions.StepTimeout = timeout;

                return true;
            case "Retry.MaxAttempts":
                if (string.IsNullOrWhiteSpace(settingValue)
                    || !int.TryParse(settingValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxAttempts))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                    failureMessage = $"Retry.MaxAttempts value is invalid: {settingValue}";

                    return false;
                }

                executionOptions.Retry = new RetryOptions { MaxAttempts = maxAttempts };

                return true;
            case "Logging.Console.Enabled":
                if (!TryReadBooleanValue(settingValue, out bool consoleEnabled, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                    failureMessage = $"Logging.Console.Enabled value is invalid: {settingValue}";

                    return false;
                }

                loggingOptions.ConsoleEnabled = consoleEnabled;

                return true;
            case "Logging.Console.Format":
                if (!TryReadLoggingFormat(settingValue, out EngineLoggingFormat consoleFormat, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                    failureMessage = $"Logging.Console.Format value is invalid: {settingValue}";

                    return false;
                }

                loggingOptions.ConsoleFormat = consoleFormat;

                return true;
            case "Logging.File.Enabled":
                if (!TryReadBooleanValue(settingValue, out bool fileEnabled, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                    failureMessage = $"Logging.File.Enabled value is invalid: {settingValue}";

                    return false;
                }

                loggingOptions.FileEnabled = fileEnabled;

                return true;
            case "Logging.File.Directory":
                loggingOptions.FileDirectory = settingValue;
                return true;
            case "Logging.File.NameFormat":
                loggingOptions.FileNameFormat = settingValue;
                return true;
            case "Logging.File.Format":
                if (!TryReadLoggingFormat(settingValue, out EngineLoggingFormat fileFormat, out failureMessage))
                {
                    failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                    failureMessage = $"Logging.File.Format value is invalid: {settingValue}";

                    return false;
                }

                loggingOptions.FileFormat = fileFormat;

                return true;
            default:
                failureCode = WorkflowErrorCodes.ConfigLoadFailed;
                failureMessage = $"Unsupported engine setting path: {settingKey}";

                return false;
        }
    }

    /// <summary>
    /// ログの有効値を検証して bool に変換します。
    /// </summary>
    /// <param name="rawValue">設定文字列。</param>
    /// <param name="value">変換結果。</param>
    /// <param name="errorMessage">変換失敗時の詳細。</param>
    /// <returns>変換成功なら true。</returns>
    private static bool TryReadBooleanValue(string rawValue, out bool value, out string errorMessage)
    {
        if (!bool.TryParse(rawValue, out value))
        {
            errorMessage = $"Boolean value is invalid: {rawValue}";

            return false;
        }

        errorMessage = "";

        return true;
    }

    /// <summary>
    /// ログ形式文字列を検証します。
    /// </summary>
    /// <param name="rawValue">設定文字列。</param>
    /// <param name="value">変換結果。</param>
    /// <param name="errorMessage">変換失敗時の詳細。</param>
    /// <returns>変換成功なら true。</returns>
    private static bool TryReadLoggingFormat(
        string rawValue,
        out EngineLoggingFormat value,
        out string errorMessage)
    {
        if (string.Equals(rawValue, "Text", StringComparison.OrdinalIgnoreCase))
        {
            value = EngineLoggingFormat.Text;
            errorMessage = "";

            return true;
        }

        if (string.Equals(rawValue, "Json", StringComparison.OrdinalIgnoreCase))
        {
            value = EngineLoggingFormat.Json;
            errorMessage = "";

            return true;
        }

        value = EngineLoggingFormat.Text;
        errorMessage = $"Logging format is invalid: {rawValue}";

        return false;
    }

    /// <summary>
    /// ログ設定を反映した logger factory を作成します。
    /// </summary>
    /// <param name="loggingOptions">ログ設定。</param>
    /// <param name="engineArguments">engine arguments。</param>
    /// <returns>有効な出力先がある場合のみ factory。無効なら null。</returns>
    private static ILoggerFactory? CreateLoggerFactory(EngineLoggingOptions loggingOptions, EngineArguments? engineArguments)
    {
        if (!loggingOptions.HasAnyOutput)
        {
            return null;
        }

        return new EngineLoggerFactory(loggingOptions, engineArguments);
    }

    /// <summary>
    /// StepTimeout の文字列を TimeSpan? として解決します。
    /// </summary>
    /// <param name="rawValue">設定文字列。</param>
    /// <param name="stepTimeout">変換結果。</param>
    /// <param name="errorMessage">変換失敗時の詳細。</param>
    /// <returns>変換成功なら true。</returns>
    private static bool TryReadTimeoutValue(string? rawValue, out TimeSpan? stepTimeout, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            stepTimeout = null;
            errorMessage = "";

            return true;
        }

        if (TimeSpan.TryParse(rawValue, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            stepTimeout = parsed;
            errorMessage = "";

            return true;
        }

        stepTimeout = null;
        errorMessage = $"Timeout value is invalid: {rawValue}";

        return false;
    }

    /// <summary>
    /// engine config YAML の最小 DTO。
    /// </summary>
    private sealed class EngineConfigDto
    {
        /// <summary>Timeout section。</summary>
        public EngineTimeoutSectionDto? Timeout { get; init; }

        /// <summary>Retry section。</summary>
        public EngineRetrySectionDto? Retry { get; init; }

        /// <summary>Logging section。</summary>
        public EngineLoggingSectionDto? Logging { get; init; }
    }

    /// <summary>
    /// Timeout section DTO。
    /// </summary>
    private sealed class EngineTimeoutSectionDto
    {
        /// <summary>StepTimeout 値。</summary>
        public string? StepTimeout { get; init; }
    }

    /// <summary>
    /// Retry section DTO。
    /// </summary>
    private sealed class EngineRetrySectionDto
    {
        /// <summary>MaxAttempts 値。</summary>
        public string? MaxAttempts { get; init; }
    }

    /// <summary>
    /// Logging section DTO。
    /// </summary>
    private sealed class EngineLoggingSectionDto
    {
        /// <summary>Console section。</summary>
        public EngineLoggingConsoleSectionDto? Console { get; init; }

        /// <summary>File section。</summary>
        public EngineLoggingFileSectionDto? File { get; init; }
    }

    /// <summary>
    /// Console section DTO。
    /// </summary>
    private sealed class EngineLoggingConsoleSectionDto
    {
        /// <summary>ログをコンソールへ出力するか。</summary>
        public bool? Enabled { get; init; }

        /// <summary>ログ形式。</summary>
        public string? Format { get; init; }
    }

    /// <summary>
    /// File section DTO。
    /// </summary>
    private sealed class EngineLoggingFileSectionDto
    {
        /// <summary>ログをファイルへ出力するか。</summary>
        public bool? Enabled { get; init; }

        /// <summary>ファイル保存先ディレクトリ。</summary>
        public string? Directory { get; init; }

        /// <summary>ファイル名テンプレート。</summary>
        public string? NameFormat { get; init; }

        /// <summary>ログ形式。</summary>
        public string? Format { get; init; }
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
        command = new CliCommand(
            "",
            "",
            null,
            "",
            new Dictionary<string, string>(),
            "",
            new Dictionary<string, string>(),
            [],
            false);
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
        string workflowConfigPath = "";
        string engineConfigPath = "";
        var workflowSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        var engineSettings = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowedNuGetReferences = new List<CsxNuGetReference>();
        bool requireNuGetLock = false;

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
                case "--workflow-config":
                    if (!TryReadValue(args, ref i, out workflowConfigPath))
                    {
                        errorMessage = "--workflow-config requires a value.";
                        return false;
                    }

                    break;
                case "--workflow-set":
                case "--wset":
                    if (!TryReadSetValue(args, ref i, out string workflowSettingKey, out string workflowSettingValue))
                    {
                        errorMessage = $"{args[i]} requires a key=value value.";
                        return false;
                    }

                    workflowSettings[workflowSettingKey] = workflowSettingValue;
                    break;
                case "--engine-config":
                    if (!TryReadValue(args, ref i, out engineConfigPath))
                    {
                        errorMessage = "--engine-config requires a value.";
                        return false;
                    }

                    break;
                case "--engine-set":
                case "--eset":
                    if (!TryReadSetValue(args, ref i, out string engineSettingKey, out string engineSettingValue))
                    {
                        errorMessage = $"{args[i]} requires a key=value value.";
                        return false;
                    }

                    engineSettings[engineSettingKey] = engineSettingValue;
                    break;
                case "--allow-nuget":
                    if (!TryReadValue(args, ref i, out string nuGetReference))
                    {
                        errorMessage = "--allow-nuget requires a PackageId,Version value.";
                        return false;
                    }

                    if (!TryParseNuGetReference(nuGetReference, out CsxNuGetReference parsedReference))
                    {
                        errorMessage = "--allow-nuget requires a PackageId,Version value.";
                        return false;
                    }

                    allowedNuGetReferences.Add(parsedReference);
                    break;
                case "--locked":
                    requireNuGetLock = true;
                    break;
                default:
                    errorMessage = $"Unknown option: {args[i]}";
                    return false;
            }
        }

        command = new CliCommand(
            commandName,
            entryPath,
            entryName,
            workflowConfigPath,
            workflowSettings,
            engineConfigPath,
            engineSettings,
            allowedNuGetReferences,
            requireNuGetLock);

        return true;
    }

    /// <summary>
    /// CLI の NuGet 許可参照指定を固定 package id と version に変換します。
    /// </summary>
    /// <param name="value">`PackageId,Version` 形式の指定値。</param>
    /// <param name="reference">変換した NuGet 参照。</param>
    /// <returns>変換できた場合は true。</returns>
    private static bool TryParseNuGetReference(string value, out CsxNuGetReference reference)
    {
        reference = new CsxNuGetReference();
        string[] parts = value.Split(',', StringSplitOptions.None);

        if (parts.Length != 2)
        {
            return false;
        }

        string packageId = parts[0].Trim();
        string version = parts[1].Trim();

        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        reference = new CsxNuGetReference(packageId, version);

        return true;
    }

    /// <summary>
    /// 設定文字列を解析して key/value を取得します。
    /// </summary>
    /// <param name="args">command-line 引数。</param>
    /// <param name="index">現在の option index。成功時は値の index に進みます。</param>
    /// <param name="key">解析した key。</param>
    /// <param name="value">解析した value。</param>
    /// <returns>key=value 形式なら true。</returns>
    private static bool TryReadSetValue(string[] args, ref int index, out string key, out string value)
    {
        key = "";
        value = "";

        if (!TryReadValue(args, ref index, out string setting))
        {
            return false;
        }

        int separatorIndex = setting.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        key = setting[..separatorIndex];
        value = setting[(separatorIndex + 1)..];
        return true;
    }

    /// <summary>
    /// 検証時に存在確認する Config path をまとめます。
    /// </summary>
    /// <param name="configPaths">workflow/config engine の config path 一覧。</param>
    /// <returns>空または重複を取り除いた path 一覧。</returns>
    private static string[] CollectValidationConfigPaths(params string?[] configPaths)
    {
        var paths = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? configPath in configPaths)
        {
            if (string.IsNullOrWhiteSpace(configPath) || !unique.Add(configPath))
            {
                continue;
            }

            paths.Add(configPath);
        }

        return paths.ToArray();
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
    /// <param name="WorkflowConfigPath">workflow config file path。</param>
    /// <param name="WorkflowSettings">--workflow-set option で指定された workflow override 設定。</param>
    /// <param name="EngineConfigPath">engine config file path。</param>
    /// <param name="EngineSettings">--engine-set option で指定された engine override 設定。</param>
    /// <param name="AllowedNuGetReferences">--allow-nuget option で指定された NuGet 参照制限。一覧が空の場合は制限しません。</param>
    /// <param name="RequireNuGetLock">--locked option で指定された NuGet lock file 必須設定。</param>
    private sealed record CliCommand(
        string Name,
        string EntryPath,
        string? EntryName,
        string WorkflowConfigPath,
        IReadOnlyDictionary<string, string> WorkflowSettings,
        string EngineConfigPath,
        IReadOnlyDictionary<string, string> EngineSettings,
        IReadOnlyList<CsxNuGetReference> AllowedNuGetReferences,
        bool RequireNuGetLock);
}
