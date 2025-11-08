namespace Asynkron.JsEngine;

/// <summary>
/// High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine
{
    private readonly Environment _global = new(isFunctionScope: true);

    /// <summary>
    /// Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine()
    {
        // Register standard library objects
        SetGlobal("Math", StandardLibrary.CreateMathObject());
        
        // Register Date constructor as a callable object with static methods
        var dateConstructor = StandardLibrary.CreateDateConstructor();
        var dateObj = StandardLibrary.CreateDateObject();
        
        // Add static methods to constructor
        if (dateConstructor is HostFunction hf)
        {
            foreach (var prop in dateObj)
            {
                hf.SetProperty(prop.Key, prop.Value);
            }
        }
        
        SetGlobal("Date", dateConstructor);
        SetGlobal("JSON", StandardLibrary.CreateJsonObject());
    }

    /// <summary>
    /// Parses JavaScript source code into an S-expression representation.
    /// </summary>
    public Cons Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.ParseProgram();
    }

    /// <summary>
    /// Parses and immediately evaluates the provided source.
    /// </summary>
    public object? Evaluate(string source)
        => Evaluate(Parse(source));

    /// <summary>
    /// Evaluates an S-expression program.
    /// </summary>
    public object? Evaluate(Cons program)
        => Evaluator.EvaluateProgram(program, _global);

    /// <summary>
    /// Registers a value in the global scope.
    /// </summary>
    public void SetGlobal(string name, object? value)
        => _global.Define(Symbol.Intern(name), value);

    /// <summary>
    /// Registers a host function that can be invoked from interpreted code.
    /// </summary>
    public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
        => _global.Define(Symbol.Intern(name), new HostFunction(handler));

    /// <summary>
    /// Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
        => _global.Define(Symbol.Intern(name), new HostFunction(handler));
}
