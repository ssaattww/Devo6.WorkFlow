using Devo6.WorkFlow.Abstractions;

namespace Devo6.WorkFlow.Tests;

/// <summary>
/// 公開 API の基礎契約を検査します。
/// </summary>
public sealed class PublicApiFoundationTests
{
    /// <summary>
    /// IStep が StepInput から値を取得して同期実行できることを検査します。
    /// </summary>
    [Fact(DisplayName = "IStep は StepInput から値を取得して同期実行できる")]
    public void IStepはStepInputから値を取得して同期実行できる()
    {
        var input = new StepInput();
        input.Add("message", "hello");

        IStep<string> step = new EchoStep();

        Assert.Equal("hello", step.Execute(input));
    }

    /// <summary>
    /// StepInput が型付き値と名前付き値を取得できることを検査します。
    /// </summary>
    [Fact(DisplayName = "StepInput は型付き取得と名前付き取得ができる")]
    public void StepInputは型付き取得と名前付き取得ができる()
    {
        var context = new StepContext();
        var input = new StepInput(context);
        input.Add(123);
        input.Add("title", "件名");

        Assert.Same(context, input.Context);
        Assert.Equal(123, input.Get<int>());
        Assert.Equal("件名", input.Get<string>("title"));
        Assert.True(input.TryGet<int>(out int typedValue));
        Assert.Equal(123, typedValue);
        Assert.True(input.TryGet<string>("title", out string? namedValue));
        Assert.Equal("件名", namedValue);
    }

    /// <summary>
    /// StepInput が同じ型と名前の重複登録を失敗させることを検査します。
    /// </summary>
    [Fact(DisplayName = "StepInput は同じ型と名前の重複登録を失敗させる")]
    public void StepInputは同じ型と名前の重複登録を失敗させる()
    {
        var input = new StepInput();
        input.Add("first");
        input.Add("left", "named");

        Assert.Throws<InvalidOperationException>(() => input.Add("second"));
        Assert.Throws<InvalidOperationException>(() => input.Add("left", "duplicate"));

        input.Add("right", "other");
        Assert.Equal("other", input.Get<string>("right"));
    }

    /// <summary>
    /// StepInput の公開 API が Context と Get と TryGet に限定されることを検査します。
    /// </summary>
    [Fact(DisplayName = "StepInput の公開 API は Context と Get と TryGet に限定する")]
    public void StepInputの公開ApiはContextとGetとTryGetに限定する()
    {
        string[] publicMembers = typeof(StepInput)
            .GetMembers()
            .Where(member => member.DeclaringType == typeof(StepInput))
            .Select(member => member.Name)
            .Distinct()
            .Order()
            .ToArray();

        Assert.DoesNotContain("Add", publicMembers);
        Assert.Empty(
            publicMembers.Except(
            [
                ".ctor",
                "Context",
                "get_Context",
                "Get",
                "TryGet",
            ]));
        Assert.Empty(
            new[]
            {
                ".ctor",
                "Context",
                "get_Context",
                "Get",
                "TryGet",
            }.Except(publicMembers));
    }

    /// <summary>
    /// StepContext が型付き値と名前付き値を明示上書きできることを検査します。
    /// </summary>
    [Fact(DisplayName = "StepContext は型付き取得と名前付き取得を明示上書きできる")]
    public void StepContextは型付き取得と名前付き取得を明示上書きできる()
    {
        var context = new StepContext();

        context.Set(1);
        context.Set(2);
        context.Set("answer", "old");
        context.Set("answer", "new");

        Assert.NotNull(context.Logger);
        Assert.Equal(2, context.Get<int>());
        Assert.Equal("new", context.Get<string>("answer"));
        Assert.True(context.TryGet<int>(out int typedValue));
        Assert.Equal(2, typedValue);
        Assert.True(context.TryGet<string>("answer", out string? namedValue));
        Assert.Equal("new", namedValue);
    }

    /// <summary>
    /// 未登録値と無効な名前が分かりやすい例外または失敗結果になることを検査します。
    /// </summary>
    [Fact(DisplayName = "未登録値と無効な名前は分かりやすく失敗する")]
    public void 未登録値と無効な名前は分かりやすく失敗する()
    {
        var input = new StepInput();
        var context = new StepContext();

        Assert.Throws<KeyNotFoundException>(() => input.Get<Guid>());
        Assert.False(input.TryGet<Guid>(out _));
        Assert.Throws<KeyNotFoundException>(() => context.Get<Guid>());
        Assert.False(context.TryGet<Guid>(out _));

        Assert.Throws<ArgumentNullException>(() => input.Get<string>(null!));
        Assert.Throws<ArgumentException>(() => input.Get<string>(" "));
        Assert.Throws<ArgumentNullException>(() => input.Add(null!, "value"));
        Assert.Throws<ArgumentException>(() => input.Add(" ", "value"));
        Assert.Throws<ArgumentNullException>(() => context.Set(null!, "value"));
        Assert.Throws<ArgumentException>(() => context.Set(" ", "value"));
    }

    /// <summary>
    /// StepValueKey が型キーと名前付きキーを区別することを検査します。
    /// </summary>
    [Fact(DisplayName = "StepValueKey は型キーと名前付きキーを区別する")]
    public void StepValueKeyは型キーと名前付きキーを区別する()
    {
        StepValueKey typedKey = StepValueKey.For<string>();
        StepValueKey titleKey = StepValueKey.For<string>("title");
        StepValueKey bodyKey = StepValueKey.For<string>("body");

        Assert.Equal(typeof(string), typedKey.ValueType);
        Assert.Null(typedKey.Name);
        Assert.Equal("title", titleKey.Name);
        Assert.NotEqual(typedKey, titleKey);
        Assert.NotEqual(titleKey, bodyKey);
        Assert.Throws<ArgumentNullException>(() => StepValueKey.For<string>(null!));
        Assert.Throws<ArgumentException>(() => StepValueKey.For<string>(" "));
    }

    /// <summary>
    /// Unit が単一の公開値を持つ readonly struct として使えることを検査します。
    /// </summary>
    [Fact(DisplayName = "Unit は単一の公開値を持つ readonly struct として使える")]
    public void Unitは単一の公開値を持つReadonlyStructとして使える()
    {
        Unit value = Unit.Value;

        Assert.Equal(default, value);
    }

    /// <summary>
    /// 入力から message 値を返す検査用 Step です。
    /// </summary>
    private sealed class EchoStep : IStep<string>
    {
        /// <summary>
        /// StepInput の message 値を返します。
        /// </summary>
        /// <param name="input">message 値を含む Step 入力。</param>
        /// <returns>入力から取得した message 値。</returns>
        public string Execute(StepInput input)
        {
            return input.Get<string>("message");
        }
    }
}
