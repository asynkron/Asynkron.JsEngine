namespace Asynkron.JsParser;

/// <summary>
/// Options that control parser behavior.
/// </summary>
public interface IParserOptions
{
    /// <summary>
    /// Controls whether import.meta expressions are allowed. Per ES spec, import.meta is only
    /// valid when the syntactic goal symbol is Module. Set to false when parsing in Script goal
    /// (such as the Function constructor). Defaults to true.
    /// </summary>
    bool AllowImportMeta { get; }
}

/// <summary>
/// Default parser options.
/// </summary>
public sealed class ParserOptions : IParserOptions
{
    /// <summary>
    /// Default options used when none are provided.
    /// </summary>
    public static ParserOptions Default { get; } = new();

    public bool AllowImportMeta { get; init; } = true;
}
