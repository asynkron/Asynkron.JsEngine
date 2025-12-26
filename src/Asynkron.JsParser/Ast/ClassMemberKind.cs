namespace Asynkron.JsParser;

/// <summary>
///     Distinguishes between regular methods, getters and setters.
/// </summary>
public enum ClassMemberKind
{
    Method,
    Getter,
    Setter
}
