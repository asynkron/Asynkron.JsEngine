namespace Asynkron.JsEngine;

public readonly record struct ScopeFrame(ScopeKind Kind, ScopeMode Mode)
{
    public bool IsStrict => Mode == ScopeMode.Strict;
    public static ScopeFrame Default { get; } = new(ScopeKind.Program, ScopeMode.Strict);
}
