using System.Threading.Channels;

namespace Asynkron.JsEngine;

/// <summary>
/// High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine
{
    private readonly Environment _global = new(isFunctionScope: true);
    private readonly CpsTransformer _cpsTransformer = new();
    private readonly Channel<Func<Task>> _eventQueue = Channel.CreateUnbounded<Func<Task>>();

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
    /// Applies CPS (Continuation-Passing Style) transformation if the code contains
    /// async functions, generators, await expressions, or yield expressions.
    /// </summary>
    public Cons Parse(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        
        // Step 2: Parse to S-expressions
        var parser = new Parser(tokens);
        var program = parser.ParseProgram();
        
        // Step 3: Apply CPS transformation if needed
        // This enables support for generators and async/await by converting
        // the S-expression tree to continuation-passing style
        if (_cpsTransformer.NeedsTransformation(program))
        {
            return _cpsTransformer.Transform(program);
        }
        
        return program;
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

    /// <summary>
    /// Parses and evaluates the provided source code, then processes any scheduled events
    /// in the event queue. The engine will continue running until the queue is empty.
    /// </summary>
    /// <param name="source">The JavaScript source code to execute</param>
    /// <returns>A task that completes when all scheduled events have been processed</returns>
    public async Task<object?> Run(string source)
    {
        // Parse and evaluate the source code
        var result = Evaluate(source);
        
        // Process the event queue until it's empty
        await ProcessEventQueue();
        
        return result;
    }

    /// <summary>
    /// Schedules a task to be executed on the event queue.
    /// This allows promises and other async operations to schedule work.
    /// </summary>
    /// <param name="task">The task to schedule</param>
    public void ScheduleTask(Func<Task> task)
    {
        _eventQueue.Writer.TryWrite(task);
    }

    /// <summary>
    /// Processes all events in the event queue until it's empty.
    /// Each event is executed and any new events scheduled during execution
    /// will also be processed.
    /// </summary>
    private async Task ProcessEventQueue()
    {
        while (_eventQueue.Reader.TryRead(out var task))
        {
            await task();
        }
    }
}
