using System.ComponentModel.DataAnnotations;
using YamlDotNet.Serialization;

namespace Devo6.WorkFlow.Engine;

internal static class StandardConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// YAML file を指定された標準 Config 型に変換し、DataAnnotations で検証します。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="configType">変換先の標準 Config 型。</param>
    /// <returns>検証済みの標準 Config instance。</returns>
    public static object Load(string configPath, Type configType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(configType);

        object config = Deserialize(configPath, configType);
        Validate(config);

        return config;
    }

    private static object Deserialize(string configPath, Type configType)
    {
        using StreamReader reader = File.OpenText(configPath);
        object? config = Deserializer.Deserialize(reader, configType);

        if (config is null)
        {
            config = Activator.CreateInstance(configType)
                ?? throw new InvalidOperationException($"Config type could not be created: {configType.FullName}");
        }

        return config;
    }

    private static void Validate(object config)
    {
        var context = new ValidationContext(config);
        var results = new List<ValidationResult>();

        if (Validator.TryValidateObject(config, context, results, validateAllProperties: true))
        {
            return;
        }

        string message = string.Join(
            Environment.NewLine,
            results.Select(result => result.ErrorMessage).Where(message => !string.IsNullOrWhiteSpace(message)));

        throw new ValidationException(message.Length == 0 ? "Config validation failed." : message);
    }
}
