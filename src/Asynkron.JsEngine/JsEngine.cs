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
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private readonly HashSet<Task> _activeTimerTasks = [];
    private int _nextTimerId = 1;

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
        
        // Register RegExp constructor
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor());
        
        // Register Promise constructor
        SetGlobal("Promise", StandardLibrary.CreatePromiseConstructor(this));
        
        // Register timer functions
        SetGlobalFunction("setTimeout", args => SetTimeout(args));
        SetGlobalFunction("setInterval", args => SetInterval(args));
        SetGlobalFunction("clearTimeout", args => ClearTimer(args));
        SetGlobalFunction("clearInterval", args => ClearTimer(args));
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

        // Wait for any active timer tasks to schedule their work
        // We poll multiple times to handle scenarios where timers schedule other timers
        for (int i = 0; i < 50 && _activeTimerTasks.Count > 0; i++)
        {
            await Task.Delay(20);
            
            // Process any newly scheduled events
            while (_eventQueue.Reader.TryRead(out var task))
            {
                await task();
            }
            
            // If there are no more active tasks, we can stop
            bool hasActiveTasks;
            lock (_activeTimerTasks)
            {
                hasActiveTasks = _activeTimerTasks.Count > 0;
            }
            
            if (!hasActiveTasks)
                break;
        }
    }

    /// <summary>
    /// Implements setTimeout - schedules a callback to run after a delay.
    /// </summary>
    private object? SetTimeout(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
            return null;

        var delay = args[1] is double d ? (int)d : 0;
        var timerId = _nextTimerId++;
        
        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        Task? timerTask = null;
        timerTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                
                if (!cts.Token.IsCancellationRequested)
                {
                    ScheduleTask(() =>
                    {
                        callback.Invoke([], null);
                        return Task.CompletedTask;
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled
            }
            finally
            {
                _timers.Remove(timerId);
                if (timerTask != null)
                {
                    lock (_activeTimerTasks)
                    {
                        _activeTimerTasks.Remove(timerTask);
                    }
                }
            }
        }, cts.Token);

        lock (_activeTimerTasks)
        {
            _activeTimerTasks.Add(timerTask);
        }

        return (double)timerId;
    }

    /// <summary>
    /// Implements setInterval - schedules a callback to run repeatedly at a fixed interval.
    /// </summary>
    private object? SetInterval(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
            return null;

        var interval = args[1] is double d ? (int)d : 0;
        var timerId = _nextTimerId++;
        
        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        Task? timerTask = null;
        timerTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(interval, cts.Token);
                    
                    if (!cts.Token.IsCancellationRequested)
                    {
                        ScheduleTask(() =>
                        {
                            callback.Invoke([], null);
                            return Task.CompletedTask;
                        });
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled
            }
            finally
            {
                _timers.Remove(timerId);
                if (timerTask != null)
                {
                    lock (_activeTimerTasks)
                    {
                        _activeTimerTasks.Remove(timerTask);
                    }
                }
            }
        }, cts.Token);

        lock (_activeTimerTasks)
        {
            _activeTimerTasks.Add(timerTask);
        }

        return (double)timerId;
    }

    /// <summary>
    /// Implements clearTimeout/clearInterval - cancels a timer.
    /// </summary>
    private object? ClearTimer(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double timerId)
            return null;

        var id = (int)timerId;
        if (_timers.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            _timers.Remove(id);
        }

        return null;
    }
}
