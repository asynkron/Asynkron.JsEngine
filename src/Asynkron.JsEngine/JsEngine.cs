using System.Collections.Immutable;
using System.Diagnostics;
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
    internal static readonly object UninitializedExportMarker = new();
    private int _activeTimerCount; // Track active timer tasks (delayed work on ThreadPool)
    private readonly Channel<string> _asyncIteratorTraceChannel = Channel.CreateUnbounded<string>();
    private readonly bool _asyncIteratorTracingEnabled;

    //DEBUG code
    private readonly Channel<DebugMessage> _debugChannel = Channel.CreateUnbounded<DebugMessage>();
    private readonly Channel<ExceptionInfo> _exceptionChannel = Channel.CreateUnbounded<ExceptionInfo>();

    private readonly Dictionary<JsObject, ModuleNamespace> _moduleNamespaces =
        new(ReferenceEqualityComparer<JsObject>.Instance);

    //-------

    private sealed class ModuleEntry
    {
        internal ModuleEntry(string path, ProgramNode program, JsEnvironment environment, JsObject exports)
        {
            Path = path;
            Program = program;
            Environment = environment;
            Exports = exports;
        }

        internal string Path { get; }
        internal ProgramNode Program { get; }
        internal JsEnvironment Environment { get; }
        internal JsObject Exports { get; }
        internal bool IsAsync { get; set; }
        internal bool Instantiating { get; set; }
        internal bool Instantiated { get; set; }
        internal bool Evaluated { get; set; }
        internal bool Evaluating { get; set; }
        internal Task<object?>? EvaluationTask { get; set; }
        internal ModuleNamespace? Namespace { get; set; }
        internal ModuleNamespace? DeferredNamespace { get; set; }
        internal JsObject? ImportMeta { get; set; }
        internal object? LastValue { get; set; }
        internal bool HasAsyncDependency { get; set; }
    }

    // Module registry: maps module paths to their exported values
    private readonly Dictionary<string, ModuleEntry> _moduleRegistry = new();
    private readonly Dictionary<int, CancellationTokenSource> _timers = new();
    private readonly TypedConstantExpressionTransformer _typedConstantTransformer = new();
    private readonly TypedCpsTransformer _typedCpsTransformer = new();
    private Task? _eventLoopTask;
    private int? _eventLoopThreadId;
    private readonly object _microtaskLock = new();
    private Channel<Func<Task>>? _eventQueue;

    // Synchronous microtask queue for top-level await support
    private readonly Queue<Action> _microtaskQueue = new();
    private bool _isDrainingMicrotasks;

    // Module loader function: allows custom module loading logic
    private Func<string, string?, string>? _moduleLoader;
    private string? _currentModulePath;
    private int _nextTimerId = 1;
    private int _pendingTaskCount; // Track pending tasks in the event queue
    private TaskCompletionSource? _drainCompletionSource; // Signals when event loop has drained
    private readonly object _drainLock = new(); // Protects _drainCompletionSource

    /// <summary>
    ///     Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine(IJsEngineOptions? options = null)
    {
        Options = options ?? JsEngineOptions.Default;
        _asyncIteratorTracingEnabled = false;
        RealmState.Options = Options;
        RealmState.Engine = this;
        GlobalEnvironment.SetRealmState(RealmState);
        GlobalExecutionScope = GlobalEnvironment;
        // Bind the global `this` value to a dedicated JS object so that
        // top-level `this` behaves like the global object (e.g. for UMD
        // wrappers such as babel-standalone).
        GlobalEnvironment.Define(Symbol.This, GlobalObject);
        GlobalObject.RealmState = RealmState;

        // Expose common aliases for the global object that many libraries
        // expect to exist (Node-style `global`, standard `globalThis`).
        SetGlobal("globalThis", GlobalObject);
        SetGlobal("global", GlobalObject);

        // Register standard library objects
        SetGlobal("console", ConsolePrototype.CreatePrototype(RealmState));
        SetGlobal("Math", MathPrototype.CreatePrototype(RealmState));
        SetGlobal("Object", StandardLibrary.CreateObjectConstructor(RealmState));

        // Per ECMAScript spec, the global object's [[Prototype]] is Object.prototype.
        // This ensures that methods like hasOwnProperty are inherited by the global object.
        if (RealmState.ObjectPrototype is not null)
        {
            GlobalObject.SetPrototype(RealmState.ObjectPrototype);
        }

        SetGlobal("Function", StandardLibrary.CreateFunctionConstructor(RealmState, this));
        SetGlobal("Number", StandardLibrary.CreateNumberConstructor(RealmState));
        var bigIntFunction = StandardLibrary.CreateBigIntFunction(RealmState);
        SetGlobal("BigInt", bigIntFunction);
        SetGlobal("Boolean", StandardLibrary.CreateBooleanConstructor(RealmState));
        SetGlobal("String", StandardLibrary.CreateStringConstructor(RealmState));
        var arrayConstructor = StandardLibrary.CreateArrayConstructor(RealmState);
        SetGlobal("Array", arrayConstructor);
        if (arrayConstructor is HostFunction)
        {
            arrayConstructor.RealmState = RealmState;
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
        SetGlobal("Infinity", double.PositiveInfinity, true);
        GlobalObject.DefineProperty("Infinity",
            new PropertyDescriptor
            {
                Value = double.PositiveInfinity, Writable = false, Enumerable = false, Configurable = false
            });

        SetGlobal("NaN", double.NaN, true);
        GlobalObject.DefineProperty("NaN",
            new PropertyDescriptor { Value = double.NaN, Writable = false, Enumerable = false, Configurable = false });

        SetGlobal("undefined", Symbol.Undefined, true);
        GlobalObject.DefineProperty("undefined",
            new PropertyDescriptor
            {
                Value = Symbol.Undefined, Writable = false, Enumerable = false, Configurable = false
            });

        // Register global functions
        SetGlobal("parseInt", StandardLibrary.CreateParseIntFunction());
        SetGlobal("parseFloat", StandardLibrary.CreateParseFloatFunction());
        SetGlobal("isNaN", StandardLibrary.CreateIsNaNFunction());
        SetGlobal("isFinite", StandardLibrary.CreateIsFiniteFunction());

        // Shared TypedArray intrinsic (abstract)
        var typedArrayCtor = StandardLibrary.EnsureTypedArrayIntrinsic(RealmState);
        SetGlobal("TypedArray", typedArrayCtor);

        // Register Date constructor
        SetGlobal("Date", StandardLibrary.CreateDateConstructor(RealmState));
        SetGlobal("JSON", JsonPrototype.CreatePrototype(RealmState));

        // Register RegExp constructor
        SetGlobal("RegExp", StandardLibrary.CreateRegExpConstructor(RealmState));

        // Error constructors
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor(RealmState));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor(RealmState, "TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor(RealmState, "RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor(RealmState, "ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor(RealmState, "SyntaxError"));
        SetGlobal("EvalError", StandardLibrary.CreateErrorConstructor(RealmState, "EvalError"));
        SetGlobal("URIError", StandardLibrary.CreateErrorConstructor(RealmState, "URIError"));
        SetGlobal("AggregateError", StandardLibrary.CreateErrorConstructor(RealmState, "AggregateError"));

        // Register Promise constructor
        var promiseConstructor = StandardLibrary.CreatePromiseConstructor(RealmState);
        SetGlobal("Promise", promiseConstructor);
        RealmState.PromiseConstructor = promiseConstructor as IJsCallable;

        // Register Symbol constructor
        SetGlobal("Symbol", StandardLibrary.CreateSymbolConstructor(RealmState));

        // Register Map constructor
        SetGlobal("Map", MapConstructor.CreateConstructor(RealmState));

        // Register Set constructor
        SetGlobal("Set", SetConstructor.CreateConstructor(RealmState));

        // Register WeakMap constructor
        SetGlobal("WeakMap", WeakMapConstructor.CreateConstructor(RealmState));

        // Minimal Proxy constructor (used by Array.isArray proxy tests)
        SetGlobal("Proxy", StandardLibrary.CreateProxyConstructor(RealmState));

        // Register WeakSet constructor
        SetGlobal("WeakSet", WeakSetConstructor.CreateConstructor(RealmState));

        SetGlobal("WeakRef", StandardLibrary.CreateWeakRefConstructor(RealmState));

        // Annex B escape/unescape
        var escapeFn = StandardLibrary.CreateEscapeFunction(RealmState);
        SetGlobal("escape", escapeFn);
        GlobalObject.DefineProperty("escape",
            new PropertyDescriptor { Value = escapeFn, Writable = true, Enumerable = false, Configurable = true });

        var unescapeFn = StandardLibrary.CreateUnescapeFunction(RealmState);
        SetGlobal("unescape", unescapeFn);
        GlobalObject.DefineProperty("unescape",
            new PropertyDescriptor { Value = unescapeFn, Writable = true, Enumerable = false, Configurable = true });

        // Minimal browser-like storage object used by debug/babel-standalone.
        SetGlobal("localStorage", StandardLibrary.CreateLocalStorageObject());

        // Reflect object
        SetGlobal("Reflect", ReflectPrototype.CreatePrototype(RealmState));

        // Register ArrayBuffer and TypedArray constructors
        SetGlobal("ArrayBuffer", StandardLibrary.CreateArrayBufferConstructor(RealmState));
        SetGlobal("SharedArrayBuffer", StandardLibrary.CreateSharedArrayBufferConstructor(RealmState));
        SetGlobal("DataView", StandardLibrary.CreateDataViewConstructor(RealmState));
        SetGlobal("Int8Array", StandardLibrary.CreateInt8ArrayConstructor(RealmState));
        SetGlobal("Uint8Array", StandardLibrary.CreateUint8ArrayConstructor(RealmState));
        SetGlobal("Uint8ClampedArray", StandardLibrary.CreateUint8ClampedArrayConstructor(RealmState));
        SetGlobal("Int16Array", StandardLibrary.CreateInt16ArrayConstructor(RealmState));
        SetGlobal("Uint16Array", StandardLibrary.CreateUint16ArrayConstructor(RealmState));
        SetGlobal("Int32Array", StandardLibrary.CreateInt32ArrayConstructor(RealmState));
        SetGlobal("Uint32Array", StandardLibrary.CreateUint32ArrayConstructor(RealmState));
        SetGlobal("Float32Array", StandardLibrary.CreateFloat32ArrayConstructor(RealmState));
        SetGlobal("Float64Array", StandardLibrary.CreateFloat64ArrayConstructor(RealmState));
        SetGlobal("BigInt64Array", StandardLibrary.CreateBigInt64ArrayConstructor(RealmState));
        SetGlobal("BigUint64Array", StandardLibrary.CreateBigUint64ArrayConstructor(RealmState));
        SetGlobal("Intl", StandardLibrary.CreateIntlObject(RealmState));
        SetGlobal("Temporal", StandardLibrary.CreateTemporalObject(RealmState));

        // Register Error constructors
        SetGlobal("Error", StandardLibrary.CreateErrorConstructor(RealmState));
        SetGlobal("TypeError", StandardLibrary.CreateErrorConstructor(RealmState, "TypeError"));
        SetGlobal("RangeError", StandardLibrary.CreateErrorConstructor(RealmState, "RangeError"));
        SetGlobal("ReferenceError", StandardLibrary.CreateErrorConstructor(RealmState, "ReferenceError"));
        SetGlobal("SyntaxError", StandardLibrary.CreateErrorConstructor(RealmState, "SyntaxError"));
        SetGlobal("EvalError", StandardLibrary.CreateErrorConstructor(RealmState, "EvalError"));

        // Register eval function as an environment-aware callable
        // This allows eval to execute code in the caller's scope without blocking the event loop
        SetGlobal("eval", new EvalHostFunction(this));

        // Register internal helpers for async iteration
        SetGlobal("__getAsyncIterator", StandardLibrary.CreateGetAsyncIteratorHelper(this));
        SetGlobal("__iteratorNext", StandardLibrary.CreateIteratorNextHelper(this));
        SetGlobal("__awaitHelper", StandardLibrary.CreateAwaitHelper(this));
        SetGlobal("$DETACHBUFFER", new HostFunction((_, args) =>
        {
            if (args.Count > 0 && args[0] is TypedArrayBase view)
            {
                view.Buffer.Detach();
            }
            else if (args.Count > 0 && args[0] is JsArrayBuffer buffer)
            {
                buffer.Detach();
            }

            return Symbol.Undefined;
        }));

        // Register timer functions
        SetGlobalFunction("setTimeout", SetTimeout);
        SetGlobalFunction("setInterval", SetInterval);
        SetGlobalFunction("clearTimeout", ClearTimer);
        SetGlobalFunction("clearInterval", ClearTimer);

        // Register dynamic import function
        var importFunction = new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Module, null), RealmState)
        {
            IsConstructor = false
        };
        importFunction.SetInvokeWithContext(
            (args, _, ctx, _) => DynamicImport(args, ctx, ImportPhase.Module, importFunction));

        var importDeferFunction =
            new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Defer, null), RealmState)
            {
                IsConstructor = false
            };
        importDeferFunction.SetInvokeWithContext(
            (args, _, ctx, _) => DynamicImport(args, ctx, ImportPhase.Defer, importDeferFunction));

        var importSourceFunction =
            new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Source, null), RealmState)
            {
                IsConstructor = false
            };
        importSourceFunction.SetInvokeWithContext(
            (args, _, ctx, _) => DynamicImport(args, ctx, ImportPhase.Source, importSourceFunction));
        importFunction.SetProperty("defer", importDeferFunction);
        importFunction.SetProperty("source", importSourceFunction);
        SetGlobal("import", importFunction);

        // Provide a stable global object helper used by Test262 harness utilities.
        SetGlobal("fnGlobalObject",
            new HostFunction(_ => GlobalObject) { Realm = GlobalObject, RealmState = RealmState }, true);

        // Register debug function as a debug-aware host function
        GlobalEnvironment.Define(Symbol.DebugIdentifier, new DebugAwareHostFunction(CaptureDebugMessage));
    }

    internal int PromiseCallDepth { get; set; }
    internal int MaxCallDepth { get; set; } = 1000;

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

    internal JsEnvironment GlobalEnvironment { get; } = new(isFunctionScope: true);
    internal JsEnvironment GlobalExecutionScope { get; private set; }

    internal RealmState RealmState { get; } = new();
    public IJsEngineOptions Options { get; }

    internal void SetGlobalExecutionScope(JsEnvironment environment)
    {
        GlobalExecutionScope = environment ?? GlobalEnvironment;
    }

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
            ReturnCompletionSignal => "Return",
            BreakCompletionSignal => "Break",
            ContinueCompletionSignal => "Continue",
            ThrowFlowCompletionSignal => "Throw",
            YieldCompletionSignal => "Yield",
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
        return ParseTypedProgram(source, options: Options);
    }

    /// <summary>
    ///     Parses JavaScript source code and returns both the transformed S-expression and the
    ///     typed AST. This is primarily used by the evaluator so we avoid rebuilding the typed
    ///     tree multiple times.
    /// </summary>
    internal ParsedProgram ParseForExecution(
        string source,
        bool forceStrict = false,
        bool allowTopLevelAwait = false,
        bool allowHtmlComments = true,
        IJsEngineOptions? options = null)
    {
        var typedProgram = ParseTypedProgram(source, forceStrict, allowTopLevelAwait, allowHtmlComments, options ?? Options);
        if (forceStrict && !typedProgram.IsStrict)
        {
            typedProgram = new ProgramNode(typedProgram.Source, typedProgram.Body, true);
        }

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
    internal object? ExecuteProgram(
        ParsedProgram program,
        JsEnvironment environment,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script)
    {
        return TypedProgramExecutor.Evaluate(program, environment, RealmState, cancellationToken, executionKind);
    }

    /// <summary>
    ///     <summary>
    ///         Parses JavaScript source code and returns the typed AST at each major
    ///         transformation stage (original, constant folded, CPS-transformed).
    ///     </summary>
    public (ProgramNode original, ProgramNode constantFolded, ProgramNode cpsTransformed)
        ParseWithTransformationSteps(string source)
    {
        var original = ParseTypedProgram(source, options: Options);
        var constantFolded = _typedConstantTransformer.Transform(original);
        var cpsTransformed = constantFolded;
        if (TypedCpsTransformer.NeedsTransformation(constantFolded))
        {
            cpsTransformed = _typedCpsTransformer.Transform(constantFolded);
        }

        return (original, constantFolded, cpsTransformed);
    }

    private static ProgramNode ParseTypedProgram(
        string source,
        bool forceStrict = false,
        bool allowTopLevelAwait = false,
        bool allowHtmlComments = true,
        IJsEngineOptions? options = null)
    {
        var lexer = new Lexer(source, allowHtmlComments);
        var tokens = lexer.Tokenize();
        var typedParser = new TypedAstParser(tokens, source, forceStrict, allowTopLevelAwait, options);
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

    internal void StartEventLoop()
    {
        if (_eventQueue is not null)
        {
            return;
        }

        // Note: Don't reset _activeTimerCount or _pendingTaskCount here!
        // Timers may have been scheduled during sync evaluation that we need to wait for.

        // Reset drain completion source for new event loop
        lock (_drainLock)
        {
            _drainCompletionSource = null;
        }

        _eventQueue = Channel.CreateUnbounded<Func<Task>>();
        _eventLoopTask = Task.Run(() => ProcessEventQueue(_eventQueue));
    }

    internal async Task DrainEventLoopAsync(CancellationToken cancellationToken)
    {
        // Check if already drained
        if (IsEventLoopDrained())
        {
            return;
        }

        // Create or get the drain completion source
        Task drainTask;
        lock (_drainLock)
        {
            // Double-check after acquiring lock
            if (IsEventLoopDrained())
            {
                return;
            }

            _drainCompletionSource ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drainTask = _drainCompletionSource.Task;
        }

        // Wait for drain with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

        try
        {
            await drainTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout reached, cancel all timers
            CancelAllTimers();
        }
    }

    internal bool IsEventLoopDrained()
    {
        var hasActiveTimers = Interlocked.CompareExchange(ref _activeTimerCount, 0, 0) > 0;
        var hasPendingTasks = Interlocked.CompareExchange(ref _pendingTaskCount, 0, 0) > 0;
        return !hasActiveTimers && !hasPendingTasks;
    }

    private void TrySignalDrainComplete()
    {
        if (!IsEventLoopDrained())
        {
            return;
        }

        lock (_drainLock)
        {
            // Double-check after acquiring lock
            if (!IsEventLoopDrained())
            {
                return;
            }

            _drainCompletionSource?.TrySetResult();
        }
    }

    private void CancelAllTimers()
    {
        foreach (var cts in _timers.Values)
        {
            cts.Cancel();
        }

        _timers.Clear();
        Interlocked.Exchange(ref _activeTimerCount, 0);
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
    ///     Synchronously evaluates JavaScript source code without using the event loop.
    ///     This is much faster for code that doesn't require async features (setTimeout,
    ///     Promises, async/await, etc.). Use this when you know the code is purely synchronous.
    /// </summary>
    /// <remarks>
    ///     This method does NOT support:
    ///     - setTimeout/setInterval callbacks
    ///     - Promise resolution (Promises will be returned but not awaited)
    ///     - async/await (will throw or return unresolved promises)
    ///     - Any other event-loop dependent features
    ///
    ///     For code that uses these features, use <see cref="Evaluate"/> instead.
    /// </remarks>
    /// <param name="source">The JavaScript source code to evaluate.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The result of the evaluation.</returns>
    public object? EvaluateSync(string source, CancellationToken cancellationToken = default)
    {
        var program = ParseForExecution(source);
        return EvaluateSyncInternal(program, cancellationToken);
    }

    /// <summary>
    ///     Synchronously evaluates a pre-parsed program without using the event loop.
    /// </summary>
    private object? EvaluateSyncInternal(
        ParsedProgram program,
        CancellationToken cancellationToken = default,
        string? sourcePath = null,
        bool forceModule = false)
    {
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        try
        {
            var isModule = forceModule || HasModuleStatements(program.Typed);
            EnsureImportMetaAllowed(program.Typed, isModule);
            if (isModule)
            {
                string? moduleKey = null;
                ModuleEntry entry;
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    moduleKey = NormalizeModulePath(sourcePath!, null, _moduleLoader is not null);
                    if (!_moduleRegistry.TryGetValue(moduleKey, out entry!))
                    {
                        entry = CreateModuleEntry(EnsureStrictProgram(program),
                            CreateModuleEnvironment(moduleKey),
                            new JsObject(),
                            moduleKey);
                        _moduleRegistry[moduleKey] = entry;
                    }
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }
                else
                {
                    entry = CreateModuleEntry(EnsureStrictProgram(program),
                        CreateModuleEnvironment(moduleKey),
                        new JsObject(),
                        string.Empty);
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }

                EnsureModuleInstantiated(entry);
                if (entry.IsAsync || entry.HasAsyncDependency)
                {
                    EnsureModuleEvaluatedAsync(entry).GetAwaiter().GetResult();
                }
                else
                {
                    EnsureModuleEvaluated(entry);
                }
                return entry.LastValue;
            }

            return ExecuteProgram(program, GlobalEnvironment, combinedToken);
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    public Task<object?> EvaluateModule(string source, string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        var program = ParseForExecution(source, true, true);
        return Evaluate(program, cancellationToken, sourcePath, true);
    }

    /// <summary>
    ///     Evaluates a program with lazy event loop initialization.
    ///     Runs synchronously first, then only starts the event loop if async work is pending.
    /// </summary>
    private async Task<object?> Evaluate(
        ParsedProgram program,
        CancellationToken cancellationToken = default,
        string? sourcePath = null,
        bool forceModule = false)
    {
        if (_eventQueue is not null && _eventLoopThreadId == Environment.CurrentManagedThreadId)
        {
            // Already running on the event loop thread; execute synchronously to avoid deadlocks
            return EvaluateInline(program, cancellationToken, sourcePath, forceModule);
        }

        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);

        try
        {
            // Step 1: Execute the code synchronously first (no event loop)
            object? result;
            var isModule = forceModule || HasModuleStatements(program.Typed);
            if (isModule)
            {
                string? moduleKey = null;
                ModuleEntry entry;
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    moduleKey = NormalizeModulePath(sourcePath!, null, _moduleLoader is not null);
                    if (!_moduleRegistry.TryGetValue(moduleKey, out entry!))
                    {
                        entry = CreateModuleEntry(EnsureStrictProgram(program),
                            CreateModuleEnvironment(moduleKey),
                            new JsObject(),
                            moduleKey);
                        _moduleRegistry[moduleKey] = entry;
                    }
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }
                else
                {
                    entry = CreateModuleEntry(EnsureStrictProgram(program),
                        CreateModuleEnvironment(moduleKey),
                        new JsObject(),
                        string.Empty);
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }

                EnsureModuleInstantiated(entry);
                if (entry.IsAsync || entry.HasAsyncDependency)
                {
                    await EnsureModuleEvaluatedAsync(entry).ConfigureAwait(false);
                }
                else
                {
                    EnsureModuleEvaluated(entry);
                }
                result = entry.LastValue;
            }
            else
            {
                result = ExecuteProgram(program, GlobalEnvironment, combinedToken);
            }

            // Flush microtasks queued during synchronous execution before checking the event loop
            DrainMicrotasks();

            // Step 2: Check if any async work was scheduled (timers, promises, etc.)
            if (IsEventLoopDrained())
            {
                // Fast path: No async work pending, return immediately
                return result;
            }

            // Step 3: Async work is pending - start event loop lazily and drain it
            StartEventLoop();

            var configured = ExecutionTimeout;
            var enforceTimeout = configured.HasValue && configured.Value > TimeSpan.Zero &&
                                 configured.Value != Timeout.InfiniteTimeSpan;
            var timeout = enforceTimeout ? configured!.Value : Timeout.InfiniteTimeSpan;

            // Wait for event loop to drain with optional timeout
            using var drainCts = enforceTimeout
                ? CancellationTokenSource.CreateLinkedTokenSource(combinedToken)
                : null;
            drainCts?.CancelAfter(timeout);

            try
            {
                await DrainEventLoopAsync(drainCts?.Token ?? combinedToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (drainCts?.IsCancellationRequested == true
                                                      && !combinedToken.IsCancellationRequested)
            {
                // Timeout during drain
                CancelAllTimers();
                throw new TimeoutException(
                    $"JavaScript execution exceeded the configured timeout of {timeout}.");
            }

            return result;
        }
        finally
        {
            CancelAllTimers();
            await StopEventLoopAsync().ConfigureAwait(false);
            timeoutCts?.Dispose();
        }
    }

    private object? EvaluateInline(
        ParsedProgram program,
        CancellationToken cancellationToken,
        string? sourcePath = null,
        bool forceModule = false)
    {
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        try
        {
            var isModule = forceModule || HasModuleStatements(program.Typed);
            EnsureImportMetaAllowed(program.Typed, isModule);
            if (isModule)
            {
                string? moduleKey = null;
                ModuleEntry entry;
                if (!string.IsNullOrEmpty(sourcePath))
                {
                    moduleKey = NormalizeModulePath(sourcePath!, null);
                    if (!_moduleRegistry.TryGetValue(moduleKey, out entry))
                    {
                        entry = CreateModuleEntry(EnsureStrictProgram(program),
                            CreateModuleEnvironment(moduleKey),
                            new JsObject(),
                            moduleKey);
                        _moduleRegistry[moduleKey] = entry;
                    }
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }
                else
                {
                    entry = CreateModuleEntry(EnsureStrictProgram(program),
                        CreateModuleEnvironment(moduleKey),
                        new JsObject(),
                        string.Empty);
                    entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                        new HashSet<string>(StringComparer.Ordinal));
                }

                EnsureModuleInstantiated(entry);
                if (entry.IsAsync || entry.HasAsyncDependency)
                {
                    EnsureModuleEvaluatedAsync(entry).GetAwaiter().GetResult();
                }
                else
                {
                    EnsureModuleEvaluated(entry);
                }
                DrainMicrotasks();
                return entry.LastValue;
            }

            var scriptResult = ExecuteProgram(program, GlobalEnvironment, combinedToken);
            DrainMicrotasks();
            return scriptResult;
        }
        finally
        {
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

    internal static bool ProgramContainsImportMeta(ProgramNode program)
    {
        return StatementsContainImportMeta(program.Body);
    }

    private void EnsureImportMetaAllowed(ProgramNode program, bool isModule, EvaluationContext? context = null)
    {
        if (isModule || !ProgramContainsImportMeta(program))
        {
            return;
        }

        var syntaxError = StandardLibrary.CreateSyntaxError(
            "'import.meta' is only valid in module code.",
            context,
            RealmState);
        throw new ThrowSignal(syntaxError);
    }

    private static bool StatementsContainImportMeta(ImmutableArray<StatementNode> statements)
    {
        foreach (var statement in statements)
        {
            if (StatementContainsImportMeta(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementContainsImportMeta(StatementNode statement)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    return StatementsContainImportMeta(block.Statements);
                case VariableDeclaration variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        if (BindingContainsImportMeta(declarator.Target))
                        {
                            return true;
                        }

                        if (declarator.Initializer is { } initializer && ExpressionContainsImportMeta(initializer))
                        {
                            return true;
                        }
                    }

                    return false;
                case ExpressionStatement expressionStatement:
                    return ExpressionContainsImportMeta(expressionStatement.Expression);
                case ReturnStatement returnStatement:
                    return returnStatement.Expression is { } returnExpression && ExpressionContainsImportMeta(returnExpression);
                case ThrowStatement throwStatement:
                    return ExpressionContainsImportMeta(throwStatement.Expression);
                case IfStatement ifStatement:
                    return ExpressionContainsImportMeta(ifStatement.Condition) || StatementContainsImportMeta(ifStatement.Then) || (ifStatement.Else is { } elseBranch && StatementContainsImportMeta(elseBranch));
                case WhileStatement whileStatement:
                    return ExpressionContainsImportMeta(whileStatement.Condition) || StatementContainsImportMeta(whileStatement.Body);
                case DoWhileStatement doWhileStatement:
                    return StatementContainsImportMeta(doWhileStatement.Body) || ExpressionContainsImportMeta(doWhileStatement.Condition);
                case WithStatement withStatement:
                    return ExpressionContainsImportMeta(withStatement.Object) || StatementContainsImportMeta(withStatement.Body);
                case ForStatement forStatement:
                    if (forStatement.Initializer is { } forInitializer && StatementContainsImportMeta(forInitializer))
                    {
                        return true;
                    }

                    if (forStatement.Condition is { } condition && ExpressionContainsImportMeta(condition))
                    {
                        return true;
                    }

                    if (forStatement.Increment is { } increment && ExpressionContainsImportMeta(increment))
                    {
                        return true;
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    return BindingContainsImportMeta(forEachStatement.Target) || ExpressionContainsImportMeta(forEachStatement.Iterable) || StatementContainsImportMeta(forEachStatement.Body);
                case LabeledStatement labeledStatement:
                    statement = labeledStatement.Statement;
                    continue;
                case TryStatement tryStatement:
                    if (StatementContainsImportMeta(tryStatement.TryBlock))
                    {
                        return true;
                    }

                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (BindingContainsImportMeta(catchClause.Binding))
                        {
                            return true;
                        }

                        if (StatementContainsImportMeta(catchClause.Body))
                        {
                            return true;
                        }
                    }

                    if (tryStatement.Finally is { } finallyBlock && StatementContainsImportMeta(finallyBlock))
                    {
                        return true;
                    }

                    return false;
                case SwitchStatement switchStatement:
                    if (ExpressionContainsImportMeta(switchStatement.Discriminant))
                    {
                        return true;
                    }

                    foreach (var switchCase in switchStatement.Cases)
                    {
                        if (switchCase.Test is { } test && ExpressionContainsImportMeta(test))
                        {
                            return true;
                        }

                        if (StatementContainsImportMeta(switchCase.Body))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionDeclaration functionDeclaration:
                    return FunctionContainsImportMeta(functionDeclaration.Function);
                case ClassDeclaration classDeclaration:
                    return ClassContainsImportMeta(classDeclaration.Definition);
                case ModuleStatement moduleStatement:
                    return ModuleStatementContainsImportMeta(moduleStatement);
                default:
                    return false;
            }
        }
    }

    private static bool ModuleStatementContainsImportMeta(ModuleStatement moduleStatement)
    {
        switch (moduleStatement)
        {
            case ExportDefaultStatement { Value: ExportDefaultExpression { Expression: { } expression } }:
                return ExpressionContainsImportMeta(expression);
            case ExportDefaultStatement
            {
                Value: ExportDefaultDeclaration { Declaration: { } declaration }
            }:
                return StatementContainsImportMeta(declaration);
            case ExportDeclarationStatement { Declaration: { } declaration }:
                return StatementContainsImportMeta(declaration);
            default:
                return false;
        }
    }

    private static bool ClassContainsImportMeta(ClassDefinition definition)
    {
        if (definition.Extends is { } extends && ExpressionContainsImportMeta(extends))
        {
            return true;
        }

        if (FunctionContainsImportMeta(definition.Constructor))
        {
            return true;
        }

        foreach (var member in definition.Members)
        {
            if (member.IsComputed && member.ComputedName is { } computedName &&
                ExpressionContainsImportMeta(computedName))
            {
                return true;
            }

            if (FunctionContainsImportMeta(member.Function))
            {
                return true;
            }
        }

        foreach (var field in definition.Fields)
        {
            if (field.IsComputed && field.ComputedName is { } computedName &&
                ExpressionContainsImportMeta(computedName))
            {
                return true;
            }

            if (field.Initializer is { } initializer && ExpressionContainsImportMeta(initializer))
            {
                return true;
            }
        }

        foreach (var staticBlock in definition.StaticBlocks)
        {
            if (StatementsContainImportMeta(staticBlock.Body.Statements))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FunctionContainsImportMeta(FunctionExpression function)
    {
        foreach (var parameter in function.Parameters)
        {
            if (BindingContainsImportMeta(parameter.Pattern))
            {
                return true;
            }

            if (parameter.DefaultValue is { } defaultValue && ExpressionContainsImportMeta(defaultValue))
            {
                return true;
            }
        }

        return StatementsContainImportMeta(function.Body.Statements);
    }

    private static bool BindingContainsImportMeta(BindingTarget? target)
    {
        while (true)
        {
            switch (target)
            {
                case null:
                    return false;
                case IdentifierBinding:
                    return false;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (BindingContainsImportMeta(element.Target))
                        {
                            return true;
                        }

                        if (element.DefaultValue is { } defaultValue && ExpressionContainsImportMeta(defaultValue))
                        {
                            return true;
                        }
                    }

                    target = arrayBinding.RestElement;
                    continue;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        if (BindingContainsImportMeta(property.Target))
                        {
                            return true;
                        }

                        if (property.DefaultValue is { } defaultValue && ExpressionContainsImportMeta(defaultValue))
                        {
                            return true;
                        }

                        if (property.NameExpression is { } nameExpression && ExpressionContainsImportMeta(nameExpression))
                        {
                            return true;
                        }
                    }

                    target = objectBinding.RestElement;
                    continue;
                case AssignmentTargetBinding assignmentTarget:
                    return ExpressionContainsImportMeta(assignmentTarget.Expression);
                default:
                    return false;
            }
        }
    }

    private static bool ExpressionContainsImportMeta(ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ImportMetaExpression:
                    return true;
                case LiteralExpression:
                case IdentifierExpression:
                case PrivateIdentifierExpression:
                case ThisExpression:
                case SuperExpression:
                case NewTargetExpression:
                    return false;
                case BinaryExpression binary:
                    return ExpressionContainsImportMeta(binary.Left) || ExpressionContainsImportMeta(binary.Right);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case ConditionalExpression conditional:
                    return ExpressionContainsImportMeta(conditional.Test) || ExpressionContainsImportMeta(conditional.Consequent) || ExpressionContainsImportMeta(conditional.Alternate);
                case FunctionExpression function:
                    return FunctionContainsImportMeta(function);
                case CallExpression call:
                    if (ExpressionContainsImportMeta(call.Callee))
                    {
                        return true;
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (ExpressionContainsImportMeta(argument.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case NewExpression newExpression:
                    if (ExpressionContainsImportMeta(newExpression.Constructor))
                    {
                        return true;
                    }

                    foreach (var argument in newExpression.Arguments)
                    {
                        if (ExpressionContainsImportMeta(argument.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case MemberExpression member:
                    return ExpressionContainsImportMeta(member.Target) || ExpressionContainsImportMeta(member.Property);
                case AssignmentExpression assignment:
                    expression = assignment.Value;
                    continue;
                case PropertyAssignmentExpression propertyAssignment:
                    return ExpressionContainsImportMeta(propertyAssignment.Target) || ExpressionContainsImportMeta(propertyAssignment.Property) || ExpressionContainsImportMeta(propertyAssignment.Value);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsImportMeta(indexAssignment.Target) || ExpressionContainsImportMeta(indexAssignment.Index) || ExpressionContainsImportMeta(indexAssignment.Value);
                case SequenceExpression sequence:
                    return ExpressionContainsImportMeta(sequence.Left) || ExpressionContainsImportMeta(sequence.Right);
                case DestructuringAssignmentExpression destructuringAssignment:
                    return BindingContainsImportMeta(destructuringAssignment.Target) || ExpressionContainsImportMeta(destructuringAssignment.Value);
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is { } elementExpression && ExpressionContainsImportMeta(elementExpression))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member.IsComputed && member.Key is ExpressionNode computedKey && ExpressionContainsImportMeta(computedKey))
                        {
                            return true;
                        }

                        if (member.Value is { } value && ExpressionContainsImportMeta(value))
                        {
                            return true;
                        }

                        if (member.Function is { } functionMember && FunctionContainsImportMeta(functionMember))
                        {
                            return true;
                        }
                    }

                    return false;
                case ClassExpression classExpression:
                    return ClassContainsImportMeta(classExpression.Definition);
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is { } partExpression && ExpressionContainsImportMeta(partExpression))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression taggedTemplate:
                    if (ExpressionContainsImportMeta(taggedTemplate.Tag) || ExpressionContainsImportMeta(taggedTemplate.StringsArray) || ExpressionContainsImportMeta(taggedTemplate.RawStringsArray))
                    {
                        return true;
                    }

                    foreach (var expr in taggedTemplate.Expressions)
                    {
                        if (ExpressionContainsImportMeta(expr))
                        {
                            return true;
                        }
                    }

                    return false;
                case YieldExpression yieldExpression:
                    return yieldExpression.Expression is { } yieldValue && ExpressionContainsImportMeta(yieldValue);
                case AwaitExpression awaitExpression:
                    expression = awaitExpression.Expression;
                    continue;
                default:
                    return false;
            }
        }
    }

    private ProgramNode EnsureStrictProgram(ParsedProgram program)
    {
        return program.Typed.IsStrict
            ? program.Typed
            : new ProgramNode(program.Typed.Source, program.Typed.Body, true);
    }

    private ModuleEntry CreateModuleEntry(ProgramNode program, JsEnvironment environment, JsObject exports,
        string modulePath)
    {
        var entry = new ModuleEntry(modulePath ?? string.Empty, program, environment, exports)
        {
            IsAsync = ContainsTopLevelAwait(program)
        };
        environment.IsAsyncModule = entry.IsAsync;
        EnsureModuleImportMeta(entry);
        return entry;
    }

    private IJsPropertyAccessor? ResolvePromisePrototypeInternal()
    {
        if (RealmState?.PromisePrototype is IJsPropertyAccessor realmPromisePrototype)
        {
            return realmPromisePrototype;
        }

        if (RealmState?.PromiseConstructor is IJsPropertyAccessor realmPromiseCtor &&
            realmPromiseCtor.TryGetProperty("prototype", out var realmPrototype) &&
            realmPrototype is IJsPropertyAccessor promisePrototypeAccessorFromCtor)
        {
            return promisePrototypeAccessorFromCtor;
        }

        if (GlobalObject.TryGetProperty("Promise", out var promiseCtor) &&
            promiseCtor is IJsPropertyAccessor promiseCtorAccessor &&
            promiseCtorAccessor.TryGetProperty("prototype", out var promiseProto) &&
            promiseProto is IJsPropertyAccessor promisePrototypeAccessor)
        {
            return promisePrototypeAccessor;
        }

        return null;
    }

    internal JsPromise CreateRealmPromise()
    {
        var prototype = ResolvePromisePrototypeInternal();
        var promise = prototype is null
            ? StandardLibrary.CreatePromise(RealmState)
            : StandardLibrary.CreatePromise(RealmState, prototype as IJsObjectLike);
        return promise;
    }

    private static bool ContainsTopLevelAwait(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            if (Ast.ShapeAnalyzer.AstShapeAnalyzer.StatementContainsAwait(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool ModuleHasAsyncDependency(
        ProgramNode program,
        string? modulePath,
        HashSet<string> visited)
    {
        if (!string.IsNullOrEmpty(modulePath) && !visited.Add(modulePath))
        {
            return false;
        }

        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case ImportStatement importStatement:
                    var importPhase = importStatement.IsDeferred ? ImportPhase.Defer : ImportPhase.Module;
                    var imported = LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null,
                        importStatement.Attributes, computeAsyncDependencies: false);
                    if (imported.IsAsync ||
                        imported.HasAsyncDependency ||
                        ModuleHasAsyncDependency(imported.Program, imported.Path, visited))
                    {
                        return true;
                    }

                    break;
                case ExportNamedStatement { FromModule: { } fromModule }:
                {
                    var sourceEntry = LoadModuleForInstantiation(fromModule, modulePath, ImportPhase.Module,
                        computeAsyncDependencies: false);
                    if (sourceEntry.IsAsync ||
                        sourceEntry.HasAsyncDependency ||
                        ModuleHasAsyncDependency(sourceEntry.Program, sourceEntry.Path, visited))
                    {
                        return true;
                    }

                    break;
                }
                case ExportAllStatement exportAll:
                {
                    var sourceEntry = LoadModuleForInstantiation(exportAll.ModulePath, modulePath, ImportPhase.Module,
                        computeAsyncDependencies: false);
                    if (sourceEntry.IsAsync ||
                        sourceEntry.HasAsyncDependency ||
                        ModuleHasAsyncDependency(sourceEntry.Program, sourceEntry.Path, visited))
                    {
                        return true;
                    }

                    break;
                }
                case ExportNamespaceAsStatement exportNamespace:
                {
                    var namespaceEntry = LoadModuleForInstantiation(exportNamespace.ModulePath, modulePath,
                        ImportPhase.Module, computeAsyncDependencies: false);
                    if (namespaceEntry.IsAsync ||
                        namespaceEntry.HasAsyncDependency ||
                        ModuleHasAsyncDependency(namespaceEntry.Program, namespaceEntry.Path, visited))
                    {
                        return true;
                    }

                    break;
                }
            }
        }

        return false;
    }

    private JsObject EnsureModuleImportMeta(ModuleEntry entry)
    {
        if (entry.ImportMeta is { } existing)
        {
            if (!entry.Environment.TryGet(Symbol.ImportMeta, out _))
            {
                entry.Environment.Define(Symbol.ImportMeta, existing, isConst: true, isLexical: true,
                    blocksFunctionScopeOverride: false);
            }

            return existing;
        }

        var importMeta = new JsObject { RealmState = RealmState };
        importMeta.SetPrototype(null);
        importMeta.DefineProperty("url",
            new PropertyDescriptor
            {
                Value = entry.Path ?? string.Empty,
                Writable = true,
                Enumerable = true,
                Configurable = true,
                HasValue = true,
                HasWritable = true,
                HasEnumerable = true,
                HasConfigurable = true
            });

        entry.Environment.Define(Symbol.ImportMeta, importMeta, isConst: true, isLexical: true,
            blocksFunctionScopeOverride: false);
        entry.ImportMeta = importMeta;
        return importMeta;
    }

    /// <summary>
    /// Creates a module environment with the correct `this` binding (undefined per ES spec).
    /// </summary>
    private JsEnvironment CreateModuleEnvironment(string? modulePath = null)
    {
        var moduleEnv = new JsEnvironment(GlobalEnvironment, true, true);
        // Per ECMAScript spec, `this` in module scope is undefined
        moduleEnv.Define(Symbol.This, Symbol.Undefined);
        moduleEnv.ModulePath = modulePath;
        return moduleEnv;
    }

    private void EnsureModuleInstantiated(
        ModuleEntry entry,
        ImportPhase phase = ImportPhase.Module,
        HashSet<string>? exportStarSet = null)
    {
        EnsureModuleImportMeta(entry);

        if (entry.Instantiated || entry.Instantiating)
        {
            return;
        }

        exportStarSet ??= new HashSet<string>(StringComparer.Ordinal);
        entry.Instantiating = true;
        PredeclareExportNames(entry.Program, entry.Environment, entry.Exports, entry.Path, phase, exportStarSet);
        entry.Instantiated = true;
        entry.Instantiating = false;
    }

    private void EnsureModuleEvaluated(ModuleEntry entry)
    {
        EnsureModuleEvaluatedAsync(entry).GetAwaiter().GetResult();
    }

    private Task<object?> EnsureModuleEvaluatedAsync(ModuleEntry entry, bool waitForAsync = true)
    {
        if (entry.Evaluated)
        {
            return entry.EvaluationTask ?? Task.FromResult(entry.LastValue);
        }

        EnsureModuleInstantiated(entry);

        var requiresAsyncEvaluation = entry.IsAsync || entry.HasAsyncDependency;

        if (!requiresAsyncEvaluation)
        {
            if (entry.Evaluating)
            {
                return entry.EvaluationTask ?? Task.FromResult(entry.LastValue);
            }

            entry.Evaluating = true;
            try
            {
                entry.LastValue = ExecuteModuleBody(entry.Program, entry.Environment, entry.Exports, entry.Path);
                entry.Evaluated = true;
                return entry.EvaluationTask ?? Task.FromResult(entry.LastValue);
            }
            finally
            {
                entry.Evaluating = false;
            }
        }

        if (entry.EvaluationTask is null)
        {
            entry.Evaluating = true;
            entry.EvaluationTask = entry.IsAsync
                ? EvaluateModuleBodyWithTopLevelAwait(entry, waitForAsync)
                : EvaluateModuleBodyWithAsyncDependencies(entry, waitForAsync);
        }

        if (!waitForAsync)
        {
            return entry.EvaluationTask;
        }

        return entry.EvaluationTask;
    }

    /// <summary>
    ///     Registers a value in the global scope.
    /// </summary>
    private void SetGlobal(string name, object? value, bool isGlobalConstant = false, bool registerBinding = false)
    {
        var symbol = Symbol.Intern(name);
        if (registerBinding)
        {
            // Only register a binding when explicitly requested (e.g., host-added globals).
            // Built-ins defined during engine initialization are exposed as global object
            // properties so they don't block later lexical declarations (let/const).
            GlobalEnvironment.Define(symbol, value, isGlobalConstant: isGlobalConstant, isLexical: false);
        }

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
                hostFunction.RealmState = RealmState;
            }

            if (RealmState.FunctionPrototype is not null && hostFunction.Properties.Prototype is null)
            {
                hostFunction.Properties.SetPrototype(RealmState.FunctionPrototype);
            }
        }
        else if (value is JsObject jsObject && jsObject.RealmState is null)
        {
            jsObject.RealmState = RealmState;
        }

        GlobalObject.SetProperty(name, value);
    }

    /// <summary>
    ///     Registers a value in the global scope (public facing).
    /// </summary>
    public void SetGlobalValue(string name, object? value)
    {
        SetGlobal(name, value, registerBinding: true);
    }

    /// <summary>
    ///     Registers a host function that can be invoked from interpreted code.
    /// </summary>
    public void SetGlobalFunction(string name, Func<IReadOnlyList<object?>, object?> handler)
    {
        SetGlobal(name, new HostFunction(handler) { Realm = GlobalObject }, registerBinding: true);
    }

    /// <summary>
    ///     Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, Func<object?, IReadOnlyList<object?>, object?> handler)
    {
        GlobalEnvironment.Define(Symbol.Intern(name), new HostFunction(handler) { Realm = GlobalObject });
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
        // Evaluate the code (uses lazy event loop internally)
        var result = await Evaluate(source).ConfigureAwait(false);

        // If there's still pending async work after Evaluate returns,
        // wait for it to complete (e.g., timers that were scheduled)
        if (!IsEventLoopDrained())
        {
            await DrainEventLoopAsync(CancellationToken.None).ConfigureAwait(false);
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
        var capturedActivity = Activity.Current;

        Interlocked.Increment(ref _pendingTaskCount);
        queue.Writer.TryWrite(async () =>
        {
            var previousActivity = Activity.Current;
            var activityChanged = !ReferenceEquals(previousActivity, capturedActivity);
            if (activityChanged)
            {
                Activity.Current = capturedActivity;
            }

            try
            {
                await task().ConfigureAwait(false);
            }
            finally
            {
                if (activityChanged)
                {
                    Activity.Current = previousActivity;
                }
            }
        });
    }

    /// <summary>
    ///     Processes all events in the event queue until it's empty.
    ///     Each event is executed and any new events scheduled during execution
    ///     will also be processed.
    ///     Exceptions from individual tasks are caught and logged to prevent the event loop from stopping.
    /// </summary>
    private async Task ProcessEventQueue(Channel<Func<Task>> queue)
    {
        _eventLoopThreadId = Environment.CurrentManagedThreadId;
        try
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
                    DrainMicrotasks();
                    // Decrement the pending task count after processing
                    Interlocked.Decrement(ref _pendingTaskCount);
                    TrySignalDrainComplete();
                }
            }
        }
        finally
        {
            _eventLoopThreadId = null;
        }
    }

    /// <summary>
    ///     Queues a microtask to be executed synchronously.
    ///     This is used for promise reactions during top-level await.
    /// </summary>
    internal void QueueMicrotask(Action task)
    {
        lock (_microtaskLock)
        {
            _microtaskQueue.Enqueue(task);
        }
    }

    internal List<Action> DetachMicrotasks()
    {
        List<Action> preserved;
        lock (_microtaskLock)
        {
            preserved = new List<Action>(_microtaskQueue.Count);
            while (_microtaskQueue.Count > 0)
            {
                preserved.Add(_microtaskQueue.Dequeue());
            }
        }

        return preserved;
    }

    internal void PrependMicrotasks(List<Action>? tasks)
    {
        if (tasks is null || tasks.Count == 0)
        {
            return;
        }

        lock (_microtaskLock)
        {
            if (_microtaskQueue.Count == 0)
            {
                foreach (var task in tasks)
                {
                    _microtaskQueue.Enqueue(task);
                }

                return;
            }

            var existing = new Queue<Action>(_microtaskQueue);
            _microtaskQueue.Clear();
            foreach (var task in tasks)
            {
                _microtaskQueue.Enqueue(task);
            }

            while (existing.Count > 0)
            {
                _microtaskQueue.Enqueue(existing.Dequeue());
            }
        }
    }

    /// <summary>
    ///     Drains all pending microtasks synchronously.
    ///     Returns when no more microtasks are pending.
    /// </summary>
    internal void DrainMicrotasks(int skipExisting = 0)
    {
        if (skipExisting < 0)
        {
            skipExisting = 0;
        }

        lock (_microtaskLock)
        {
            if (_isDrainingMicrotasks)
            {
                return;
            }

            _isDrainingMicrotasks = true;
        }

        try
        {
            List<Action>? deferred = null;
            while (true)
            {
                Action? task;
                lock (_microtaskLock)
                {
                    if (skipExisting > 0 && _microtaskQueue.Count > 0)
                    {
                        deferred ??= new List<Action>();
                        deferred.Add(_microtaskQueue.Dequeue());
                        skipExisting--;
                        continue;
                    }

                    if (_microtaskQueue.Count == 0)
                    {
                        break;
                    }

                    task = _microtaskQueue.Dequeue();
                }

                try
                {
                    task();
                }
                catch (Exception ex)
                {
                    // Log but don't propagate - microtask exceptions shouldn't kill the drain
                    Console.Error.WriteLine($"[DrainMicrotasks] Exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (deferred is { Count: > 0 })
            {
                foreach (var deferredTask in deferred)
                {
                    _microtaskQueue.Enqueue(deferredTask);
                }
            }
        }
        finally
        {
            lock (_microtaskLock)
            {
                _isDrainingMicrotasks = false;
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

        // For zero delay, schedule directly to event queue without ThreadPool overhead
        if (delay <= 0)
        {
            ScheduleTask(() =>
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    callback.Invoke([], null);
                }
                _timers.Remove(timerId);
                return Task.CompletedTask;
            });
            return (double)timerId;
        }

        // For non-zero delay, use ThreadPool to wait then schedule
        Interlocked.Increment(ref _activeTimerCount);

        _ = Task.Run(async () =>
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
                Interlocked.Decrement(ref _activeTimerCount);
                TrySignalDrainComplete();
            }
        }, cts.Token);

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

        // Increment active timer count before starting the timer
        Interlocked.Increment(ref _activeTimerCount);

        _ = Task.Run(async () =>
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
                Interlocked.Decrement(ref _activeTimerCount);
                TrySignalDrainComplete();
            }
        }, cts.Token);

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

    private enum ImportPhase
    {
        Module,
        Defer,
        Source
    }

    /// <summary>
    ///     Implements dynamic import() - loads a module and returns a Promise that resolves to the module's exports.
    /// </summary>
    private object? DynamicImport(IReadOnlyList<object?> args)
    {
        return DynamicImport(args, null, ImportPhase.Module, null);
    }

    private object? DynamicImport(
        IReadOnlyList<object?> args,
        EvaluationContext? context,
        ImportPhase phase,
        HostFunction? callee)
    {
        // Create a promise that will resolve with the module exports
        var promise = new JsPromise(this);
        var promiseObj = promise.JsObject;

        var promisePrototype = ResolvePromisePrototype();
        if (promisePrototype is not null)
        {
            promiseObj.SetPrototype(promisePrototype);
        }

        // Add promise instance methods (then, catch, finally)
        StandardLibrary.AddPromiseInstanceMethods(promiseObj, promise, this);

        // Schedule loading the module asynchronously using ScheduleTask
        // to properly track pending tasks for the event loop
        ScheduleTask(async () =>
        {
            try
            {
                if (args.Count == 0)
                {
                    var typeError = StandardLibrary.CreateTypeError(
                        "import() requires a module specifier",
                        context,
                        RealmState);
                    promise.Reject(typeError);
                    return;
                }

                object? specifierStringObj;
                try
                {
                    var specifier = args.GetArgument(0);
                    specifierStringObj = JsOps.ToJsString(specifier, context);
                }
                catch (ThrowSignal signal)
                {
                    promise.Reject(signal.ThrownValue);
                    return;
                }

                if (context?.IsThrow == true)
                {
                    promise.Reject(context.FlowValue);
                    return;
                }

                var specifierString = specifierStringObj?.ToString() ?? string.Empty;
                if (phase == ImportPhase.Source)
                {
                    // Source phase imports are not supported by this host; reject with SyntaxError.
                    var syntaxError = StandardLibrary.CreateSyntaxError(
                        "Source phase imports are not supported",
                        context,
                        RealmState);
                    promise.Reject(syntaxError);
                    return;
                }

                try
                {
                    // Load the module synchronously (it's cached if already loaded)
                    var referrerPath = callee?.CallingJsEnvironment is JsEnvironment env ? env.ModulePath : _currentModulePath;
                    var moduleEntry = LoadModule(specifierString, referrerPath, phase);
                    if (moduleEntry.IsAsync || moduleEntry.HasAsyncDependency)
                    {
                        await EnsureModuleEvaluatedAsync(moduleEntry).ConfigureAwait(false);
                    }
                    else
                    {
                        EnsureModuleEvaluated(moduleEntry);
                    }
                    var namespaceObject = GetModuleNamespace(moduleEntry, phase);
                    promise.Resolve(namespaceObject);
                }
                catch (Exception ex)
                {
                    var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                    promise.Reject(error);
                }
            }
            catch (Exception ex)
            {
                var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                promise.Reject(error);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        });

        return promiseObj;

        IJsPropertyAccessor? ResolvePromisePrototype() => ResolvePromisePrototypeInternal();
    }

    /// <summary>
    ///     Sets a custom module loader function that will be called to load module source code.
    ///     The function receives the module path and should return the module source code.
    ///     If not set, the engine will use File.ReadAllText to load modules from the file system.
    /// </summary>
    public void SetModuleLoader(Func<string, string> loader)
    {
        _moduleLoader = (path, _) => loader(path);
    }

    public void SetModuleLoader(Func<string, string?, string> loader)
    {
        _moduleLoader = loader;
    }

    /// <summary>
    ///     Loads and evaluates a module, returning its exports object.
    ///     If the module has already been loaded, returns the cached exports.
    /// </summary>
    private ModuleEntry LoadModule(
        string modulePath,
        string? referrerPath = null,
        ImportPhase phase = ImportPhase.Module,
        HashSet<string>? exportStarSet = null,
        ImmutableArray<ImportAttribute> attributes = default)
    {
        var resolvedPath = NormalizeModulePath(modulePath, referrerPath, _moduleLoader is not null);
        var isJsonModule = IsJsonModule(attributes);

        // Check if module is already loaded
        if (_moduleRegistry.TryGetValue(resolvedPath, out var cachedEntry))
        {
            EnsureModuleImportMeta(cachedEntry);
            var cachedHasAsyncDependency = ModuleHasAsyncDependency(cachedEntry.Program, cachedEntry.Path,
                new HashSet<string>(StringComparer.Ordinal));
            cachedEntry.HasAsyncDependency = cachedHasAsyncDependency;
            cachedEntry.Environment.IsAsyncModule = cachedEntry.IsAsync;
            EnsureModuleInstantiated(cachedEntry, phase, exportStarSet);
            if (phase == ImportPhase.Module)
            {
                if (cachedEntry.IsAsync || cachedEntry.HasAsyncDependency)
                {
                    EnsureModuleEvaluatedAsync(cachedEntry, waitForAsync: false);
                }
                else
                {
                    EnsureModuleEvaluated(cachedEntry);
                }
            }

            return cachedEntry;
        }

        // Load module source
        string source;
        if (_moduleLoader != null)
        {
            source = _moduleLoader(resolvedPath, referrerPath);
        }
        else
            // Default: load from file system
        {
            source = File.ReadAllText(resolvedPath);
        }

        ModuleEntry entry;
        if (isJsonModule)
        {
            // Create a JSON module - parse JSON and create a synthetic module with the value as default export
            entry = CreateJsonModule(source, resolvedPath);
        }
        else
        {
            // Parse the module
            var program = ParseForExecution(source, true, true);

            // Create a module exports object
            var exports = new JsObject();
            var moduleEnv = CreateModuleEnvironment(resolvedPath);
            entry = CreateModuleEntry(EnsureStrictProgram(program), moduleEnv, exports, resolvedPath);
        }

        _moduleRegistry[resolvedPath] = entry;

        var computedHasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
            new HashSet<string>(StringComparer.Ordinal));
        entry.HasAsyncDependency = computedHasAsyncDependency;
        entry.Environment.IsAsyncModule = entry.IsAsync;

        EnsureModuleInstantiated(entry, phase, exportStarSet);
        if (phase == ImportPhase.Module)
        {
            if (entry.IsAsync || entry.HasAsyncDependency)
            {
                EnsureModuleEvaluatedAsync(entry, waitForAsync: false);
            }
            else
            {
                EnsureModuleEvaluated(entry);
            }
        }

        return entry;
    }

    private static bool IsJsonModule(ImmutableArray<ImportAttribute> attributes)
    {
        if (attributes.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var attr in attributes)
        {
            if (attr.Key == "type" && attr.Value == "json")
            {
                return true;
            }
        }

        return false;
    }

    private ModuleEntry CreateJsonModule(string source, string resolvedPath)
    {
        // Parse JSON using JSON.parse
        var jsonValue = StandardLibrary.ParseJsonWithReviver(source, RealmState, null, null);

        // Create a synthetic module with the JSON value as default export
        var exports = new JsObject();
        exports["default"] = jsonValue;

        var moduleEnv = CreateModuleEnvironment(resolvedPath);

        // IMPORTANT: Define the "default" binding in the module environment
        // This is needed for import binding resolution to work correctly
        var defaultSymbol = Symbol.Intern("default");
        moduleEnv.Define(defaultSymbol, jsonValue, isConst: true, isLexical: true, blocksFunctionScopeOverride: false);

        // Create a minimal parsed program (empty) - JSON modules don't have executable code
        var emptyStatements = ImmutableArray<StatementNode>.Empty;
        var emptyProgram = new ProgramNode(null, emptyStatements, true);

        var entry = CreateModuleEntry(emptyProgram, moduleEnv, exports, resolvedPath);
        entry.Instantiated = true;
        entry.Evaluated = true;

        return entry;
    }

    /// <summary>
    /// Loads a module entry for instantiation only (does not evaluate).
    /// This is used during import binding hoisting to set up bindings without
    /// triggering evaluation of imported modules.
    /// </summary>
    private ModuleEntry LoadModuleForInstantiation(
        string modulePath,
        string? referrerPath,
        ImportPhase phase,
        HashSet<string>? exportStarSet = null,
        ImmutableArray<ImportAttribute> attributes = default,
        bool computeAsyncDependencies = true)
    {
        var resolvedPath = NormalizeModulePath(modulePath, referrerPath, _moduleLoader is not null);
        var isJsonModule = IsJsonModule(attributes);

        // Check if module is already loaded
        if (_moduleRegistry.TryGetValue(resolvedPath, out var cachedEntry))
        {
            EnsureModuleImportMeta(cachedEntry);
            if (computeAsyncDependencies)
            {
                var hasAsyncDependency = ModuleHasAsyncDependency(cachedEntry.Program, cachedEntry.Path,
                    new HashSet<string>(StringComparer.Ordinal));
                cachedEntry.HasAsyncDependency = hasAsyncDependency;
                cachedEntry.Environment.IsAsyncModule = cachedEntry.IsAsync;
            }
            // Only instantiate, don't evaluate
            EnsureModuleInstantiated(cachedEntry, phase, exportStarSet);
            return cachedEntry;
        }

        // Load module source
        string source;
        if (_moduleLoader != null)
        {
            source = _moduleLoader(resolvedPath, referrerPath);
        }
        else
        {
            source = File.ReadAllText(resolvedPath);
        }

        ModuleEntry entry;
        if (isJsonModule)
        {
            // Create a JSON module - parse JSON and create a synthetic module with the value as default export
            entry = CreateJsonModule(source, resolvedPath);
        }
        else
        {
            // Parse the module
            var program = ParseForExecution(source, true, true);

            // Create a module exports object
            var exports = new JsObject();
            var moduleEnv = CreateModuleEnvironment(resolvedPath);
            entry = CreateModuleEntry(EnsureStrictProgram(program), moduleEnv, exports, resolvedPath);
        }

        _moduleRegistry[resolvedPath] = entry;
        if (computeAsyncDependencies)
        {
            var hasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
                new HashSet<string>(StringComparer.Ordinal));
            entry.HasAsyncDependency = hasAsyncDependency;
            entry.Environment.IsAsyncModule = entry.IsAsync;
        }

        // Only instantiate, don't evaluate
        EnsureModuleInstantiated(entry, phase, exportStarSet);

        return entry;
    }

    private static string NormalizeModulePath(string modulePath, string? referrerPath, bool preferRelative = false)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new Exception("Module path cannot be empty.");
        }

        var specifier = modulePath.Replace('\\', '/');
        var referrer = referrerPath?.Replace('\\', '/');

        if (specifier.StartsWith("./", StringComparison.Ordinal) ||
            specifier.StartsWith("../", StringComparison.Ordinal))
        {
            var lastSlash = referrer?.LastIndexOf('/') ?? -1;
            var baseDir = lastSlash >= 0 ? referrer![..lastSlash] : string.Empty;

            if (!preferRelative && !string.IsNullOrEmpty(referrer) && Path.IsPathRooted(referrer))
            {
                var rootedBase = Path.GetDirectoryName(referrer) ?? string.Empty;
                var combined = Path.GetFullPath(Path.Combine(rootedBase, specifier));
                return combined.Replace('\\', '/');
            }

            return NormalizeRelativeModulePath(baseDir, specifier);
        }

        if (Path.IsPathRooted(specifier))
        {
            return Path.GetFullPath(specifier).Replace('\\', '/');
        }

        if (preferRelative && !string.IsNullOrEmpty(referrer))
        {
            var lastSlash = referrer.LastIndexOf('/');
            var baseDir = lastSlash >= 0 ? referrer[..lastSlash] : string.Empty;
            if (!string.IsNullOrEmpty(baseDir))
            {
                return NormalizeRelativeModulePath(baseDir, "./" + specifier);
            }
        }

        return specifier;
    }

    private static string NormalizeRelativeModulePath(string baseDir, string specifier)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(baseDir))
        {
            parts.AddRange(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var segment in specifier.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    private ModuleNamespace GetModuleNamespace(ModuleEntry entry, ImportPhase phase = ImportPhase.Module)
    {
        ModuleNamespace? cached = phase switch
        {
            ImportPhase.Defer => entry.DeferredNamespace,
            _ when _moduleNamespaces.TryGetValue(entry.Exports, out var eager) => eager,
            _ => entry.Namespace
        };

        if (cached is not null)
        {
            return cached;
        }

        EnsureModuleInstantiated(entry, phase);
        var exportedNames = GetExportedNames(entry, new HashSet<string>(StringComparer.Ordinal));
        var resolvedNames = new List<string>();
        foreach (var name in exportedNames)
        {
            var resolution = ResolveExport(entry, name, phase, new HashSet<(ModuleEntry, string)>());
            if (resolution.Kind == ExportResolutionKind.Resolved &&
                !name.StartsWith("__getter__", StringComparison.Ordinal) &&
                !name.StartsWith("__setter__", StringComparison.Ordinal) &&
                !name.StartsWith("@@symbol:", StringComparison.Ordinal))
            {
                resolvedNames.Add(name);
            }
        }

        var exportNames = resolvedNames
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        object? Lookup(string name)
        {
            if (!entry.Exports.TryGetValue(name, out var value))
            {
                return Symbol.Undefined;
            }

            if (value is LiveExportBinding liveBinding)
            {
                value = liveBinding.GetValue();
            }

            return value;
        }

        void EnsureEvaluated()
        {
            EnsureModuleEvaluated(entry);
        }

        var ns = new ModuleNamespace(exportNames, Lookup, RealmState, UninitializedExportMarker,
            phase == ImportPhase.Defer, phase == ImportPhase.Defer ? EnsureEvaluated : null);

        if (phase == ImportPhase.Defer)
        {
            entry.DeferredNamespace = ns;
        }
        else
        {
            entry.Namespace = ns;
            _moduleNamespaces[entry.Exports] = ns;
        }

        return ns;
    }

    private void PredeclareExportNames(
        ProgramNode program,
        JsEnvironment moduleEnv,
        JsObject exports,
        string? modulePath,
        ImportPhase phase,
        HashSet<string> exportStarSet)
    {
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case ExportDefaultStatement exportDefaultStmt:
                    if (moduleEnv.IsAsyncModule)
                    {
                        var promise = CreateRealmPromise();
                        exports["default"] = promise.JsObject;
                        moduleEnv.DefineExportPromiseBinding(Symbol.Intern("*default*"), promise, isLexical: false,
                            isConst: false);
                    }
                    else
                    {
                        exports["default"] = UninitializedExportMarker;
                    }
                    // For hoistable anonymous function declarations, binding is created during HoistFunctionDeclarations
                    // For all other default exports (classes, expressions), we need to create the *default* binding here in TDZ
                    // Note: `export default function() {}` is hoistable (flag set), but `export default (function() {})` is not
                    if (exportDefaultStmt.Value is ExportDefaultExpression { Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true } })
                    {
                        // Will be handled by HoistFunctionDeclarations
                    }
                    else if (exportDefaultStmt.Value is ExportDefaultDeclaration { Declaration: FunctionDeclaration })
                    {
                        // Named function declarations are hoisted with their name, not *default*
                    }
                    else
                    {
                        // All other default exports: create *default* binding
                        // This includes: anonymous classes, expression exports, non-hoistable function expressions
                        // Use isLexical: false so we can initialize it later via Assign without TDZ errors
                        // The binding is initialized during EvaluateExportDefault
                        var defaultSymbol = Symbol.Intern("*default*");
                        if (!moduleEnv.HasBinding(defaultSymbol))
                        {
                            if (moduleEnv.IsAsyncModule && exports.TryGetValue("default", out var defaultExport) &&
                                defaultExport is JsObject defaultPromise &&
                                JsPromise.TryGetInternalPromise(defaultPromise, out var promise))
                            {
                                moduleEnv.DefineExportPromiseBinding(defaultSymbol, promise, isLexical: false, isConst: false);
                            }
                            else
                            {
                                moduleEnv.Define(defaultSymbol, JsEnvironment.Uninitialized, isLexical: false, blocksFunctionScopeOverride: false);
                            }
                        }
                    }
                    break;
                case ExportDeclarationStatement exportDeclaration:
                    if (exportDeclaration.Declaration is VariableDeclaration variableDeclaration)
                    {
                        foreach (var declarator in variableDeclaration.Declarators)
                        {
                            if (declarator.Target is not IdentifierBinding identifier)
                            {
                                continue;
                            }

                            var symbol = identifier.Name;
                            var isVar = variableDeclaration.Kind == VariableKind.Var;
                            var exportInitValue = isVar ? Symbol.Undefined : UninitializedExportMarker;
                            var envInitValue = isVar ? (object?)Symbol.Undefined : JsEnvironment.Uninitialized;

                            if (moduleEnv.IsAsyncModule)
                            {
                                var promise = CreateRealmPromise();
                                exports[symbol.Name] = promise.JsObject;
                                moduleEnv.DefineExportPromiseBinding(symbol, promise, isLexical: !isVar, isConst: variableDeclaration.Kind == VariableKind.Const);
                            }
                            else
                            {
                                exports[symbol.Name] = exportInitValue;
                                moduleEnv.Define(symbol, envInitValue,
                                    isLexical: !isVar,
                                    blocksFunctionScopeOverride: false);
                            }
                        }

                        break;
                    }

                    foreach (var symbol in GetDeclaredSymbols(exportDeclaration.Declaration))
                    {
                        if (moduleEnv.IsAsyncModule)
                        {
                            var promise = CreateRealmPromise();
                            exports[symbol.Name] = promise.JsObject;
                            moduleEnv.DefineExportPromiseBinding(symbol, promise, isLexical: true, isConst: true);
                        }
                        else
                        {
                            exports[symbol.Name] = UninitializedExportMarker;
                            moduleEnv.Define(symbol, JsEnvironment.Uninitialized, isLexical: true,
                                blocksFunctionScopeOverride: false);
                        }
                    }

                    break;
                case ExportNamedStatement exportNamed:
                    foreach (var specifier in exportNamed.Specifiers)
                    {
                        if (moduleEnv.IsAsyncModule)
                        {
                            var promise = CreateRealmPromise();
                            exports[specifier.Exported.Name] = promise.JsObject;
                            moduleEnv.DefineExportPromiseBinding(specifier.Local, promise, isLexical: true, isConst: true);
                        }
                        else
                        {
                            exports[specifier.Exported.Name] = UninitializedExportMarker;
                        }
                    }

                    break;
                case ExportAllStatement exportAll:
                    var sourceEntry = LoadModuleForInstantiation(exportAll.ModulePath, modulePath, phase, exportStarSet);
                    if (exportStarSet.Contains(sourceEntry.Path))
                    {
                        break;
                    }

                    exportStarSet.Add(sourceEntry.Path);
                    EnsureModuleInstantiated(sourceEntry, phase, exportStarSet);
                    var exportedNames = GetExportedNames(sourceEntry, exportStarSet);
                    foreach (var name in exportedNames)
                    {
                        if (string.Equals(name, "default", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var resolution =
                            ResolveExport(sourceEntry, name, phase, new HashSet<(ModuleEntry, string)>());
                        if (resolution.Kind == ExportResolutionKind.Resolved)
                        {
                            exports[name] = UninitializedExportMarker;
                        }
                    }

                    exportStarSet.Remove(sourceEntry.Path);
                    break;
                case ExportNamespaceAsStatement exportNamespace:
                    exports[exportNamespace.Exported.Name] = UninitializedExportMarker;
                    break;
            }
        }

        // Per ES spec, import bindings must be created during module instantiation
        HoistImportBindings(program, moduleEnv, modulePath, phase);

        // Per ES spec, all var declarations and function declarations in a module must be hoisted
        // before the module body executes.
        HoistModuleDeclarations(program, moduleEnv);
    }

    private void HoistImportBindings(ProgramNode program, JsEnvironment moduleEnv, string? modulePath, ImportPhase phase)
    {
        foreach (var statement in program.Body)
        {
            if (statement is ImportStatement importStatement)
            {
                // Determine the phase for this specific import - deferred imports use Defer phase
                var importPhase = importStatement.IsDeferred ? ImportPhase.Defer : phase;

                // Load and instantiate the module but DON'T evaluate it yet
                var importedModule = LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null, importStatement.Attributes);
                EnsureModuleInstantiated(importedModule, importPhase);

                // Handle default import
                if (importStatement.DefaultBinding is { } defaultBinding)
                {
                    CreateImportBinding(moduleEnv, defaultBinding, importedModule, Symbol.Intern("default"), importPhase);
                }

                // Handle namespace import
                if (importStatement.NamespaceBinding is { } nsBinding)
                {
                    // Namespace binding is the whole module namespace object
                    // Per ES spec 16.2.1.6.2 step 12.b.ii: CreateImmutableBinding(in.[[LocalName]], true)
                    // The binding must be immutable - assignment should throw TypeError in strict mode
                    var ns = GetModuleNamespace(importedModule, importPhase);
                    moduleEnv.Define(nsBinding, ns, isConst: true, isLexical: true, blocksFunctionScopeOverride: false);
                }

                // Handle named imports
                foreach (var specifier in importStatement.NamedImports)
                {
                    CreateImportBinding(moduleEnv, specifier.Local, importedModule, specifier.Imported, importPhase);
                }
            }
        }
    }

    private enum ExportResolutionKind
    {
        NotFound,
        Resolved,
        Ambiguous
    }

    private readonly record struct ExportResolution(ExportResolutionKind Kind, ModuleEntry? Module, Symbol BindingName)
    {
        public static readonly ExportResolution NotFound = new(ExportResolutionKind.NotFound, null, default);
        public static readonly ExportResolution Ambiguous = new(ExportResolutionKind.Ambiguous, null, default);

        public ExportResolution(ModuleEntry module, Symbol bindingName) : this(ExportResolutionKind.Resolved, module,
            bindingName)
        {
        }

        public bool IsResolved => Kind == ExportResolutionKind.Resolved && Module is not null;
    }

    private void CreateImportBinding(
        JsEnvironment moduleEnv,
        Symbol localName,
        ModuleEntry importedModule,
        Symbol importedName,
        ImportPhase importPhase)
    {
        var resolved = ResolveExport(importedModule, importedName.Name, importPhase,
            new HashSet<(ModuleEntry, string)>());
        if (!resolved.IsResolved)
        {
            throw new InvalidOperationException(
                $"SyntaxError: The requested module '{importedModule.Path}' does not provide an export named '{importedName.Name}'");
        }

        moduleEnv.DefineImportBinding(localName, resolved.Module!.Environment, resolved.BindingName);
    }

    private Symbol GetDefaultExportBindingName(ExportDefaultStatement exportDefault)
    {
        if (exportDefault.Value is ExportDefaultDeclaration { Declaration: FunctionDeclaration funcDecl })
        {
            return funcDecl.Name.Name == "" ? Symbol.Intern("*default*") : funcDecl.Name;
        }

        if (exportDefault.Value is ExportDefaultDeclaration { Declaration: ClassDeclaration classDecl })
        {
            return classDecl.Name.Name == "" ? Symbol.Intern("*default*") : classDecl.Name;
        }

        return Symbol.Intern("*default*");
    }

    private IEnumerable<string> GetExportedNames(ModuleEntry module, HashSet<string> exportStarSet)
    {
        if (exportStarSet.Contains(module.Path))
        {
            return Array.Empty<string>();
        }

        exportStarSet.Add(module.Path);
        if (module.Program.Body.IsEmpty && module.Exports.Count > 0)
        {
            exportStarSet.Remove(module.Path);
            return module.Exports.Keys;
        }

        var exportedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var statement in module.Program.Body)
        {
            switch (statement)
            {
                case ExportDefaultStatement:
                    exportedNames.Add("default");
                    break;
                case ExportDeclarationStatement exportDeclaration:
                    foreach (var symbol in GetDeclaredSymbols(exportDeclaration.Declaration))
                    {
                        exportedNames.Add(symbol.Name);
                    }

                    break;
                case ExportNamedStatement exportNamed:
                    foreach (var specifier in exportNamed.Specifiers)
                    {
                        exportedNames.Add(specifier.Exported.Name);
                    }

                    break;
                case ExportNamespaceAsStatement exportNamespace:
                    exportedNames.Add(exportNamespace.Exported.Name);
                    break;
                case ExportAllStatement exportAll:
                    var sourceModule =
                        LoadModuleForInstantiation(exportAll.ModulePath, module.Path, ImportPhase.Module, exportStarSet);
                    EnsureModuleInstantiated(sourceModule, ImportPhase.Module, exportStarSet);
                    foreach (var name in GetExportedNames(sourceModule, exportStarSet))
                    {
                        if (string.Equals(name, "default", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        exportedNames.Add(name);
                    }

                    break;
            }
        }

        exportStarSet.Remove(module.Path);
        return exportedNames;
    }

    private ExportResolution ResolveExport(
        ModuleEntry module,
        string exportName,
        ImportPhase phase,
        HashSet<(ModuleEntry, string)>? resolveSet = null)
    {
        resolveSet ??= new HashSet<(ModuleEntry, string)>();
        if (resolveSet.Contains((module, exportName)))
        {
            return ExportResolution.NotFound;
        }

        resolveSet.Add((module, exportName));

        if (module.Program.Body.IsEmpty && module.Exports.ContainsKey(exportName))
        {
            return new ExportResolution(module, Symbol.Intern(exportName));
        }

        foreach (var statement in module.Program.Body)
        {
            switch (statement)
            {
                case ExportDefaultStatement exportDefault when string.Equals(exportName, "default", StringComparison.Ordinal):
                    return new ExportResolution(module, GetDefaultExportBindingName(exportDefault));
                case ExportDeclarationStatement exportDeclaration:
                    foreach (var symbol in GetDeclaredSymbols(exportDeclaration.Declaration))
                    {
                        if (string.Equals(symbol.Name, exportName, StringComparison.Ordinal))
                        {
                            return new ExportResolution(module, symbol);
                        }
                    }

                    break;
                case ExportNamedStatement { FromModule: null } localExport:
                    foreach (var specifier in localExport.Specifiers)
                    {
                        if (string.Equals(specifier.Exported.Name, exportName, StringComparison.Ordinal))
                        {
                            return new ExportResolution(module, specifier.Local);
                        }
                    }

                    break;
                case ExportNamespaceAsStatement exportNamespace
                    when string.Equals(exportNamespace.Exported.Name, exportName, StringComparison.Ordinal):
                    return new ExportResolution(module, exportNamespace.Exported);
                case ExportNamedStatement { FromModule: { } fromModule } reExport:
                    foreach (var specifier in reExport.Specifiers)
                    {
                        if (!string.Equals(specifier.Exported.Name, exportName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var sourceModule =
                            LoadModuleForInstantiation(fromModule, module.Path, phase);
                        var resolved = ResolveExport(sourceModule, specifier.Local.Name, phase, resolveSet);
                        return resolved;
                    }

                    break;
            }
        }

        ExportResolution? starResolution = null;
        foreach (var statement in module.Program.Body)
        {
            if (statement is not ExportAllStatement exportAll)
            {
                continue;
            }

            if (string.Equals(exportName, "default", StringComparison.Ordinal))
            {
                continue;
            }

            var sourceModule = LoadModuleForInstantiation(exportAll.ModulePath, module.Path, phase);
            var resolved = ResolveExport(sourceModule, exportName, phase, resolveSet);
            if (resolved.Kind == ExportResolutionKind.NotFound)
            {
                continue;
            }

            if (resolved.Kind == ExportResolutionKind.Ambiguous)
            {
                return ExportResolution.Ambiguous;
            }

            if (starResolution is null)
            {
                starResolution = resolved;
            }
            else if (!ReferenceEquals(starResolution.Value.Module, resolved.Module) ||
                     !Equals(starResolution.Value.BindingName, resolved.BindingName))
            {
                return ExportResolution.Ambiguous;
            }
        }

        return starResolution ?? ExportResolution.NotFound;
    }

    private static object CreateLiveBinding(ExportResolution resolution)
    {
        return new LiveExportBinding(() => resolution.Module!.Environment.Get(resolution.BindingName));
    }

    /// <summary>
    /// Hoists all var, lexical, and function declarations in the module.
    /// Per ES spec:
    /// - Var declarations are initialized to undefined
    /// - Lexical declarations (let/const/class) are created but uninitialized (TDZ)
    /// - Function declarations are initialized with their function value
    /// </summary>
    private void HoistModuleDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        // First pass: collect and hoist var declarations
        HoistVarDeclarations(program, moduleEnv);

        // Second pass: hoist lexical declarations (let/const/class) in TDZ
        HoistLexicalDeclarations(program, moduleEnv);

        // Third pass: hoist function declarations (overwrites any var with same name)
        HoistFunctionDeclarations(program, moduleEnv);
    }

    private void HoistLexicalDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            CollectAndHoistLexicals(statement, moduleEnv);
        }
    }

    private void CollectAndHoistLexicals(StatementNode statement, JsEnvironment moduleEnv)
    {
        switch (statement)
        {
            case VariableDeclaration
            {
                Kind: VariableKind.Let or VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing
            } lexDecl:
                foreach (var declarator in lexDecl.Declarators)
                {
                    var isConst = lexDecl.Kind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                    HoistLexicalBinding(declarator.Target, moduleEnv, isConst);
                }
                break;
            case ClassDeclaration classDecl:
                // Class declarations are lexically scoped and start uninitialized
                if (!moduleEnv.HasBinding(classDecl.Name))
                {
                    moduleEnv.Define(classDecl.Name, JsEnvironment.Uninitialized, isLexical: true, blocksFunctionScopeOverride: false);
                }
                break;
            // Note: exported let/const/class are already handled by PredeclareExportNames
        }
    }

    private void HoistLexicalBinding(BindingTarget target, JsEnvironment moduleEnv, bool isConst)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!moduleEnv.HasBinding(id.Name))
                    {
                        moduleEnv.Define(id.Name, JsEnvironment.Uninitialized, isLexical: true, blocksFunctionScopeOverride: false, isConst: isConst);
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } binding)
                        {
                            HoistLexicalBinding(binding, moduleEnv, isConst);
                        }
                    }

                    if (arrayBinding.RestElement is { } rest)
                    {
                        target = rest;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        HoistLexicalBinding(prop.Target, moduleEnv, isConst);
                    }

                    if (objectBinding.RestElement is { } objRest)
                    {
                        target = objRest;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private void HoistVarDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            CollectAndHoistVars(statement, moduleEnv);
        }
    }

    private void CollectAndHoistVars(StatementNode statement, JsEnvironment moduleEnv)
    {
        switch (statement)
        {
            case VariableDeclaration { Kind: VariableKind.Var } varDecl:
                foreach (var declarator in varDecl.Declarators)
                {
                    HoistVarBinding(declarator.Target, moduleEnv);
                }
                break;
            case ForStatement { Initializer: VariableDeclaration { Kind: VariableKind.Var } forVarDecl }:
                foreach (var declarator in forVarDecl.Declarators)
                {
                    HoistVarBinding(declarator.Target, moduleEnv);
                }
                break;
            case ForEachStatement { DeclarationKind: VariableKind.Var } forEach:
                HoistVarBinding(forEach.Target, moduleEnv);
                break;
            case ExportDeclarationStatement { Declaration: VariableDeclaration { Kind: VariableKind.Var } exportVarDecl }:
                foreach (var declarator in exportVarDecl.Declarators)
                {
                    HoistVarBinding(declarator.Target, moduleEnv);
                }
                break;
        }
    }

    private void HoistVarBinding(BindingTarget target, JsEnvironment moduleEnv)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!moduleEnv.HasBinding(id.Name))
                    {
                        moduleEnv.Define(id.Name, Symbol.Undefined, isLexical: false, blocksFunctionScopeOverride: false);
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } binding)
                        {
                            HoistVarBinding(binding, moduleEnv);
                        }
                    }

                    if (arrayBinding.RestElement is { } rest)
                    {
                        target = rest;
                        continue;
                    }

                    break;
                case ObjectBinding objectBinding:
                    foreach (var prop in objectBinding.Properties)
                    {
                        HoistVarBinding(prop.Target, moduleEnv);
                    }

                    if (objectBinding.RestElement is { } objRest)
                    {
                        target = objRest;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private void HoistFunctionDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case FunctionDeclaration funcDecl:
                    // Create the function value and define it
                    var function = TypedAstEvaluator.CreateModuleFunction(funcDecl.Function, moduleEnv, RealmState, program.IsStrict);
                    moduleEnv.Define(funcDecl.Name, function, isLexical: false, blocksFunctionScopeOverride: false);
                    break;
                case ExportDeclarationStatement { Declaration: FunctionDeclaration exportedFuncDecl }:
                    // Exported function declarations also need to be hoisted
                    var exportedFunction = TypedAstEvaluator.CreateModuleFunction(exportedFuncDecl.Function, moduleEnv, RealmState, program.IsStrict);
                    moduleEnv.Define(exportedFuncDecl.Name, exportedFunction, isLexical: false, blocksFunctionScopeOverride: false);
                    break;
                case ExportDefaultStatement { Value: ExportDefaultDeclaration { Declaration: FunctionDeclaration defaultFuncDecl } }:
                    // Default exported named function declarations need to be hoisted
                    var defaultFunction = TypedAstEvaluator.CreateModuleFunction(defaultFuncDecl.Function, moduleEnv, RealmState, program.IsStrict);
                    moduleEnv.Define(defaultFuncDecl.Name, defaultFunction, isLexical: false, blocksFunctionScopeOverride: false);
                    break;
                case ExportDefaultStatement { Value: ExportDefaultExpression { Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true } funcExpr } }:
                    // Anonymous default exported function declarations (not expressions!) need to be hoisted with *default* binding
                    // Per ES spec, SetFunctionName(F, "default") is called for anonymous default exports
                    // Note: `export default function() {}` is hoistable, but `export default (function() {})` is not
                    var anonFunction = TypedAstEvaluator.CreateModuleFunction(funcExpr, moduleEnv, RealmState, program.IsStrict, "default");
                    moduleEnv.Define(Symbol.Intern("*default*"), anonFunction, isLexical: false, blocksFunctionScopeOverride: false);
                    break;
            }
        }
    }

    private void EvaluateRequestedModules(ProgramNode program, string? modulePath)
    {
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case ImportStatement importStatement:
                    var importPhase = importStatement.IsDeferred ? ImportPhase.Defer : ImportPhase.Module;
                    if (importPhase == ImportPhase.Module)
                    {
                        LoadModule(importStatement.ModulePath, modulePath, importPhase, null, importStatement.Attributes);
                    }
                    else
                    {
                        LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null,
                            importStatement.Attributes);
                    }

                    break;
                case ExportNamedStatement { FromModule: { } fromModule }:
                    LoadModule(fromModule, modulePath, ImportPhase.Module);
                    break;
                case ExportAllStatement exportAll:
                    LoadModule(exportAll.ModulePath, modulePath, ImportPhase.Module);
                    break;
                case ExportNamespaceAsStatement exportNamespace:
                    LoadModule(exportNamespace.ModulePath, modulePath, ImportPhase.Module);
                    break;
            }
        }
    }

    private object? ExecuteModuleBody(
        ProgramNode typedProgram,
        JsEnvironment moduleEnv,
        JsObject exports,
        string? modulePath,
        bool drainAwaitMicrotasks = true)
    {
        var previousModulePath = _currentModulePath;
        _currentModulePath = modulePath;
        object? lastValue = null;
        try
        {
            EvaluateRequestedModules(typedProgram, modulePath);
            foreach (var statement in typedProgram.Body)
            {
                switch (statement)
                {
                    case ImportStatement importStatement:
                        EvaluateImport(importStatement, moduleEnv, modulePath);
                        break;
                    case ExportDefaultStatement exportDefault:
                        var defaultValue = EvaluateExportDefault(exportDefault, moduleEnv, typedProgram.IsStrict);
                        exports["default"] = defaultValue;
                        break;
                    case ExportNamedStatement exportNamed:
                        EvaluateExportNamed(exportNamed, moduleEnv, exports, modulePath);
                        break;
                    case ExportDeclarationStatement exportDeclaration:
                        EvaluateExportDeclaration(exportDeclaration, moduleEnv, exports, typedProgram.IsStrict);
                        break;
                    case ExportAllStatement exportAll:
                        EvaluateExportAll(exportAll, exports, modulePath);
                        break;
                    case ExportNamespaceAsStatement exportNamespace:
                        var namespaceEntry = LoadModule(exportNamespace.ModulePath, modulePath, ImportPhase.Module);
                        var namespaceObj = GetModuleNamespace(namespaceEntry);
                        exports[exportNamespace.Exported.Name] = namespaceObj;
                        // Also define in the environment so import bindings can read it
                        moduleEnv.Define(exportNamespace.Exported, namespaceObj, isConst: true, isLexical: true,
                            blocksFunctionScopeOverride: false);
                        break;
                    case FunctionDeclaration:
                        // Function declarations are already hoisted during module instantiation,
                        // so their evaluation is a no-op per ES spec
                        break;
                    default:
                        lastValue = ExecuteTypedStatement(
                            statement,
                            moduleEnv,
                            typedProgram.IsStrict,
                            false,
                            drainAwaitMicrotasks: drainAwaitMicrotasks);
                        break;
                }
            }

            return lastValue;
        }
        finally
        {
            _currentModulePath = previousModulePath;
        }
    }

    private IReadOnlyList<ModuleEntry> GetModuleDependencies(ModuleEntry entry)
    {
        var dependencies = new List<ModuleEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var modulePath = entry.Path;

        foreach (var statement in entry.Program.Body)
        {
            switch (statement)
            {
                case ImportStatement importStatement:
                    if (importStatement.IsDeferred)
                    {
                        continue;
                    }

                    var importPhase = importStatement.IsDeferred ? ImportPhase.Defer : ImportPhase.Module;
                    var imported = LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null,
                        importStatement.Attributes);
                    if (string.Equals(imported.Path, entry.Path, StringComparison.Ordinal) ||
                        !seen.Add(imported.Path))
                    {
                        continue;
                    }

                    dependencies.Add(imported);
                    break;
                case ExportNamedStatement { FromModule: { } fromModule }:
                {
                    var sourceEntry = LoadModuleForInstantiation(fromModule, modulePath, ImportPhase.Module);
                    if (string.Equals(sourceEntry.Path, entry.Path, StringComparison.Ordinal) ||
                        !seen.Add(sourceEntry.Path))
                    {
                        continue;
                    }

                    dependencies.Add(sourceEntry);
                    break;
                }
                case ExportAllStatement exportAll:
                {
                    var sourceEntry = LoadModuleForInstantiation(exportAll.ModulePath, modulePath, ImportPhase.Module);
                    if (string.Equals(sourceEntry.Path, entry.Path, StringComparison.Ordinal) ||
                        !seen.Add(sourceEntry.Path))
                    {
                        continue;
                    }

                    dependencies.Add(sourceEntry);
                    break;
                }
                case ExportNamespaceAsStatement exportNamespace:
                {
                    var namespaceEntry =
                        LoadModuleForInstantiation(exportNamespace.ModulePath, modulePath, ImportPhase.Module);
                    if (string.Equals(namespaceEntry.Path, entry.Path, StringComparison.Ordinal) ||
                        !seen.Add(namespaceEntry.Path))
                    {
                        continue;
                    }

                    dependencies.Add(namespaceEntry);
                    break;
                }
            }
        }

        return dependencies;
    }

    private async Task DrainAsyncDependencies(List<Task<object?>> pendingAsyncDependencies)
    {
        while (pendingAsyncDependencies.Count > 0)
        {
            DrainMicrotasks();

            for (var i = pendingAsyncDependencies.Count - 1; i >= 0; i--)
            {
                var asyncDependency = pendingAsyncDependencies[i];
                if (!asyncDependency.IsCompleted)
                {
                    continue;
                }

                pendingAsyncDependencies.RemoveAt(i);
                await asyncDependency.ConfigureAwait(false);
            }

            if (pendingAsyncDependencies.Count == 0)
            {
                break;
            }

            await Task.Yield();
        }

        DrainMicrotasks();
        pendingAsyncDependencies.Clear();
    }

    private async Task<object?> EvaluateModuleBodyWithAsyncDependencies(
        ModuleEntry entry,
        bool drainAwaitMicrotasks = true)
    {
        entry.Evaluating = true;
        try
        {
            var pendingAsyncDependencies = new List<Task<object?>>();
            var dependencies = GetModuleDependencies(entry);
            for (var i = 0; i < dependencies.Count; i++)
            {
                var dependency = dependencies[i];
                EnsureModuleInstantiated(dependency);
                var isAsyncDependency = dependency.IsAsync || dependency.HasAsyncDependency;
                var evaluation = EnsureModuleEvaluatedAsync(dependency, waitForAsync: !isAsyncDependency);
                if (isAsyncDependency)
                {
                    pendingAsyncDependencies.Add(evaluation);
                    var nextIsAsync = i + 1 < dependencies.Count &&
                                      (dependencies[i + 1].IsAsync || dependencies[i + 1].HasAsyncDependency);
                    if (nextIsAsync)
                    {
                        await DrainAsyncDependencies(pendingAsyncDependencies).ConfigureAwait(false);
                    }

                    continue;
                }

                await evaluation.ConfigureAwait(false);
            }

            if (pendingAsyncDependencies.Count > 0)
            {
                await DrainAsyncDependencies(pendingAsyncDependencies).ConfigureAwait(false);
            }

            var previousModulePath = _currentModulePath;
            _currentModulePath = entry.Path;
            try
            {
                var result = ExecuteModuleBody(
                    entry.Program,
                    entry.Environment,
                    entry.Exports,
                    entry.Path,
                    drainAwaitMicrotasks);
                entry.LastValue = result;
                entry.Evaluated = true;
                return result;
            }
            finally
            {
                _currentModulePath = previousModulePath;
            }
        }
        finally
        {
            entry.Evaluating = false;
        }
    }

    private async Task<object?> EvaluateModuleBodyWithTopLevelAwait(
        ModuleEntry entry,
        bool drainAwaitMicrotasks = true)
    {
        entry.Evaluating = true;
        try
        {
            var pendingAsyncDependencies = new List<Task<object?>>();
            var dependencies = GetModuleDependencies(entry);
            for (var i = 0; i < dependencies.Count; i++)
            {
                var dependency = dependencies[i];
                EnsureModuleInstantiated(dependency);
                var evaluation = EnsureModuleEvaluatedAsync(dependency, waitForAsync: true);
                var isAsyncDependency = dependency.IsAsync || dependency.HasAsyncDependency;
                if (isAsyncDependency)
                {
                    pendingAsyncDependencies.Add(evaluation);
                    var nextIsAsync = i + 1 < dependencies.Count &&
                                      (dependencies[i + 1].IsAsync || dependencies[i + 1].HasAsyncDependency);
                    if (nextIsAsync)
                    {
                        await DrainAsyncDependencies(pendingAsyncDependencies).ConfigureAwait(false);
                    }

                    continue;
                }

                await evaluation.ConfigureAwait(false);
            }

            if (pendingAsyncDependencies.Count > 0)
            {
                await DrainAsyncDependencies(pendingAsyncDependencies).ConfigureAwait(false);
            }

            object? result;
            if (drainAwaitMicrotasks)
            {
                var previousModulePath = _currentModulePath;
                _currentModulePath = entry.Path;
                try
                {
                    result = ExecuteModuleBody(
                        entry.Program,
                        entry.Environment,
                        entry.Exports,
                        entry.Path,
                        drainAwaitMicrotasks);
                }
                finally
                {
                    _currentModulePath = previousModulePath;
                }
            }
            else
            {
                result = await Task
                    .Run(() =>
                    {
                        var previousModulePath = _currentModulePath;
                        _currentModulePath = entry.Path;
                        try
                        {
                            return ExecuteModuleBody(
                                entry.Program,
                                entry.Environment,
                                entry.Exports,
                                entry.Path,
                                drainAwaitMicrotasks);
                        }
                        finally
                        {
                            _currentModulePath = previousModulePath;
                        }
                    })
                    .ConfigureAwait(false);
            }
            entry.LastValue = result;
            entry.Evaluated = true;
            return result;
        }
        finally
        {
            entry.Evaluating = false;
        }
    }

    /// <summary>
    ///     Evaluates a module program and populates the exports object.
    ///     Returns the last evaluated value.
    /// </summary>
    private object? EvaluateModule(
        ParsedProgram program,
        JsEnvironment moduleEnv,
        JsObject exports,
        string? modulePath = null)
    {
        var typedProgram = EnsureStrictProgram(program);
        PredeclareExportNames(typedProgram, moduleEnv, exports, modulePath, ImportPhase.Module,
            new HashSet<string>(StringComparer.Ordinal));
        return ExecuteModuleBody(typedProgram, moduleEnv, exports, modulePath);
    }

    private void WaitForAsyncModule(ModuleEntry moduleEntry)
    {
        EnsureModuleEvaluatedAsync(moduleEntry, waitForAsync: true).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Processes an import statement and brings imported values into the module environment.
    /// </summary>
    private void EvaluateImport(
        ImportStatement importStatement,
        JsEnvironment moduleEnv,
        string? referrerPath)
    {
        var phase = importStatement.IsDeferred ? ImportPhase.Defer : ImportPhase.Module;
        var moduleEntry = LoadModule(importStatement.ModulePath, referrerPath, phase, null, importStatement.Attributes);

        if (importStatement.IsDeferred &&
            (importStatement.DefaultBinding is not null || !importStatement.NamedImports.IsEmpty))
        {
            throw new NotSupportedException("Deferred imports only support namespace bindings.");
        }

        var engine = moduleEnv.RealmState?.Engine;
        var isAsyncImport = moduleEntry.IsAsync || moduleEntry.HasAsyncDependency;
        var requiresModuleCompletion =
            importStatement.DefaultBinding is not null ||
            !importStatement.NamedImports.IsEmpty ||
            importStatement.NamespaceBinding is not null;

        List<Action>? preservedMicrotasks = null;

        try
        {
            var shouldPreserveMicrotasks =
                requiresModuleCompletion &&
                !importStatement.IsDeferred &&
                isAsyncImport &&
                !string.Equals(moduleEntry.Path, referrerPath, StringComparison.Ordinal) &&
                engine is not null;

            if (shouldPreserveMicrotasks)
            {
                preservedMicrotasks = engine.DetachMicrotasks();
            }

            if (!importStatement.IsDeferred)
            {
                if (isAsyncImport &&
                    !string.Equals(moduleEntry.Path, referrerPath, StringComparison.Ordinal))
                {
                    if (requiresModuleCompletion)
                    {
                        WaitForAsyncModule(moduleEntry);
                    }
                    else
                    {
                        EnsureModuleEvaluatedAsync(moduleEntry, waitForAsync: false);
                    }
                }
                else
                {
                    EnsureModuleEvaluated(moduleEntry);
                }
            }
        }
        finally
        {
            if (preservedMicrotasks is { Count: > 0 })
            {
                engine?.PrependMicrotasks(preservedMicrotasks);
            }
        }

        // Import bindings were already created during HoistImportBindings.
        // We only need to set up namespace bindings here, as they reference the namespace object.
        // Default and named imports are now handled by ImportBindingWrapper created during hoisting.

        if (importStatement.NamespaceBinding is { } namespaceBinding)
        {
            // Namespace bindings aren't hoisted as import bindings, they get the namespace object
            if (!moduleEnv.HasBinding(namespaceBinding))
            {
                var namespaceObject = GetModuleNamespace(moduleEntry, phase);
                moduleEnv.Define(namespaceBinding, namespaceObject);
            }
        }
    }

    private object? EvaluateExportDefault(ExportDefaultStatement statement, JsEnvironment moduleEnv, bool isStrict)
    {
        var defaultBindingName = Symbol.Intern("*default*");

        // For hoistable anonymous function declarations, the function was already hoisted with *default* binding
        // `export default function() {}` is hoistable (IsHoistableDefaultExport = true)
        // `export default (function() {})` is NOT hoistable (it's a parenthesized expression)
        if (statement.Value is ExportDefaultExpression { Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true } })
        {
            return new LiveExportBinding(() => moduleEnv.Get(defaultBindingName));
        }

        // For ExportDefaultDeclaration (named function/class declarations), delegate to specialized handler
        if (statement.Value is ExportDefaultDeclaration declaration)
        {
            return EvaluateExportDefaultDeclaration(declaration, moduleEnv, isStrict);
        }

        // For all other expression default exports, evaluate the expression and define the *default* binding
        if (statement.Value is ExportDefaultExpression expression)
        {
            // Per spec: If IsAnonymousFunctionDefinition(AssignmentExpression) is true, perform SetFunctionName(value, "default")
            // This applies to anonymous function expressions, arrow functions, generator expressions, and anonymous class expressions
            var isAnonymousFunctionDefinition = expression.Expression switch
            {
                FunctionExpression { Name: null } => true,
                ClassExpression { Name: null } => true,
                _ => false
            };

            var value = ExecuteTypedExpression(
                expression.Expression,
                moduleEnv,
                isStrict,
                isAnonymousFunctionDefinition ? Symbol.Intern("default") : null);

            // Initialize the *default* binding (it was created in TDZ during PredeclareExportNames)
            moduleEnv.Assign(defaultBindingName, value);

            return new LiveExportBinding(() => moduleEnv.Get(defaultBindingName));
        }

        return Symbol.Undefined;
    }

    private object? EvaluateExportDefaultDeclaration(ExportDefaultDeclaration declaration, JsEnvironment moduleEnv,
        bool isStrict)
    {
        // For function declarations, they were already hoisted during module instantiation
        // For anonymous functions, the binding name is "*default*"
        if (declaration.Declaration is FunctionDeclaration functionDeclaration)
        {
            var bindingName = functionDeclaration.Name.Name == ""
                ? Symbol.Intern("*default*")
                : functionDeclaration.Name;
            return new LiveExportBinding(() => moduleEnv.Get(bindingName));
        }

        // Classes need to be evaluated (they aren't hoisted like functions)
        ExecuteTypedStatement(declaration.Declaration, moduleEnv, isStrict, false);
        return declaration.Declaration switch
        {
            ClassDeclaration classDeclaration => new LiveExportBinding(() =>
            {
                var bindingName = classDeclaration.Name.Name == ""
                    ? Symbol.Intern("*default*")
                    : classDeclaration.Name;
                return moduleEnv.Get(bindingName);
            }),
            _ => Symbol.Undefined
        };
    }

    private void EvaluateExportNamed(
        ExportNamedStatement statement,
        JsEnvironment moduleEnv,
        JsObject exports,
        string? modulePath)
    {
        if (statement.FromModule is { } fromModule)
        {
            var sourceEntry = LoadModule(fromModule, modulePath, ImportPhase.Module);
            foreach (var specifier in statement.Specifiers)
            {
                var resolution = ResolveExport(sourceEntry, specifier.Local.Name, ImportPhase.Module,
                    new HashSet<(ModuleEntry, string)>());
                if (resolution.Kind == ExportResolutionKind.Resolved)
                {
                    exports[specifier.Exported.Name] = CreateLiveBinding(resolution);
                }
            }

            return;
        }

        foreach (var specifier in statement.Specifiers)
        {
            exports[specifier.Exported.Name] = new LiveExportBinding(() => moduleEnv.Get(specifier.Local));
        }
    }

    private void EvaluateExportDeclaration(ExportDeclarationStatement statement, JsEnvironment moduleEnv,
        JsObject exports, bool isStrict)
    {
        ExecuteTypedStatement(statement.Declaration, moduleEnv, isStrict, false);
        foreach (var symbol in GetDeclaredSymbols(statement.Declaration))
        {
            var value = moduleEnv.Get(symbol);
            exports[symbol.Name] = new LiveExportBinding(() => moduleEnv.Get(symbol));
        }
    }

    private void EvaluateExportAll(ExportAllStatement statement, JsObject exports, string? modulePath)
    {
        var sourceEntry = LoadModule(statement.ModulePath, modulePath, ImportPhase.Module);
        var exportedNames = GetExportedNames(sourceEntry, new HashSet<string>(StringComparer.Ordinal));
        foreach (var name in exportedNames)
        {
            if (name.StartsWith("__getter__", StringComparison.Ordinal) ||
                name.StartsWith("__setter__", StringComparison.Ordinal) ||
                name.StartsWith("@@symbol:", StringComparison.Ordinal) ||
                string.Equals(name, "default", StringComparison.Ordinal))
            {
                continue;
            }

            var resolution = ResolveExport(sourceEntry, name, ImportPhase.Module,
                new HashSet<(ModuleEntry, string)>());
            if (resolution.Kind != ExportResolutionKind.Resolved)
            {
                continue;
            }

            exports[name] = CreateLiveBinding(resolution);
        }
    }

    private static IEnumerable<Symbol> GetDeclaredSymbols(StatementNode declaration)
    {
        switch (declaration)
        {
            case VariableDeclaration variableDeclaration:
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    foreach (var symbol in GetBindingSymbols(declarator.Target))
                    {
                        yield return symbol;
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

    private static IEnumerable<Symbol> GetBindingSymbols(BindingTarget target)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding identifier:
                    yield return identifier.Name;
                    yield break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is null)
                        {
                            continue;
                        }

                        foreach (var symbol in GetBindingSymbols(element.Target))
                        {
                            yield return symbol;
                        }
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        target = arrayBinding.RestElement;
                        continue;
                    }

                    yield break;
                case ObjectBinding objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        foreach (var symbol in GetBindingSymbols(property.Target))
                        {
                            yield return symbol;
                        }
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        target = objectBinding.RestElement;
                        continue;
                    }

                    yield break;
                default:
                    yield break;
            }
        }
    }

    private object? ExecuteTypedExpression(
        ExpressionNode expression,
        JsEnvironment environment,
        bool isStrict,
        Symbol? functionNameHint = null)
    {
        var statement = new ExpressionStatement(expression.Source, expression);
        return ExecuteTypedStatement(statement, environment, isStrict, functionNameHint: functionNameHint);
    }

    private object? ExecuteTypedStatement(
        StatementNode statement,
        JsEnvironment environment,
        bool isStrict,
        bool createStrictEnvironment = true,
        Symbol? functionNameHint = null,
        bool drainAwaitMicrotasks = true)
    {
        var program = new ProgramNode(statement.Source, [statement], isStrict);
        return program.EvaluateProgram(environment, RealmState,
            executionKind: ExecutionKind.Script, createStrictEnvironment: createStrictEnvironment,
            functionNameHint: functionNameHint,
            drainAwaitMicrotasks: drainAwaitMicrotasks);
    }
}
