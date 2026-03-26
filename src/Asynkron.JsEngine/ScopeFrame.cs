namespace Asynkron.JsEngine;

public readonly record struct ScopeFrame(ScopeKind Kind, ScopeMode Mode)
{
    public bool IsStrict => Mode == ScopeMode.Strict;
    // Default must be sloppy — when the scope stack is empty (e.g., IR execution path
    // doesn't push scope frames), operations that check CurrentScope.IsStrict should
    // NOT assume strict mode. Strict mode is opt-in, not the default.
    public static ScopeFrame Default { get; } = new(ScopeKind.Program, ScopeMode.Sloppy);
}
