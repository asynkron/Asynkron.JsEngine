#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an <c>import</c> declaration.
/// </summary>
public sealed record ImportStatement(
    SourceReference? Source,
    string ModulePath,
    Symbol? DefaultBinding,
    Symbol? NamespaceBinding,
    ImmutableArray<ImportBinding> NamedImports,
    bool IsDeferred,
    ImmutableArray<ImportAttribute> Attributes = default) : ModuleStatement(Source);
