using System.Collections.Immutable;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

/// <summary>
///     Represents an object destructuring binding with optional rest binding.
/// </summary>
public sealed record ObjectBinding(
    SourceReference? Source,
    ImmutableArray<ObjectBindingProperty> Properties,
    BindingTarget? RestElement) : BindingTarget(Source);
