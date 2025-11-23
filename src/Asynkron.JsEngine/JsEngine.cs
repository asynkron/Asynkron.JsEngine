using System.Threading.Channels;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine;

/// <summary>
///     High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine : IAsyncDisposable
{
    private readonly HashSet<Task> _activeTimerTasks = [];
    private readonly Channel<string> _asyncIteratorTraceChannel = Channel.CreateUnbounded<string>();
    private readonly bool _asyncIteratorTracingEnabled;

    //DEBUG code
    private readonly Channel<DebugMessage> _debugChannel = Channel.CreateUnbounded<DebugMessage>();
    private readonly Channel<ExceptionInfo> _exceptionChannel = Channel.CreateUnbounded<ExceptionInfo>();
    private readonly JsEnvironment _global = new(isFunctionScope: true);
    private Channel<Func<Task>>? _eventQueue;
    private Task? _eventLoopTask;

    //-------

    // Module registry: maps module paths to their exported values
    private readonly Dictionary<string, JsObject> _moduleRegistry = new();
    private readonly RealmState _realm = new();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private readonly TypedConstantExpressionTransformer _typedConstantTransformer = new();
    private readonly TypedCpsTransformer _typedCpsTransformer = new();
    private readonly TypedProgramExecutor _typedExecutor = new();

    // Module loader function: allows custom module loading logic
    private Func<string, string>? _moduleLoader;
    private int _nextTimerId = 1;
    private int _pendingTaskCount; // Track pending tasks in the event queue

    /// <summary>
    ///     Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine()
    {
        _asyncIteratorTracingEnabled = false;
        // Bind the global `this` value to a dedicated JS object so that
        // top-level `this` behaves like the global object (e.g. for UMD
        // wrappers such as babel-standalone).
        _global.Define(Symbols.This, GlobalObject);

        // Expose common aliases for the global object that many libraries
        // expect to exist (Node-style `global`, standard `globalThis`).
        SetGlobal("globalThis", GlobalObject);
        SetGlobal("global", GlobalObject);

        // Register standard library objects
        SetGlobal("console", StandardLibrary.CreateConsoleObject());
        SetGlobal("Math", StandardLibrary.CreateMathObject());
        SetGlobal("Object", StandardLibrary.CreateObjectConstructor(_realm));
        SetGlobal("Function", StandardLibrary.CreateFunctionConstructor(_realm));
        SetGlobal("Number", StandardLibrary.CreateNumberConstructor(_realm));
        var bigIntFunction = StandardLibrary.CreateBigIntFunction(_realm);
        SetGlobal("BigInt", bigIntFunction);
        SetGlobal("Boolean", StandardLibrary.CreateBooleanConstructor(_realm));
        SetGlobal("String", StandardLibrary.CreateStringConstructor(_realm));
        var arrayConstructor = StandardLibrary.CreateArrayConstructor(_realm);
        SetGlobal("Array", arrayConstructor);
        if (arrayConstructor is HostFunction arrayHost)
        {
            arrayHost.RealmState = _realm;
        }

        GlobalObject.DefineProperty("Array",
            new PropertyDescriptor
            {
                Value = arrayConstructor, Writable = true, Enumerable = false, Configurable = true
            });
        GlobalObject.DefineProperty("BigInt",
            new PropertyDescriptor
            {
                Value = bigIntFunction, Writable = true, Enumerable = false, Configurable = true
            });

        // Register global constants
        SetGlobal("Infinity", double.PositiveInfinity, isGlobalConstant: true);
        GlobalObject.DefineProperty("Infinity",
            new PropertyDescriptor
            {
                Value = double.PositiveInfinity,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        SetGlobal("NaN", double.NaN, isGlobalConstant: true);
        GlobalObject.DefineProperty("NaN",
            new PropertyDescriptor
            {
                Value = double.NaN,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        SetGlobal("undefined", Symbols.Undefined, isGlobalConstant: true);
        GlobalObject.DefineProperty("undefined",
            new PropertyDescriptor
            {
                Value = Symbols.Undefined,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        // Register global functions
        SetGlobal("parseInt", StandardLibrary.CreateParseIntFunction());
        SetGlobal("parseFloat", StandardLibrary.CreateParseFloatFunction());
        SetGlobal("isNaN", StandardLibrary.CreateIsNaNFunction());
        SetGlobal("isFinite", StandardLibrary.CreateIsFiniteFunction());

        // Register Date constructor as a callable object with static methods
        var dateConstructor = StandardLibrary.CreateDateConstructor(_realm);
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
        SetGlobal("JSON", StandardLibrary.CreateJsonObject(_realm));

        // Register RegExp constructor
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor(_realm));

        // Register Promise constructor
        SetGlobal("Promise", StandardLibrary.CreatePromiseConstructor(this));

        // Register Symbol constructor
        SetGlobal("Symbol", StandardLibrary.CreateSymbolConstructor());

        // Register Map constructor
        SetGlobal("Map", StandardLibrary.CreateMapConstructor());

        // Register Set constructor
        SetGlobal("Set", StandardLibrary.CreateSetConstructor());

        // Register WeakMap constructor
        SetGlobal("WeakMap", StandardLibrary.CreateWeakMapConstructor());

        // Minimal Proxy constructor (used by Array.isArray proxy tests)
        SetGlobal("Proxy", StandardLibrary.CreateProxyConstructor(_realm));

        // Register WeakSet constructor
        SetGlobal("WeakSet", StandardLibrary.CreateWeakSetConstructor());

        // Annex B escape/unescape
        var escapeFn = StandardLibrary.CreateEscapeFunction(_realm);
        SetGlobal("escape", escapeFn);
        GlobalObject.DefineProperty("escape",
            new PropertyDescriptor { Value = escapeFn, Writable = true, Enumerable = false, Configurable = true });

        var unescapeFn = StandardLibrary.CreateUnescapeFunction(_realm);
        SetGlobal("unescape", unescapeFn);
        GlobalObject.DefineProperty("unescape",
            new PropertyDescriptor { Value = unescapeFn, Writable = true, Enumerable = false, Configurable = true });

        // Minimal browser-like storage object used by debug/babel-standalone.
        SetGlobal("localStorage", StandardLibrary.CreateLocalStorageObject());

        // Reflect object
        SetGlobal("Reflect", StandardLibrary.CreateReflectObject(_realm));

        // Register ArrayBuffer and TypedArray constructors
        SetGlobal("ArrayBuffer", StandardLibrary.CreateArrayBufferConstructor(_realm));
        SetGlobal("DataView", StandardLibrary.CreateDataViewConstructor(_realm));
        SetGlobal("Int8Array", StandardLibrary.CreateInt8ArrayConstructor(_realm));
        SetGlobal("Uint8Array", StandardLibrary.CreateUint8ArrayConstructor(_realm));
        SetGlobal("Uint8ClampedArray", StandardLibrary.CreateUint8ClampedArrayConstructor(_realm));
        SetGlobal("Int16Array", StandardLibrary.CreateInt16ArrayConstructor(_realm));
        SetGlobal("Uint16Array", StandardLibrary.CreateUint16ArrayConstructor(_realm));
        SetGlobal("Int32Array", StandardLibrary.CreateInt32ArrayConstructor(_realm));
        SetGlobal("Uint32Array", StandardLibrary.CreateUint32ArrayConstructor(_realm));
        SetGlobal("Float32Array", StandardLibrary.CreateFloat32ArrayConstructor(_realm));
        SetGlobal("Float64Array", StandardLibrary.CreateFloat64ArrayConstructor(_realm));
        SetGlobal("BigInt64Array", StandardLibrary.CreateBigInt64ArrayConstructor(_realm));
        SetGlobal("BigUint64Array", StandardLibrary.CreateBigUint64ArrayConstructor(_realm));
        SetGlobal("Intl", StandardLibrary.CreateIntlObject(_realm));
        SetGlobal("Temporal", StandardLibrary.CreateTemporalObject(_realm));

        // Register Error constructors
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor(_realm));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor(_realm, "TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor(_realm, "RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor(_realm, "ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor(_realm, "SyntaxError"));

        // Register eval function as an environment-aware callable
        // This allows eval to execute code in the caller's scope without blocking the event loop
        SetGlobal("eval", new EvalHostFunction(this));

        // Register internal helpers for async iteration
        SetGlobal("__getAsyncIterator", StandardLibrary.CreateGetAsyncIteratorHelper(this));
        SetGlobal("__iteratorNext", StandardLibrary.CreateIteratorNextHelper(this));
        SetGlobal("__awaitHelper", StandardLibrary.CreateAwaitHelper(this));

        // Register timer functions
        SetGlobalFunction("setTimeout", SetTimeout);
        SetGlobalFunction("setInterval", SetInterval);
        SetGlobalFunction("clearTimeout", ClearTimer);
        SetGlobalFunction("clearInterval", ClearTimer);

        // Register dynamic import function
        SetGlobalFunction("import", DynamicImport);

        // Provide a stable global object helper used by Test262 harness utilities.
        SetGlobal("fnGlobalObject",
            new HostFunction(_ => GlobalObject) { Realm = GlobalObject, RealmState = _realm }, true);

        // Register debug function as a debug-aware host function
        _global.Define(Symbol.Intern("__debug"), new DebugAwareHostFunction(CaptureDebugMessage));
    }

    /// <summary>
    ///     Maximum wall-clock time to allow a single evaluation to run before failing.
    ///     Null or non-positive values disable the timeout.
    /// </summary>
    // Keep a finite timeout to avoid runaway scripts, but give heavy test cases
    // (e.g. crypto/NBody fixtures) enough headroom to finish.
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Exposes the global object for realm-like scenarios (e.g. Test262 realms).
    /// </summary>
    public JsObject GlobalObject { get; } = new();

    internal RealmState RealmState => _realm;

    public async ValueTask DisposeAsync()
    {
        CancelAllTimers();
        await StopEventLoopAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Returns a channel reader that can be used to read debug messages captured during execution.
    /// </summary>
    public ChannelReader<DebugMessage> DebugMessages()
    {
        return _debugChannel.Reader;
    }

    /// <summary>
    ///     Returns a channel reader that can be used to read exceptions that occurred during execution.
    /// </summary>
    public ChannelReader<ExceptionInfo> Exceptions()
    {
        return _exceptionChannel.Reader;
    }

    /// <summary>
    ///     Logs an exception to the exception channel.
    /// </summary>
    internal void LogException(Exception exception, string context, JsEnvironment? environment = null)
    {
        var callStack = environment?.BuildCallStack() ?? [];
        var exceptionInfo = new ExceptionInfo(exception, context, callStack);
        _exceptionChannel.Writer.TryWrite(exceptionInfo);
    }

    /// <summary>
    ///     Captures the current execution state and writes a debug message to the debug channel.
    /// </summary>
    private object? CaptureDebugMessage(JsEnvironment environment, EvaluationContext context,
        IReadOnlyList<object?> args)
    {
        // Get all variables from the current environment and parent scopes
        var variables = environment.GetAllVariables();

        // Get the control flow state from the signal
        var controlFlowState = context.CurrentSignal switch
        {
            null => "None",
            ReturnSignal => "Return",
            BreakSignal => "Break",
            ContinueSignal => "Continue",
            ThrowFlowSignal => "Throw",
            YieldSignal => "Yield",
            _ => "Unknown"
        };

        // Get the call stack by traversing the environment chain
        var callStack = environment.BuildCallStack();

        // Create and write the debug message
        var debugMessage = new DebugMessage(variables, controlFlowState, callStack);
        _debugChannel.Writer.TryWrite(debugMessage);

        return null;
    }

    /// <summary>
    ///     Writes a trace message to the async iterator trace channel when tracing is enabled.
    ///     Internal helpers use this to surface branch decisions for testing and diagnostics.
    /// </summary>
    /// <param name="message">Human readable trace message.</param>
    internal void WriteAsyncIteratorTrace(string message)
    {
        if (!_asyncIteratorTracingEnabled)
        {
            return;
        }

        _asyncIteratorTraceChannel.Writer.TryWrite(message);
    }

    /// <summary>
    ///     Parses JavaScript source code into a typed AST without applying constant
    ///     folding or CPS rewrites. This is primarily used by tests and tooling
    ///     that need to inspect the raw syntax tree produced by the typed parser.
    /// </summary>
    public ProgramNode Parse(string source)
    {
        return ParseTypedProgram(source);
    }

    /// <summary>
    ///     Parses JavaScript source code and returns both the transformed S-expression and the
    ///     typed AST. This is primarily used by the evaluator so we avoid rebuilding the typed
    ///     tree multiple times.
    /// </summary>
    internal ParsedProgram ParseForExecution(string source)
    {
        var typedProgram = ParseTypedProgram(source);
        typedProgram = _typedConstantTransformer.Transform(typedProgram);

        if (TypedCpsTransformer.NeedsTransformation(typedProgram))
        {
            typedProgram = _typedCpsTransformer.Transform(typedProgram);
        }

        return new ParsedProgram(typedProgram);
    }

    /// <summary>
    ///     Executes a transformed program through the typed evaluator. The legacy
    ///     cons interpreter is no longer part of the runtime path; cons data is only
    ///     used earlier for parsing and transformation.
    /// </summary>
    internal object? ExecuteProgram(ParsedProgram program, JsEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        return _typedExecutor.Evaluate(program, environment, _realm, cancellationToken);
    }

    /// <summary>
    ///     <summary>
    ///         Parses JavaScript source code and returns the typed AST at each major
    ///         transformation stage (original, constant folded, CPS-transformed).
    ///     </summary>
    public (ProgramNode original, ProgramNode constantFolded, ProgramNode cpsTransformed)
        ParseWithTransformationSteps(string source)
    {
        var original = ParseTypedProgram(source);
        var constantFolded = _typedConstantTransformer.Transform(original);
        var cpsTransformed = constantFolded;
        if (TypedCpsTransformer.NeedsTransformation(constantFolded))
        {
            cpsTransformed = _typedCpsTransformer.Transform(constantFolded);
        }

        return (original, constantFolded, cpsTransformed);
    }

    private static ProgramNode ParseTypedProgram(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var typedParser = new TypedAstParser(tokens, source);
        return typedParser.ParseProgram();
    }

    private CancellationToken CreateEvaluationCancellationToken(CancellationToken cancellationToken,
        out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (ExecutionTimeout is { } timeout && timeout > TimeSpan.Zero &&
            timeout != Timeout.InfiniteTimeSpan)
        {
            var cts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();

            cts.CancelAfter(timeout);
            timeoutCts = cts;
            return cts.Token;
        }

        return cancellationToken;
    }

    private void StartEventLoop()
    {
        if (_eventQueue is not null)
        {
            return;
        }

        CancelAllTimers();
        _pendingTaskCount = 0;
        _eventQueue = Channel.CreateUnbounded<Func<Task>>();
        _eventLoopTask = Task.Run(() => ProcessEventQueue(_eventQueue));
    }

    private async Task DrainEventLoopAsync(CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        var maxWait = TimeSpan.FromMilliseconds(1500);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool hasActiveTimerTasks;
            lock (_activeTimerTasks)
            {
                hasActiveTimerTasks = _activeTimerTasks.Count > 0;
            }

            var hasPendingTasks = Interlocked.CompareExchange(ref _pendingTaskCount, 0, 0) > 0;
            if (!hasActiveTimerTasks && !hasPendingTasks)
            {
                break;
            }

            if (DateTime.UtcNow - start > maxWait)
            {
                CancelAllTimers();
                break;
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    private void CancelAllTimers()
    {
        foreach (var cts in _timers.Values)
        {
            cts.Cancel();
        }

        _timers.Clear();

        lock (_activeTimerTasks)
        {
            _activeTimerTasks.Clear();
        }
    }

    private async Task StopEventLoopAsync()
    {
        var queue = _eventQueue;
        if (queue is null)
        {
            return;
        }

        queue.Writer.TryComplete();

        if (_eventLoopTask is not null)
        {
            try
            {
                await _eventLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore shutdown exceptions; we are tearing down the loop.
            }
        }

        _eventQueue = null;
        _eventLoopTask = null;
    }


    /// <summary>
    ///     Parses and schedules evaluation of the provided source on the event queue.
    ///     This ensures all code executes through the event loop, maintaining proper
    ///     single-threaded execution semantics.
    /// </summary>
    public Task<object?> Evaluate(string source, CancellationToken cancellationToken = default)
    {
        var program = ParseForExecution(source);
        return Evaluate(program, cancellationToken);
    }

    /// <summary>
    ///     Evaluates an S-expression program by scheduling it on the event queue.
    ///     This ensures all code executes through the event loop, maintaining proper
    ///     single-threaded execution semantics.
    /// </summary>
    private async Task<object?> Evaluate(ParsedProgram program, CancellationToken cancellationToken = default)
    {
        StartEventLoop();

        var tcs = new TaskCompletionSource<object?>();
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        var configured = ExecutionTimeout;
        var timeout = configured.HasValue && configured.Value > TimeSpan.Zero
            ? configured.Value
            : TimeSpan.FromSeconds(10);
        var watchdog = Task.Delay(timeout, cancellationToken);

        // Schedule the evaluation on the event queue
        // This ensures ALL code runs through the event loop
        ScheduleTask(() =>
        {
            try
            {
                object? result;
                // Check if the program contains any import/export statements
                if (HasModuleStatements(program.Typed))
                {
                    // Treat as a module
                    var exports = new JsObject();
                    result = EvaluateModule(program, _global, exports);
                }
                else
                {
                    result = ExecuteProgram(program, _global, combinedToken);
                }

                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && timeoutCts?.IsCancellationRequested == true)
                {
                    tcs.SetException(new TimeoutException(
                        $"JavaScript execution exceeded the configured timeout of {ExecutionTimeout}.", ex));
                }
                else
                {
                    tcs.SetException(ex);
                }
            }

            return Task.CompletedTask;
        });

        try
        {
            var completed = await Task.WhenAny(tcs.Task, watchdog).ConfigureAwait(false);
            if (completed == tcs.Task)
            {
                return await tcs.Task.ConfigureAwait(false);
            }

            CancelAllTimers();
            await StopEventLoopAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(combinedToken);
            }

            if (timeoutCts?.IsCancellationRequested == true || watchdog.IsCanceled == false)
            {
                throw new TimeoutException(
                    $"JavaScript execution exceeded the configured timeout of {timeout}.");
            }

            throw new OperationCanceledException(combinedToken);
        }
        finally
        {
            try
            {
                await DrainEventLoopAsync(combinedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                CancelAllTimers();
            }

            CancelAllTimers();
            await StopEventLoopAsync().ConfigureAwait(false);
            timeoutCts?.Dispose();
        }
    }

    /// <summary>
    ///     Checks if a program contains any import or export statements.
    /// </summary>
    private static bool HasModuleStatements(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            if (statement is ModuleStatement)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Registers a value in the global scope.
    /// </summary>
    private void SetGlobal(string name, object? value, bool isGlobalConstant = false)
    {
        var symbol = Symbol.Intern(name);
        _global.Define(symbol, value, isGlobalConstant: isGlobalConstant);

        // Also mirror globals onto the global object so that code using
        // `this.foo` or `global.foo` can see host-provided bindings.
        if (value is HostFunction hostFunction)
        {
            if (hostFunction.Realm is null)
            {
                hostFunction.Realm = GlobalObject;
            }

            if (hostFunction.RealmState is null)
            {
                hostFunction.RealmState = _realm;
            }

            if (_realm.FunctionPrototype is not null && hostFunction.Properties.Prototype is null)
            {
                hostFunction.Properties.SetPrototype(_realm.FunctionPrototype);
            }
        }

        GlobalObject.SetProperty(name, value);
    }

    /// <summary>
    ///     Registers a value in the global scope (public facing).
    /// </summary>
    public void SetGlobalValue(string name, object? value)
    {
        SetGlobal(name, value);
    }

    /// <summary>
    ///     Registers a host function that can be invoked from interpreted code.
    /// </summary>
    public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
    {
        _global.Define(Symbol.Intern(name), new HostFunction(handler) { Realm = GlobalObject });
    }

    /// <summary>
    ///     Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        _global.Define(Symbol.Intern(name), new HostFunction(handler) { Realm = GlobalObject });
    }

    /// <summary>
    ///     Parses and evaluates the provided source code, then processes any scheduled events
    ///     in the event queue. The engine will continue running until the queue is empty
    ///     and all pending timer tasks have completed.
    /// </summary>
    /// <param name="source">The JavaScript source code to execute</param>
    /// <returns>A task that completes when all scheduled events have been processed</returns>
    public async Task<object?> Run(string source)
    {
        // Schedule evaluation on the event queue
        var evaluateTask = Evaluate(source);

        // Get the result from evaluation
        var result = await evaluateTask.ConfigureAwait(false);

        // Wait for all pending work to complete:
        // - Event queue to drain (no pending tasks)
        // - Timer tasks to complete and schedule their callbacks
        // We loop with a timeout to avoid hanging forever
        var startTime = DateTime.UtcNow;
        var maxWaitTime = TimeSpan.FromMilliseconds(1500); // Leave some margin for the 2000ms test timeout

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            // Check if we have any active timer tasks
            bool hasActiveTasks;
            lock (_activeTimerTasks)
            {
                hasActiveTasks = _activeTimerTasks.Count > 0;
            }

            // Check if we have any pending tasks in the event queue
            var hasPendingTasks = Interlocked.CompareExchange(ref _pendingTaskCount, 0, 0) > 0;

            if (!hasActiveTasks && !hasPendingTasks)
                // No active tasks and no pending tasks, we're done
            {
                break;
            }

            // Wait a bit for timer tasks and event queue to process
            await Task.Delay(20).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    ///     Schedules a task to be executed on the event queue.
    ///     This allows promises and other async operations to schedule work.
    /// </summary>
    /// <param name="task">The task to schedule</param>
    public void ScheduleTask(Func<Task> task)
    {
        StartEventLoop();
        var queue = _eventQueue ?? throw new InvalidOperationException("Event loop is not running.");

        Interlocked.Increment(ref _pendingTaskCount);
        queue.Writer.TryWrite(async () => { await task().ConfigureAwait(false); });
    }

    /// <summary>
    ///     Processes all events in the event queue until it's empty.
    ///     Each event is executed and any new events scheduled during execution
    ///     will also be processed.
    ///     Exceptions from individual tasks are caught and logged to prevent the event loop from stopping.
    /// </summary>
    private async Task ProcessEventQueue(Channel<Func<Task>> queue)
    {
        await foreach (var x in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await x().ConfigureAwait(false);
            }
            catch (OutOfMemoryException)
            {
                Console.Error.WriteLine("[ProcessEventQueue] OOM Exception");
            }
            catch (StackOverflowException)
            {
                Console.Error.WriteLine("[ProcessEventQueue] Stack overflow occurred in event queue task.");
            }
            catch (Exception ex)
            {
                // Log the exception but don't let it kill the event loop
                // Individual task failures should not stop the event queue processing
                Console.Error.WriteLine(
                    $"[ProcessEventQueue] Unhandled exception in event queue task: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"[ProcessEventQueue] Stack trace: {ex.StackTrace}");
            }
            finally
            {
                // Decrement the pending task count after processing
                Interlocked.Decrement(ref _pendingTaskCount);
            }
        }
    }

    /// <summary>
    ///     Implements setTimeout - schedules a callback to run after a delay.
    /// </summary>
    private object? SetTimeout(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
        {
            return null;
        }

        var delay = args[1] is double d ? (int)d : 0;
        var timerId = _nextTimerId++;

        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        Task? timerTask = null;
        timerTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);

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
    ///     Implements setInterval - schedules a callback to run repeatedly at a fixed interval.
    /// </summary>
    private object? SetInterval(IReadOnlyList<object?> args)
    {
        if (args.Count < 2 || args[0] is not IJsCallable callback)
        {
            return null;
        }

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
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);

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
    ///     Implements clearTimeout/clearInterval - cancels a timer.
    /// </summary>
    private object? ClearTimer(IReadOnlyList<object?> args)
    {
        if (args.Count == 0 || args[0] is not double timerId)
        {
            return null;
        }

        var id = (int)timerId;
        if (_timers.TryGetValue(id, out var cts))
        {
            cts.Cancel();
            _timers.Remove(id);
        }

        return null;
    }

    /// <summary>
    ///     Implements dynamic import() - loads a module and returns a Promise that resolves to the module's exports.
    /// </summary>
    private object? DynamicImport(IReadOnlyList<object?> args)
    {
        if (args.Count == 0)
        {
            throw new Exception("import() requires a module specifier");
        }

        var modulePath = args[0]?.ToString();
        if (string.IsNullOrEmpty(modulePath))
        {
            throw new Exception("import() requires a valid module specifier");
        }

        // Create a promise that will resolve with the module exports
        var promise = new JsPromise(this);
        var promiseObj = promise.JsObject;

        // Add promise instance methods (then, catch, finally)
        StandardLibrary.AddPromiseInstanceMethods(promiseObj, promise, this);

        // Schedule loading the module asynchronously using ScheduleTask
        // to properly track pending tasks for the event loop
        ScheduleTask(async () =>
        {
            try
            {
                // Load the module synchronously (it's cached if already loaded)
                var exports = LoadModule(modulePath);
                promise.Resolve(exports);
            }
            catch (Exception ex)
            {
                promise.Reject(ex.Message);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        });

        return promiseObj;
    }

    /// <summary>
    ///     Sets a custom module loader function that will be called to load module source code.
    ///     The function receives the module path and should return the module source code.
    ///     If not set, the engine will use File.ReadAllText to load modules from the file system.
    /// </summary>
    public void SetModuleLoader(Func<string, string> loader)
    {
        _moduleLoader = loader;
    }

    /// <summary>
    ///     Loads and evaluates a module, returning its exports object.
    ///     If the module has already been loaded, returns the cached exports.
    /// </summary>
    private JsObject LoadModule(string modulePath)
    {
        // Check if module is already loaded
        if (_moduleRegistry.TryGetValue(modulePath, out var cachedExports))
        {
            return cachedExports;
        }

        // Load module source
        string source;
        if (_moduleLoader != null)
        {
            source = _moduleLoader(modulePath);
        }
        else
            // Default: load from file system
        {
            source = File.ReadAllText(modulePath);
        }

        // Parse the module
        var program = ParseForExecution(source);

        // Create a module exports object
        var exports = new JsObject();

        // Create a module environment (inherits from global)
        var moduleEnv = new JsEnvironment(_global);

        // Evaluate the module with export tracking
        EvaluateModule(program, moduleEnv, exports);

        // Cache the exports
        _moduleRegistry[modulePath] = exports;

        return exports;
    }

    /// <summary>
    ///     Evaluates a module program and populates the exports object.
    ///     Returns the last evaluated value.
    /// </summary>
    private object? EvaluateModule(ParsedProgram program, JsEnvironment moduleEnv, JsObject exports)
    {
        object? lastValue = null;
        foreach (var statement in program.Typed.Body)
        {
            switch (statement)
            {
                case ImportStatement importStatement:
                    EvaluateImport(importStatement, moduleEnv);
                    break;
                case ExportDefaultStatement exportDefault:
                    var defaultValue = EvaluateExportDefault(exportDefault, moduleEnv, program.Typed.IsStrict);
                    exports["default"] = defaultValue;
                    break;
                case ExportNamedStatement exportNamed:
                    EvaluateExportNamed(exportNamed, moduleEnv, exports);
                    break;
                case ExportDeclarationStatement exportDeclaration:
                    EvaluateExportDeclaration(exportDeclaration, moduleEnv, exports, program.Typed.IsStrict);
                    break;
                default:
                    lastValue = ExecuteTypedStatement(statement, moduleEnv, program.Typed.IsStrict);
                    break;
            }
        }

        return lastValue;
    }

    /// <summary>
    ///     Processes an import statement and brings imported values into the module environment.
    /// </summary>
    private void EvaluateImport(ImportStatement importStatement, JsEnvironment moduleEnv)
    {
        var exports = LoadModule(importStatement.ModulePath);

        if (importStatement.DefaultBinding is null && importStatement.NamespaceBinding is null &&
            importStatement.NamedImports.IsEmpty)
        {
            return;
        }

        if (importStatement.DefaultBinding is { } defaultBinding &&
            exports.TryGetValue("default", out var defaultValue))
        {
            moduleEnv.Define(defaultBinding, defaultValue);
        }

        if (importStatement.NamespaceBinding is { } namespaceBinding)
        {
            moduleEnv.Define(namespaceBinding, exports);
        }

        foreach (var binding in importStatement.NamedImports)
        {
            if (exports.TryGetValue(binding.Imported.Name, out var value))
            {
                moduleEnv.Define(binding.Local, value);
            }
        }
    }

    private object? EvaluateExportDefault(ExportDefaultStatement statement, JsEnvironment moduleEnv, bool isStrict)
    {
        return statement.Value switch
        {
            ExportDefaultExpression expression => ExecuteTypedExpression(expression.Expression, moduleEnv, isStrict),
            ExportDefaultDeclaration declaration => EvaluateExportDefaultDeclaration(declaration, moduleEnv, isStrict),
            _ => Symbols.Undefined
        };
    }

    private object? EvaluateExportDefaultDeclaration(ExportDefaultDeclaration declaration, JsEnvironment moduleEnv,
        bool isStrict)
    {
        ExecuteTypedStatement(declaration.Declaration, moduleEnv, isStrict);
        return declaration.Declaration switch
        {
            FunctionDeclaration functionDeclaration => moduleEnv.Get(functionDeclaration.Name),
            ClassDeclaration classDeclaration => moduleEnv.Get(classDeclaration.Name),
            _ => Symbols.Undefined
        };
    }

    private void EvaluateExportNamed(ExportNamedStatement statement, JsEnvironment moduleEnv, JsObject exports)
    {
        if (statement.FromModule is { } fromModule)
        {
            var sourceExports = LoadModule(fromModule);
            foreach (var specifier in statement.Specifiers)
            {
                if (sourceExports.TryGetValue(specifier.Local.Name, out var value))
                {
                    exports[specifier.Exported.Name] = value;
                }
            }

            return;
        }

        foreach (var specifier in statement.Specifiers)
        {
            var value = moduleEnv.Get(specifier.Local);
            exports[specifier.Exported.Name] = value;
        }
    }

    private void EvaluateExportDeclaration(ExportDeclarationStatement statement, JsEnvironment moduleEnv,
        JsObject exports, bool isStrict)
    {
        ExecuteTypedStatement(statement.Declaration, moduleEnv, isStrict);
        foreach (var symbol in GetDeclaredSymbols(statement.Declaration))
        {
            var value = moduleEnv.Get(symbol);
            exports[symbol.Name] = value;
        }
    }

    private static IEnumerable<Symbol> GetDeclaredSymbols(StatementNode declaration)
    {
        switch (declaration)
        {
            case VariableDeclaration variableDeclaration:
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    if (declarator.Target is IdentifierBinding identifier)
                    {
                        yield return identifier.Name;
                    }
                }

                break;
            case FunctionDeclaration functionDeclaration:
                yield return functionDeclaration.Name;
                break;
            case ClassDeclaration classDeclaration:
                yield return classDeclaration.Name;
                break;
        }
    }

    private object? ExecuteTypedExpression(ExpressionNode expression, JsEnvironment environment, bool isStrict)
    {
        var statement = new ExpressionStatement(expression.Source, expression);
        return ExecuteTypedStatement(statement, environment, isStrict);
    }

    private object? ExecuteTypedStatement(StatementNode statement, JsEnvironment environment, bool isStrict)
    {
        var program = new ProgramNode(statement.Source, [statement], isStrict);
        return TypedAstEvaluator.EvaluateProgram(program, environment, _realm);
    }
}
