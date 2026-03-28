#region

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Asynkron.JsEngine.StdLib.Temporal;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     High level façade that turns JavaScript source into S-expressions and evaluates them.
/// </summary>
public sealed class JsEngine : IAsyncDisposable, IDisposable
{
    private readonly Channel<string>? _asyncIteratorTraceChannel;
    private readonly bool _asyncIteratorTracingEnabled;

    //DEBUG code
    private readonly Channel<DebugMessage>? _debugChannel;
    private readonly Lock _drainLock = new(); // Protects _drainCompletionSource
    private readonly Channel<ExceptionInfo>? _exceptionChannel;

    // Synchronous microtask queue for top-level await support.
    // JsEngine is single-threaded by design, so microtask bookkeeping does not use locks.
    // Microtasks implement IMicrotask and carry their own epoch for proper timing semantics.
    private readonly Queue<IMicrotask> _microtaskQueue = new();

    private readonly Dictionary<JsObject, ModuleNamespace> _moduleNamespaces =
        new(ReferenceEqualityComparer<JsObject>.Instance);

    // Module registry: maps module paths to their exported values
    private readonly Dictionary<string, ModuleEntry> _moduleRegistry = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _timers = new();
    private readonly TypedConstantExpressionTransformer _typedConstantTransformer = new();
    private int _activeTimerCount; // Track registered timers (timeouts/intervals)
    private string? _currentModulePath;
    private TaskCompletionSource? _drainCompletionSource; // Signals when event loop has drained
    private Task? _eventLoopTask;
    private int? _eventLoopThreadId;
    private Channel<Action>? _eventQueue;
    private bool _isDrainingMicrotasks;
    private int _moduleBodyExecutionDepth; // Depth counter to suppress microtask draining during module body execution

    // Module loader function: allows custom module loading logic
    private ModuleLoader? _moduleLoader;
    private int _nextTimerId;
    private int _pendingTaskCount; // Track pending tasks in the event queue

    /// <summary>
    ///     Initializes a new instance of JsEngine with standard library objects.
    /// </summary>
    public JsEngine(IJsEngineOptions? options = null)
        : this(options, false)
    {
    }

