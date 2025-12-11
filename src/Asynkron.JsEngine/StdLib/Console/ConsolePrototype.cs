using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("console", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ConsolePrototype : JsPrototype
{
    [JsHostMethod("log", Length = 0d)]
    public object Log(object? _, IReadOnlyList<object?> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return Symbol.Undefined;
    }

    [JsHostMethod("error", Length = 0d)]
    public object Error(object? _, IReadOnlyList<object?> args)
    {
        Console.Error.WriteLine(FormatConsoleArgs(args));
        return Symbol.Undefined;
    }

    [JsHostMethod("warn", Length = 0d)]
    public object Warn(object? _, IReadOnlyList<object?> args)
    {
        Console.WriteLine($"Warning: {FormatConsoleArgs(args)}");
        return Symbol.Undefined;
    }

    [JsHostMethod("info", Length = 0d)]
    public object Info(object? _, IReadOnlyList<object?> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return Symbol.Undefined;
    }

    [JsHostMethod("debug", Length = 0d)]
    public object Debug(object? _, IReadOnlyList<object?> args)
    {
        Console.WriteLine($"Debug: {FormatConsoleArgs(args)}");
        return Symbol.Undefined;
    }
}
