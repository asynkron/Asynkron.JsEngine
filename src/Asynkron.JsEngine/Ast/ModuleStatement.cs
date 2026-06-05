using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Base type for module import/export statements. Concrete records capture the
///     typed shape of each construct so higher layers no longer need to reason
///     about parser-era intermediate forms.
/// </summary>
public abstract record ModuleStatement(SourceReference? Source) : StatementNode(Source);
