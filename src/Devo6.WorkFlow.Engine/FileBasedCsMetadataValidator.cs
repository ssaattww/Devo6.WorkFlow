#if NET10_0
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Engine;

/// <summary>
/// File-based Library を実行せずに Entry metadata を検証します。
/// </summary>
internal static class FileBasedCsMetadataValidator
{
    private const string EntryProviderTypeName = "Devo6.WorkFlow.Engine.IWorkflowEntryProvider";
    private const string EntryTypeName = "Devo6.WorkFlow.Engine.IWorkflowEntry";
    private const string EntryAttributeTypeName = "Devo6.WorkFlow.Engine.WorkflowEntryAttribute";
    private const string EngineAssemblyName = "Devo6.WorkFlow.Engine";
    private const string DefaultEntryName = "Main";

    /// <summary>
    /// 実行せずに file-based Library の Entry metadata を検証します。
    /// </summary>
    internal static WorkflowValidationResult Validate(string assemblyPath, string? requestedEntryName)
    {
        return Validate(assemblyPath, requestedEntryName, out _, out _);
    }

    /// <summary>
    /// 実行せずに file-based Library の Entry metadata を検証します。
    /// </summary>
    internal static WorkflowValidationResult Validate(
        string assemblyPath,
        string? requestedEntryName,
        out string? providerTypeName,
        out string? qualifiedEntryName)
    {
        providerTypeName = null;
        qualifiedEntryName = null;
        try
        {
            using FileStream stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                return Failure(assemblyPath, WorkflowErrorCodes.ScriptLoadFailed, "The built file does not contain CLR metadata.");
            }

            MetadataReader reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
            {
                return Failure(assemblyPath, WorkflowErrorCodes.ScriptLoadFailed, "The built file is not a managed assembly.");
            }

            if (!HasCompatibleAssemblyReference(reader, typeof(WorkflowEntryAttribute).Assembly.GetName()))
            {
                return Failure(
                    assemblyPath,
                    WorkflowErrorCodes.ScriptApiIdentityMismatch,
                    "The built workflow does not reference the host Devo6.WorkFlow.Engine assembly identity.");
            }

            AssemblyName abstractionsAssembly = typeof(WorkflowErrorCodes).Assembly.GetName();
            if (reader.AssemblyReferences.Any(handle => string.Equals(
                    reader.GetString(reader.GetAssemblyReference(handle).Name),
                    abstractionsAssembly.Name,
                    StringComparison.Ordinal))
                && !HasCompatibleAssemblyReference(reader, abstractionsAssembly))
            {
                return Failure(
                    assemblyPath,
                    WorkflowErrorCodes.ScriptApiIdentityMismatch,
                    "The built workflow references a Devo6.WorkFlow.Abstractions identity that differs from the host.");
            }

            var candidates = new List<EntryCandidate>();
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                foreach (CustomAttributeHandle attributeHandle in type.GetCustomAttributes())
                {
                    CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                    if (!IsWorkflowEntryAttribute(reader, attribute))
                    {
                        continue;
                    }

                    if (!TryReadEntryAttribute(reader, attribute, out string name, out string? namespaceName))
                    {
                        return Failure(
                            GetTypeFullName(reader, typeHandle),
                            WorkflowErrorCodes.ScriptLoadFailed,
                            "WorkflowEntryAttribute metadata is malformed.");
                    }

                    string qualifiedName = string.IsNullOrWhiteSpace(namespaceName)
                        ? name
                        : $"{namespaceName}.{name}";
                    candidates.Add(new EntryCandidate(
                        typeHandle,
                        name,
                        namespaceName,
                        qualifiedName,
                        IsValidProvider(reader, typeHandle)));
                }
            }

            EntryCandidate? duplicate = candidates
                .GroupBy(candidate => candidate.QualifiedName, StringComparer.Ordinal)
                .Where(group => group.Key.Length > 0 && group.Count() > 1)
                .Select(group => group.First())
                .FirstOrDefault();
            if (duplicate is not null)
            {
                return Failure(
                    duplicate.QualifiedName,
                    WorkflowErrorCodes.DuplicateStepName,
                    $"Duplicate public workflow entry name was found: {duplicate.QualifiedName}");
            }

