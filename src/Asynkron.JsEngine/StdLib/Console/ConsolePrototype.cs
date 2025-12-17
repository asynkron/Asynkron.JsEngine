using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime.Prototypes;
using static Asynkron.JsEngine.StdLib.StandardLibrary;

namespace Asynkron.JsEngine.StdLib;

[JsPrototype("console", ObjectKind = PrototypeObjectKind.Object)]
public sealed partial class ConsolePrototype
{
    [JsHostMethod("log", Length = 0d)]
    public JsValue Log(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    [JsHostMethod("error", Length = 0d)]
    public JsValue Error(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        Console.Error.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    [JsHostMethod("warn", Length = 0d)]
    public JsValue Warn(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        Console.WriteLine($"Warning: {FormatConsoleArgs(args)}");
        return JsValue.Undefined;
    }

    [JsHostMethod("info", Length = 0d)]
    public JsValue Info(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        Console.WriteLine(FormatConsoleArgs(args));
        return JsValue.Undefined;
    }

    [JsHostMethod("debug", Length = 0d)]
    public JsValue Debug(JsValue thisValue, IReadOnlyList<JsValue> args)
    {
        Console.WriteLine($"Debug: {FormatConsoleArgs(args)}");
        return JsValue.Undefined;
    }
}
