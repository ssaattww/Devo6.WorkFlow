using System.ComponentModel.DataAnnotations;
using System.Collections;
using System.Globalization;
using System.Reflection;
using YamlDotNet.Serialization;

namespace Devo6.WorkFlow.Engine;

internal static class StandardConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// YAML file を指定された標準 Config 型に変換し、CLI override を適用してから DataAnnotations で検証します。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="configType">変換先の標準 Config 型。</param>
    /// <param name="settings">標準 Config に適用する raw CLI override。</param>
    /// <returns>検証済みの標準 Config instance。</returns>
    public static object Load(string configPath, Type configType, IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(configType);

        object config = Deserialize(configPath, configType);
        ApplySettings(config, settings);
        Validate(config);

        return config;
    }

    /// <summary>
    /// YAML file を標準 Config 型の instance に変換します。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="configType">変換先の標準 Config 型。</param>
    /// <returns>YAML から作成した標準 Config instance。</returns>
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

    /// <summary>
    /// CLI override を標準 Config instance に適用します。
    /// </summary>
    /// <param name="config">適用先の標準 Config instance。</param>
    /// <param name="settings">適用する raw CLI override。</param>
    private static void ApplySettings(object config, IReadOnlyDictionary<string, string>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, string> setting in settings)
        {
            ApplySetting(config, setting.Key, setting.Value);
        }
    }

    /// <summary>
    /// 単一の CLI override を標準 Config instance に適用します。
    /// </summary>
    /// <param name="config">適用先の標準 Config instance。</param>
    /// <param name="path">C# public instance property 名で構成された override path。</param>
    /// <param name="value">対象 property に変換して設定する raw 値。</param>
    private static void ApplySetting(object config, string path, string value)
    {
        string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Length != path.Split('.').Length)
        {
            throw new InvalidOperationException($"Config override path is invalid: {path}");
        }

        object current = config;
        for (int i = 0; i < segments.Length; i++)
        {
            bool isLast = i == segments.Length - 1;
            OverrideSegment segment = ParseSegment(segments[i]);
            PropertyInfo property = FindProperty(current.GetType(), segment.PropertyName);

            if (segment.Index is not null)
            {
                if (isLast)
                {
                    object? convertedValue = ConvertValue(value, GetElementType(property.PropertyType));
                    SetIndexedValue(current, property, segment.Index.Value, convertedValue);
                    return;
                }

                current = GetIndexedValue(current, property, segment.Index.Value);
                continue;
            }

            if (isLast)
            {
                property.SetValue(current, ConvertValue(value, property.PropertyType));
                return;
            }

            object? next = property.GetValue(current);
            if (next is null)
            {
                next = CreateIntermediateInstance(property.PropertyType, path);
                property.SetValue(current, next);
            }

            current = next;
        }
    }

    /// <summary>
    /// override path の 1 要素を property 名と添字に分解します。
    /// </summary>
    /// <param name="segment">分解する path 要素。</param>
    /// <returns>property 名と添字を保持する path 要素。</returns>
    private static OverrideSegment ParseSegment(string segment)
    {
        int bracketStart = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracketStart < 0)
        {
            if (segment.Length == 0 || segment.IndexOf("]", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException($"Config override path segment is invalid: {segment}");
            }

            return new OverrideSegment(segment, null);
        }

        if (!segment.EndsWith("]", StringComparison.Ordinal) || bracketStart == 0)
        {
            throw new InvalidOperationException($"Config override path segment is invalid: {segment}");
        }

        string indexText = segment[(bracketStart + 1)..^1];
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0)
        {
            throw new InvalidOperationException($"Config override index is invalid: {segment}");
        }

        return new OverrideSegment(segment[..bracketStart], index);
    }

    /// <summary>
    /// 指定された型から ordinal 完全一致する public instance property を取得します。
    /// </summary>
    /// <param name="type">property を検索する型。</param>
    /// <param name="propertyName">検索する property 名。</param>
    /// <returns>一致した property。</returns>
    private static PropertyInfo FindProperty(Type type, string propertyName)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Config override property was not found: {propertyName}");
    }

    /// <summary>
    /// list または array property から既存要素を取得します。
    /// </summary>
    /// <param name="target">property を持つ object。</param>
    /// <param name="property">list または array property。</param>
    /// <param name="index">取得する添字。</param>
    /// <returns>指定された既存要素。</returns>
    private static object GetIndexedValue(object target, PropertyInfo property, int index)
    {
        IList list = GetListValue(target, property);
        if (index >= list.Count)
        {
            throw new InvalidOperationException($"Config override index is out of range: {property.Name}[{index}]");
        }

        object? value = list[index];
        if (value is not null)
        {
            return value;
        }

        object created = CreateIntermediateInstance(GetElementType(property.PropertyType), property.Name);
        list[index] = created;

        return created;
    }

    /// <summary>
    /// list または array property の既存要素を設定します。
    /// </summary>
    /// <param name="target">property を持つ object。</param>
    /// <param name="property">list または array property。</param>
    /// <param name="index">設定する添字。</param>
    /// <param name="value">設定する値。</param>
    private static void SetIndexedValue(object target, PropertyInfo property, int index, object? value)
    {
        IList list = GetListValue(target, property);
        if (index >= list.Count)
        {
            throw new InvalidOperationException($"Config override index is out of range: {property.Name}[{index}]");
        }

        list[index] = value;
    }

    /// <summary>
    /// property 値を list または array として取得します。
    /// </summary>
    /// <param name="target">property を持つ object。</param>
    /// <param name="property">list または array property。</param>
    /// <returns>添字操作が可能な collection。</returns>
    private static IList GetListValue(object target, PropertyInfo property)
    {
        object? value = property.GetValue(target);
        if (value is not IList list)
        {
            throw new InvalidOperationException($"Config override target is not a list or array: {property.Name}");
        }

        return list;
    }

    /// <summary>
    /// collection 型から要素型を取得します。
    /// </summary>
    /// <param name="collectionType">要素型を調べる collection 型。</param>
    /// <returns>collection の要素型。</returns>
    private static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType()
                ?? throw new InvalidOperationException($"Config override array element type was not found: {collectionType.FullName}");
        }

        return collectionType.GetInterfaces()
            .Append(collectionType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
            .Select(type => type.GetGenericArguments()[0])
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Config override list element type was not found: {collectionType.FullName}");
    }

    /// <summary>
    /// 入れ子 property の中間 object を生成します。
    /// </summary>
    /// <param name="type">生成する型。</param>
    /// <param name="path">エラー表示用の override path。</param>
    /// <returns>生成した中間 object。</returns>
    private static object CreateIntermediateInstance(Type type, string path)
    {
        if (!type.IsClass || type == typeof(string) || type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"Config override intermediate object could not be created: {path}");
        }

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Config override intermediate object could not be created: {path}");
    }

    /// <summary>
    /// raw CLI 値を対象 property 型へ変換します。
    /// </summary>
    /// <param name="value">変換する raw CLI 値。</param>
    /// <param name="targetType">変換先の型。</param>
    /// <returns>変換後の値。</returns>
    private static object? ConvertValue(string value, Type targetType)
    {
        Type conversionType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (Nullable.GetUnderlyingType(targetType) is not null && value.Length == 0)
        {
            return null;
        }

        if (conversionType == typeof(string))
        {
            return value;
        }

        try
        {
            if (conversionType == typeof(bool))
            {
                return bool.Parse(value);
            }

            if (conversionType == typeof(int))
            {
                return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (conversionType == typeof(long))
            {
                return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            if (conversionType == typeof(double))
            {
                return double.Parse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            }

            if (conversionType == typeof(decimal))
            {
                return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            if (conversionType.IsEnum)
            {
                return Enum.Parse(conversionType, value, ignoreCase: false);
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException($"Config override value could not be converted to {targetType.FullName}.", exception);
        }

        throw new InvalidOperationException($"Config override target type is not supported: {targetType.FullName}");
    }

    /// <summary>
    /// 標準 Config instance を DataAnnotations で検証します。
    /// </summary>
    /// <param name="config">検証する標準 Config instance。</param>
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

    /// <summary>
    /// CLI override path の property 名と添字を保持します。
    /// </summary>
    /// <param name="PropertyName">対象 property 名。</param>
    /// <param name="Index">collection 添字。添字指定がない場合は null。</param>
    private sealed record OverrideSegment(string PropertyName, int? Index);
}
