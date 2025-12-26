namespace Asynkron.JsParser;

/// <summary>
///     Enumerates the supported object literal member kinds.
/// </summary>
public enum ObjectMemberKind
{
    Property,
    Method,
    Getter,
    Setter,
    Field,
    Spread,
    Unknown
}
