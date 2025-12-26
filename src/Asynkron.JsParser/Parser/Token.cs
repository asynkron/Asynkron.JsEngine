namespace Asynkron.JsParser;

public sealed record Token(
    TokenType Type,
    string Lexeme,
    object? Literal,
    int Line,
    int Column,
    int StartPosition,
    int EndPosition)
{
    public override string ToString()
    {
        return $"{Type} '{Lexeme}'";
    }
}
