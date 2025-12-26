



namespace Asynkron.JsParser;

/// <summary>
///     Base type for module import/export statements. Concrete records capture the
///     typed shape of each construct so higher layers no longer need to reason
///     about the underlying cons cells.
/// </summary>
public abstract record ModuleStatement(SourceReference? Source) : StatementNode(Source);
