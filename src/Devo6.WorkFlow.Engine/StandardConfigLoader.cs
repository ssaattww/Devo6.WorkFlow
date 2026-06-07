using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using YamlDotNet.RepresentationModel;
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
    /// Step 登録単位 Config metadata に基づいてすべての Step Config を読み込みます。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="boundaryConfigType">YAML 全体を変換する CompositeStep 境界 Config 型。</param>
    /// <param name="registrations">Step 登録単位 Config metadata の一覧。</param>
    /// <param name="settings">raw CLI override。</param>
    /// <returns>検証済みの Step Config instance 一覧。</returns>
    internal static IReadOnlyList<StepConfigValue> LoadStepConfigs(
        string configPath,
        Type boundaryConfigType,
        IReadOnlyList<StepConfigRegistration> registrations,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentNullException.ThrowIfNull(boundaryConfigType);
        ArgumentNullException.ThrowIfNull(registrations);

        EnsureSectionPathsAreUsable(registrations);
        string[] sectionPaths = registrations.Select(registration => registration.SectionPath).Distinct(StringComparer.Ordinal).ToArray();
        EnsureSettingsTargetDeclared(sectionPaths, settings);
        YamlNode configRoot = LoadConfigRoot(configPath, sectionPaths);
        EnsureSectionsExist(configRoot, sectionPaths);

        object boundaryConfig = Deserialize(configRoot, boundaryConfigType);
        ApplySettings(boundaryConfig, settings);
        Validate(boundaryConfig);

        return registrations
            .Select(registration =>
            {
                object config = ExtractStepConfig(boundaryConfig, registration.SectionPath, registration.ConfigType);
                Validate(config);

                return new StepConfigValue(registration.StepIndex, registration.ConfigType, config);
            })
            .ToArray();
    }

    /// <summary>
    /// YAML file を標準 Config 型の instance に変換します。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="configType">変換先の標準 Config 型。</param>
    /// <returns>YAML から作成した標準 Config instance。</returns>
    private static object Deserialize(string configPath, Type configType)
    {
        return Deserialize(LoadConfigRoot(configPath, []), configType);
    }

    /// <summary>
    /// YAML node を標準 Config 型の instance に変換します。
    /// </summary>
    /// <param name="rootNode">変換する YAML root node。</param>
    /// <param name="configType">変換先の標準 Config 型。</param>
    /// <returns>YAML から作成した標準 Config instance。</returns>
    private static object Deserialize(YamlNode rootNode, Type configType)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var yaml = new YamlStream(new YamlDocument(rootNode));
        yaml.Save(writer, assignAnchors: false);
        using var reader = new StringReader(writer.ToString());
        object? config = Deserializer.Deserialize(reader, configType);

        return EnsureConfigInstance(config, configType);
    }

    /// <summary>
    /// YAML file を読み込み、宣言済み Step Config 区画の YAML 断片参照を解決します。
    /// </summary>
    /// <param name="configPath">読み込む YAML file path。</param>
    /// <param name="referenceSectionPaths">YAML 断片参照を許可する区画 path。</param>
    /// <returns>YAML 断片参照を反映した root node。</returns>
    private static YamlNode LoadConfigRoot(string configPath, IReadOnlyList<string> referenceSectionPaths)
    {
        string fullPath = Path.GetFullPath(configPath);
        YamlNode rootNode = ReadYamlRoot(fullPath);
        var loadingPaths = new HashSet<string>(StringComparer.Ordinal) { fullPath };

        ResolveYamlFragmentReferences(
            rootNode,
            "",
            Path.GetDirectoryName(fullPath)!,
            referenceSectionPaths.ToHashSet(StringComparer.Ordinal),
            loadingPaths);

        return rootNode;
    }

    /// <summary>
    /// YAML file の root node を読み取ります。
    /// </summary>
    /// <param name="configPath">読み取る YAML file path。</param>
    /// <returns>読み取った YAML root node。</returns>
    private static YamlNode ReadYamlRoot(string configPath)
    {
        var yaml = new YamlStream();
        using StreamReader reader = File.OpenText(configPath);
        yaml.Load(reader);

        return yaml.Documents.Count == 0
            ? new YamlMappingNode()
            : yaml.Documents[0].RootNode;
    }

    /// <summary>
    /// 宣言済み区画の値が YAML 断片 path の場合、その YAML root node へ差し替えます。
    /// </summary>
    /// <param name="node">検査対象の YAML node。</param>
    /// <param name="currentPath">現在の property path。</param>
    /// <param name="baseDirectory">相対 path の基準 directory。</param>
    /// <param name="referenceSectionPaths">YAML 断片参照を許可する区画 path。</param>
    /// <param name="loadingPaths">循環検出用の読み込み中 path。</param>
    private static void ResolveYamlFragmentReferences(
        YamlNode node,
        string currentPath,
        string baseDirectory,
        IReadOnlySet<string> referenceSectionPaths,
        HashSet<string> loadingPaths)
    {
        if (node is not YamlMappingNode mapping)
        {
            return;
        }

        foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children.ToArray())
        {
            if (child.Key is not YamlScalarNode keyNode || string.IsNullOrWhiteSpace(keyNode.Value))
            {
                continue;
            }

            string childPath = string.IsNullOrEmpty(currentPath) ? keyNode.Value : $"{currentPath}.{keyNode.Value}";
            if (referenceSectionPaths.Contains(childPath)
                && child.Value is YamlScalarNode valueNode
                && IsYamlFragmentPath(valueNode.Value))
            {
                mapping.Children[child.Key] = LoadYamlFragment(valueNode.Value!, baseDirectory, referenceSectionPaths, loadingPaths);
                continue;
            }

            ResolveYamlFragmentReferences(child.Value, childPath, baseDirectory, referenceSectionPaths, loadingPaths);
        }
    }

    /// <summary>
    /// YAML 断片 path を読み込みます。
    /// </summary>
    /// <param name="fragmentPath">YAML 断片 path。</param>
    /// <param name="baseDirectory">相対 path の基準 directory。</param>
    /// <param name="referenceSectionPaths">YAML 断片参照を許可する区画 path。</param>
    /// <param name="loadingPaths">循環検出用の読み込み中 path。</param>
    /// <returns>読み込んだ YAML root node。</returns>
    private static YamlNode LoadYamlFragment(
        string fragmentPath,
        string baseDirectory,
        IReadOnlySet<string> referenceSectionPaths,
        HashSet<string> loadingPaths)
    {
        string resolvedPath = Path.GetFullPath(
            Path.IsPathRooted(fragmentPath) ? fragmentPath : Path.Combine(baseDirectory, fragmentPath));

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Config fragment file was not found: {resolvedPath}", resolvedPath);
        }

        if (!loadingPaths.Add(resolvedPath))
        {
            throw new InvalidOperationException($"Config fragment cycle was detected: {resolvedPath}");
        }

        try
        {
            YamlNode rootNode = ReadYamlRoot(resolvedPath);
            ResolveYamlFragmentReferences(
                rootNode,
                "",
                Path.GetDirectoryName(resolvedPath)!,
                referenceSectionPaths,
                loadingPaths);

            return rootNode;
        }
        finally
        {
            loadingPaths.Remove(resolvedPath);
        }
    }

    /// <summary>
    /// scalar 値が YAML 断片 path として扱えるかどうかを判定します。
    /// </summary>
    /// <param name="value">判定する scalar 値。</param>
    /// <returns>YAML 断片 path の場合は true。</returns>
    private static bool IsYamlFragmentPath(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".yml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// null の変換結果を引数なし constructor で生成した Config instance に置き換えます。
    /// </summary>
    /// <param name="config">YAML から変換した Config instance。</param>
    /// <param name="configType">生成対象の標準 Config 型。</param>
    /// <returns>null ではない標準 Config instance。</returns>
    private static object EnsureConfigInstance(object? config, Type configType)
    {
        if (config is null)
        {
            config = Activator.CreateInstance(configType)
                ?? throw new InvalidOperationException($"Config type could not be created: {configType.FullName}");
        }

        return config;
    }

    /// <summary>
    /// YAML root node から境界 Config 型上の property path に対応する node を取得します。
    /// </summary>
    /// <param name="rootNode">読み込み済み YAML root node。</param>
    /// <param name="sectionPath">境界 Config 型上の property path。</param>
    /// <returns>指定された property path の YAML node。</returns>
    private static YamlNode ReadSectionNode(YamlNode rootNode, string sectionPath)
    {
        YamlNode current = rootNode;
        foreach (string segment in SplitSectionPath(sectionPath))
        {
            if (current is not YamlMappingNode mapping)
            {
                throw new InvalidOperationException($"Config section was not found: {sectionPath}");
            }

            KeyValuePair<YamlNode, YamlNode>? pair = mapping.Children
                .FirstOrDefault(child => child.Key is YamlScalarNode scalar
                    && string.Equals(scalar.Value, segment, StringComparison.Ordinal));

            if (pair is null || pair.Value.Value is null)
            {
                throw new InvalidOperationException($"Config section was not found: {sectionPath}");
            }

            current = pair.Value.Value;
        }

        return current;
    }

    /// <summary>
    /// Step Config property path の prefix 関係と path 書式を検査します。
    /// </summary>
    /// <param name="registrations">検査する Step 登録単位 Config metadata の一覧。</param>
    private static void EnsureSectionPathsAreUsable(IReadOnlyList<StepConfigRegistration> registrations)
    {
        string[] sectionPaths = registrations
            .Select(registration => registration.SectionPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (string sectionPath in sectionPaths)
        {
            _ = SplitSectionPath(sectionPath);
        }

        for (int i = 0; i < sectionPaths.Length; i++)
        {
            for (int j = i + 1; j < sectionPaths.Length; j++)
            {
                if (SectionPathIsPrefixOf(sectionPaths[i], sectionPaths[j])
                    || SectionPathIsPrefixOf(sectionPaths[j], sectionPaths[i]))
                {
                    throw new InvalidOperationException($"Config section paths must not have a prefix relationship: {sectionPaths[i]}, {sectionPaths[j]}");
                }
            }
        }
    }

    /// <summary>
    /// 宣言済み property path が YAML root node に存在することを検査します。
    /// </summary>
    /// <param name="rootNode">読み込み済み YAML root node。</param>
    /// <param name="sectionPaths">存在を確認する宣言済み property path の一覧。</param>
    private static void EnsureSectionsExist(YamlNode rootNode, IReadOnlyList<string> sectionPaths)
    {
        foreach (string sectionPath in sectionPaths)
        {
            _ = ReadSectionNode(rootNode, sectionPath);
        }
    }

    /// <summary>
    /// raw CLI override が宣言済み property path の接頭辞を対象にしていることを検査します。
    /// </summary>
    /// <param name="sectionPaths">宣言済み property path の一覧。</param>
    /// <param name="settings">raw CLI override。</param>
    private static void EnsureSettingsTargetDeclared(IReadOnlyList<string> sectionPaths, IReadOnlyDictionary<string, string>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, string> setting in settings)
        {
            string? matchedSection = sectionPaths.SingleOrDefault(sectionPath => TryRemoveSectionPrefix(setting.Key, sectionPath, out _));
            if (matchedSection is null)
            {
                throw new InvalidOperationException($"Config override section was not declared: {setting.Key}");
            }
        }
    }

    /// <summary>
    /// 境界 Config 型から宣言済み Step Config property path の値を抽出します。
    /// </summary>
    /// <param name="boundaryConfig">境界 Config instance。</param>
    /// <param name="sectionPath">境界 Config 型上の property path。</param>
    /// <param name="configType">StepContext へ登録する Step Config 型。</param>
    /// <returns>抽出した Step Config instance。</returns>
    private static object ExtractStepConfig(object boundaryConfig, string sectionPath, Type configType)
    {
        object? value = GetPropertyPathValue(boundaryConfig, sectionPath);
        if (value is null)
        {
            value = Activator.CreateInstance(configType)
                ?? throw new InvalidOperationException($"Step config type could not be created: {configType.FullName}");
        }

        if (!configType.IsAssignableFrom(value.GetType()))
        {
            throw new InvalidOperationException($"Config section type does not match declared step config type: {sectionPath}");
        }

        return value;
    }

    /// <summary>
    /// object の公開 property path をたどって値を取得します。
    /// </summary>
    /// <param name="source">property path をたどる起点 object。</param>
    /// <param name="path">C# public instance property 名で構成された path。</param>
    /// <returns>path の終端 property 値。</returns>
    private static object? GetPropertyPathValue(object source, string path)
    {
        object? current = source;
        foreach (string segment in SplitSectionPath(path))
        {
            if (current is null)
            {
                return null;
            }

            PropertyInfo property = FindProperty(current.GetType(), segment);
            current = property.GetValue(current);
        }

        return current;
    }

    /// <summary>
    /// override path から指定 property path の接頭辞を剥がします。
    /// </summary>
    /// <param name="settingPath">raw CLI override path。</param>
    /// <param name="sectionPath">宣言済み property path。</param>
    /// <param name="propertyPath">宣言済み property path を剥がした override property path。</param>
    /// <returns>override path が宣言済み property path と一致する場合は true。</returns>
    private static bool TryRemoveSectionPrefix(string settingPath, string sectionPath, out string propertyPath)
    {
        string[] settingSegments = SplitSectionPath(settingPath);
        string[] sectionSegments = SplitSectionPath(sectionPath);
        propertyPath = "";

        if (settingSegments.Length <= sectionSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < sectionSegments.Length; i++)
        {
            if (!string.Equals(settingSegments[i], sectionSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        propertyPath = string.Join('.', settingSegments.Skip(sectionSegments.Length));

        return true;
    }

    /// <summary>
    /// 左の property path が右の property path の真の prefix かどうかを返します。
    /// </summary>
    /// <param name="left">prefix 候補の property path。</param>
    /// <param name="right">比較対象の property path。</param>
    /// <returns>左が右の真の prefix の場合は true。</returns>
    private static bool SectionPathIsPrefixOf(string left, string right)
    {
        string[] leftSegments = SplitSectionPath(left);
        string[] rightSegments = SplitSectionPath(right);

        return leftSegments.Length < rightSegments.Length
            && leftSegments.Zip(rightSegments).All(pair => string.Equals(pair.First, pair.Second, StringComparison.Ordinal));
    }

    /// <summary>
    /// property または override path を `.` 区切りの要素に分割します。
    /// </summary>
    /// <param name="path">分割する path。</param>
    /// <returns>path 要素の一覧。</returns>
    private static string[] SplitSectionPath(string path)
    {
        string[] segments = path.Split('.');
        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"Config section path is invalid: {path}");
        }

        return segments;
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
