namespace Devo6.WorkFlow.Engine;

/// <summary>
/// File-based workflow の Entry が持つ名前を表します。
/// </summary>
public interface IWorkflowEntry
{
    /// <summary>
    /// Entry の完全修飾名を取得します。
    /// </summary>
    string QualifiedName { get; }
}

/// <summary>
/// File-based workflow の Entry を構築する Provider の契約です。
/// </summary>
public interface IWorkflowEntryProvider
{
    /// <summary>
    /// workflow Entry を構築します。
    /// </summary>
    IWorkflowEntry Create();
}

/// <summary>
/// File-based workflow の Provider を Entry 名へ登録します。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class WorkflowEntryAttribute : Attribute
{
    /// <summary>
    /// Entry 名と任意の namespace を指定して Provider を登録します。
    /// </summary>
    public WorkflowEntryAttribute(string name, string? namespaceName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (namespaceName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        }

        Name = name;
        NamespaceName = namespaceName;
    }

    /// <summary>
    /// 短い Entry 名を取得します。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Entry の namespace を取得します。
    /// </summary>
    public string? NamespaceName { get; }
}