            string resolvedName = string.IsNullOrWhiteSpace(requestedEntryName) ? DefaultEntryName : requestedEntryName;
            EntryCandidate? selected = ResolveEntry(candidates, resolvedName, out string resolutionError);
            if (selected is null)
            {
                return Failure(resolvedName, WorkflowErrorCodes.EntryStepNotFound, resolutionError);
            }

            if (!selected.IsValidProvider)
            {
                return Failure(
                    GetTypeFullName(reader, selected.TypeHandle),
                    WorkflowErrorCodes.ScriptEntryProviderInvalid,
                    "A workflow entry provider must be a public sealed non-abstract non-generic class with a public parameterless constructor and a public Create() method returning IWorkflowEntry.");
            }

            providerTypeName = GetTypeFullName(reader, selected.TypeHandle);
            qualifiedEntryName = selected.QualifiedName;
            return new WorkflowValidationResult();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return Failure(assemblyPath, WorkflowErrorCodes.ScriptLoadFailed, exception.Message);
        }
    }

    /// <summary>
    /// 要求名から一意の Entry candidate を選択します。
    /// </summary>
    private static EntryCandidate? ResolveEntry(
        IReadOnlyList<EntryCandidate> candidates,
        string requestedEntryName,
        out string error)
    {
        error = $"Entry step was not found: {requestedEntryName}";
        EntryCandidate? qualified = candidates.FirstOrDefault(candidate => candidate.QualifiedName == requestedEntryName);
        if (qualified is not null)
        {
            return qualified;
        }

        EntryCandidate? root = candidates.FirstOrDefault(candidate => candidate.NamespaceName is null && candidate.Name == requestedEntryName);
        if (root is not null)
        {
            return root;
        }

        List<EntryCandidate> shortMatches = candidates.Where(candidate => candidate.Name == requestedEntryName).ToList();
        if (shortMatches.Count == 1)
        {
            return shortMatches[0];
        }

        if (shortMatches.Count > 1)
        {
            error = $"Entry step name '{requestedEntryName}' matched multiple entries: {string.Join(", ", shortMatches.Select(candidate => candidate.QualifiedName))}. Specify the qualified entry name.";
        }

        return null;
    }

    /// <summary>
    /// Provider 型が公開契約を満たすか metadata で判定します。
    /// </summary>
    private static bool IsValidProvider(MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        TypeAttributes attributes = type.Attributes;
        TypeAttributes visibility = attributes & TypeAttributes.VisibilityMask;
        bool externallyVisible = visibility == TypeAttributes.Public
            || visibility == TypeAttributes.NestedPublic && IsPublicDeclaringChain(reader, type.GetDeclaringType());
        bool isClass = (attributes & TypeAttributes.ClassSemanticsMask) == 0;
        string baseTypeName = type.BaseType.IsNil ? "" : GetTypeFullName(reader, type.BaseType);
        isClass &= baseTypeName is not "System.ValueType" and not "System.Enum";
        bool isConcrete = (attributes & TypeAttributes.Abstract) == 0;
        bool isSealed = (attributes & TypeAttributes.Sealed) != 0;
        bool isNonGeneric = type.GetGenericParameters().Count == 0;
        if (!externallyVisible || !isClass || !isConcrete || !isSealed || !isNonGeneric)
        {
            return false;
        }

        bool hasPublicParameterlessConstructor = false;
        bool hasPublicCreateMethod = false;
        var signatureProvider = new MetadataSignatureTypeProvider();
        foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            MethodAttributes methodAttributes = method.Attributes;
            bool isPublicInstance = (methodAttributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public
                && (methodAttributes & MethodAttributes.Static) == 0;
            MethodSignature<string> signature = method.DecodeSignature(signatureProvider, genericContext: null);
            string methodName = reader.GetString(method.Name);

            if (methodName == ".ctor" && isPublicInstance && signature.ParameterTypes.Length == 0)
            {
                hasPublicParameterlessConstructor = true;
            }

            if (methodName == "Create"
                && isPublicInstance
                && IsCreateSignature(signature))
            {
                hasPublicCreateMethod = true;
            }
        }

        EntityHandle providerInterface = type.GetInterfaceImplementations()
            .Select(handle => reader.GetInterfaceImplementation(handle).Interface)
            .FirstOrDefault(interfaceHandle => GetTypeFullName(reader, interfaceHandle) == EntryProviderTypeName
                && IsEngineTypeReference(reader, interfaceHandle));
        bool implementsProvider = !providerInterface.IsNil;
        bool hasExplicitCreateMethod = implementsProvider && type.GetMethodImplementations()
            .Select(handle => reader.GetMethodImplementation(handle))
            .Any(implementation => IsCreateInterfaceImplementation(reader, implementation, signatureProvider));

        return implementsProvider && hasPublicParameterlessConstructor && (hasPublicCreateMethod || hasExplicitCreateMethod);
    }

    /// <summary>
    /// Provider が必須 Create interface method を持つか確認します。
    /// </summary>
    private static bool IsCreateInterfaceImplementation(
        MetadataReader reader,
        MethodImplementation implementation,
        MetadataSignatureTypeProvider signatureProvider)
    {
        if (implementation.MethodDeclaration.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        MemberReference declaration = reader.GetMemberReference((MemberReferenceHandle)implementation.MethodDeclaration);
        if (reader.GetString(declaration.Name) != "Create"
            || GetTypeFullName(reader, declaration.Parent) != EntryProviderTypeName
            || !IsEngineTypeReference(reader, declaration.Parent)
            || implementation.MethodBody.Kind != HandleKind.MethodDefinition)
        {
            return false;
        }

        MethodDefinition body = reader.GetMethodDefinition((MethodDefinitionHandle)implementation.MethodBody);
        return IsCreateSignature(body.DecodeSignature(signatureProvider, genericContext: null));
    }

    /// <summary>
    /// Create method の metadata signature を確認します。
    /// </summary>
    private static bool IsCreateSignature(MethodSignature<string> signature)
    {
        return signature.ParameterTypes.Length == 0 && signature.ReturnType == EntryTypeName;
    }

    /// <summary>
    /// nested type の宣言階層が公開か確認します。
    /// </summary>
    private static bool IsPublicDeclaringChain(MetadataReader reader, TypeDefinitionHandle declaringType)
    {
        while (!declaringType.IsNil)
        {
            TypeDefinition parent = reader.GetTypeDefinition(declaringType);
            TypeAttributes visibility = parent.Attributes & TypeAttributes.VisibilityMask;
            if (visibility == TypeAttributes.NestedPublic)
            {
                declaringType = parent.GetDeclaringType();
                continue;
            }

            if (visibility != TypeAttributes.Public)
            {
                return false;
            }

            return true;
        }

        return true;
    }

    /// <summary>
    /// custom attribute が WorkflowEntryAttribute か metadata で確認します。
    /// </summary>
    private static bool IsWorkflowEntryAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        EntityHandle attributeType = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            _ => default,
        };

        return GetTypeFullName(reader, attributeType) == EntryAttributeTypeName
            && IsEngineTypeReference(reader, attributeType);
    }

    /// <summary>
    /// WorkflowEntryAttribute の constructor metadata を読み取ります。
    /// </summary>
    private static bool TryReadEntryAttribute(
        MetadataReader reader,
        CustomAttribute attribute,
        out string name,
        out string? namespaceName)
    {
        BlobReader value = reader.GetBlobReader(attribute.Value);
        if (value.ReadUInt16() != 1)
        {
            name = "";
            namespaceName = null;
            return false;
        }

        name = value.ReadSerializedString() ?? "";
        namespaceName = value.ReadSerializedString();
        return !string.IsNullOrWhiteSpace(name)
            && (namespaceName is null || !string.IsNullOrWhiteSpace(namespaceName));
    }

    /// <summary>
    /// type reference が共有 Engine contract を指すか確認します。
    /// </summary>
    private static bool IsEngineTypeReference(MetadataReader reader, EntityHandle typeHandle)
    {
        if (typeHandle.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference typeReference = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
        if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        AssemblyReference assemblyReference = reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope);
        return reader.GetString(assemblyReference.Name) == EngineAssemblyName;
    }

    /// <summary>
    /// assembly reference が host assembly identity と互換か確認します。
    /// </summary>
    private static bool HasCompatibleAssemblyReference(MetadataReader reader, AssemblyName hostAssembly)
    {
        foreach (AssemblyReferenceHandle referenceHandle in reader.AssemblyReferences)
        {
            AssemblyReference reference = reader.GetAssemblyReference(referenceHandle);
            if (!string.Equals(reader.GetString(reference.Name), hostAssembly.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (reference.Version != hostAssembly.Version)
            {
                return false;
            }

            string culture = reference.Culture.IsNil ? "" : reader.GetString(reference.Culture);
            if (!string.Equals(culture, hostAssembly.CultureName ?? "", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            byte[] expectedToken = hostAssembly.GetPublicKeyToken() ?? [];
            byte[] actualKeyOrToken = reader.GetBlobBytes(reference.PublicKeyOrToken);
            if ((reference.Flags & AssemblyFlags.PublicKey) != 0)
            {
                var referenceName = new AssemblyName();
                referenceName.SetPublicKey(actualKeyOrToken);
                actualKeyOrToken = referenceName.GetPublicKeyToken() ?? [];
            }

            return expectedToken.SequenceEqual(actualKeyOrToken);
        }

        return false;
    }

    /// <summary>
    /// metadata handle の型を完全修飾名に変換します。
    /// </summary>
    private static string GetTypeFullName(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFullName(reader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceFullName(reader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(new MetadataSignatureTypeProvider(), genericContext: null),
            _ => "",
        };
    }

    /// <summary>
    /// metadata handle の型を完全修飾名に変換します。
    /// </summary>
    private static string GetTypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
        {
            return $"{GetTypeFullName(reader, declaringType)}+{name}";
        }

        string namespaceName = type.Namespace.IsNil ? "" : reader.GetString(type.Namespace);
        return namespaceName.Length == 0 ? name : $"{namespaceName}.{name}";
    }

    /// <summary>
    /// type reference の完全修飾名を取得します。
    /// </summary>
    private static string GetTypeReferenceFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return $"{GetTypeReferenceFullName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{name}";
        }

        string namespaceName = type.Namespace.IsNil ? "" : reader.GetString(type.Namespace);
        return namespaceName.Length == 0 ? name : $"{namespaceName}.{name}";
    }

    /// <summary>
    /// workflow 検証または実行の失敗結果を作成します。
    /// </summary>
    private static WorkflowValidationResult Failure(string path, string code, string message)
    {
        return new WorkflowValidationResult
        {
            Errors =
            [
                new ValidationError
                {
                    Path = path,
                    Code = code,
                    Message = message,
                },
            ],
        };
    }

    /// <summary>
    /// EntryCandidate の内部処理を行います。
    /// </summary>
    private sealed record EntryCandidate(
        TypeDefinitionHandle TypeHandle,
        string Name,
        string? NamespaceName,
        string QualifiedName,
        bool IsValidProvider);

    private sealed class MetadataSignatureTypeProvider : ISignatureTypeProvider<string, object?>
    {
        /// <summary>
        /// metadata signature の array 型名を作成します。
        /// </summary>
        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
        /// <summary>
        /// metadata signature の by-reference 型名を作成します。
        /// </summary>
        public string GetByReferenceType(string elementType) => $"{elementType}&";
        /// <summary>
        /// metadata signature の function pointer 型名を作成します。
        /// </summary>
        public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
        /// <summary>
        /// metadata signature の generic 型名を作成します。
        /// </summary>
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(",", typeArguments)} >";
        /// <summary>
        /// metadata signature の generic method parameter 名を作成します。
        /// </summary>
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        /// <summary>
        /// metadata signature の generic type parameter 名を作成します。
        /// </summary>
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        /// <summary>
        /// metadata signature の modified 型名を作成します。
        /// </summary>
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        /// <summary>
        /// metadata signature の pinned 型名を作成します。
        /// </summary>
        public string GetPinnedType(string elementType) => $"{elementType} pinned";
        /// <summary>
        /// metadata signature の pointer 型名を作成します。
        /// </summary>
        public string GetPointerType(string elementType) => $"{elementType}*";
        /// <summary>
        /// metadata signature の primitive 型名を作成します。
        /// </summary>
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        /// <summary>
        /// metadata signature の single-dimensional array 型名を作成します。
        /// </summary>
        public string GetSZArrayType(string elementType) => $"{elementType}[]";
        /// <summary>
        /// TypeDefinition handle を型名に変換します。
        /// </summary>
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeFullName(reader, handle);
        /// <summary>
        /// TypeReference handle を型名に変換します。
        /// </summary>
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeReferenceFullName(reader, handle);
        /// <summary>
        /// TypeSpecification handle を型名に変換します。
        /// </summary>
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
#endif