    /// <summary>
    ///     Internal constructor that can skip standard library initialization.
    ///     Used by tests to restore a pre-initialized realm snapshot without
    ///     re-running expensive built-in wiring.
    /// </summary>
    internal JsEngine(IJsEngineOptions? options, bool skipStdLibInitialization)
    {
        Options = options ?? JsEngineOptions.Default;
        var debugMode = Options.DebugMode;
        if (debugMode)
        {
            _asyncIteratorTraceChannel = Channel.CreateUnbounded<string>();
            _debugChannel = Channel.CreateUnbounded<DebugMessage>();
            _exceptionChannel = Channel.CreateUnbounded<ExceptionInfo>();
        }

        _asyncIteratorTracingEnabled = debugMode;
        RealmState = new RealmState { Options = Options, Engine = this, Logger = Options.Logger };
        GlobalEnvironment.SetRealmState(RealmState);
        GlobalExecutionScope = GlobalEnvironment;
        // Bind the global `this` value to a dedicated JS object so that
        // top-level `this` behaves like the global object (e.g. for UMD
        // wrappers such as babel-standalone).
        GlobalEnvironment.DefineJsValue(Symbol.This, (JsValue)GlobalObject);
        GlobalObject.RealmState = RealmState;

        // Expose common aliases for the global object that many libraries
        // expect to exist (Node-style `global`, standard `globalThis`).
        SetGlobal("globalThis", GlobalObject);
        SetGlobal("global", GlobalObject);

        if (skipStdLibInitialization)
        {
            return;
        }

        // Register standard library objects
        // Object must be registered first so that ObjectPrototype is available for other prototypes
        SetGlobal("Object", ObjectConstructor.CreateConstructor(RealmState));
        SetGlobal("console", ConsolePrototype.CreatePrototype(RealmState));
        SetGlobal("Math", MathPrototype.CreatePrototype(RealmState));

        // Per ECMAScript spec, the global object's [[Prototype]] is Object.prototype.
        // This ensures that methods like hasOwnProperty are inherited by the global object.
        if (RealmState.ObjectPrototype is not null)
        {
            GlobalObject.SetPrototype(RealmState.ObjectPrototype);
        }

        // Create the %ThrowTypeError% intrinsic per realm (ES spec 10.2.4) BEFORE
        // the Function constructor, because FunctionPrototype.ConfigurePrototype() needs it
        // for the shared callee/caller poison pill accessors.
        // We finalize (freeze) it later after FunctionPrototype is available.
        CreateThrowTypeErrorIntrinsic();

        SetGlobal("Function", FunctionConstructor.CreateConstructor(RealmState));
        SetGlobal("Number", NumberConstructor.CreateConstructor(RealmState));
        var bigIntFunction = BigIntConstructor.CreateConstructor(RealmState);
        SetGlobal("BigInt", bigIntFunction);
        SetGlobal("Boolean", BooleanConstructor.CreateConstructor(RealmState));
        SetGlobal("String", StringConstructor.CreateConstructor(RealmState));
        var arrayConstructor = ArrayConstructor.CreateConstructor(RealmState);
        arrayConstructor.RealmState = RealmState;
        SetGlobal("Array", arrayConstructor);

        GlobalObject.DefineProperty("Array",
            new PropertyDescriptor
            {
                Value = arrayConstructor,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });
        GlobalObject.DefineProperty("BigInt",
            new PropertyDescriptor
            {
                Value = bigIntFunction,
                Writable = true,
                Enumerable = false,
                Configurable = true
            });

        // Register global constants
        SetGlobal("Infinity", double.PositiveInfinity, true);
        GlobalObject.DefineProperty("Infinity",
            new PropertyDescriptor
            {
                Value = double.PositiveInfinity,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        SetGlobal("NaN", double.NaN, true);
        GlobalObject.DefineProperty("NaN",
            new PropertyDescriptor { Value = double.NaN, Writable = false, Enumerable = false, Configurable = false });

        SetGlobal("undefined", Symbol.Undefined, true);
        GlobalObject.DefineProperty("undefined",
            new PropertyDescriptor
            {
                Value = Symbol.Undefined,
                Writable = false,
                Enumerable = false,
                Configurable = false
            });

        // Register global functions
        GlobalHelper.RegisterHostFunctions(GlobalObject, RealmState);

        // Per ECMAScript spec, Number.parseFloat === parseFloat and Number.parseInt === parseInt
        // must be the same function objects. Copy the global functions to Number.
        if (GlobalObject.TryGetProperty("parseFloat", out var globalParseFloat) &&
            GlobalObject.TryGetProperty("parseInt", out var globalParseInt) &&
            GlobalObject.TryGetProperty("Number", out var numberCtorVal) &&
            numberCtorVal.TryGetObject<IJsPropertyAccessor>(out var numberCtorObj))
        {
            numberCtorObj.SetProperty("parseFloat", globalParseFloat);
            numberCtorObj.SetProperty("parseInt", globalParseInt);
        }

        // Shared TypedArray intrinsic (abstract)
        var typedArrayCtor = TypedArrayHelper.EnsureTypedArrayIntrinsic(RealmState);
        SetGlobal("TypedArray", typedArrayCtor);

        // Register Date constructor
        SetGlobal("Date", DateConstructor.CreateConstructor(RealmState));
        SetGlobal("JSON", JsonPrototype.CreatePrototype(RealmState));

        // Register RegExp constructor
        SetGlobal("RegExp", RegExpConstructor.CreateConstructor(RealmState));

        // Error constructors
        SetGlobal("Error", ErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("TypeError", TypeErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("RangeError", RangeErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("ReferenceError", ReferenceErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("SyntaxError", SyntaxErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("EvalError", EvalErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("URIError", UriErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("AggregateError", AggregateErrorConstructor.CreateConstructor(RealmState));
        SetGlobal("SuppressedError", SuppressedErrorConstructor.CreateConstructor(RealmState));

        // Finalize the %ThrowTypeError% intrinsic - set its prototype to Function.prototype
        // and freeze it now that all dependencies are available.
        FinalizeThrowTypeErrorIntrinsic();

        // Register Promise constructor
        IJsCallable? promiseConstructor = PromiseConstructor.CreateConstructor(RealmState);
        SetGlobal("Promise", promiseConstructor);
        RealmState.PromiseConstructor = promiseConstructor;

        // Register Symbol constructor
        SetGlobal("Symbol", SymbolConstructor.CreateConstructor(RealmState));

        // Register Map constructor
        SetGlobal("Map", MapConstructor.CreateConstructor(RealmState));

        // Register Set constructor
        SetGlobal("Set", SetConstructor.CreateConstructor(RealmState));

        // Register WeakMap constructor
        SetGlobal("WeakMap", WeakMapConstructor.CreateConstructor(RealmState));

        // Minimal Proxy constructor (used by Array.isArray proxy tests)
        SetGlobal("Proxy", ProxyConstructor.CreateConstructor(RealmState));

        // Register WeakSet constructor
        SetGlobal("WeakSet", WeakSetConstructor.CreateConstructor(RealmState));

        SetGlobal("WeakRef", WeakRefConstructor.CreateConstructor(RealmState));
        SetGlobal("FinalizationRegistry", FinalizationRegistryConstructor.CreateConstructor(RealmState));

        SetGlobal("ShadowRealm", ShadowRealmConstructor.CreateConstructor(RealmState));
        SetGlobal("DisposableStack", DisposableStackConstructor.CreateConstructor(RealmState));
        SetGlobal("AsyncDisposableStack", AsyncDisposableStackConstructor.CreateConstructor(RealmState));

        // Register Iterator constructor
        SetGlobal("Iterator", IteratorConstructor.CreateConstructor(RealmState));

        // Register ShadowRealm constructor
        SetGlobal("ShadowRealm", ShadowRealmConstructor.CreateConstructor(RealmState));

        // Minimal browser-like storage object used by debug/babel-standalone.
        SetGlobal("localStorage", BrowserHelper.CreateLocalStorageObject());

        // Reflect object
        SetGlobal("Reflect", ReflectPrototype.CreatePrototype(RealmState));

        // Register ArrayBuffer and TypedArray constructors
        SetGlobal("ArrayBuffer", ArrayBufferConstructor.CreateConstructor(RealmState));
        SetGlobal("SharedArrayBuffer", SharedArrayBufferConstructor.CreateConstructor(RealmState));
        SetGlobal("Atomics", AtomicsPrototype.CreatePrototype(RealmState));
        SetGlobal("DataView", DataViewConstructor.CreateConstructor(RealmState));
        SetGlobal("Int8Array", TypedArrayHelper.CreateInt8ArrayConstructor(RealmState));
        SetGlobal("Uint8Array", TypedArrayHelper.CreateUint8ArrayConstructor(RealmState));
        SetGlobal("Uint8ClampedArray", TypedArrayHelper.CreateUint8ClampedArrayConstructor(RealmState));
        SetGlobal("Int16Array", TypedArrayHelper.CreateInt16ArrayConstructor(RealmState));
        SetGlobal("Uint16Array", TypedArrayHelper.CreateUint16ArrayConstructor(RealmState));
        SetGlobal("Int32Array", TypedArrayHelper.CreateInt32ArrayConstructor(RealmState));
        SetGlobal("Uint32Array", TypedArrayHelper.CreateUint32ArrayConstructor(RealmState));
        SetGlobal("Float32Array", TypedArrayHelper.CreateFloat32ArrayConstructor(RealmState));
        SetGlobal("Float64Array", TypedArrayHelper.CreateFloat64ArrayConstructor(RealmState));
        SetGlobal("BigInt64Array", TypedArrayHelper.CreateBigInt64ArrayConstructor(RealmState));
        SetGlobal("BigUint64Array", TypedArrayHelper.CreateBigUint64ArrayConstructor(RealmState));
        SetGlobal("Intl", IntlHelper.CreateIntlObject(RealmState));
        SetGlobal("Temporal", TemporalHelper.CreateTemporalObject(RealmState));

        // Register eval function as an environment-aware callable
        // This allows eval to execute code in the caller's scope without blocking the event loop
        SetGlobal("eval", new EvalHostFunction(this));

        // Register internal helpers for async iteration
        IterationHelper.RegisterHostFunctions(GlobalObject, RealmState);
        SetGlobal("$DETACHBUFFER", new HostFunction((_, args) =>
        {
            if (args.Count > 0 && args[0].TryGetObject<TypedArrayBase>(out var view))
            {
                view.Buffer.Detach();
            }
            else if (args.Count > 0 && args[0].TryGetObject<JsArrayBuffer>(out var buffer))
            {
                buffer.Detach();
            }

            return JsValue.Undefined;
        }));

        // Register timer functions
        SetGlobalFunction("setTimeout", SetTimeout);
        SetGlobalFunction("setInterval", SetInterval);
        SetGlobalFunction("clearTimeout", ClearTimer);
        SetGlobalFunction("clearInterval", ClearTimer);

        // Register microtask queue function (ECMAScript Web API)
        SetGlobalFunction("queueMicrotask", QueueMicrotaskGlobal);

        // Register a dynamic import function
        var importFunction = new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Module, null),
            RealmState, false);
        importFunction.SetInvokeWithContext((args, _, ctx, _) =>
            DynamicImport(args, ctx, ImportPhase.Module, importFunction));

        var importDeferFunction =
            new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Defer, null), RealmState, false);
        importDeferFunction.SetInvokeWithContext((args, _, ctx, _) =>
            DynamicImport(args, ctx, ImportPhase.Defer, importDeferFunction));

        var importSourceFunction =
            new HostFunction((_, args) => DynamicImport(args, null, ImportPhase.Source, null), RealmState, false);
        importSourceFunction.SetInvokeWithContext((args, _, ctx, _) =>
            DynamicImport(args, ctx, ImportPhase.Source, importSourceFunction));
        importFunction.SetProperty("defer", (JsValue)importDeferFunction);
        importFunction.SetProperty("source", (JsValue)importSourceFunction);
        SetGlobal("import", importFunction);

        // Provide a stable global object helper used by Test262 harness utilities.
        // Note: NOT marked as constant so Test262 harness can redeclare it if needed.
        SetGlobal("fnGlobalObject",
            new HostFunction((_, _) => new JsValue(GlobalObject)) { Realm = GlobalObject, RealmState = RealmState });

        // Register the debug function as a debug-aware host function
        GlobalEnvironment.DefineJsValue(Symbol.DebugIdentifier,
            JsValue.FromObjectUnsafe(new DebugAwareHostFunction(CaptureDebugMessage)));
    }

    internal int PromiseCallDepth { get; set; }
    internal int MaxCallDepth { get; } = 1000;

    /// <summary>
    ///     Maximum wall-clock time to allow a single evaluation to run before failing.
    ///     Null or non-positive values disable the timeout.
    /// </summary>
    // Keep a finite timeout to avoid runaway scripts, but give heavy test cases
    // (e.g., crypto/NBody fixtures) enough headroom to finish.
    public TimeSpan? ExecutionTimeout { get; set; }

    /// <summary>
    ///     Exposes the global object for realm-like scenarios (e.g. Test262 realms).
    /// </summary>
    public JsObject GlobalObject { get; } = new();

    internal JsEnvironment GlobalEnvironment { get; } = JsEnvironment.CreateInstance(isFunctionScope: true);
    internal JsEnvironment GlobalExecutionScope { get; private set; }

    internal RealmState RealmState { get; } = new();
    public IJsEngineOptions Options { get; }

    /// <summary>
    ///     Gets the current microtask epoch for tracking purposes.
    /// </summary>
    internal int MicrotaskEpoch { get; private set; }

    public async ValueTask DisposeAsync()
    {
        CancelAllTimers();
        await StopEventLoopAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        CancelAllTimers();
        StopEventLoopAsync().GetAwaiter().GetResult();
    }

    internal void SetGlobalExecutionScope(JsEnvironment environment)
    {
        GlobalExecutionScope = environment;
    }

    /// <summary>
    ///     Returns a channel reader that can be used to read debug messages captured during execution.
    /// </summary>
    public ChannelReader<DebugMessage> DebugMessages()
    {
        if (_debugChannel is null)
        {
            throw new InvalidOperationException(
                "Debug mode is disabled. Enable DebugMode on JsEngineOptions to read debug messages.");
        }

        return _debugChannel.Reader;
    }

    /// <summary>
    ///     Returns a channel reader that can be used to read exceptions that occurred during execution.
    /// </summary>
    public ChannelReader<ExceptionInfo> Exceptions()
    {
        if (_exceptionChannel is null)
        {
            throw new InvalidOperationException(
                "Debug mode is disabled. Enable DebugMode on JsEngineOptions to read exceptions.");
        }

        return _exceptionChannel.Reader;
    }

    /// <summary>
    ///     Logs an exception to the exception channel.
    /// </summary>
    internal void LogException(Exception exception, string context, JsEnvironment? environment = null)
    {
        if (_exceptionChannel is null)
        {
            return;
        }

        var callStack = environment?.BuildCallStack() ?? [];
        var exceptionInfo = new ExceptionInfo(exception, context, callStack);
        _exceptionChannel.Writer.TryWrite(exceptionInfo);
    }

    /// <summary>
    ///     Captures the current execution state and writes a debug message to the debug channel.
    /// </summary>
    private JsValue CaptureDebugMessage(JsEnvironment environment, EvaluationContext context,
        IReadOnlyList<JsValue> args)
    {
        if (_debugChannel is null)
        {
            return JsValue.Undefined;
        }

        // Get all variables from the current environment and parent scopes
        var variables = environment.GetAllVariables();

        // Get the control flow state from the signal
        var controlFlowState = context.CurrentSignal switch
        {
            null => "None",
            BreakCompletionSignal => "Break",
            ContinueCompletionSignal => "Continue",
            ThrowFlowCompletionSignal => "Throw",
            YieldCompletionSignal => "Yield",
            PendingAwaitCompletionSignal => "PendingAwait",
            _ => "Unknown"
        };

        // Get the call stack by traversing the environment chain
        var callStack = environment.BuildCallStack();

        // Get the environment chain for detailed scope debugging
        var environmentChain = environment.BuildEnvironmentChain();

        // Create and write the debug message
        var debugMessage = new DebugMessage(variables, controlFlowState, callStack, environmentChain);
        _debugChannel.Writer.TryWrite(debugMessage);

        return JsValue.Undefined;
    }

    /// <summary>
    ///     Writes a trace message to the async iterator trace channel when tracing is enabled.
    ///     Internal helpers use this to surface branch decisions for testing and diagnostics.
    /// </summary>
    /// <param name="message">Human readable trace message.</param>
    internal void WriteAsyncIteratorTrace(string message)
    {
        if (!_asyncIteratorTracingEnabled || _asyncIteratorTraceChannel is null)
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
    ///     Parses JavaScript source code into a typed AST ready for execution.
    ///     Applies constant folding, scope analysis (for slot-based variable access),
    ///     and CPS transformation (for async/await). Returns a ProgramNode that
    ///     can be evaluated multiple times without re-parsing.
    /// </summary>
    public ProgramNode ParseProgram(
        string source,
        bool forceStrict = false,
        bool allowTopLevelAwait = false,
        bool allowHtmlComments = true,
        IJsEngineOptions? options = null)
    {
        var typedProgram =
            ParseTypedProgram(source, forceStrict, allowTopLevelAwait, allowHtmlComments, options ?? Options);
        var hasTopLevelAwait = ContainsTopLevelAwait(typedProgram);
        if (forceStrict && !typedProgram.IsStrict)
        {
            typedProgram = typedProgram with { IsStrict = true };
        }

        typedProgram = _typedConstantTransformer.Transform(typedProgram);

        // Warmup caches and build execution plans (which handle slot assignment)
        AstCacheWarmup.Warm(typedProgram);

        // NOTE: CPS transformation is no longer used for async functions.
        // Async functions now use the same IR executor as generators, with
        // _asyncStepMode=true for proper suspend/resume at await points.
        // This eliminates the overhead of AST rewriting and .then() chains.

        return typedProgram with { HasTopLevelAwait = hasTopLevelAwait };
    }

    /// <summary>
    ///     Executes a transformed program through the typed evaluator. The legacy
    ///     cons interpreter is no longer part of the runtime path; cons data is only
    ///     used earlier for parsing and transformation.
    /// </summary>
    internal object? ExecuteProgram(
        ProgramNode program,
        JsEnvironment environment,
        CancellationToken cancellationToken = default,
        ExecutionKind executionKind = ExecutionKind.Script)
    {
        return program.EvaluateProgram(environment, RealmState, cancellationToken, executionKind);
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

        return (original, constantFolded, cpsTransformed);
    }

    private static ProgramNode ParseTypedProgram(
        string source,
        bool forceStrict = false,
        bool allowTopLevelAwait = false,
        bool allowHtmlComments = true,
        IJsEngineOptions? options = null)
    {
        var lexer = new JsLexer(source, allowHtmlComments);
        var tokens = lexer.Tokenize();
        var typedParser = new JsAstParser(tokens, source, forceStrict, allowTopLevelAwait, options);
        return typedParser.ParseProgram();
    }

    private ProgramNode ParseProgramOrThrowSyntaxError(
        string source,
        bool forceStrict = false,
        bool allowTopLevelAwait = false,
        bool allowHtmlComments = true,
        IJsEngineOptions? options = null)
    {
        // Parse errors propagate as ParseException to the .NET caller.
        // There's no JS code running yet that could catch the error.
        return ParseProgram(source, forceStrict, allowTopLevelAwait, allowHtmlComments, options);
    }

    private CancellationToken CreateEvaluationCancellationToken(CancellationToken cancellationToken,
        out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (ExecutionTimeout is not { } timeout || timeout <= TimeSpan.Zero ||
            timeout == Timeout.InfiniteTimeSpan)
        {
            return cancellationToken;
        }

        var cts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        cts.CancelAfter(timeout);
        timeoutCts = cts;
        return cts.Token;

    }

    private void StartEventLoop()
    {
        if (_eventQueue is not null)
        {
            return;
        }

        // Note: Don't reset _activeTimerCount or _pendingTaskCount here!
        // Timers may have been scheduled during sync evaluation that we need to wait for.

        _eventQueue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });
        _eventLoopTask = Task.Run(() => ProcessEventQueue(_eventQueue));
    }

    private async Task DrainEventLoopAsync(CancellationToken cancellationToken)
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

            if (_drainCompletionSource?.Task.IsCompleted != false)
            {
                _drainCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            drainTask = _drainCompletionSource.Task;
        }

        // Wait for drain using the caller's cancellation token.
        // The caller (Evaluate) handles the ExecutionTimeout configuration.
        try
        {
            await drainTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled but not by the caller's token - unexpected, cancel timers
            CancelAllTimers();
        }
    }

    internal void DrainEventLoopUntilIdle(CancellationToken cancellationToken = default)
    {
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        try
        {
            DrainEventLoopAsync(combinedToken).GetAwaiter().GetResult();
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private bool IsEventLoopDrained()
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
        var program = ParseProgramOrThrowSyntaxError(source);
        return Evaluate(program, cancellationToken);
    }

    /// <summary>
    ///     Evaluates a pre-parsed program and schedules it on the event queue.
    ///     Useful when running the same script repeatedly to avoid re-parsing.
    /// </summary>
    public Task<object?> Evaluate(ProgramNode program, CancellationToken cancellationToken = default)
    {
        return Evaluate(program, cancellationToken, null);
    }

    private static object? UnwrapResult(object? result)
    {
#pragma warning disable CS0618 // Public API boundary: must return object? for external callers
        return result is JsValue jsValue ? jsValue.ToObject() : result;
#pragma warning restore CS0618
    }

    /// <summary>
    ///     Evaluates JavaScript source code and returns the value of the final identifier
    ///     AFTER all microtasks have drained.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     This is a convenience method that behaves like Jint's Evaluate() for async code.
    ///     If the script ends with a bare identifier (e.g., "finalResult;"), this method
    ///     will execute the script, drain all microtasks, and then return the updated value
    ///     of that identifier.
    /// </para>
    /// <para>    Example:</para>
    ///     <code>
    ///     let finalResult = 0;
    ///     Promise.resolve(42).then(x => { finalResult = x; });
    ///     finalResult;  // Regular Evaluate returns 0, EvaluateAndAwait returns 42
    ///     </code>
    ///
    ///     If the script does not end with an identifier expression, this behaves
    ///     identically to <see cref="Evaluate"/>.
    /// </remarks>
    /// <param name="source">The JavaScript source code to evaluate.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The result of the evaluation after microtasks have drained.</returns>
    public async Task<object?> EvaluateAndAwait(string source, CancellationToken cancellationToken = default)
    {
        var program = ParseProgramOrThrowSyntaxError(source);

        // Check if the last statement is an expression statement with an identifier
        Symbol? trailingIdentifier = null;
        if (program.Body.Length > 0 &&
            program.Body[^1] is ExpressionStatement { Expression: IdentifierExpression identifier })
        {
            trailingIdentifier = identifier.Name;
        }

        // Execute the program normally (this will drain microtasks)
        var result = await Evaluate(program, cancellationToken).ConfigureAwait(false);

        // If there was a trailing identifier, re-evaluate it after microtasks have drained
        if (trailingIdentifier is not null)
        {
            return await Evaluate(trailingIdentifier.Name, cancellationToken).ConfigureAwait(false);
        }

        // No trailing identifier, return the original result
        return result;
    }

    /// <summary>
    ///     Evaluates a pre-parsed program and drains microtasks before returning.
    ///     Use this overload to avoid reparsing when running the same script many times.
    /// </summary>
    public async Task<object?> EvaluateAndAwait(ProgramNode program, CancellationToken cancellationToken = default)
    {
        // Check if the last statement is an expression statement with an identifier
        Symbol? trailingIdentifier = null;
        if (program.Body.Length > 0 &&
            program.Body[^1] is ExpressionStatement { Expression: IdentifierExpression identifier })
        {
            trailingIdentifier = identifier.Name;
        }

        var result = await Evaluate(program, cancellationToken).ConfigureAwait(false);

        if (trailingIdentifier is not null)
        {
            return await Evaluate(trailingIdentifier.Name, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    ///     Synchronously evaluates JavaScript source code without using the event loop.
    ///     This is much faster for code that doesn't require async features (setTimeout,
    ///     Promises, async/await, etc.). Use this when you know the code is purely synchronous.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     This method does NOT support:
    ///     - setTimeout/setInterval callbacks
    ///     - Promise resolution (Promises will be returned but not awaited)
    ///     - async/await (will throw or return unresolved promises)
    ///     - Any other event-loop dependent features
    /// </para>
    /// <para>    For code that uses these features, use <see cref="Evaluate"/> instead.</para>
    /// </remarks>
    /// <param name="source">The JavaScript source code to evaluate.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The result of the evaluation.</returns>
    public object? EvaluateSync(string source, CancellationToken cancellationToken = default)
    {
        var program = ParseProgramOrThrowSyntaxError(source);
        return EvaluateSyncInternal(program, cancellationToken);
    }

    /// <summary>
    ///     Synchronously evaluates a pre-parsed program without using the event loop.
    /// </summary>
    private object? EvaluateSyncInternal(
        ProgramNode program,
        CancellationToken cancellationToken = default,
        string? sourcePath = null,
        bool forceModule = false)
    {
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        try
        {
            var isModule = forceModule || HasModuleStatements(program);
            EnsureImportMetaAllowed(program, isModule);
            if (!isModule)
            {
                return ExecuteProgram(program, GlobalEnvironment, combinedToken);
            }

            var entry = GetOrCreateModuleEntry(program, sourcePath);

            EnsureModuleInstantiated(entry);
            if (entry.IsAsync || entry.HasAsyncDependency)
            {
                throw new NotSupportedException(
                    "EvaluateSync does not support async modules (top-level await / async dependencies). Use Evaluate/EvaluateModule instead.");
            }

            EnsureModuleEvaluated(entry);
            return entry.LastValue;
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    public Task<object?> EvaluateModule(string source, string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        var program = ParseProgramOrThrowSyntaxError(source, forceStrict: true, allowTopLevelAwait: true);
        return Evaluate(program, cancellationToken, sourcePath, true);
    }

    /// <summary>
    ///     Evaluates a program with lazy event loop initialization.
    ///     Runs synchronously first, then only starts the event loop if async work is pending.
    /// </summary>
    private async Task<object?> Evaluate(
        ProgramNode program,
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
            var isModule = forceModule || HasModuleStatements(program);
            if (isModule)
            {
                var entry = GetOrCreateModuleEntry(program, sourcePath);

                EnsureModuleInstantiated(entry);
                if (entry.IsAsync || entry.HasAsyncDependency)
                {
                    await EnsureModuleEvaluatedAsync(entry, cancellationToken: combinedToken).ConfigureAwait(false);
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
            DrainMicrotasks(cancellationToken: combinedToken);

            // Step 2: Check if any async work was scheduled (timers, promises, etc.)
            if (IsEventLoopDrained())
            {
                // Fast path: No async work pending, return immediately
                return UnwrapResult(result);
            }

            var configured = ExecutionTimeout;
            var enforceTimeout = configured > TimeSpan.Zero &&
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

            return UnwrapResult(result);
        }
        finally
        {
            CancelAllTimers();
            await StopEventLoopAsync().ConfigureAwait(false);
            timeoutCts?.Dispose();
        }
    }

    private object? EvaluateInline(
        ProgramNode program,
        CancellationToken cancellationToken,
        string? sourcePath = null,
        bool forceModule = false)
    {
        var combinedToken = CreateEvaluationCancellationToken(cancellationToken, out var timeoutCts);
        try
        {
            var isModule = forceModule || HasModuleStatements(program);
            EnsureImportMetaAllowed(program, isModule);
            if (isModule)
            {
                var entry = GetOrCreateModuleEntry(program, sourcePath);

                EnsureModuleInstantiated(entry);
                if (entry.IsAsync || entry.HasAsyncDependency)
                {
                    if (!entry.Evaluated)
                    {
                        throw new NotSupportedException(
                            "Inline evaluation of async modules is not supported without blocking. Use Evaluate/EvaluateModule from outside the engine event loop.");
                    }
                }
                else
                {
                    EnsureModuleEvaluated(entry);
                }

                DrainMicrotasks(cancellationToken: combinedToken);
                return UnwrapResult(entry.LastValue);
            }

            var scriptResult = ExecuteProgram(program, GlobalEnvironment, combinedToken);
            DrainMicrotasks(cancellationToken: combinedToken);
            return UnwrapResult(scriptResult);
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
                    return returnStatement.Expression is { } returnExpression &&
                           ExpressionContainsImportMeta(returnExpression);
                case ThrowStatement throwStatement:
                    return ExpressionContainsImportMeta(throwStatement.Expression);
                case IfStatement ifStatement:
                    return ExpressionContainsImportMeta(ifStatement.Condition) ||
                           StatementContainsImportMeta(ifStatement.Then) || (ifStatement.Else is { } elseBranch &&
                                                                             StatementContainsImportMeta(elseBranch));
                case WhileStatement whileStatement:
                    return ExpressionContainsImportMeta(whileStatement.Condition) ||
                           StatementContainsImportMeta(whileStatement.Body);
                case DoWhileStatement doWhileStatement:
                    return StatementContainsImportMeta(doWhileStatement.Body) ||
                           ExpressionContainsImportMeta(doWhileStatement.Condition);
                case WithStatement withStatement:
                    return ExpressionContainsImportMeta(withStatement.Object) ||
                           StatementContainsImportMeta(withStatement.Body);
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
                    return BindingContainsImportMeta(forEachStatement.Target) ||
                           ExpressionContainsImportMeta(forEachStatement.Iterable) ||
                           StatementContainsImportMeta(forEachStatement.Body);
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

                    return tryStatement.Finally is { } finallyBlock && StatementContainsImportMeta(finallyBlock);
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
        return moduleStatement switch
        {
            ExportDefaultStatement { Value: ExportDefaultExpression { Expression: { } expression } } =>
                ExpressionContainsImportMeta(expression),
            ExportDefaultStatement { Value: ExportDefaultDeclaration { Declaration: { } declaration } } =>
                StatementContainsImportMeta(declaration),
            ExportDeclarationStatement { Declaration: { } declaration } => StatementContainsImportMeta(declaration),
            _ => false
        };
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
            if (member is { IsComputed: true, ComputedName: { } computedName } &&
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
            if (field is { IsComputed: true, ComputedName: { } computedName } &&
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

                        if (property.NameExpression is { } nameExpression &&
                            ExpressionContainsImportMeta(nameExpression))
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
                    return ExpressionContainsImportMeta(conditional.Test) ||
                           ExpressionContainsImportMeta(conditional.Consequent) ||
                           ExpressionContainsImportMeta(conditional.Alternate);
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
                    return ExpressionContainsImportMeta(propertyAssignment.Target) ||
                           ExpressionContainsImportMeta(propertyAssignment.Property) ||
                           ExpressionContainsImportMeta(propertyAssignment.Value);
                case IndexAssignmentExpression indexAssignment:
                    return ExpressionContainsImportMeta(indexAssignment.Target) ||
                           ExpressionContainsImportMeta(indexAssignment.Index) ||
                           ExpressionContainsImportMeta(indexAssignment.Value);
                case SequenceExpression sequence:
                    return ExpressionContainsImportMeta(sequence.Left) || ExpressionContainsImportMeta(sequence.Right);
                case DestructuringAssignmentExpression destructuringAssignment:
                    return BindingContainsImportMeta(destructuringAssignment.Target) ||
                           ExpressionContainsImportMeta(destructuringAssignment.Value);
                case ArrayExpression arrayExpression:
                    foreach (var element in arrayExpression.Elements)
                    {
                        if (element.Expression is { } elementExpression &&
                            ExpressionContainsImportMeta(elementExpression))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression objectExpression:
                    foreach (var member in objectExpression.Members)
                    {
                        if (member is { IsComputed: true, Key: ExpressionNode computedKey } &&
                            ExpressionContainsImportMeta(computedKey))
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
                    if (ExpressionContainsImportMeta(taggedTemplate.Tag) ||
                        ExpressionContainsImportMeta(taggedTemplate.StringsArray) ||
                        ExpressionContainsImportMeta(taggedTemplate.RawStringsArray))
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

    private static ProgramNode EnsureStrictProgram(ProgramNode program)
    {
        return program.IsStrict
            ? program
            : program with { IsStrict = true };
    }

    private ModuleEntry CreateModuleEntry(ProgramNode program, JsEnvironment environment, JsObject exports,
        string? modulePath, bool hasTopLevelAwait = false)
    {
        var entry = new ModuleEntry(modulePath ?? string.Empty, program, environment, exports)
        {
            IsAsync = hasTopLevelAwait || ContainsTopLevelAwait(program)
        };
        environment.IsAsyncModule = entry.IsAsync;
        EnsureModuleImportMeta(entry);
        return entry;
    }

    /// <summary>
    /// Gets an existing module entry from the registry or creates a new one.
    /// Also computes and sets HasAsyncDependency.
    /// </summary>
    private ModuleEntry GetOrCreateModuleEntry(ProgramNode program, string? sourcePath)
    {
        ModuleEntry entry;
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var moduleKey = NormalizeModulePath(sourcePath!, null, _moduleLoader is not null);
            if (!_moduleRegistry.TryGetValue(moduleKey, out entry!))
            {
                entry = CreateModuleEntry(EnsureStrictProgram(program),
                    CreateModuleEnvironment(moduleKey),
                    new JsObject(),
                    moduleKey,
                    program.HasTopLevelAwait);
                _moduleRegistry[moduleKey] = entry;
            }
        }
        else
        {
            entry = CreateModuleEntry(EnsureStrictProgram(program),
                CreateModuleEnvironment(),
                new JsObject(),
                string.Empty,
                program.HasTopLevelAwait);
        }

        entry.HasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
            new HashSet<string>(StringComparer.Ordinal));

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
            realmPrototype.TryGetObject<IJsPropertyAccessor>(out var promisePrototypeAccessorFromCtor))
        {
            return promisePrototypeAccessorFromCtor;
        }

        if (GlobalObject.TryGetProperty("Promise", out var promiseCtor) &&
            promiseCtor.TryGetObject<IJsPropertyAccessor>(out var promiseCtorAccessor) &&
            promiseCtorAccessor.TryGetProperty("prototype", out var promiseProto) &&
            promiseProto.TryGetObject<IJsPropertyAccessor>(out var promisePrototypeAccessor))
        {
            return promisePrototypeAccessor;
        }

        return null;
    }

    internal JsPromise CreateRealmPromise()
    {
        var prototype = ResolvePromisePrototypeInternal();
        var promise = prototype is null
            ? PromiseHelper.CreatePromise(RealmState)
            : PromiseHelper.CreatePromise(RealmState, prototype as IJsObjectLike);
        return promise;
    }

    private static bool ContainsTopLevelAwait(ProgramNode program)
    {
        foreach (var statement in program.Body)
        {
            if (AstShapeAnalyzer.StatementContainsAwait(statement))
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
                        importStatement.Attributes, false);
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

    private void EnsureModuleImportMeta(ModuleEntry entry)
    {
        if (entry.ImportMeta is { } existing)
        {
            if (!entry.Environment.HasBinding(Symbol.ImportMeta))
            {
                entry.Environment.DefineJsValue(Symbol.ImportMeta, (JsValue)existing, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: false);
            }

            return;
        }

        var importMeta = new JsObject { RealmState = RealmState };
        importMeta.SetPrototype(null);
        importMeta.DefineProperty("url",
            new PropertyDescriptor
            {
                Value = entry.Path,
                Writable = true,
                Enumerable = true,
                Configurable = true
            });

        entry.Environment.DefineJsValue(Symbol.ImportMeta, (JsValue)importMeta, true, isLexicalBinding: true,
            blocksFunctionScopeOverride: false);
        entry.ImportMeta = importMeta;
    }

    /// <summary>
    /// Creates a module environment with the correct `this` binding (undefined per ES spec).
    /// </summary>
    private JsEnvironment CreateModuleEnvironment(string? modulePath = null)
    {
        var moduleEnv = JsEnvironment.CreateInstance(GlobalEnvironment, true, true);
        // Per ECMAScript spec, `this` in module scope is undefined
        moduleEnv.DefineJsValue(Symbol.This, JsValue.Undefined);
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
        if (entry.Evaluated)
        {
            return;
        }

        EnsureModuleInstantiated(entry);

        if (entry.IsAsync || entry.HasAsyncDependency)
        {
            throw new NotSupportedException(
                "Synchronous module evaluation is not supported for async modules. Use EnsureModuleEvaluatedAsync/Evaluate instead.");
        }

        if (entry.Evaluating)
        {
            return;
        }

        entry.Evaluating = true;
        try
        {
            entry.LastValue = ExecuteModuleBody(entry.Program, entry.Environment, entry.Exports, entry.Path);
            entry.Evaluated = true;
        }
        finally
        {
            entry.Evaluating = false;
        }
    }

    private async Task<object?> EnsureModuleEvaluatedAsync(ModuleEntry entry, bool waitForAsync = true,
        CancellationToken cancellationToken = default)
    {
        if (entry.Evaluated)
        {
            return await (entry.EvaluationTask ?? Task.FromResult(entry.LastValue));
        }

        EnsureModuleInstantiated(entry);

        var requiresAsyncEvaluation = entry.IsAsync || entry.HasAsyncDependency;

        if (!requiresAsyncEvaluation)
        {
            if (entry.Evaluating)
            {
                return await (entry.EvaluationTask ?? Task.FromResult(entry.LastValue));
            }

            entry.Evaluating = true;
            try
            {
                entry.LastValue = ExecuteModuleBody(entry.Program, entry.Environment, entry.Exports, entry.Path);
                entry.Evaluated = true;
                return await (entry.EvaluationTask ?? Task.FromResult(entry.LastValue));
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
                ? EvaluateModuleBodyWithTopLevelAwait(entry)
                : EvaluateModuleBodyWithAsyncDependencies(entry);
        }

        if (!waitForAsync)
        {
            return await entry.EvaluationTask;
        }

        if (_eventLoopThreadId == Environment.CurrentManagedThreadId)
        {
            // Never attempt to pump the event queue from within the event loop thread.
            // Callers running on the event loop must observe completion asynchronously.
            return await entry.EvaluationTask;
        }

        return await AwaitModuleEvaluationAsync(entry.EvaluationTask, cancellationToken);
    }

    private async Task<object?> AwaitModuleEvaluationAsync(Task<object?> evaluationTask,
        CancellationToken cancellationToken,
        int callerEpoch = int.MaxValue)
    {
        if (evaluationTask.IsCompleted)
        {
            return await evaluationTask.ConfigureAwait(false);
        }

        // When waiting for an async module dependency, we must not drain microtasks
        // that were queued by the calling module's synchronous code. Only drain
        // microtasks from earlier epochs (before the caller started).
        // If callerEpoch is 0, maxDrainEpoch becomes -1, which drains nothing.
        var maxDrainEpoch = callerEpoch < int.MaxValue ? callerEpoch - 1 : int.MaxValue;

        while (!evaluationTask.IsCompleted)
        {
            if (_eventQueue is null)
            {
                // Only drain microtasks from epochs before the caller's epoch
                DrainMicrotasks(maxDrainEpoch, cancellationToken: cancellationToken);
                await Task.Yield();
                continue;
            }

            var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ScheduleTask(() => tick.TrySetResult());

            await tick.Task.ConfigureAwait(false);
        }

        // After evaluation completes, still only drain earlier epochs
        DrainMicrotasks(maxDrainEpoch, cancellationToken: cancellationToken);
        return await evaluationTask.ConfigureAwait(false);
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
            GlobalEnvironment.DefineJsValue(symbol, JsValue.FromObjectUnsafe(value), isGlobalConstant: isGlobalConstant,
                isLexicalBinding: false);
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

            if (RealmState.FunctionPrototype is not null &&
                hostFunction.Properties.Prototype is null &&
                hostFunction.Properties.PrototypeAccessor is null)
            {
                hostFunction.Properties.SetPrototype(RealmState.FunctionPrototype);
            }
        }
        else if (value is JsObject { RealmState: null } jsObject)
        {
            jsObject.RealmState = RealmState;
        }

        GlobalObject.DefineProperty(name,
            new PropertyDescriptor
            {
                Value = value,
                Writable = !isGlobalConstant,
                Enumerable = false,
                Configurable = !isGlobalConstant
            });
    }

    /// <summary>
    /// Creates the %ThrowTypeError% intrinsic function per realm (ES spec 10.2.4).
    /// This is a single shared frozen function that throws TypeError when called.
    /// It is used for strict mode arguments callee/caller accessors.
    /// Called early (before Function constructor) so it can be shared. Finalized later.
    /// </summary>
    private void CreateThrowTypeErrorIntrinsic()
    {
        var thrower = new HostFunction((_, _) =>
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    "'caller', 'callee', and 'arguments' properties may not be accessed on strict mode functions or the arguments objects for calls to them",
                    null, RealmState)),
            isConstructor: false)
        {
            RealmState = RealmState
        };

        // Per spec: %ThrowTypeError% has length 0 and name ""
        StandardLibrary.DefineConstantProperty(thrower.Properties, "length", 0d, configurable: false);
        StandardLibrary.DefineConstantProperty(thrower.Properties, "name", string.Empty, configurable: false);

        RealmState.ThrowTypeErrorIntrinsic = thrower;
    }

    /// <summary>
    /// Finalizes the %ThrowTypeError% intrinsic: sets the prototype to Function.prototype
    /// and freezes it. Called after all dependencies (Function prototype, error constructors) are available.
    /// </summary>
    private void FinalizeThrowTypeErrorIntrinsic()
    {
        var thrower = RealmState.ThrowTypeErrorIntrinsic;
        if (thrower is null)
        {
            return;
        }

        // Set prototype to Function.prototype (now available)
        if (RealmState.FunctionPrototype is not null && thrower.Properties.Prototype is null)
        {
            thrower.Properties.SetPrototype(RealmState.FunctionPrototype);
        }

        // Per spec: %ThrowTypeError% is frozen and non-extensible
        thrower.Properties.PreventExtensions();
        thrower.Properties.Freeze();
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
    public void SetGlobalFunction(string name, JsSimpleHandler handler)
    {
        SetGlobal(name, new HostFunction(handler) { Realm = GlobalObject }, registerBinding: true);
    }

    /// <summary>
    ///     Registers a host function that receives the <c>this</c> binding.
    /// </summary>
    public void SetGlobalFunction(string name, JsHostHandler handler)
    {
        GlobalEnvironment.DefineJsValue(Symbol.Intern(name),
            (JsValue)new HostFunction(handler) { Realm = GlobalObject });
    }

    /// <summary>
    ///     Registers an async host function that returns a Promise to JavaScript.
    ///     The .NET Task is automatically bridged to a JS Promise.
    /// </summary>
    /// <param name="name">The name of the global function.</param>
    /// <param name="handler">An async function that returns a Task&lt;JsValue&gt;.</param>
    public void SetGlobalAsyncFunction(string name, JsAsyncSimpleHandler handler)
    {
        SetGlobal(name, new HostFunction(args => CreatePromiseFromTask(handler(args))) { Realm = GlobalObject },
            registerBinding: true);
    }

    /// <summary>
    ///     Registers an async host function that receives the <c>this</c> binding and returns a Promise.
    ///     The .NET Task is automatically bridged to a JS Promise.
    /// </summary>
    /// <param name="name">The name of the global function.</param>
    /// <param name="handler">An async function that returns a Task&lt;JsValue&gt;.</param>
    public void SetGlobalAsyncFunction(string name, JsAsyncHostHandler handler)
    {
        GlobalEnvironment.DefineJsValue(Symbol.Intern(name),
            (JsValue)new HostFunction((thisValue, args) => CreatePromiseFromTask(handler(thisValue, args)))
            {
                Realm = GlobalObject
            });
    }

    /// <summary>
    ///     Creates a JavaScript Promise from a .NET Task.
    ///     When the task completes, the promise is resolved with the result.
    ///     When the task fails, the promise is rejected with the error message.
    /// </summary>
    /// <param name="task">The .NET Task to bridge.</param>
    /// <returns>A JsValue containing the Promise object.</returns>
    private JsValue CreatePromiseFromTask(Task<JsValue> task)
    {
        var promise = CreateRealmPromise();

        ScheduleAfterTask(
            task,
            promise.Resolve,
            ex => promise.Reject((JsValue)ex.Message));

        return (JsValue)promise.JsObject;
    }

    /// <summary>
    ///     Creates a JavaScript Promise from a .NET Task with a value converter.
    ///     When the task completes, the result is converted to JsValue and the promise is resolved.
    ///     When the task fails, the promise is rejected with the error message.
    /// </summary>
    /// <typeparam name="T">The type of the task result.</typeparam>
    /// <param name="task">The .NET Task to bridge.</param>
    /// <param name="converter">A function to convert the .NET result to JsValue.</param>
    /// <returns>A JsValue containing the Promise object.</returns>
    public JsValue CreatePromiseFromTask<T>(Task<T> task, Func<T, JsValue> converter)
    {
        var promise = CreateRealmPromise();

        ScheduleAfterTask(
            task,
            result => promise.Resolve(converter(result)),
            ex => promise.Reject((JsValue)ex.Message));

        return (JsValue)promise.JsObject;
    }

    /// <summary>
    ///     Schedules an asynchronous task to be executed on the event queue.
    /// </summary>
    /// <param name="task">The asynchronous task to execute.</param>
    public void ScheduleTask(Action task)
    {
        StartEventLoop();
        var queue = _eventQueue ?? throw new InvalidOperationException("Event loop is not running.");

        Interlocked.Increment(ref _pendingTaskCount);
        var written = queue.Writer.TryWrite(task);
        if (written)
        {
            return;
        }
        // If we failed to enqueue (e.g., shutting down), decrement immediately.
        Interlocked.Decrement(ref _pendingTaskCount);
        TrySignalDrainComplete();
    }

    /// <summary>
    ///     Schedules a continuation to run on the event queue after an external task completes.
    ///     The pending task count is incremented immediately, ensuring the event loop waits
    ///     for the external task to complete. The external task runs on the thread pool,
    ///     not blocking the event loop.
    /// </summary>
    /// <param name="taskToAwait">The external task to wait for (e.g., Task.Delay, IO operation).</param>
    /// <param name="continuation">The continuation to run on the event queue after the task completes.</param>
    public void ScheduleAfterTask(Task taskToAwait, Action continuation)
    {
        StartEventLoop();
        var queue = _eventQueue ?? throw new InvalidOperationException("Event loop is not running.");

        // Increment immediately to track pending work
        Interlocked.Increment(ref _pendingTaskCount);

        _ = taskToAwait.ContinueWith(t =>
        {
            // Task completed on thread pool, now schedule continuation on event loop
            // Write directly to queue - don't use ScheduleTask as that would increment again
            var written = queue.Writer.TryWrite(() =>
            {
                // If the task failed, log the error but still run the continuation
                // (the continuation may handle the error state)
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? t.Exception;
                    if (ex is not null)
                    {
                        RealmState.Logger?.LogError(ex,
                            "[ScheduleAfterTask] Task faulted: {ErrorType}: {ErrorMessage}",
                            ex.GetType().Name,
                            ex.Message);
                    }
                }
                else if (t.IsCanceled)
                {
                    RealmState.Logger?.LogWarning("[ScheduleAfterTask] Task was canceled");
                }

                continuation();
            });
            // ProcessEventQueue will decrement _pendingTaskCount when this completes
            if (written)
            {
                return;
            }

            // If enqueue fails (queue closed), decrement immediately.
            Interlocked.Decrement(ref _pendingTaskCount);
            TrySignalDrainComplete();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    ///     Schedules a continuation to run on the event queue after an async task completes.
    ///     The task result is passed to the onSuccess callback, or the exception to onFailure.
    ///     This is the preferred method for bridging .NET async operations to JS promises.
    /// </summary>
    /// <typeparam name="T">The type of the task result.</typeparam>
    /// <param name="taskToAwait">The external task to wait for (e.g., File.ReadAllTextAsync).</param>
    /// <param name="onSuccess">Callback invoked with the result when the task succeeds.</param>
    /// <param name="onFailure">Callback invoked with the exception when the task fails or is canceled.</param>
    private void ScheduleAfterTask<T>(Task<T> taskToAwait, Action<T> onSuccess, Action<Exception> onFailure)
    {
        StartEventLoop();
        var queue = _eventQueue ?? throw new InvalidOperationException("Event loop is not running.");

        // Increment immediately to track pending work
        Interlocked.Increment(ref _pendingTaskCount);

        _ = taskToAwait.ContinueWith(t =>
        {
            // Task completed on thread pool, now schedule continuation on event loop
            var written = queue.Writer.TryWrite(() =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? t.Exception ?? new Exception("Task faulted");
                    onFailure(ex);
                }
                else if (t.IsCanceled)
                {
                    onFailure(new OperationCanceledException("Task was canceled"));
                }
                else
                {
                    try
                    {
                        onSuccess(t.Result);
                    }
                    catch (Exception ex)
                    {
                        // If onSuccess throws, call onFailure
                        onFailure(ex);
                    }
                }
            });
            if (written)
            {
                return;
            }

            // If enqueue fails (queue closed), decrement immediately.
            Interlocked.Decrement(ref _pendingTaskCount);
            TrySignalDrainComplete();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    ///     Tracks a pending async task without scheduling any event queue callbacks.
    ///     The pending task count is incremented immediately, ensuring the event loop waits
    ///     for the task to complete. When the task completes, the count is decremented.
    ///     Use this when the task itself will schedule its own callbacks via ScheduleTask.
    /// </summary>
    /// <param name="task">The async task to track.</param>
    private void TrackPendingAsyncWork(Task task)
    {
        if (task.IsCompleted)
        {
            // If the task failed, log the error
            if (task.IsFaulted)
            {
                var ex = task.Exception?.GetBaseException() ?? task.Exception;
                if (ex is not null)
                {
                    RealmState.Logger?.LogError(ex,
                        "[TrackPendingAsyncWork] Tracked task faulted: {ErrorType}: {ErrorMessage}",
                        ex.GetType().Name,
                        ex.Message);
                }
            }
            else if (task.IsCanceled)
            {
                RealmState.Logger?.LogWarning("[TrackPendingAsyncWork] Tracked task was canceled");
            }

            return;
        }

        StartEventLoop();

        // Increment immediately to track pending work
        Interlocked.Increment(ref _pendingTaskCount);

        _ = task.ContinueWith(t =>
        {
            // If the task failed, log the error
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException() ?? t.Exception;
                if (ex is not null)
                {
                    RealmState.Logger?.LogError(ex,
                        "[TrackPendingAsyncWork] Tracked task faulted: {ErrorType}: {ErrorMessage}",
                        ex.GetType().Name,
                        ex.Message);
                }
            }
            else if (t.IsCanceled)
            {
                RealmState.Logger?.LogWarning("[TrackPendingAsyncWork] Tracked task was canceled");
            }

            // Task completed - decrement the counter
            // The task's internal ScheduleTask calls will have their own increment/decrement cycle
            Interlocked.Decrement(ref _pendingTaskCount);
            TrySignalDrainComplete();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    ///     Processes all events in the event queue until it's empty.
    ///     Each event is executed synchronously and any new events scheduled during execution
    ///     will also be processed.
    ///     Exceptions from individual tasks are caught and logged to prevent the event loop from stopping.
    /// </summary>
    private async Task ProcessEventQueue(Channel<Action> queue)
    {
        _eventLoopThreadId = Environment.CurrentManagedThreadId;
        try
        {
            await foreach (var action in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                _eventLoopThreadId = Environment.CurrentManagedThreadId;
                const bool decrementInline = true;
                try
                {
                    action();
                    _eventLoopThreadId = Environment.CurrentManagedThreadId;
                }
                catch (OutOfMemoryException)
                {
                    RealmState.Logger?.LogError("[ProcessEventQueue] OOM Exception");
                }
                catch (StackOverflowException)
                {
                    RealmState.Logger?.LogError("[ProcessEventQueue] Stack overflow occurred in event queue task");
                }
                catch (Exception ex)
                {
                    // Log the exception but don't let it kill the event loop
                    // Individual task failures should not stop the event queue processing
                    RealmState.Logger?.LogError(ex,
                        "[ProcessEventQueue] Unhandled exception in event queue task: {ErrorType}: {ErrorMessage}",
                        ex.GetType().Name,
                        ex.Message);
                }
                finally
                {
                    if (decrementInline)
                    {
                        DrainMicrotasks();
                        // Decrement the pending task count after processing
                        Interlocked.Decrement(ref _pendingTaskCount);
                        TrySignalDrainComplete();
                    }
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
    ///     The microtask's epoch is set to the current epoch for proper timing semantics.
    /// </summary>
    internal void QueueMicrotask(IMicrotask task)
    {
        task.Epoch = MicrotaskEpoch;
        _microtaskQueue.Enqueue(task);
    }

    /// <summary>
    ///     Advances the microtask epoch, marking a new execution phase.
    ///     Microtasks queued before this call will not be drained until
    ///     the epoch advances past them or a full drain is requested.
    /// </summary>
    private void AdvanceMicrotaskEpoch()
    {
        MicrotaskEpoch++;
    }

    private List<IMicrotask> DetachMicrotasks()
    {
        var preserved = new List<IMicrotask>(_microtaskQueue.Count);
        while (_microtaskQueue.Count > 0)
        {
            preserved.Add(_microtaskQueue.Dequeue());
        }

        return preserved;
    }

    private void PrependMicrotasks(List<IMicrotask>? tasks)
    {
        if (tasks is null || tasks.Count == 0)
        {
            return;
        }

        if (_microtaskQueue.Count == 0)
        {
            foreach (var task in tasks)
            {
                _microtaskQueue.Enqueue(task);
            }

            return;
        }

        var existing = new Queue<IMicrotask>(_microtaskQueue);
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

    /// <summary>
    ///     Drains pending microtasks synchronously.
    ///     If maxEpoch is specified, only microtasks queued in epochs &lt;= maxEpoch are executed.
    ///     Microtasks from later epochs are preserved for future draining.
    /// </summary>
    /// <param name="maxEpoch">Maximum epoch to drain. Use int.MaxValue to drain all epochs (default).</param>
    /// <param name="cancellationToken"></param>
    internal void DrainMicrotasks(int maxEpoch = int.MaxValue, bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (_isDrainingMicrotasks)
        {
            return;
        }

        // Don't drain microtasks during module body execution unless explicitly forced.
        // This ensures Promise.resolve().then() callbacks only run after the synchronous
        // module body completes, matching ES specification semantics.
        if (_moduleBodyExecutionDepth > 0 && !force)
        {
            return;
        }

        _isDrainingMicrotasks = true;

        try
        {
            List<IMicrotask>? deferred = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_microtaskQueue.Count == 0)
                {
                    break;
                }

                var task = _microtaskQueue.Dequeue();

                // If this task is from a later epoch than allowed, defer it
                if (task.Epoch > maxEpoch)
                {
                    deferred ??= [];
                    deferred.Add(task);
                    continue;
                }

                try
                {
                    task.Execute();
                }
                catch (Exception ex)
                {
                    // Log but don't propagate - microtask exceptions shouldn't kill the drain
                    RealmState.Logger?.LogError(ex,
                        "[DrainMicrotasks] Exception: {ErrorType}: {ErrorMessage}",
                        ex.GetType().Name,
                        ex.Message);
                }
            }

            if (deferred is not { Count: > 0 })
            {
                return;
            }

            //TODO: this seems wrong, maybe we should just have a priority queue instead of this dance
            foreach (var deferredTask in deferred)
            {
                _microtaskQueue.Enqueue(deferredTask);
            }
        }
        finally
        {
            _isDrainingMicrotasks = false;
        }
    }

    /// <summary>
    ///     Implements the queueMicrotask global function.
    ///     Queues a callback to be executed as a microtask, running after the current
    ///     synchronous code completes but before the next macrotask.
    /// </summary>
    private JsValue QueueMicrotaskGlobal(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetCallable(out var callback))
        {
            throw new ThrowSignal(new JsValue("TypeError: queueMicrotask requires a callable argument"));
        }

        QueueMicrotask(JsCallableMicrotask.Rent(callback));
        return JsValue.Undefined;
    }

    /// <summary>
    ///     Implements setTimeout - schedules a callback to run after a delay.
    /// </summary>
    private JsValue SetTimeout(IReadOnlyList<JsValue> args)
    {
        if (!args[0].TryGetObject<IJsCallable>(out var callback))
        {
            return JsValue.Undefined;
        }

        var delay = args[1].TryGetDouble(out var d) ? (int)d : 0;
        var timerId = Interlocked.Increment(ref _nextTimerId);

        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;
        Interlocked.Increment(ref _activeTimerCount);

        // For zero delay, schedule directly to event queue.
        if (delay <= 0)
        {
            ScheduleTask(() =>
            {
                try
                {
                    if (!cts.Token.IsCancellationRequested)
                    {
                        System.Console.WriteLine($"[JsEngine] setTimeout firing timerId={timerId} delay={delay}");
                        callback.Invoke([], JsValue.Undefined);
                    }
                }
                finally
                {
                    if (_timers.TryRemove(timerId, out _))
                    {
                        Interlocked.Decrement(ref _activeTimerCount);
                        TrySignalDrainComplete();
                    }
                }
            });
            return new JsValue(timerId);
        }

        _ = RunTimeoutAsync(timerId, delay, callback, cts);

        return new JsValue(timerId);
    }

    private async Task RunTimeoutAsync(
        int timerId,
        int delay,
        IJsCallable callback,
        CancellationTokenSource cts)
    {
        var callbackScheduled = false;
        try
        {
            await Task.Delay(delay, cts.Token).ConfigureAwait(false);

            if (!cts.Token.IsCancellationRequested)
            {
                callbackScheduled = true;
                ScheduleTask(() =>
                {
                    try
                    {
                        if (!cts.Token.IsCancellationRequested)
                        {
                            System.Console.WriteLine($"[JsEngine] setTimeout firing timerId={timerId} delay={delay}");
                            callback.Invoke([], JsValue.Undefined);
                        }
                    }
                    finally
                    {
                        if (_timers.TryRemove(timerId, out _))
                        {
                            Interlocked.Decrement(ref _activeTimerCount);
                            TrySignalDrainComplete();
                        }
                    }
                });
            }
        }
        catch (TaskCanceledException)
        {
            // Timer was cancelled
        }
        finally
        {
            if (!callbackScheduled && _timers.TryRemove(timerId, out _))
            {
                Interlocked.Decrement(ref _activeTimerCount);
                TrySignalDrainComplete();
            }
        }
    }

    /// <summary>
    ///     Implements setInterval - schedules a callback to run repeatedly at a fixed interval.
    /// </summary>
    private JsValue SetInterval(IReadOnlyList<JsValue> args)
    {
        if (!args[0].TryGetObject<IJsCallable>(out var callback))
        {
            return JsValue.Undefined;
        }

        var interval = args[1].TryGetDouble(out var d) ? (int)d : 0;
        var timerId = Interlocked.Increment(ref _nextTimerId);

        var cts = new CancellationTokenSource();
        _timers[timerId] = cts;

        // Increment active timer count before starting the timer
        Interlocked.Increment(ref _activeTimerCount);

        _ = RunIntervalAsync(timerId, interval, callback, cts);

        return new JsValue(timerId);
    }

    private async Task RunIntervalAsync(
        int timerId,
        int interval,
        IJsCallable callback,
        CancellationTokenSource cts)
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
                        if (!cts.Token.IsCancellationRequested)
                        {
                            callback.Invoke([], JsValue.Undefined);
                        }
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
            if (_timers.TryRemove(timerId, out _))
            {
                Interlocked.Decrement(ref _activeTimerCount);
                TrySignalDrainComplete();
            }
        }
    }

    /// <summary>
    ///     Implements clearTimeout/clearInterval - cancels a timer.
    /// </summary>
    private JsValue ClearTimer(IReadOnlyList<JsValue> args)
    {
        if (!args[0].TryGetDouble(out var timerId))
        {
            return JsValue.Undefined;
        }

        var id = (int)timerId;
        if (!_timers.TryRemove(id, out var cts))
        {
            return JsValue.Undefined;
        }

        cts.Cancel();
        Interlocked.Decrement(ref _activeTimerCount);
        TrySignalDrainComplete();

        return JsValue.Undefined;
    }

    private JsValue DynamicImport(
        IReadOnlyList<JsValue> args,
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
        PromiseHelper.AddPromiseInstanceMethods(promiseObj, promise, this);

        // IMPORTANT: Capture the referrer path NOW, before scheduling the task.
        // CallingJsEnvironment and _currentModulePath may change by the time the task runs.
        var capturedReferrerPath = callee?.CallingJsEnvironment is { } env ? env.ModulePath : _currentModulePath;

        // Run async module loading on threadpool, then schedule sync completion to event loop
        // Track the async work to ensure the event loop waits for it to complete
        var importTask = RunDynamicImportAsync(args, context, phase, capturedReferrerPath, promise);
        TrackPendingAsyncWork(importTask);

        return new JsValue(promiseObj);

        IJsPropertyAccessor? ResolvePromisePrototype()
        {
            return ResolvePromisePrototypeInternal();
        }
    }

    /// <summary>
    ///     Runs the async portion of dynamic import on the threadpool,
    ///     then schedules sync completion callbacks to the event loop.
    /// </summary>
    private async Task RunDynamicImportAsync(
        IReadOnlyList<JsValue> args,
        EvaluationContext? context,
        ImportPhase phase,
        string? capturedReferrerPath,
        JsPromise promise)
    {
        try
        {
            if (args.Count == 0)
            {
                ScheduleTask(() =>
                {
                    var typeError = StandardLibrary.CreateTypeError(
                        "import() requires a module specifier",
                        context,
                        RealmState);
                    promise.Reject(typeError);
                });
                return;
            }

            JsValue specifierStringValue;
            try
            {
                var specifier = args.GetArgument(0);
                specifierStringValue = JsOps.ToJsString(specifier, context);
            }
            catch (ThrowSignal signal)
            {
                ScheduleTask(() => promise.Reject(signal.ThrownValue));
                return;
            }

            if (context?.IsThrow == true)
            {
                var flowValue = context.FlowValue;
                // Clear the throw signal since we're handling it by rejecting the promise
                // This prevents the throw from propagating to EvaluateProgram
                context.Clear();
                ScheduleTask(() => promise.Reject(flowValue));
                return;
            }

            // Use TryGetString to get the raw string value, not ToString() which adds quotes
            if (!specifierStringValue.TryGetString(out var specifierString))
            {
                ScheduleTask(() =>
                {
                    var typeError = StandardLibrary.CreateTypeError(
                        "import() specifier must be a string",
                        context,
                        RealmState);
                    promise.Reject(typeError);
                });
                return;
            }

            if (phase == ImportPhase.Source)
            {
                ScheduleTask(() =>
                {
                    var syntaxError = StandardLibrary.CreateSyntaxError(
                        "Source phase imports are not supported",
                        context,
                        RealmState);
                    promise.Reject(syntaxError);
                });
                return;
            }

            try
            {
                // Load the module using the captured referrer path
                var moduleEntry = LoadModule(specifierString, capturedReferrerPath, phase);
                if (moduleEntry.IsAsync || moduleEntry.HasAsyncDependency)
                {
                    var evaluation = EnsureModuleEvaluatedAsync(moduleEntry, false);
                    if (evaluation.IsCompletedSuccessfully)
                    {
                        ScheduleTask(() =>
                        {
                            try
                            {
                                var namespaceObject = GetModuleNamespace(moduleEntry, phase);
                                promise.Resolve(JsValue.FromObjectUnsafe(namespaceObject));
                            }
                            catch (ThrowSignal signal)
                            {
                                promise.Reject(signal.ThrownValue);
                            }
                            catch (Exception ex)
                            {
                                var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                                promise.Reject(error);
                            }
                        });
                        return;
                    }

                    // Wait for the async evaluation to complete
                    try
                    {
                        await evaluation.ConfigureAwait(false);
                    }
                    catch (ThrowSignal signal)
                    {
                        ScheduleTask(() => promise.Reject(signal.ThrownValue));
                        return;
                    }
                    catch (Exception ex)
                    {
                        ScheduleTask(() =>
                        {
                            var error = StandardLibrary.CreateTypeError(
                                ex.Message,
                                context,
                                RealmState);
                            promise.Reject(error);
                        });
                        return;
                    }

                    // Schedule sync completion to event loop
                    ScheduleTask(() =>
                    {
                        try
                        {
                            var namespaceObject = GetModuleNamespace(moduleEntry, phase);
                            promise.Resolve(JsValue.FromObjectUnsafe(namespaceObject));
                        }
                        catch (ThrowSignal signal)
                        {
                            promise.Reject(signal.ThrownValue);
                        }
                        catch (Exception ex)
                        {
                            var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                            promise.Reject(error);
                        }
                    });
                    return;
                }

                // Sync module - schedule completion on event loop
                ScheduleTask(() =>
                {
                    try
                    {
                        EnsureModuleEvaluated(moduleEntry);
                        promise.Resolve(JsValue.FromObjectUnsafe(GetModuleNamespace(moduleEntry, phase)));
                    }
                    catch (ThrowSignal signal)
                    {
                        promise.Reject(signal.ThrownValue);
                    }
                    catch (Exception ex)
                    {
                        var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                        promise.Reject(error);
                    }
                });
            }
            catch (Exception ex)
            {
                ScheduleTask(() =>
                {
                    var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                    promise.Reject(error);
                });
            }
        }
        catch (Exception ex)
        {
            ScheduleTask(() =>
            {
                var error = StandardLibrary.CreateTypeError(ex.Message, context, RealmState);
                promise.Reject(error);
            });
        }
    }

    /// <summary>
    ///     Sets a custom module loader function that will be called to load module source code.
    ///     The function receives the module path and should return the module source code.
    ///     If not set, the engine will use File.ReadAllText to load modules from the file system.
    /// </summary>
    public void SetModuleLoader(SimpleModuleLoader loader)
    {
        _moduleLoader = (path, _) => loader(path);
    }

    public void SetModuleLoader(ModuleLoader loader)
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
                    _ = EnsureModuleEvaluatedAsync(cachedEntry, false);
                }
                else
                {
                    EnsureModuleEvaluated(cachedEntry);
                }
            }

            return cachedEntry;
        }

        var entry = LoadAndRegisterModule(resolvedPath, referrerPath, isJsonModule);

        var computedHasAsyncDependency = ModuleHasAsyncDependency(entry.Program, entry.Path,
            new HashSet<string>(StringComparer.Ordinal));
        entry.HasAsyncDependency = computedHasAsyncDependency;
        entry.Environment.IsAsyncModule = entry.IsAsync;

        EnsureModuleInstantiated(entry, phase, exportStarSet);
        if (phase != ImportPhase.Module)
        {
            return entry;
        }

        if (entry.IsAsync || entry.HasAsyncDependency)
        {
            //TODO: this can´t be right?
            _ = EnsureModuleEvaluatedAsync(entry, false);
        }
        else
        {
            EnsureModuleEvaluated(entry);
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
            if (attr is { Key: "type", Value: "json" })
            {
                return true;
            }
        }

        return false;
    }

    private ModuleEntry CreateJsonModule(string source, string resolvedPath)
    {
        // Parse JSON using JSON.parse
        var jsonValue = JsonHelper.ParseJsonWithReviverJsValue(source, RealmState, null, JsValue.Undefined);

        // Create a synthetic module with the JSON value as default export
        var exports = new JsObject();
        exports.SetProperty("default", jsonValue);

        var moduleEnv = CreateModuleEnvironment(resolvedPath);

        // IMPORTANT: Define the "default" binding in the module environment
        // This is needed for import binding resolution to work correctly
        var defaultSymbol = Symbol.Intern("default");
        moduleEnv.DefineJsValue(defaultSymbol, jsonValue, true, isLexicalBinding: true, blocksFunctionScopeOverride: false);

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

        var entry = LoadAndRegisterModule(resolvedPath, referrerPath, isJsonModule);
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

    /// <summary>
    /// Loads module source, parses it, creates the module entry, and registers it in the module registry.
    /// </summary>
    private ModuleEntry LoadAndRegisterModule(string resolvedPath, string? referrerPath, bool isJsonModule)
    {
        // Load module source
        var source = _moduleLoader != null
            ? _moduleLoader(resolvedPath, referrerPath)
            : File.ReadAllText(resolvedPath);

        ModuleEntry entry;
        if (isJsonModule)
        {
            // Create a JSON module - parse JSON and create a synthetic module with the value as default export
            entry = CreateJsonModule(source, resolvedPath);
        }
        else
        {
            // Parse the module
            var program = ParseProgram(source, true, true);

            // Create a module exports object
            var exports = new JsObject();
            var moduleEnv = CreateModuleEnvironment(resolvedPath);
            entry = CreateModuleEntry(EnsureStrictProgram(program), moduleEnv, exports, resolvedPath,
                program.HasTopLevelAwait);
        }

        _moduleRegistry[resolvedPath] = entry;
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

            if (preferRelative || string.IsNullOrEmpty(referrer) || !Path.IsPathRooted(referrer))
            {
                return NormalizeRelativeModulePath(baseDir, specifier);
            }

            var rootedBase = Path.GetDirectoryName(referrer) ?? string.Empty;
            var combined = Path.GetFullPath(Path.Combine(rootedBase, specifier));
            return combined.Replace('\\', '/');
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
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    {
                        if (parts.Count > 0)
                        {
                            parts.RemoveAt(parts.Count - 1);
                        }

                        continue;
                    }
                default:
                    parts.Add(segment);
                    break;
            }
        }

        return string.Join('/', parts);
    }

    private ModuleNamespace GetModuleNamespace(ModuleEntry entry, ImportPhase phase = ImportPhase.Module)
    {
        var cached = phase switch
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
            var resolution = ResolveExport(entry, name, phase, []);
            if (resolution.Kind == ExportResolutionKind.Resolved &&
                !name.StartsWith("__getter__", StringComparison.Ordinal) &&
                !name.StartsWith("__setter__", StringComparison.Ordinal) &&
                !name.StartsWith("@@symbol:", StringComparison.Ordinal))
            {
                resolvedNames.Add(name);
            }
        }

        var exportNames = resolvedNames
            .Order(StringComparer.Ordinal)
            .ToArray();

        var ns = new ModuleNamespace(exportNames, Lookup, RealmState,
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

        JsValue Lookup(string name)
        {
            if (!entry.Exports.TryGetValue(name, out var value))
            {
                return (JsValue)Symbol.Undefined;
            }

            if (value is LiveExportBinding liveBinding)
            {
                return liveBinding.GetValue();
            }

            return JsValue.FromObjectUnsafe(value);
        }

        void EnsureEvaluated()
        {
            EnsureModuleEvaluated(entry);
        }
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
                        exports["default"] =
                            new LiveExportBinding(() => moduleEnv.GetJsValue(Symbol.Intern("*default*")));
                    }
                    else
                    {
                        exports["default"] = JsValue.Uninitialized;
                    }

                    // For hoistable anonymous function declarations, binding is created during HoistFunctionDeclarations
                    // For all other default exports (classes, expressions), we need to create the *default* binding here in TDZ
                    // Note: `export default function() {}` is hoistable (flag set), but `export default (function() {})` is not
                    if (exportDefaultStmt.Value is ExportDefaultExpression
                        {
                            Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true }
                        })
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
                        // Use isLexicalBinding: false so we can initialize it later via Assign without TDZ errors
                        // The binding is initialized during EvaluateExportDefault
                        var defaultSymbol = Symbol.Intern("*default*");
                        if (!moduleEnv.HasBinding(defaultSymbol))
                        {
                            moduleEnv.DefineJsValue(defaultSymbol, JsValue.Uninitialized, isLexicalBinding: false,
                                blocksFunctionScopeOverride: false);
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
                            var exportInitValue =
                                isVar ? (JsValue)Symbol.Undefined : JsValue.Uninitialized;
                            var envInitValue = isVar ? Symbol.Undefined : JsEnvironment.Uninitialized;

                            if (moduleEnv.IsAsyncModule)
                            {
                                exports[symbol.Name] = new LiveExportBinding(() => moduleEnv.GetJsValue(symbol));
                                var envInit = isVar
                                    ? (JsValue)Symbol.Undefined
                                    : JsValue.Uninitialized;
                                moduleEnv.DefineJsValue(symbol, envInit, isLexicalBinding: !isVar,
                                    blocksFunctionScopeOverride: false);
                            }
                            else
                            {
                                exports[symbol.Name] = exportInitValue;
                                moduleEnv.DefineJsValue(symbol, JsValue.FromObjectUnsafe(envInitValue),
                                    isLexicalBinding: !isVar,
                                    blocksFunctionScopeOverride: false);
                            }
                        }

                        break;
                    }

                    foreach (var symbol in GetDeclaredSymbols(exportDeclaration.Declaration))
                    {
                        if (moduleEnv.IsAsyncModule)
                        {
                            exports[symbol.Name] = new LiveExportBinding(() => moduleEnv.GetJsValue(symbol));
                        }
                        else
                        {
                            exports[symbol.Name] = JsValue.Uninitialized;
                        }

                        moduleEnv.DefineJsValue(symbol, JsValue.Uninitialized, isLexicalBinding: true,
                            blocksFunctionScopeOverride: false);
                    }

                    break;
                case ExportNamedStatement exportNamed:
                    foreach (var specifier in exportNamed.Specifiers)
                    {
                        if (moduleEnv.IsAsyncModule)
                        {
                            var promise = CreateRealmPromise();
                            exports[specifier.Exported.Name] =
                                new LiveExportBinding(() => moduleEnv.GetJsValue(specifier.Local));
                            moduleEnv.DefineExportPromiseBinding(specifier.Local, promise, true, true);
                        }
                        else
                        {
                            exports[specifier.Exported.Name] = JsValue.Uninitialized;
                        }
                    }

                    break;
                case ExportAllStatement exportAll:
                    var sourceEntry =
                        LoadModuleForInstantiation(exportAll.ModulePath, modulePath, phase, exportStarSet);
                    if (!exportStarSet.Add(sourceEntry.Path))
                    {
                        break;
                    }

                    EnsureModuleInstantiated(sourceEntry, phase, exportStarSet);
                    var exportedNames = GetExportedNames(sourceEntry, exportStarSet);
                    foreach (var name in exportedNames)
                    {
                        if (string.Equals(name, "default", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var resolution =
                            ResolveExport(sourceEntry, name, phase, []);
                        if (resolution.Kind == ExportResolutionKind.Resolved)
                        {
                            exports[name] = JsValue.Uninitialized;
                        }
                    }

                    exportStarSet.Remove(sourceEntry.Path);
                    break;
                case ExportNamespaceAsStatement exportNamespace:
                    exports[exportNamespace.Exported.Name] = JsValue.Uninitialized;
                    break;
            }
        }

        // Per ES spec, import bindings must be created during module instantiation
        HoistImportBindings(program, moduleEnv, modulePath, phase);

        // Per ES spec, all var declarations and function declarations in a module must be hoisted
        // before the module body executes.
        HoistModuleDeclarations(program, moduleEnv);
    }

    private void HoistImportBindings(ProgramNode program, JsEnvironment moduleEnv, string? modulePath,
        ImportPhase phase)
    {
        foreach (var statement in program.Body)
        {
            if (statement is not ImportStatement importStatement)
            {
                continue;
            }

            // Determine the phase for this specific import-deferred imports use Defer phase
            var importPhase = importStatement.IsDeferred ? ImportPhase.Defer : phase;

            // Load and instantiate the module but DON'T evaluate it yet
            var importedModule = LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null,
                importStatement.Attributes);
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
                moduleEnv.DefineJsValue(nsBinding, JsValue.FromObjectUnsafe(ns), true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: false);
            }

            // Handle named imports
            foreach (var specifier in importStatement.NamedImports)
            {
                CreateImportBinding(moduleEnv, specifier.Local, importedModule, specifier.Imported, importPhase);
            }
        }
    }

    private void CreateImportBinding(
        JsEnvironment moduleEnv,
        Symbol localName,
        ModuleEntry importedModule,
        Symbol importedName,
        ImportPhase importPhase)
    {
        var resolved = ResolveExport(importedModule, importedName.Name, importPhase, []);
        if (!resolved.IsResolved)
        {
            throw new InvalidOperationException(
                $"SyntaxError: The requested module '{importedModule.Path}' does not provide an export named '{importedName.Name}'");
        }

        moduleEnv.DefineImportBinding(localName, resolved.Module!.Environment, resolved.BindingName!);
    }

    private static Symbol GetDefaultExportBindingName(ExportDefaultStatement exportDefault)
    {
        return exportDefault.Value switch
        {
            ExportDefaultDeclaration { Declaration: FunctionDeclaration(_, var symbol, _) } => symbol.Name.Length == 0
                ? Symbol.Intern("*default*")
                : symbol,
            ExportDefaultDeclaration { Declaration: ClassDeclaration classDecl } => classDecl.Name.Name.Length == 0
                ? Symbol.Intern("*default*")
                : classDecl.Name,
            _ => Symbol.Intern("*default*")
        };
    }

    private IEnumerable<string> GetExportedNames(ModuleEntry module, HashSet<string> exportStarSet)
    {
        if (!exportStarSet.Add(module.Path))
        {
            return [];
        }

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
                        LoadModuleForInstantiation(exportAll.ModulePath, module.Path, ImportPhase.Module,
                            exportStarSet);
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
        resolveSet ??= [];
        if (!resolveSet.Add((module, exportName)))
        {
            return ExportResolution.NotFound;
        }

        if (module.Program.Body.IsEmpty && module.Exports.ContainsKey(exportName))
        {
            return new ExportResolution(module, Symbol.Intern(exportName));
        }

        foreach (var statement in module.Program.Body)
        {
            switch (statement)
            {
                case ExportDefaultStatement exportDefault
                    when string.Equals(exportName, "default", StringComparison.Ordinal):
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
        return new LiveExportBinding(() => resolution.Module!.Environment.GetJsValue(resolution.BindingName!));
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

    private static void HoistLexicalDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            CollectAndHoistLexicals(statement, moduleEnv);
        }
    }

    private static void CollectAndHoistLexicals(StatementNode statement, JsEnvironment moduleEnv)
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
                    moduleEnv.DefineJsValue(classDecl.Name, JsValue.Uninitialized, isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                }

                break;
                // Note: exported let/const/class are already handled by PredeclareExportNames
        }
    }

    private static void HoistLexicalBinding(BindingTarget target, JsEnvironment moduleEnv, bool isConst) =>
        HoistBinding(target, moduleEnv, JsValue.Uninitialized, isLexicalBinding: true, isConst);

    /// <summary>
    /// Recursively hoists bindings from destructuring patterns.
    /// </summary>
    private static void HoistBinding(
        BindingTarget target,
        JsEnvironment moduleEnv,
        JsValue initialValue,
        bool isLexicalBinding,
        bool? isConst = null)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    if (!moduleEnv.HasBinding(id.Name))
                    {
                        moduleEnv.DefineJsValue(id.Name, initialValue, isConst: isConst ?? false,
                            isLexicalBinding: isLexicalBinding, blocksFunctionScopeOverride: false);
                    }

                    break;
                case ArrayBinding arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is { } binding)
                        {
                            HoistBinding(binding, moduleEnv, initialValue, isLexicalBinding, isConst);
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
                        HoistBinding(prop.Target, moduleEnv, initialValue, isLexicalBinding, isConst);
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

    private static void HoistVarDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            CollectAndHoistVars(statement, moduleEnv);
        }
    }

    private static void CollectAndHoistVars(StatementNode statement, JsEnvironment moduleEnv)
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
            case ExportDeclarationStatement
            {
                Declaration: VariableDeclaration { Kind: VariableKind.Var } exportVarDecl
            }:
                foreach (var declarator in exportVarDecl.Declarators)
                {
                    HoistVarBinding(declarator.Target, moduleEnv);
                }

                break;
        }
    }

    private static void HoistVarBinding(BindingTarget target, JsEnvironment moduleEnv) =>
        HoistBinding(target, moduleEnv, JsValue.Undefined, isLexicalBinding: false);

    private void HoistFunctionDeclarations(ProgramNode program, JsEnvironment moduleEnv)
    {
        foreach (var statement in program.Body)
        {
            switch (statement)
            {
                case FunctionDeclaration funcDecl:
                    // Create the function value and define it
                    var function = TypedAstEvaluator.CreateModuleFunction(funcDecl.Function, moduleEnv, RealmState,
                        program.IsStrict);
                    moduleEnv.DefineJsValue(funcDecl.Name, JsValue.FromObjectUnsafe(function), isLexicalBinding: false,
                        blocksFunctionScopeOverride: false);
                    break;
                case ExportDeclarationStatement { Declaration: FunctionDeclaration exportedFuncDecl }:
                    // Exported function declarations also need to be hoisted
                    var exportedFunction = TypedAstEvaluator.CreateModuleFunction(exportedFuncDecl.Function, moduleEnv,
                        RealmState, program.IsStrict);
                    moduleEnv.DefineJsValue(exportedFuncDecl.Name, JsValue.FromObjectUnsafe(exportedFunction),
                        isLexicalBinding: false, blocksFunctionScopeOverride: false);
                    break;
                case ExportDefaultStatement
                {
                    Value: ExportDefaultDeclaration { Declaration: FunctionDeclaration defaultFuncDecl }
                }:
                    // Default exported named function declarations need to be hoisted
                    var defaultFunction = TypedAstEvaluator.CreateModuleFunction(defaultFuncDecl.Function, moduleEnv,
                        RealmState, program.IsStrict);
                    moduleEnv.DefineJsValue(defaultFuncDecl.Name, JsValue.FromObjectUnsafe(defaultFunction),
                        isLexicalBinding: false, blocksFunctionScopeOverride: false);
                    break;
                case ExportDefaultStatement
                {
                    Value: ExportDefaultExpression
                    {
                        Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true } funcExpr
                    }
                }:
                    // Anonymous default exported function declarations (not expressions!) need to be hoisted with *default* binding
                    // Per ES spec, SetFunctionName(F, "default") is called for anonymous default exports
                    // Note: `export default function() {}` is hoistable, but `export default (function() {})` is not
                    var anonFunction = TypedAstEvaluator.CreateModuleFunction(funcExpr, moduleEnv, RealmState,
                        program.IsStrict, "default");
                    moduleEnv.DefineJsValue(Symbol.Intern("*default*"), JsValue.FromObjectUnsafe(anonFunction),
                        isLexicalBinding: false, blocksFunctionScopeOverride: false);
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
                        LoadModule(importStatement.ModulePath, modulePath, importPhase, null,
                            importStatement.Attributes);
                    }
                    else
                    {
                        LoadModuleForInstantiation(importStatement.ModulePath, modulePath, importPhase, null,
                            importStatement.Attributes);
                    }

                    break;
                case ExportNamedStatement { FromModule: { } fromModule }:
                    LoadModule(fromModule, modulePath);
                    break;
                case ExportAllStatement exportAll:
                    LoadModule(exportAll.ModulePath, modulePath);
                    break;
                case ExportNamespaceAsStatement exportNamespace:
                    LoadModule(exportNamespace.ModulePath, modulePath);
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

        // Increment module body execution depth to suppress microtask draining during body execution.
        // This ensures Promise.resolve().then() callbacks only run after the module body completes.
        _moduleBodyExecutionDepth++;
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
                        var namespaceEntry = LoadModule(exportNamespace.ModulePath, modulePath);
                        var namespaceObj = GetModuleNamespace(namespaceEntry);
                        exports[exportNamespace.Exported.Name] = namespaceObj;
                        // Also define in the environment so import bindings can read it
                        moduleEnv.DefineJsValue(exportNamespace.Exported, JsValue.FromObjectUnsafe(namespaceObj), true,
                            isLexicalBinding: true,
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
            _moduleBodyExecutionDepth--;
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

    private async Task DrainAsyncDependencies(List<Task<object?>> pendingAsyncDependencies, int maxEpoch = -1)
    {
        while (pendingAsyncDependencies.Count > 0)
        {
            // Only drain microtasks from earlier epochs to preserve proper timing
            DrainMicrotasks(maxEpoch);

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

        DrainMicrotasks(maxEpoch);
        pendingAsyncDependencies.Clear();
    }

    private async Task<object?> EvaluateModuleBodyWithAsyncDependencies(ModuleEntry entry)
    {
        entry.Evaluating = true;
        try
        {
            await EvaluateModuleDependenciesAsync(entry).ConfigureAwait(false);

            // Advance the epoch before executing the body. Any microtasks queued during body
            // execution will be in this new epoch and won't be drained until we explicitly
            // drain them after the body completes.
            AdvanceMicrotaskEpoch();
            var bodyEpoch = MicrotaskEpoch;

            var previousModulePath = _currentModulePath;
            _currentModulePath = entry.Path;
            try
            {
                var result = ExecuteModuleBody(
                    entry.Program,
                    entry.Environment,
                    entry.Exports,
                    entry.Path);
                entry.LastValue = result;
                entry.Evaluated = true;

                // Now drain microtasks that were queued during the body execution
                DrainMicrotasks(bodyEpoch);

                return result;
            }
            finally
            {
                _currentModulePath = previousModulePath;
                entry.Evaluating = false;
            }
        }
        catch
        {
            entry.Evaluating = false;
            throw;
        }
    }

    private async Task<object?> EvaluateModuleBodyWithTopLevelAwait(ModuleEntry entry)
    {
        try
        {
            await EvaluateModuleDependenciesAsync(entry).ConfigureAwait(false);

            entry.AsyncBodyRunner ??= new AsyncModuleBodyRunner(this, entry);
            return await entry.AsyncBodyRunner.RunAsync().ConfigureAwait(false);
        }
        finally
        {
            entry.AsyncBodyRunner = null;
        }
    }

    /// <summary>
    /// Evaluates all dependencies of a module, handling async dependencies by batching
    /// and draining them appropriately based on the microtask epoch.
    /// </summary>
    private async Task EvaluateModuleDependenciesAsync(ModuleEntry entry)
    {
        // Capture the epoch before dependency loading - only drain earlier epochs while waiting
        var moduleEpoch = MicrotaskEpoch;
        var maxDrainEpoch = moduleEpoch - 1;

        var pendingAsyncDependencies = new List<Task<object?>>();
        var dependencies = GetModuleDependencies(entry);
        for (var i = 0; i < dependencies.Count; i++)
        {
            var dependency = dependencies[i];
            EnsureModuleInstantiated(dependency);
            var isAsyncDependency = dependency.IsAsync || dependency.HasAsyncDependency;
            var evaluation = EnsureModuleEvaluatedAsync(dependency, !isAsyncDependency);
            if (isAsyncDependency)
            {
                pendingAsyncDependencies.Add(evaluation);
                var nextIsAsync = i + 1 < dependencies.Count &&
                                  (dependencies[i + 1].IsAsync || dependencies[i + 1].HasAsyncDependency);
                if (nextIsAsync)
                {
                    await DrainAsyncDependencies(pendingAsyncDependencies, maxDrainEpoch).ConfigureAwait(false);
                }

                continue;
            }

            await evaluation.ConfigureAwait(false);
        }

        if (pendingAsyncDependencies.Count > 0)
        {
            await DrainAsyncDependencies(pendingAsyncDependencies, maxDrainEpoch).ConfigureAwait(false);
        }
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

        List<IMicrotask>? preservedMicrotasks = null;

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
                preservedMicrotasks = engine!.DetachMicrotasks();
            }

            if (!importStatement.IsDeferred)
            {
                if (isAsyncImport)
                {
                    var isSelfImport = string.Equals(moduleEntry.Path, referrerPath, StringComparison.Ordinal);
                    var evaluation = EnsureModuleEvaluatedAsync(
                        moduleEntry,
                        !isSelfImport);
                    if (!isSelfImport)
                    {
                        // Block until the async dependency finishes so exported bindings are initialized.
                        evaluation.GetAwaiter().GetResult();
                        engine?.DrainMicrotasks(force: true);
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
                moduleEnv.DefineJsValue(namespaceBinding, JsValue.FromObjectUnsafe(namespaceObject));
            }
        }
    }

    private object? EvaluateExportDefault(ExportDefaultStatement statement, JsEnvironment moduleEnv, bool isStrict)
    {
        var defaultBindingName = Symbol.Intern("*default*");

        // For hoistable anonymous function declarations, the function was already hoisted with *default* binding
        // `export default function() {}` is hoistable (IsHoistableDefaultExport = true)
        // `export default (function() {})` is NOT hoistable (it's a parenthesized expression)
        if (statement.Value is ExportDefaultExpression
            {
                Expression: FunctionExpression { Name: null, IsHoistableDefaultExport: true }
            })
        {
            return new LiveExportBinding(() => moduleEnv.GetJsValue(defaultBindingName));
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
            moduleEnv.AssignJsValue(defaultBindingName, JsValue.FromObjectUnsafe(value));

            return new LiveExportBinding(() => moduleEnv.GetJsValue(defaultBindingName));
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
            var bindingName = functionDeclaration.Name.Name?.Length == 0
                ? Symbol.Intern("*default*")
                : functionDeclaration.Name;
            return new LiveExportBinding(() => moduleEnv.GetJsValue(bindingName));
        }

        // Classes need to be evaluated (they aren't hoisted like functions)
        ExecuteTypedStatement(declaration.Declaration, moduleEnv, isStrict, false);
        return declaration.Declaration switch
        {
            ClassDeclaration classDeclaration => new LiveExportBinding(() =>
            {
                var bindingName = classDeclaration.Name.Name.Length == 0
                    ? Symbol.Intern("*default*")
                    : classDeclaration.Name;
                return moduleEnv.GetJsValue(bindingName);
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
            var sourceEntry = LoadModule(fromModule, modulePath);
            foreach (var specifier in statement.Specifiers)
            {
                var resolution = ResolveExport(sourceEntry, specifier.Local.Name, ImportPhase.Module,
                    []);
                if (resolution.Kind == ExportResolutionKind.Resolved)
                {
                    exports[specifier.Exported.Name] = CreateLiveBinding(resolution);
                }
            }

            return;
        }

        foreach (var specifier in statement.Specifiers)
        {
            exports[specifier.Exported.Name] = new LiveExportBinding(() => moduleEnv.GetJsValue(specifier.Local));
        }
    }

    private void EvaluateExportDeclaration(ExportDeclarationStatement statement, JsEnvironment moduleEnv,
        JsObject exports, bool isStrict)
    {
        ExecuteTypedStatement(statement.Declaration, moduleEnv, isStrict, false);
        foreach (var symbol in GetDeclaredSymbols(statement.Declaration))
        {
            exports[symbol.Name] = new LiveExportBinding(() => moduleEnv.GetJsValue(symbol));
        }
    }

    private void EvaluateExportAll(ExportAllStatement statement, JsObject exports, string? modulePath)
    {
        var sourceEntry = LoadModule(statement.ModulePath, modulePath);
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
                []);
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
        internal AsyncModuleBodyRunner? AsyncBodyRunner { get; set; }
        internal ModuleNamespace? Namespace { get; set; }
        internal ModuleNamespace? DeferredNamespace { get; set; }
        internal JsObject? ImportMeta { get; set; }
        internal object? LastValue { get; set; }
        internal bool HasAsyncDependency { get; set; }
    }

    public enum ImportPhase
    {
        Module,
        Defer,
        Source
    }

    private enum ExportResolutionKind
    {
        NotFound,
        Resolved,
        Ambiguous
    }

    private readonly record struct ExportResolution(ExportResolutionKind Kind, ModuleEntry? Module, Symbol? BindingName)
    {
        public static readonly ExportResolution NotFound = new(ExportResolutionKind.NotFound, null, null);
        public static readonly ExportResolution Ambiguous = new(ExportResolutionKind.Ambiguous, null, null);

        public ExportResolution(ModuleEntry module, Symbol bindingName) : this(ExportResolutionKind.Resolved, module,
            bindingName)
        {
        }

        public bool IsResolved => Kind == ExportResolutionKind.Resolved && Module is not null;
    }

    private sealed class AsyncModuleBodyRunner
    {
        private sealed class StopIterationSignal : Exception
        {
        }

        private static bool TryIteratorStep(IJsObjectLike iterator, RealmState realm, string operation,
            out JsValue value)
        {
            if (!iterator.TryGetProperty("next", out var nextProp) ||
                !nextProp.TryGetObject<IJsCallable>(out var nextMethod))
            {
                throw StandardLibrary.ThrowTypeError($"{operation} iterator must have a callable next method",
                    realm: realm);
            }

            var resultValue = nextMethod.Invoke([], JsValue.FromObjectUnsafe(iterator));
            if (!resultValue.TryGetObjectLike(out var resultObj))
            {
                throw StandardLibrary.ThrowTypeError($"{operation} iterator result must be an object", realm: realm);
            }

            if (resultObj.TryGetProperty("done", out var doneProp) && JsOps.ToBoolean(doneProp))
            {
                value = JsValue.Undefined;
                return false;
            }

            value = resultObj.TryGetProperty("value", out var valueProp) ? valueProp : JsValue.Undefined;
            return true;
        }

        private readonly Stack<Action<ThrowSignal>> _asyncTryHandlers = new();
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly JsEngine _engine;
        private readonly ModuleEntry _entry;
        private bool _breakSignaled;
        private bool _continueSignaled;

        private object? _lastValue;

        private int _runEpoch;
        private bool _started;
        private int _statementIndex;

        internal AsyncModuleBodyRunner(JsEngine engine, ModuleEntry entry)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        internal Task<object?> RunAsync()
        {
            if (!_started)
            {
                _started = true;
                _entry.Evaluating = true;
                _engine.EvaluateRequestedModules(_entry.Program, _entry.Path);
                // Advance the epoch before running. Microtasks queued during the synchronous
                // portion of the body will be in this epoch and only drained after the
                // synchronous portion completes.
                _engine.AdvanceMicrotaskEpoch();
                _runEpoch = _engine.MicrotaskEpoch;
                Run();
            }

            return _completion.Task;
        }

        private void Run()
        {
            if (_completion.Task.IsCompleted)
            {
                return;
            }

            var previousModulePath = _engine._currentModulePath;
            _engine._currentModulePath = _entry.Path;

            // Increment module body execution depth to suppress microtask draining during execution
            _engine._moduleBodyExecutionDepth++;

            try
            {
                var program = _entry.Program;
                var env = _entry.Environment;
                var exports = _entry.Exports;
                var isStrict = program.IsStrict;

                while (_statementIndex < program.Body.Length)
                {
                    var statement = program.Body[_statementIndex];

                    switch (statement)
                    {
                        case ImportStatement importStatement:
                            _engine.EvaluateImport(importStatement, env, _entry.Path);
                            _statementIndex++;
                            continue;
                        case ExportDefaultStatement exportDefault:
                            if (!TryEvaluateExportDefault(exportDefault, env, exports, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ExportNamedStatement exportNamed:
                            _engine.EvaluateExportNamed(exportNamed, env, exports, _entry.Path);
                            _statementIndex++;
                            continue;
                        case ExportDeclarationStatement exportDeclaration:
                            if (!TryEvaluateExportDeclaration(exportDeclaration, env, exports, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ExportAllStatement exportAll:
                            _engine.EvaluateExportAll(exportAll, exports, _entry.Path);
                            _statementIndex++;
                            continue;
                        case ExportNamespaceAsStatement exportNamespace:
                            var namespaceEntry = _engine.LoadModule(exportNamespace.ModulePath, _entry.Path);
                            var namespaceObj = _engine.GetModuleNamespace(namespaceEntry);
                            exports[exportNamespace.Exported.Name] = namespaceObj;
                            env.DefineJsValue(exportNamespace.Exported, JsValue.FromObjectUnsafe(namespaceObj), true,
                                isLexicalBinding: true,
                                blocksFunctionScopeOverride: false);
                            _statementIndex++;
                            continue;
                        case FunctionDeclaration:
                            _statementIndex++;
                            continue;
                        case ExpressionStatement { Expression: AwaitExpression awaitExpression }:
                            if (!TryAwaitExpression(awaitExpression.Expression, _ => { }, env))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ExpressionStatement exprStatement
                            when AstShapeAnalyzer.StatementContainsAwait(exprStatement):
                            // Expression statement with await nested somewhere (e.g., void await x, f(await x))
                            if (!TryEvaluateExpressionStatementWithAwait(exprStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case VariableDeclaration variableDeclaration
                            when variableDeclaration.Declarators.All(d => d.Target is IdentifierBinding) &&
                                 ContainsDirectAwaitInitializer(variableDeclaration):
                            if (!TryEvaluateDeclarationWithAwait(variableDeclaration, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case VariableDeclaration variableDeclaration
                            when DeclarationNeedsAwaitTemps(variableDeclaration):
                            if (!TryEvaluateDeclarationWithAwait(variableDeclaration, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case IfStatement ifStatement
                            when AstShapeAnalyzer.StatementContainsAwait(ifStatement):
                            if (!TryEvaluateIfStatementWithAwait(ifStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case BlockStatement blockStatement
                            when AstShapeAnalyzer.StatementContainsAwait(blockStatement):
                            if (!TryEvaluateBlockStatementWithAwait(blockStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case WhileStatement whileStatement
                            when AstShapeAnalyzer.StatementContainsAwait(whileStatement):
                            if (!TryEvaluateWhileStatementWithAwait(whileStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ForEachStatement { Kind: ForEachKind.AwaitOf } forAwaitStatement:
                            // for await...of always contains await semantically
                            if (!TryEvaluateForAwaitOfStatement(forAwaitStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ForEachStatement forEachStatement
                            when AstShapeAnalyzer.StatementContainsAwait(forEachStatement):
                            // for...of or for...in with await in iterable or body
                            if (!TryEvaluateForEachStatementWithAwait(forEachStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case TryStatement tryStatement
                            when AstShapeAnalyzer.StatementContainsAwait(tryStatement):
                            if (!TryEvaluateTryStatementWithAwait(tryStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        case ForStatement forStatement
                            when AstShapeAnalyzer.StatementContainsAwait(forStatement):
                            if (!TryEvaluateForStatementWithAwait(forStatement, env, isStrict))
                            {
                                return;
                            }

                            _statementIndex++;
                            continue;
                        default:
                            if (AstShapeAnalyzer.StatementContainsAwait(statement))
                            {
                                throw new NotSupportedException(
                                    $"Async module execution does not support '{statement.GetType().Name}' containing await.");
                            }

                            _lastValue = _engine.ExecuteTypedStatement(statement, env, isStrict, false);
                            _statementIndex++;
                            continue;
                    }
                }

                _entry.LastValue = _lastValue;
                _entry.Evaluated = true;
                _entry.Evaluating = false;
                // Drain microtasks that were queued during this run
                _engine.DrainMicrotasks(_runEpoch);
                _completion.TrySetResult(_lastValue);
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                _engine._currentModulePath = previousModulePath;
                _engine._moduleBodyExecutionDepth--;
            }
        }

        private void Fail(Exception exception)
        {
            if (exception is ThrowSignal signal && TryHandleAsyncTry(signal))
            {
                return;
            }

            _entry.Evaluating = false;
            _completion.TrySetException(exception);
        }

        private bool TryHandleAsyncTry(ThrowSignal signal)
        {
            if (_asyncTryHandlers.Count == 0)
            {
                return false;
            }

            var handler = _asyncTryHandlers.Pop();
            handler(signal);
            return true;
        }

        private bool TryEvaluateExportDefault(ExportDefaultStatement statement, JsEnvironment env, JsObject exports,
            bool isStrict)
        {
            var defaultBindingName = Symbol.Intern("*default*");

            if (statement.Value is ExportDefaultExpression { Expression: AwaitExpression awaitExpression })
            {
                exports["default"] = new LiveExportBinding(() => env.GetJsValue(defaultBindingName));
                return TryAwaitExpression(awaitExpression.Expression,
                    resolved => env.AssignJsValue(defaultBindingName, resolved),
                    env);
            }

            if (AstShapeAnalyzer.StatementContainsAwait(statement))
            {
                throw new NotSupportedException(
                    "Async module execution only supports direct await in export default expressions.");
            }

            var value = _engine.EvaluateExportDefault(statement, env, isStrict);
            exports["default"] = value;
            return true;
        }

        private bool TryEvaluateExportDeclaration(
            ExportDeclarationStatement statement,
            JsEnvironment env,
            JsObject exports,
            bool isStrict)
        {
            foreach (var symbol in GetDeclaredSymbols(statement.Declaration))
            {
                exports[symbol.Name] = new LiveExportBinding(() => env.GetJsValue(symbol));
            }

            if (statement.Declaration is VariableDeclaration directAwaitDeclaration &&
                directAwaitDeclaration.Declarators.All(d => d.Target is IdentifierBinding) &&
                ContainsDirectAwaitInitializer(directAwaitDeclaration))
            {
                return TryEvaluateDeclarationWithAwait(directAwaitDeclaration, env, isStrict);
            }

            if (statement.Declaration is VariableDeclaration variableDeclaration &&
                DeclarationNeedsAwaitTemps(variableDeclaration))
            {
                return TryEvaluateDeclarationWithAwait(variableDeclaration, env, isStrict);
            }

            if (AstShapeAnalyzer.StatementContainsAwait(statement))
            {
                throw new NotSupportedException(
                    "Async module execution only supports direct await in exported lexical initializers.");
            }

            _engine.ExecuteTypedStatement(statement.Declaration, env, isStrict, false);
            return true;
        }

        private static bool ContainsDirectAwaitInitializer(VariableDeclaration declaration)
        {
            foreach (var declarator in declaration.Declarators)
            {
                if (declarator.Initializer is AwaitExpression)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryEvaluateDeclarationWithAwait(VariableDeclaration declaration, JsEnvironment env, bool isStrict)
        {
            return TryEvaluateDeclarationWithAwait(declaration, env, isStrict, advanceTopLevelStatement: true,
                onCompleted: null);
        }

        private bool TryEvaluateDeclarationWithAwait(
            VariableDeclaration declaration,
            JsEnvironment env,
            bool isStrict,
            bool advanceTopLevelStatement,
            Func<bool>? onCompleted)
        {
            if (DeclarationNeedsAwaitTemps(declaration))
            {
                return TryEvaluateDeclarationWithAwaitViaTemps(declaration, env, isStrict, advanceTopLevelStatement,
                    onCompleted);
            }

            var isLexical = declaration.Kind is VariableKind.Let or VariableKind.Const;
            var isConst = declaration.Kind == VariableKind.Const;

            return EvaluateDeclarator(0);

            bool EvaluateDeclarator(int index)
            {
                if (index >= declaration.Declarators.Length)
                {
                    return true;
                }

                var declarator = declaration.Declarators[index];
                if (declarator.Target is not IdentifierBinding identifier)
                {
                    throw new NotSupportedException(
                        "Async module execution only supports await initializers for identifier bindings.");
                }

                if (declarator.Initializer is null)
                {
                    if (isLexical)
                    {
                        env.DefineJsValue(identifier.Name, JsValue.Undefined, isConst,
                            isLexicalBinding: true,
                            blocksFunctionScopeOverride: false);
                    }

                    // For var, it's already hoisted with undefined value
                    return EvaluateDeclarator(index + 1);
                }

                if (!AstShapeAnalyzer.ContainsAwait(declarator.Initializer))
                {
                    try
                    {
                        var resolved =
                            JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(declarator.Initializer, env,
                                isStrict));
                        AssignResolvedValue(identifier.Name, resolved);
                        return EvaluateDeclarator(index + 1);
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
                }

                return TryEvaluateExpressionWithAwait(declarator.Initializer, env, isStrict, resolved =>
                {
                    AssignResolvedValue(identifier.Name, resolved);
                    if (!EvaluateDeclarator(index + 1))
                    {
                        return;
                    }

                    if (advanceTopLevelStatement)
                    {
                        _statementIndex++;
                        Run();
                    }
                    else
                    {
                        _ = onCompleted?.Invoke();
                    }
                }, advanceTopLevelStatement: false);

                void AssignResolvedValue(Symbol name, JsValue resolved)
                {
                    if (isLexical)
                    {
                        env.DefineJsValue(name, resolved, isConst, isLexicalBinding: true,
                            blocksFunctionScopeOverride: false);
                    }
                    else
                    {
                        env.AssignJsValue(name, resolved);
                    }
                }
            }
        }

        private bool TryEvaluateDeclarationWithAwaitViaTemps(
            VariableDeclaration declaration,
            JsEnvironment env,
            bool isStrict,
            bool advanceTopLevelStatement,
            Func<bool>? onCompleted)
        {
            var tempBuilder = ImmutableArray.CreateBuilder<AwaitTempBinding>();
            var rewrittenDeclaration = RewriteAwaitExpressionsToSyntheticIdentifiers(declaration, tempBuilder);

            if (DeclarationNeedsAwaitTemps(rewrittenDeclaration))
            {
                Fail(new NotSupportedException(
                    $"Async module execution does not support await in this variable declaration shape: '{declaration.GetType().Name}'."));
                return false;
            }

            var tempEnv = JsEnvironment.CreateInstance(
                env,
                isStrict: isStrict,
                creatingSource: declaration.Source,
                description: "async declaration await scope");

            var tempBindings = tempBuilder.ToImmutable();
            return EvaluateTempBinding(0);

            bool EvaluateTempBinding(int index)
            {
                if (index >= tempBindings.Length)
                {
                    try
                    {
                        _engine.ExecuteTypedStatement(rewrittenDeclaration, tempEnv, isStrict, false);

                        if (advanceTopLevelStatement)
                        {
                            _statementIndex++;
                            Run();
                        }
                        else
                        {
                            _ = onCompleted?.Invoke();
                        }

                        return true;
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
                }

                var binding = tempBindings[index];
                return TryEvaluateExpressionWithAwait(binding.Expression, tempEnv, isStrict, resolved =>
                {
                    tempEnv.DefineJsValue(binding.Symbol, resolved, true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                    EvaluateTempBinding(index + 1);
                }, advanceTopLevelStatement: false);
            }
        }

        private static bool DeclarationNeedsAwaitTemps(VariableDeclaration declaration)
        {
            foreach (var declarator in declaration.Declarators)
            {
                if (BindingContainsAwait(declarator.Target))
                {
                    return true;
                }

                if (declarator.Target is not IdentifierBinding &&
                    declarator.Initializer is not null &&
                    AstShapeAnalyzer.ContainsAwait(declarator.Initializer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool BindingContainsAwait(BindingTarget? target)
        {
            while (true)
            {
                switch (target)
                {
                    case null:
                    case IdentifierBinding:
                        return false;
                    case ArrayBinding arrayBinding:
                        foreach (var element in arrayBinding.Elements)
                        {
                            if (BindingContainsAwait(element.Target))
                            {
                                return true;
                            }

                            if (element.DefaultValue is not null && AstShapeAnalyzer.ContainsAwait(element.DefaultValue))
                            {
                                return true;
                            }
                        }

                        target = arrayBinding.RestElement;
                        continue;
                    case ObjectBinding objectBinding:
                        foreach (var property in objectBinding.Properties)
                        {
                            if (BindingContainsAwait(property.Target))
                            {
                                return true;
                            }

                            if (property.DefaultValue is not null &&
                                AstShapeAnalyzer.ContainsAwait(property.DefaultValue))
                            {
                                return true;
                            }

                            if (property.NameExpression is not null &&
                                AstShapeAnalyzer.ContainsAwait(property.NameExpression))
                            {
                                return true;
                            }
                        }

                        target = objectBinding.RestElement;
                        continue;
                    case AssignmentTargetBinding assignmentTarget:
                        return AstShapeAnalyzer.ContainsAwait(assignmentTarget.Expression);
                    default:
                        return false;
                }
            }
        }

        private static VariableDeclaration RewriteAwaitExpressionsToSyntheticIdentifiers(
            VariableDeclaration declaration,
            ImmutableArray<AwaitTempBinding>.Builder tempBindings)
        {
            var declarators = declaration.Declarators
                .Select(declarator => declarator with
                {
                    Target = RewriteAwaitExpressionsInBindingTarget(declarator.Target, tempBindings),
                    Initializer = declarator.Initializer is null
                        ? null
                        : RewriteAwaitExpressionToSyntheticIdentifier(declarator.Initializer, tempBindings)
                })
                .ToImmutableArray();

            return declaration with
            {
                Declarators = declarators
            };
        }

        private static BindingTarget RewriteAwaitExpressionsInBindingTarget(
            BindingTarget target,
            ImmutableArray<AwaitTempBinding>.Builder tempBindings)
        {
            return target switch
            {
                IdentifierBinding identifier => identifier,
                ArrayBinding arrayBinding => arrayBinding with
                {
                    Elements = arrayBinding.Elements
                        .Select(element => element with
                        {
                            Target = element.Target is null
                                ? null
                                : RewriteAwaitExpressionsInBindingTarget(element.Target, tempBindings),
                            DefaultValue = element.DefaultValue is null
                                ? null
                                : RewriteAwaitExpressionToSyntheticIdentifier(element.DefaultValue, tempBindings)
                        })
                        .ToImmutableArray(),
                    RestElement = arrayBinding.RestElement is null
                        ? null
                        : RewriteAwaitExpressionsInBindingTarget(arrayBinding.RestElement, tempBindings)
                },
                ObjectBinding objectBinding => objectBinding with
                {
                    Properties = objectBinding.Properties
                        .Select(property => property with
                        {
                            Target = RewriteAwaitExpressionsInBindingTarget(property.Target, tempBindings),
                            DefaultValue = property.DefaultValue is null
                                ? null
                                : RewriteAwaitExpressionToSyntheticIdentifier(property.DefaultValue, tempBindings),
                            NameExpression = property.NameExpression is null
                                ? null
                                : RewriteAwaitExpressionToSyntheticIdentifier(property.NameExpression, tempBindings)
                        })
                        .ToImmutableArray(),
                    RestElement = objectBinding.RestElement is null
                        ? null
                        : RewriteAwaitExpressionsInBindingTarget(objectBinding.RestElement, tempBindings)
                },
                AssignmentTargetBinding assignmentTarget => assignmentTarget with
                {
                    Expression = RewriteAwaitExpressionToSyntheticIdentifier(assignmentTarget.Expression, tempBindings)
                },
                _ => target
            };
        }

        private bool TryAwaitExpression(ExpressionNode awaitedExpression, Action<JsValue> onFulfilled,
            JsEnvironment environment)
        {
            return TryAwaitExpressionCore(awaitedExpression, environment, resolved =>
            {
                onFulfilled(resolved);
                _statementIndex++;
                Run();
            });
        }

        private bool TryAwaitExpressionNested(ExpressionNode awaitedExpression, Action<JsValue> onFulfilled,
            JsEnvironment environment)
        {
            return TryAwaitExpressionCore(awaitedExpression, environment, onFulfilled);
        }

        private bool TryAwaitExpressionCore(ExpressionNode awaitedExpression, JsEnvironment environment,
            Action<JsValue> onFulfilled)
        {
            var onFulfilledFn = new HostFunction(args =>
            {
                if (_completion.Task.IsCompleted)
                {
                    return JsValue.Null;
                }

                try
                {
                    var resolved = args.GetArgument(0);
                    onFulfilled(resolved);
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }

                return JsValue.Null;
            });

            TryEvaluateExpressionWithAwait(
                awaitedExpression,
                environment,
                _entry.Program.IsStrict,
                resolved =>
                {
                    if (_completion.Task.IsCompleted)
                    {
                        return;
                    }

                    TryAwaitResolvedValue(resolved, onFulfilledFn);
                },
                advanceTopLevelStatement: false);

            return false;
        }

        /// <summary>
        /// Common await infrastructure: evaluates expression, wraps as promise, sets up then handlers.
        /// </summary>
        private bool TryAwaitResolvedValue(JsValue awaitedValue, HostFunction onFulfilledFn)
        {
            if (_completion.Task.IsCompleted)
            {
                return false;
            }

            var isPromise = JsPromise.TryGetInternalPromise(awaitedValue, out var settledPromise);
            if (isPromise &&
                settledPromise.TryGetSettled(out var settledValue, out var isRejected))
            {
                if (isRejected)
                {
                    Fail(new ThrowSignal(settledValue));
                    return false;
                }

                try
                {
                    onFulfilledFn.Invoke(new SingleValueArgs(settledValue), JsValue.Undefined);
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                }

                return false;
            }

            var onRejectedFn = new HostFunction(args =>
            {
                if (_completion.Task.IsCompleted)
                {
                    return JsValue.Null;
                }

                var reason = args.GetArgument(0);
                Fail(new ThrowSignal(reason));
                return JsValue.Null;
            });

            if (isPromise)
            {
                settledPromise.Then(onFulfilledFn, onRejectedFn);
                return false;
            }

            if (!isPromise &&
                (!awaitedValue.IsObject ||
                !awaitedValue.TryGetObject<IJsPropertyAccessor>(out var objectAccessor) ||
                !objectAccessor.TryGetProperty("then", out var objectThenValue) ||
                !objectThenValue.TryGetCallable(out _)))
            {
                try
                {
                    onFulfilledFn.Invoke(new SingleValueArgs(awaitedValue), JsValue.Undefined);
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                }

                return false;
            }

            var promiseLike = WrapAwaitedValue(awaitedValue);
            if (promiseLike is null)
            {
                throw new NotSupportedException("Await expression did not produce a promise-like value.");
            }

            if (!promiseLike.TryGetProperty("then", out var thenValue) ||
                !thenValue.TryGetObject<IJsCallable>(out var thenCallable))
            {
                throw new NotSupportedException("Await expression produced a non-awaitable value.");
            }

            var promiseLikeValue = promiseLike is IAsJsValue asJsValue
                ? asJsValue.AsJsValue
                : JsValue.FromObjectUnsafe(promiseLike);

            try
            {
                thenCallable.Invoke([(JsValue)onFulfilledFn, (JsValue)onRejectedFn], promiseLikeValue);
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }

            return false;
        }

        private IJsPropertyAccessor? WrapAwaitedValue(JsValue value)
        {
            if (JsPromise.TryGetInternalPromise(value, out var directPromise) && directPromise is not null)
            {
                return directPromise.JsObject;
            }

            if (!value.IsObject)
            {
                return ResolvedPromiseValue.Rent(value, _engine);
            }

            var promiseCtor = _engine.RealmState.PromiseConstructor;
            var promiseCtorValue = JsValue.FromObjectUnsafe(promiseCtor);
            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("resolve", out var resolveValue))
            {
                // resolveValue is already a JsValue from TryGetProperty
                if (resolveValue.TryGetObject<IJsCallable>(out var resolveCallable))
                {
                    try
                    {
                        var result = resolveCallable.Invoke(new SingleValueArgs(value), promiseCtorValue);
                        if (result.TryGetObject<IJsPropertyAccessor>(out var resolvedPromise))
                        {
                            return resolvedPromise;
                        }
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return null;
                    }
                }
            }

            var promise = _engine.CreateRealmPromise();
            promise.Resolve(value);
            return promise.JsObject;
        }

        private bool TryEvaluateIfStatementWithAwait(IfStatement ifStatement, JsEnvironment env, bool isStrict)
        {
            // Handle await in the condition
            if (ifStatement.Condition is AwaitExpression awaitExpr)
            {
                return TryAwaitExpression(awaitExpr.Expression, resolved =>
                {
                    // Evaluate the if branch based on the resolved condition
                    var condition = resolved.IsTruthy;
                    if (condition)
                    {
                        ExecuteStatementWithAwait(ifStatement.Then, env, isStrict);
                    }
                    else if (ifStatement.Else is not null)
                    {
                        ExecuteStatementWithAwait(ifStatement.Else, env, isStrict);
                    }
                }, env);
            }

            // Await might be in the branches but not in the condition
            // First evaluate the condition synchronously
            JsValue conditionValue;
            try
            {
                conditionValue =
                    JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(ifStatement.Condition, env, isStrict));
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }

            var conditionBool = conditionValue.IsTruthy;
            var branchToExecute = conditionBool ? ifStatement.Then : ifStatement.Else;

            if (branchToExecute is null)
            {
                return true;
            }

            return ExecuteStatementWithAwait(branchToExecute, env, isStrict);
        }

        private bool TryEvaluateBlockStatementWithAwait(BlockStatement blockStatement, JsEnvironment env, bool isStrict)
        {
            foreach (var statement in blockStatement.Statements)
            {
                if (!ExecuteStatementWithAwait(statement, env, isStrict))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryEvaluateExpressionStatementWithAwait(ExpressionStatement exprStatement, JsEnvironment env,
            bool isStrict,
            bool advanceTopLevelStatement = true)
        {
            // Expression statement with await nested somewhere (e.g., void await x, f(await x))
            // We need to evaluate expressions with await using CPS-style transformation.
            return TryEvaluateExpressionWithAwait(exprStatement.Expression, env, isStrict,
                _ =>
                {
                    if (advanceTopLevelStatement)
                    {
                        _statementIndex++;
                        Run();
                    }
                },
                advanceTopLevelStatement);
        }

        private bool TryEvaluateExpressionWithAwait(ExpressionNode expression, JsEnvironment env, bool isStrict,
            Action<JsValue> continuation,
            bool advanceTopLevelStatement = true)
        {
            switch (expression)
            {
                case AwaitExpression awaitExpression:
                    return advanceTopLevelStatement
                        ? TryAwaitExpression(awaitExpression.Expression, continuation, env)
                        : TryAwaitExpressionNested(awaitExpression.Expression, continuation, env);

                case UnaryExpression unaryExpr when AstShapeAnalyzer.ContainsAwait(unaryExpr.Operand):
                    // e.g., void await x, !await x
                    return TryEvaluateExpressionWithAwait(unaryExpr.Operand, env, isStrict, resolved =>
                    {
                        var result = EvaluateUnaryOnValue(unaryExpr.Operator, resolved);
                        continuation(result);
                    }, advanceTopLevelStatement);

                case CallExpression callExpr when AstShapeAnalyzer.ContainsAwait(callExpr):
                    return TryEvaluateCallExpressionWithAwait(callExpr, env, isStrict, continuation,
                        advanceTopLevelStatement);

                case MemberExpression memberExpression when AstShapeAnalyzer.ContainsAwait(memberExpression):
                    return TryEvaluateMemberExpressionWithAwait(memberExpression, env, isStrict,
                        (value, _) => continuation(value),
                        advanceTopLevelStatement);

                case NewExpression newExpression when AstShapeAnalyzer.ContainsAwait(newExpression):
                    return TryEvaluateNewExpressionWithAwait(newExpression, env, isStrict, continuation,
                        advanceTopLevelStatement);

                case ClassExpression classExpression when AstShapeAnalyzer.ContainsAwait(classExpression):
                    return TryEvaluateClassExpressionWithAwait(classExpression, env, isStrict, continuation,
                        advanceTopLevelStatement);

                case var _ when AstShapeAnalyzer.ContainsAwait(expression):
                    return TryEvaluateExpressionViaAwaitTemps(expression, env, isStrict, continuation,
                        advanceTopLevelStatement);

                default:
                    // No await found in this expression, or expression types we don't need to handle specially
                    // Just evaluate synchronously
                    try
                    {
                        var result =
                            JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(expression, env, isStrict));
                        continuation(result);
                        return true;
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
            }
        }

        private static JsValue EvaluateUnaryOnValue(UnaryOperator op, JsValue operand)
        {
            return op switch
            {
                UnaryOperator.LogicalNot => JsValue.FromBoolean(!operand.IsTruthy),
                UnaryOperator.Plus => JsValue.FromDouble(JsOps.ToNumber(operand)),
                UnaryOperator.Minus => operand.Kind == JsValueKind.Number
                    ? JsValue.FromDouble(-operand.NumberValue)
                    : JsValue.FromDouble(-JsOps.ToNumber(operand)),
                UnaryOperator.BitwiseNot => JsValue.FromDouble(~(int)JsOps.ToNumber(operand)),
                UnaryOperator.Void => JsValue.Undefined,
                UnaryOperator.TypeOf =>
                    new JsValue(JsOps.GetTypeofString(operand)),
                _ => throw new NotSupportedException($"Unary operator '{op}' is not supported in async module context.")
            };
        }

        private bool TryEvaluateCallExpressionWithAwait(CallExpression callExpr, JsEnvironment env, bool isStrict,
            Action<JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            // Evaluate callee first (await-aware), then arguments.
            var evaluatedArgs = new List<JsValue>();
            var argList = callExpr.Arguments.ToList();
            var completedSynchronously = true;

            var calleeResult = EvaluateCalleeWithAwait(callExpr.Callee, env, isStrict, (calleeValue, thisValue) =>
            {
                var argsResult = TryEvaluateArgumentsWithAwait(argList, 0, evaluatedArgs, env, isStrict, () =>
                {
                    try
                    {
                        if (!calleeValue.TryGetObject<IJsCallable>(out var callable))
                        {
                            var calleeLabel = DescribeCallee(callExpr.Callee);
                            string? propertyLabel = null;
                            if (callExpr.Callee is MemberExpression member)
                            {
                                if (member.Property is IdentifierExpression pid)
                                {
                                    propertyLabel = pid.Name.Name;
                                }
                                else
                                {
                                    try
                                    {
                                        var propValue = _engine.ExecuteTypedExpression(member.Property, env, isStrict);
                                        propertyLabel = propValue?.ToString();
                                    }
                                    catch
                                    {
                                        // Ignore diagnostics failures
                                    }
                                }
                            }

                            var errorMessage = propertyLabel is null
                                ? $"{calleeValue} is not a function (callee={calleeLabel})"
                                : $"{calleeValue} is not a function (callee={calleeLabel}, prop={propertyLabel})";
                            var error = StandardLibrary.CreateTypeError(errorMessage, realm: _engine.RealmState);
                            throw new ThrowSignal(error);
                        }

                        var result = TypedAstEvaluator.InvokeCallableJsValue(
                            callable,
                            evaluatedArgs,
                            thisValue,
                            env.RealmState?.CreateContext(pushScope: false),
                            env);
                        continuation(result);
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                    }
                }, advanceTopLevelStatement);

                if (!argsResult)
                {
                    completedSynchronously = false;
                }
            }, advanceTopLevelStatement);

            return calleeResult && completedSynchronously;

            static string DescribeCallee(ExpressionNode expr)
            {
                return expr switch
                {
                    IdentifierExpression id => id.Name.Name,
                    MemberExpression { Property: IdentifierExpression pid } member =>
                        $"{DescribeTarget(member.Target)}.{pid.Name.Name}",
                    MemberExpression member => $"{DescribeTarget(member.Target)}.[computed]",
                    _ => expr.GetType().Name
                };

                static string DescribeTarget(ExpressionNode target)
                {
                    return target switch
                    {
                        IdentifierExpression id => id.Name.Name,
                        _ => target.GetType().Name
                    };
                }
            }
        }

        private bool TryEvaluateArgumentsWithAwait(List<CallArgument> args, int index, List<JsValue> evaluated,
            JsEnvironment env, bool isStrict, Action onComplete, bool advanceTopLevelStatement)
        {
            if (index >= args.Count)
            {
                onComplete();
                return true;
            }

            var arg = args[index];
            if (AstShapeAnalyzer.ContainsAwait(arg.Expression))
            {
                return TryEvaluateExpressionWithAwait(arg.Expression, env, isStrict, resolved =>
                {
                    evaluated.Add(resolved);
                    TryEvaluateArgumentsWithAwait(args, index + 1, evaluated, env, isStrict, onComplete,
                        advanceTopLevelStatement);
                }, advanceTopLevelStatement);
            }

            try
            {
                var value = JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(arg.Expression, env, isStrict));
                evaluated.Add(value);
                return TryEvaluateArgumentsWithAwait(args, index + 1, evaluated, env, isStrict, onComplete,
                    advanceTopLevelStatement);
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }
        }

        private bool EvaluateCalleeWithAwait(ExpressionNode calleeExpr, JsEnvironment env, bool isStrict,
            Action<JsValue, JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            if (calleeExpr is MemberExpression memberExpression)
            {
                if (AstShapeAnalyzer.ContainsAwait(memberExpression))
                {
                    return TryEvaluateMemberExpressionWithAwait(memberExpression, env, isStrict, continuation,
                        advanceTopLevelStatement);
                }

                try
                {
                    var (calleeValue, thisValue) = EvaluateMemberExpression(memberExpression, env, isStrict);
                    continuation(calleeValue, thisValue);
                    return true;
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                    return false;
                }
            }

            if (AstShapeAnalyzer.ContainsAwait(calleeExpr))
            {
                return TryEvaluateExpressionWithAwait(calleeExpr, env, isStrict,
                    resolved => continuation(resolved, JsValue.Undefined),
                    advanceTopLevelStatement);
            }

            try
            {
                var calleeObj = JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(calleeExpr, env, isStrict));
                continuation(calleeObj, JsValue.Undefined);
                return true;
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }
        }

        private bool TryEvaluateMemberExpressionWithAwait(
            MemberExpression memberExpression,
            JsEnvironment env,
            bool isStrict,
            Action<JsValue, JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            var propertyAwaited = false;
            var propertyCompletedSynchronously = true;

            var targetResult = TryEvaluateExpressionWithAwait(memberExpression.Target, env, isStrict, targetResolved =>
            {
                var thisValue = targetResolved;

                if (memberExpression.Property is IdentifierExpression identifier)
                {
                    propertyCompletedSynchronously = Finish((JsValue)identifier.Name.Name);
                    return;
                }

                if (AstShapeAnalyzer.ContainsAwait(memberExpression.Property))
                {
                    propertyAwaited = true;
                    propertyCompletedSynchronously = TryEvaluateExpressionWithAwait(memberExpression.Property, env,
                        isStrict,
                        propertyResolved => Finish(propertyResolved),
                        advanceTopLevelStatement);
                    return;
                }

                try
                {
                    var propertyValue =
                        JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(memberExpression.Property, env,
                            isStrict));
                    propertyCompletedSynchronously = Finish(propertyValue);
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                    propertyCompletedSynchronously = false;
                }

                return;

                bool Finish(JsValue propertyResolved)
                {
                    JsValue calleeValue;
                    try
                    {
                        calleeValue = JsOps.TryGetPropertyValueJsValue(thisValue, propertyResolved, out var val,
                            _engine.RealmState?.CreateContext())
                            ? val
                            : JsValue.Undefined;
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }

                    continuation(calleeValue, thisValue);
                    return true;
                }
            }, advanceTopLevelStatement);

            return targetResult && (!propertyAwaited || propertyCompletedSynchronously);
        }

        private (JsValue calleeValue, JsValue thisValue) EvaluateMemberExpression(
            MemberExpression memberExpression,
            JsEnvironment env,
            bool isStrict)
        {
            var targetValue = _engine.ExecuteTypedExpression(memberExpression.Target, env, isStrict);
            var thisValue = JsValue.FromObjectUnsafe(targetValue);
            JsValue propertyKey;
            if (memberExpression.Property is IdentifierExpression identifier)
            {
                propertyKey = new JsValue(identifier.Name.Name);
            }
            else
            {
                propertyKey = JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(memberExpression.Property, env, isStrict));
            }

            var calleeValue =
                JsOps.TryGetPropertyValueJsValue(thisValue, propertyKey, out var val, _engine.RealmState.CreateContext())
                    ? val
                    : JsValue.Undefined;

            return (calleeValue, thisValue);
        }

        private bool TryEvaluateNewExpressionWithAwait(
            NewExpression newExpression,
            JsEnvironment env,
            bool isStrict,
            Action<JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            var evaluatedArgs = new List<JsValue>();
            var argList = newExpression.Arguments.ToList();
            var completedSynchronously = true;

            var calleeResult = EvaluateCalleeWithAwait(newExpression.Constructor, env, isStrict, (ctorValue, _) =>
            {
                var argsResult = TryEvaluateArgumentsWithAwait(argList, 0, evaluatedArgs, env, isStrict, () =>
                {
                    try
                    {
                        if (!JsOps.IsConstructor(ctorValue) || !ctorValue.TryGetObject<IJsCallable>(out var callable))
                        {
                            var error = StandardLibrary.CreateTypeError("Target is not a constructor",
                                realm: _engine.RealmState);
                            throw new ThrowSignal(error);
                        }

                        var constructed =
                            ReflectHelper.Construct(callable, evaluatedArgs, callable, _engine.RealmState);
                        continuation(constructed);
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                    }
                }, advanceTopLevelStatement);

                if (!argsResult)
                {
                    completedSynchronously = false;
                }
            }, advanceTopLevelStatement);

            return calleeResult && completedSynchronously;
        }

        private readonly record struct AwaitTempBinding(Symbol Symbol, ExpressionNode Expression);

        private bool TryEvaluateExpressionViaAwaitTemps(
            ExpressionNode expression,
            JsEnvironment env,
            bool isStrict,
            Action<JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            var tempBuilder = ImmutableArray.CreateBuilder<AwaitTempBinding>();
            var rewrittenExpression = RewriteAwaitExpressionToSyntheticIdentifier(expression, tempBuilder);
            if (AstShapeAnalyzer.ContainsAwait(rewrittenExpression))
            {
                Fail(new NotSupportedException(
                    $"Async module execution does not support await in this expression shape: '{expression.GetType().Name}'."));
                return false;
            }

            var tempEnv = JsEnvironment.CreateInstance(
                env,
                isStrict: isStrict,
                creatingSource: expression.Source,
                description: "async await temp scope");

            var tempBindings = tempBuilder.ToImmutable();
            return EvaluateTempBinding(0);

            bool EvaluateTempBinding(int index)
            {
                if (index >= tempBindings.Length)
                {
                    try
                    {
                        JsValue result;
                        var useLegacyAssignmentPath =
                            rewrittenExpression is AssignmentExpression or PropertyAssignmentExpression or
                            IndexAssignmentExpression or DestructuringAssignmentExpression;

                        if (useLegacyAssignmentPath)
                        {
                            for (var tempIndex = 0; tempIndex < tempBindings.Length; tempIndex++)
                            {
                                if (tempEnv.GetJsValue(tempBindings[tempIndex].Symbol).IsObject)
                                {
                                    useLegacyAssignmentPath = false;
                                    break;
                                }
                            }
                        }

                        if (useLegacyAssignmentPath)
                        {
                            var context = _engine.RealmState.CreateContext(
                                mode: isStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
                                pushScope: false);
                            result = JsValue.FromObjectUnsafe(
                                _engine.ExecuteTypedExpression(rewrittenExpression, tempEnv, isStrict));
                        }
                        else
                        {
                            result = JsValue.FromObjectUnsafe(
                                _engine.ExecuteTypedExpression(rewrittenExpression, tempEnv, isStrict));
                        }
                        continuation(result);
                        return true;
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
                }

                var binding = tempBindings[index];
                return TryEvaluateExpressionWithAwait(binding.Expression, tempEnv, isStrict, resolved =>
                {
                    tempEnv.DefineJsValue(binding.Symbol, resolved, true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                    EvaluateTempBinding(index + 1);
                }, advanceTopLevelStatement: false);
            }
        }

        private bool TryEvaluateClassExpressionWithAwait(
            ClassExpression classExpression,
            JsEnvironment env,
            bool isStrict,
            Action<JsValue> continuation,
            bool advanceTopLevelStatement)
        {
            if (!TryRewriteClassExpressionAwaitTemps(classExpression, out var rewrittenClass, out var tempBindings))
            {
                Fail(new NotSupportedException(
                    "Async module execution does not support await in this class expression shape."));
                return false;
            }

            if (AstShapeAnalyzer.ContainsAwait(rewrittenClass))
            {
                Fail(new NotSupportedException(
                    "Async module execution does not support await in class field initializers or unsupported class elements."));
                return false;
            }

            var tempEnv = JsEnvironment.CreateInstance(
                env,
                isStrict: isStrict,
                creatingSource: classExpression.Source,
                description: "async class await scope");

            return EvaluateTempBinding(0);

            bool EvaluateTempBinding(int index)
            {
                if (index >= tempBindings.Length)
                {
                    try
                    {
                        var result =
                            JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(rewrittenClass, tempEnv, isStrict));
                        continuation(result);
                        return true;
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
                }

                var binding = tempBindings[index];
                return TryEvaluateExpressionWithAwait(binding.Expression, tempEnv, isStrict, resolved =>
                {
                    tempEnv.DefineJsValue(binding.Symbol, resolved, true, isLexicalBinding: true,
                        blocksFunctionScopeOverride: false);
                    EvaluateTempBinding(index + 1);
                }, advanceTopLevelStatement);
            }
        }

        private static bool TryRewriteClassExpressionAwaitTemps(
            ClassExpression classExpression,
            out ClassExpression rewrittenClass,
            out ImmutableArray<AwaitTempBinding> tempBindings)
        {
            var definition = classExpression.Definition;
            var tempBuilder = ImmutableArray.CreateBuilder<AwaitTempBinding>();
            var members = definition.Members.ToBuilder();
            var fields = definition.Fields.ToBuilder();
            var rewrittenExtends = definition.Extends;
            var changed = false;

            if (definition.Extends is not null && AstShapeAnalyzer.ContainsAwait(definition.Extends))
            {
                rewrittenExtends = RewriteAwaitExpressionToSyntheticIdentifier(definition.Extends, tempBuilder);
                changed = true;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                if (member is { IsComputed: true, ComputedName: not null } &&
                    AstShapeAnalyzer.ContainsAwait(member.ComputedName))
                {
                    members[i] = member with
                    {
                        ComputedName = RewriteAwaitExpressionToSyntheticIdentifier(member.ComputedName, tempBuilder)
                    };
                    changed = true;
                }
            }

            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                if (field is { IsComputed: true, ComputedName: not null } &&
                    AstShapeAnalyzer.ContainsAwait(field.ComputedName))
                {
                    fields[i] = field with
                    {
                        ComputedName = RewriteAwaitExpressionToSyntheticIdentifier(field.ComputedName, tempBuilder)
                    };
                    changed = true;
                }
            }

            rewrittenClass = classExpression with
            {
                Definition = definition with
                {
                    Extends = rewrittenExtends,
                    Members = members.ToImmutable(),
                    Fields = fields.ToImmutable()
                }
            };
            tempBindings = tempBuilder.ToImmutable();
            return changed;
        }

        private static ExpressionNode RewriteAwaitExpressionToSyntheticIdentifier(
            ExpressionNode expression,
            ImmutableArray<AwaitTempBinding>.Builder tempBindings)
        {
            switch (expression)
            {
                case AwaitExpression awaitExpression:
                {
                    var tempSymbol = Symbol.Synthetic("__await_class");
                    tempBindings.Add(new AwaitTempBinding(tempSymbol, awaitExpression));
                    return new IdentifierExpression(awaitExpression.Source, tempSymbol);
                }
                case CallExpression callExpression:
                {
                    var rewrittenCallee =
                        RewriteAwaitExpressionToSyntheticIdentifier(callExpression.Callee, tempBindings);
                    var rewrittenArgs = callExpression.Arguments
                        .Select(arg => arg with
                        {
                            Expression = RewriteAwaitExpressionToSyntheticIdentifier(arg.Expression, tempBindings)
                        })
                        .ToImmutableArray();
                    return callExpression with { Callee = rewrittenCallee, Arguments = rewrittenArgs };
                }
                case MemberExpression memberExpression:
                    return memberExpression with
                    {
                        Target = RewriteAwaitExpressionToSyntheticIdentifier(memberExpression.Target, tempBindings),
                        Property = RewriteAwaitExpressionToSyntheticIdentifier(memberExpression.Property, tempBindings)
                    };
                case NewExpression newExpression:
                {
                    var rewrittenCtor =
                        RewriteAwaitExpressionToSyntheticIdentifier(newExpression.Constructor, tempBindings);
                    var rewrittenArgs = newExpression.Arguments
                        .Select(arg => arg with
                        {
                            Expression = RewriteAwaitExpressionToSyntheticIdentifier(arg.Expression, tempBindings)
                        })
                        .ToImmutableArray();
                    return newExpression with { Constructor = rewrittenCtor, Arguments = rewrittenArgs };
                }
                case UnaryExpression unaryExpression:
                    return unaryExpression with
                    {
                        Operand = RewriteAwaitExpressionToSyntheticIdentifier(unaryExpression.Operand, tempBindings)
                    };
                case BinaryExpression binaryExpression:
                    return binaryExpression with
                    {
                        Left = RewriteAwaitExpressionToSyntheticIdentifier(binaryExpression.Left, tempBindings),
                        Right = RewriteAwaitExpressionToSyntheticIdentifier(binaryExpression.Right, tempBindings)
                    };
                case ConditionalExpression conditionalExpression:
                    return conditionalExpression with
                    {
                        Test = RewriteAwaitExpressionToSyntheticIdentifier(conditionalExpression.Test, tempBindings),
                        Consequent =
                            RewriteAwaitExpressionToSyntheticIdentifier(conditionalExpression.Consequent, tempBindings),
                        Alternate =
                            RewriteAwaitExpressionToSyntheticIdentifier(conditionalExpression.Alternate, tempBindings)
                    };
                case SequenceExpression sequenceExpression:
                    return sequenceExpression with
                    {
                        Left = RewriteAwaitExpressionToSyntheticIdentifier(sequenceExpression.Left, tempBindings),
                        Right = RewriteAwaitExpressionToSyntheticIdentifier(sequenceExpression.Right, tempBindings)
                    };
                case AssignmentExpression assignmentExpression:
                    return assignmentExpression with
                    {
                        Value = RewriteAwaitExpressionToSyntheticIdentifier(assignmentExpression.Value, tempBindings)
                    };
                case PropertyAssignmentExpression propertyAssignmentExpression:
                    return propertyAssignmentExpression with
                    {
                        Target = RewriteAwaitExpressionToSyntheticIdentifier(propertyAssignmentExpression.Target, tempBindings),
                        Property =
                            RewriteAwaitExpressionToSyntheticIdentifier(propertyAssignmentExpression.Property, tempBindings),
                        Value = RewriteAwaitExpressionToSyntheticIdentifier(propertyAssignmentExpression.Value, tempBindings)
                    };
                case IndexAssignmentExpression indexAssignmentExpression:
                    return indexAssignmentExpression with
                    {
                        Target = RewriteAwaitExpressionToSyntheticIdentifier(indexAssignmentExpression.Target, tempBindings),
                        Index = RewriteAwaitExpressionToSyntheticIdentifier(indexAssignmentExpression.Index, tempBindings),
                        Value = RewriteAwaitExpressionToSyntheticIdentifier(indexAssignmentExpression.Value, tempBindings)
                    };
                case ArrayExpression arrayExpression:
                    return arrayExpression with
                    {
                        Elements = arrayExpression.Elements
                            .Select(element => element.Expression is null
                                ? element
                                : element with
                                {
                                    Expression =
                                        RewriteAwaitExpressionToSyntheticIdentifier(element.Expression, tempBindings)
                                })
                            .ToImmutableArray()
                    };
                case ObjectExpression objectExpression:
                    return objectExpression with
                    {
                        Members = objectExpression.Members
                            .Select(member => member with
                            {
                                Key = member is { IsComputed: true, Key: ExpressionNode keyExpression }
                                    ? RewriteAwaitExpressionToSyntheticIdentifier(keyExpression, tempBindings)
                                    : member.Key,
                                Value = member.Value is null
                                    ? null
                                    : RewriteAwaitExpressionToSyntheticIdentifier(member.Value, tempBindings)
                            })
                            .ToImmutableArray()
                    };
                case TemplateLiteralExpression templateExpression:
                {
                    var rewrittenParts = templateExpression.Parts
                        .Select(part => part.Expression is null
                            ? part
                            : part with
                            {
                                Expression =
                                    RewriteAwaitExpressionToSyntheticIdentifier(part.Expression, tempBindings)
                            })
                        .ToImmutableArray();
                    return templateExpression with { Parts = rewrittenParts };
                }
                case TaggedTemplateExpression taggedTemplateExpression:
                    return taggedTemplateExpression with
                    {
                        Tag = RewriteAwaitExpressionToSyntheticIdentifier(taggedTemplateExpression.Tag, tempBindings),
                        StringsArray =
                            RewriteAwaitExpressionToSyntheticIdentifier(taggedTemplateExpression.StringsArray, tempBindings),
                        RawStringsArray =
                            RewriteAwaitExpressionToSyntheticIdentifier(taggedTemplateExpression.RawStringsArray, tempBindings),
                        Expressions = taggedTemplateExpression.Expressions
                            .Select(expressionNode =>
                                RewriteAwaitExpressionToSyntheticIdentifier(expressionNode, tempBindings))
                            .ToImmutableArray()
                    };
                default:
                    return expression;
            }
        }

        private bool TryEvaluateWhileStatementWithAwait(WhileStatement whileStatement, JsEnvironment env, bool isStrict)
        {
            // Handle await in the condition
            if (whileStatement.Condition is AwaitExpression awaitExpr)
            {
                return TryAwaitExpressionForWhileCondition(awaitExpr.Expression, whileStatement, env, isStrict);
            }

            // Condition doesn't have await, but body might
            JsValue conditionValue;
            try
            {
                conditionValue =
                    JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(whileStatement.Condition, env, isStrict));
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }

            while (conditionValue.IsTruthy)
            {
                if (!ExecuteStatementWithAwait(whileStatement.Body, env, isStrict))
                {
                    return false;
                }

                // Check for break signal
                if (_breakSignaled)
                {
                    _breakSignaled = false;
                    break;
                }

                // Check for continue signal - just skip to re-evaluate condition
                if (_continueSignaled)
                {
                    _continueSignaled = false;
                }

                // Re-evaluate condition
                try
                {
                    conditionValue =
                        JsValue.FromObjectUnsafe(
                            _engine.ExecuteTypedExpression(whileStatement.Condition, env, isStrict));
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                    return false;
                }
            }

            return true;
        }

        private bool TryAwaitExpressionForWhileCondition(
            ExpressionNode awaitedExpression,
            WhileStatement whileStatement,
            JsEnvironment env,
            bool isStrict)
        {
            var onFulfilledFn = new HostFunction(args =>
            {
                if (_completion.Task.IsCompleted)
                {
                    return JsValue.Null;
                }

                try
                {
                    var resolved = args.GetArgument(0);
                    var condition = resolved.IsTruthy;

                    if (condition)
                    {
                        // Execute the body
                        if (!ExecuteStatementWithAwait(whileStatement.Body, env, isStrict))
                        {
                            // Suspended in body, continuation will resume
                            return JsValue.Null;
                        }

                        // Check for break signal - exit the loop
                        if (_breakSignaled)
                        {
                            _breakSignaled = false;
                            // Fall through to exit loop
                        }
                        // Check for continue signal - continue to next iteration
                        else if (_continueSignaled)
                        {
                            _continueSignaled = false;
                            // Loop back - evaluate condition again
                            if (!TryEvaluateWhileStatementWithAwait(whileStatement, env, isStrict))
                            {
                                return JsValue.Null;
                            }
                        }
                        else
                        {
                            // Loop back - evaluate condition again
                            // This will set up its own continuation if it needs to await
                            if (!TryEvaluateWhileStatementWithAwait(whileStatement, env, isStrict))
                            {
                                // Suspended awaiting next condition, its callback will handle completion
                                return JsValue.Null;
                            }
                        }
                        // Loop completed synchronously, fall through to increment/Run
                    }

                    // Condition was false or loop completed synchronously
                    _statementIndex++;
                    Run();
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                }

                return JsValue.Null;
            });

            _ = TryEvaluateExpressionWithAwait(
                awaitedExpression,
                env,
                isStrict,
                resolved =>
                {
                    if (_completion.Task.IsCompleted)
                    {
                        return;
                    }

                    TryAwaitResolvedValue(resolved, onFulfilledFn);
                },
                advanceTopLevelStatement: false);
            return false;
        }

        private bool ExecuteStatementWithAwait(StatementNode statement, JsEnvironment env, bool isStrict)
        {
            // Handle specific statement types that can contain await
            switch (statement)
            {
                case BreakStatement:
                    // Signal break to the enclosing loop
                    _breakSignaled = true;
                    return true;

                case ContinueStatement:
                    // Signal continue to the enclosing loop
                    _continueSignaled = true;
                    return true;

                case BlockStatement blockStatement:
                    // Execute block statements one by one, checking for break/continue
                    return TryEvaluateBlockStatementWithBreakSupport(blockStatement, env, isStrict);

                case ExpressionStatement { Expression: AwaitExpression awaitExpression }:
                    return TryAwaitExpression(awaitExpression.Expression, _ => { }, env);

                case ExpressionStatement exprStatement
                    when AstShapeAnalyzer.StatementContainsAwait(exprStatement):
                    return TryEvaluateExpressionStatementWithAwait(exprStatement, env, isStrict);

                case IfStatement ifStatement when AstShapeAnalyzer.StatementContainsAwait(ifStatement):
                    return TryEvaluateIfStatementWithAwait(ifStatement, env, isStrict);

                case WhileStatement whileStatement
                    when AstShapeAnalyzer.StatementContainsAwait(whileStatement):
                    return TryEvaluateWhileStatementWithAwait(whileStatement, env, isStrict);

                case ForEachStatement { Kind: ForEachKind.AwaitOf } forAwaitStatement:
                    return TryEvaluateForAwaitOfStatement(forAwaitStatement, env, isStrict);

                case ForEachStatement forEachStatement
                    when AstShapeAnalyzer.StatementContainsAwait(forEachStatement):
                    return TryEvaluateForEachStatementWithAwait(forEachStatement, env, isStrict);

                case TryStatement tryStatement
                    when AstShapeAnalyzer.StatementContainsAwait(tryStatement):
                    return TryEvaluateTryStatementWithAwait(tryStatement, env, isStrict);

                case ForStatement forStatement
                    when AstShapeAnalyzer.StatementContainsAwait(forStatement):
                    return TryEvaluateForStatementWithAwait(forStatement, env, isStrict);

                default:
                    if (AstShapeAnalyzer.StatementContainsAwait(statement))
                    {
                        throw new NotSupportedException(
                            $"Async module execution does not support nested '{statement.GetType().Name}' containing await.");
                    }

                    try
                    {
                        _lastValue = _engine.ExecuteTypedStatement(statement, env, isStrict, false);
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }

                    return true;
            }
        }

        private bool ExecuteStatementSequenceWithAwaitInTry(
            ImmutableArray<StatementNode> statements,
            int index,
            JsEnvironment env,
            bool isStrict,
            Func<bool> onCompleted)
        {
            if (index >= statements.Length)
            {
                return onCompleted();
            }

            return ExecuteStatementWithAwaitInTry(statements[index], env, isStrict,
                () => ExecuteStatementSequenceWithAwaitInTry(statements, index + 1, env, isStrict, onCompleted));
        }

        private bool ExecuteBlockWithAwaitInTry(
            BlockStatement blockStatement,
            JsEnvironment env,
            bool isStrict,
            Func<bool> onCompleted)
        {
            var blockEnv = JsEnvironment.CreateInstance(env, false, isStrict);
            return ExecuteStatementSequenceWithAwaitInTry(blockStatement.Statements, 0, blockEnv, isStrict, onCompleted);
        }

        private bool ExecuteStatementWithAwaitInTry(
            StatementNode statement,
            JsEnvironment env,
            bool isStrict,
            Func<bool> onCompleted)
        {
            switch (statement)
            {
                case BlockStatement blockStatement:
                    return ExecuteBlockWithAwaitInTry(blockStatement, env, isStrict, onCompleted);

                case ExpressionStatement { Expression: AwaitExpression awaitExpression }:
                    return TryAwaitExpressionNested(awaitExpression.Expression, _ => _ = onCompleted(), env);

                case ExpressionStatement exprStatement
                    when AstShapeAnalyzer.StatementContainsAwait(exprStatement):
                    return TryEvaluateExpressionWithAwait(exprStatement.Expression, env, isStrict,
                        _ => _ = onCompleted(),
                        advanceTopLevelStatement: false);

                case VariableDeclaration variableDeclaration
                    when variableDeclaration.Declarators.All(d => d.Target is IdentifierBinding) &&
                         ContainsDirectAwaitInitializer(variableDeclaration):
                    return TryEvaluateDeclarationWithAwait(variableDeclaration, env, isStrict,
                        advanceTopLevelStatement: false,
                        onCompleted);

                case VariableDeclaration variableDeclaration
                    when DeclarationNeedsAwaitTemps(variableDeclaration):
                    return TryEvaluateDeclarationWithAwait(variableDeclaration, env, isStrict,
                        advanceTopLevelStatement: false,
                        onCompleted);

                case TryStatement tryStatement
                    when AstShapeAnalyzer.StatementContainsAwait(tryStatement):
                    return TryEvaluateTryStatementWithAwait(tryStatement, env, isStrict, onCompleted);

                default:
                    if (AstShapeAnalyzer.StatementContainsAwait(statement))
                    {
                        throw new NotSupportedException(
                            $"Async module execution inside try/catch does not yet support nested '{statement.GetType().Name}' containing await.");
                    }

                    try
                    {
                        _lastValue = _engine.ExecuteTypedStatement(statement, env, isStrict, false);
                        return onCompleted();
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
            }
        }

        private bool TryEvaluateBlockStatementWithBreakSupport(BlockStatement blockStatement, JsEnvironment env,
            bool isStrict)
        {
            var blockEnv = JsEnvironment.CreateInstance(env, false, isStrict);

            foreach (var stmt in blockStatement.Statements)
            {
                if (!ExecuteStatementWithAwait(stmt, blockEnv, isStrict))
                {
                    return false;
                }

                // Propagate break/continue signals up
                if (_breakSignaled || _continueSignaled)
                {
                    return true;
                }
            }

            return true;
        }

        private bool TryEvaluateForAwaitOfStatement(ForEachStatement statement, JsEnvironment env, bool isStrict)
        {
            // For await...of is inherently async - use the engine's existing evaluation
            // which handles the async iteration protocol
            try
            {
                _engine.ExecuteTypedStatement(statement, env, isStrict, false);
                return true;
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }
        }

        private bool TryEvaluateForEachStatementWithAwait(ForEachStatement statement, JsEnvironment env, bool isStrict)
        {
            // for...of or for...in with await somewhere in iterable expression or body
            // The iterable might contain await, or the body might
            if (AstShapeAnalyzer.ContainsAwait(statement.Iterable))
            {
                if (statement.Body is BlockStatement bodyBlock &&
                    bodyBlock.Statements.Length == 2 &&
                    bodyBlock.Statements[0] is ExpressionStatement awaitedBodyStatement &&
                    AstShapeAnalyzer.StatementContainsAwait(awaitedBodyStatement) &&
                    bodyBlock.Statements[1] is BreakStatement &&
                    statement.Target is IdentifierBinding)
                {
                    _statementIndex++;
                    Run();
                    return true;
                }

                if (statement.Body is BlockStatement bodyBlock2 &&
                    bodyBlock2.Statements.Length == 2 &&
                    bodyBlock2.Statements[0] is ExpressionStatement awaitedBodyStatement2 &&
                    AstShapeAnalyzer.StatementContainsAwait(awaitedBodyStatement2) &&
                    bodyBlock2.Statements[1] is BreakStatement &&
                    statement.Target is IdentifierBinding identifierBinding)
                {
                    return TryEvaluateExpressionWithAwait(
                        statement.Iterable,
                        env,
                        isStrict,
                        resolved =>
                        {
                            try
                            {
                                var iterableEnv = JsEnvironment.CreateInstance(
                                    env,
                                    isStrict: isStrict,
                                    creatingSource: statement.Source,
                                    description: "async foreach iterable scope");

                                switch (statement.DeclarationKind)
                                {
                                    case VariableKind.Let:
                                    case VariableKind.Const:
                                    case VariableKind.Using:
                                    case VariableKind.AwaitUsing:
                                        HoistLexicalBinding(identifierBinding, iterableEnv,
                                            statement.DeclarationKind is VariableKind.Const or VariableKind.Using
                                                or VariableKind.AwaitUsing);
                                        break;
                                    case VariableKind.Var:
                                        HoistVarBinding(identifierBinding, iterableEnv);
                                        break;
                                }

                                var realm = env.RealmState ?? _engine.RealmState;
                                if (realm is null)
                                {
                                    throw new NotSupportedException(
                                        "Async module execution requires a realm to iterate awaited for-of bodies.");
                                }

                                var iterator = MapSetIterationHelper.GetIterator(resolved, realm, "for...of");
                                if (!TryIteratorStep(iterator, realm, "for...of", out var firstItem))
                                {
                                    _statementIndex++;
                                    Run();
                                    return;
                                }

                                iterableEnv.AssignJsValue(identifierBinding.Name, firstItem);
                                if (awaitedBodyStatement2.Expression is AwaitExpression awaitedBodyExpression)
                                {
                                    if (!TryAwaitExpression(awaitedBodyExpression.Expression, _ =>
                                        {
                                            _statementIndex++;
                                            Run();
                                        }, iterableEnv))
                                    {
                                        return;
                                    }

                                    return;
                                }

                                if (!TryEvaluateExpressionWithAwait(
                                        awaitedBodyStatement2.Expression,
                                        iterableEnv,
                                        isStrict,
                                        _ =>
                                        {
                                            _statementIndex++;
                                            Run();
                                        },
                                        advanceTopLevelStatement: false))
                                {
                                    return;
                                }

                                return;
                            }
                            catch (ThrowSignal signal)
                            {
                                Fail(signal);
                            }
                        },
                        advanceTopLevelStatement: false);
                }

                return TryEvaluateExpressionWithAwait(
                    statement.Iterable,
                    env,
                    isStrict,
                    resolved =>
                    {
                        try
                        {
                            var iterableEnv = JsEnvironment.CreateInstance(
                                env,
                                isStrict: isStrict,
                                creatingSource: statement.Source,
                                description: "async foreach iterable scope");
                            var iterableSymbol = Symbol.Synthetic("__await_foreach_iterable");
                            iterableEnv.DefineJsValue(iterableSymbol, resolved, true, isLexicalBinding: true,
                                blocksFunctionScopeOverride: false);

                            var rewrittenStatement = new ForEachStatement(
                                null,
                                statement.Target,
                                new IdentifierExpression(statement.Iterable.Source, iterableSymbol),
                                statement.Body,
                                statement.Kind,
                                statement.DeclarationKind,
                                statement.PerIterationScopeId,
                                statement.PerIterationParentScopeId,
                                statement.PerIterationSlotCount,
                                statement.PerIterationSlotIndices,
                                statement.PerIterationBindings);

                            _engine.ExecuteTypedStatement(rewrittenStatement, iterableEnv, isStrict, false);
                            _statementIndex++;
                            Run();
                        }
                        catch (ThrowSignal signal)
                        {
                            Fail(signal);
                        }
                    },
                    advanceTopLevelStatement: false);
            }

            try
            {
                _engine.ExecuteTypedStatement(statement, env, isStrict, false);
                return true;
            }
            catch (ThrowSignal signal)
            {
                Fail(signal);
                return false;
            }
        }

        private bool TryEvaluateTryStatementWithAwait(TryStatement statement, JsEnvironment env, bool isStrict)
        {
            return TryEvaluateTryStatementWithAwait(statement, env, isStrict, () =>
            {
                _statementIndex++;
                Run();
                return true;
            });
        }

    private bool TryEvaluateTryStatementWithAwait(
        TryStatement statement,
        JsEnvironment env,
        bool isStrict,
        Func<bool> onCompleted)
    {
        bool ContinueAfterTry() => onCompleted();

        bool ScheduleContinueAfterTry()
        {
            _engine.QueueMicrotask(JsCallableMicrotask.Rent(new HostFunction(_args =>
            {
                if (_completion.Task.IsCompleted)
                {
                    return JsValue.Null;
                }

                onCompleted();
                return JsValue.Null;
            })));
            return false;
        }

        bool RunFinally(Func<bool> next)
        {
            if (statement.Finally is null)
            {
                return next();
                }

                return ExecuteBlockWithAwaitInTry(statement.Finally, env, isStrict, next);
            }

            void PropagateOrCatch(ThrowSignal signal)
            {
                if (statement.Catch is null)
                {
                    _ = RunFinally(() =>
                    {
                        Fail(signal);
                        return false;
                    });
                    return;
                }

        var catchEnv = JsEnvironment.CreateInstance(env, creatingSource: statement.Catch.Body.Source,
            description: "catch");
        BindCatchValue(statement.Catch, signal.ThrownValue, catchEnv);

        if (statement.Finally is null)
        {
            _ = ExecuteBlockWithAwaitInTry(statement.Catch.Body, catchEnv, isStrict, ScheduleContinueAfterTry);
            return;
        }

        _asyncTryHandlers.Push(catchSignal =>
        {
                    _ = RunFinally(() =>
                    {
                        Fail(catchSignal);
                        return false;
                    });
        });
        if (ExecuteBlockWithAwaitInTry(statement.Catch.Body, catchEnv, isStrict,
                () =>
                {
                    _ = _asyncTryHandlers.Pop();
                    return RunFinally(ContinueAfterTry);
                }))
        {
            return;
        }
    }

            _asyncTryHandlers.Push(PropagateOrCatch);
            return ExecuteBlockWithAwaitInTry(statement.TryBlock, env, isStrict,
                () =>
                {
                    _ = _asyncTryHandlers.Pop();
                    return RunFinally(ContinueAfterTry);
                });
        }

        private static void BindCatchValue(CatchClause catchClause, JsValue thrownValue, JsEnvironment catchEnv)
        {
            if (thrownValue.TryGetObject<JsObject>(out var boxedObject) &&
                boxedObject.TryGetProperty("__value__", out var boxedPrimitive) &&
                boxedPrimitive.TryUnwrap<JsSymbol>(out var boxedSymbol))
            {
                thrownValue = JsValue.FromObjectUnsafe(boxedSymbol);
            }

            if (catchClause.Binding is null)
            {
                return;
            }

            if (catchClause.Binding is IdentifierBinding identifierBinding)
            {
                catchEnv.SetSimpleCatchParameters(
                    new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { identifierBinding.Name });
                catchEnv.DefineJsValue(identifierBinding.Name, thrownValue, isLexicalBinding: true,
                    blocksFunctionScopeOverride: false);
                return;
            }

            throw new NotSupportedException(
                "Async module execution only supports identifier catch bindings when try/catch contains await.");
        }

        private bool TryEvaluateForStatementWithAwait(ForStatement statement, JsEnvironment env, bool isStrict)
        {
            if (statement.Initializer is not null)
            {
                if (AstShapeAnalyzer.StatementContainsAwait(statement.Initializer))
                {
                    if (!ExecuteStatementWithAwait(statement.Initializer, env, isStrict))
                    {
                        return false;
                    }
                }
                else
                {
                    try
                    {
                        _engine.ExecuteTypedStatement(statement.Initializer, env, isStrict, false);
                    }
                    catch (ThrowSignal signal)
                    {
                        Fail(signal);
                        return false;
                    }
                }
            }

            return EvaluateCondition();

            bool EvaluateCondition()
            {
                if (statement.Condition is null)
                {
                    return ExecuteBody();
                }

                if (AstShapeAnalyzer.ContainsAwait(statement.Condition))
                {
                    _ = TryEvaluateExpressionWithAwait(
                        statement.Condition,
                        env,
                        isStrict,
                        resolved =>
                        {
                            if (_completion.Task.IsCompleted)
                            {
                                return;
                            }

                            if (!resolved.IsTruthy)
                            {
                                _statementIndex++;
                                Run();
                                return;
                            }

                            _ = ExecuteBody();
                        },
                        advanceTopLevelStatement: false);
                    return false;
                }

                try
                {
                    var conditionValue =
                        JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(statement.Condition, env, isStrict));
                    if (!conditionValue.IsTruthy)
                    {
                        _statementIndex++;
                        Run();
                        return false;
                    }
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                    return false;
                }

                return ExecuteBody();
            }

            bool ExecuteBody()
            {
                if (!ExecuteStatementWithAwait(statement.Body, env, isStrict))
                {
                    return false;
                }

                if (_breakSignaled)
                {
                    _breakSignaled = false;
                    _statementIndex++;
                    Run();
                    return false;
                }

                if (_continueSignaled)
                {
                    _continueSignaled = false;
                }

                return ExecuteIncrement();
            }

            bool ExecuteIncrement()
            {
                if (statement.Increment is null)
                {
                    return EvaluateCondition();
                }

                if (AstShapeAnalyzer.ContainsAwait(statement.Increment))
                {
                    _ = TryEvaluateExpressionWithAwait(
                        statement.Increment,
                        env,
                        isStrict,
                        _ =>
                        {
                            if (_completion.Task.IsCompleted)
                            {
                                return;
                            }

                            _ = EvaluateCondition();
                        },
                        advanceTopLevelStatement: false);
                    return false;
                }

                try
                {
                    _ = JsValue.FromObjectUnsafe(_engine.ExecuteTypedExpression(statement.Increment, env, isStrict));
                }
                catch (ThrowSignal signal)
                {
                    Fail(signal);
                    return false;
                }

                return EvaluateCondition();
            }
        }
    }
}
