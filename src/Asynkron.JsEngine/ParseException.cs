namespace Asynkron.JsEngine;

public sealed class ParseException : Exception
{
    public int? Line { get; }
    public int? Column { get; }

    public ParseException(string message) : base(message)
    {
    }

    public ParseException(string message, int line, int column) 
        : base($"{message} at line {line}, column {column}")
    {
        Line = line;
        Column = column;
    }

    public ParseException(string message, Token token) 
        : base($"{message} at line {token.Line}, column {token.Column}")
    {
        Line = token.Line;
        Column = token.Column;
    }
}