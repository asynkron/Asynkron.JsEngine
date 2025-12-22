#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

#endregion

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an array destructuring binding with optional rest element.
/// </summary>
public sealed record ArrayBinding(
    SourceReference? Source,
    ImmutableArray<ArrayBindingElement> Elements,
    BindingTarget? RestElement) : BindingTarget(Source);
