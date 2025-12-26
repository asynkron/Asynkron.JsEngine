
using System.Collections.Immutable;



namespace Asynkron.JsParser;

/// <summary>
///     Represents an array destructuring binding with optional rest element.
/// </summary>
public sealed record ArrayBinding(
    SourceReference? Source,
    ImmutableArray<ArrayBindingElement> Elements,
    BindingTarget? RestElement) : BindingTarget(Source);
