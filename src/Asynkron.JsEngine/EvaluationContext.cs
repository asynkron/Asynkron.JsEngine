#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

/// <summary>
///     Tracks the current control flow state during evaluation using typed signals.
///     Used as an alternative to exception-based control flow.
/// </summary>
public sealed class EvaluationContext(
    RealmState realmState,
    CancellationToken cancellationToken = default,
    ExecutionKind executionKind = ExecutionKind.Script) : IRentable
{
    private readonly Stack<Symbol> _functionNameHints = new();

    /// <summary>
    ///     Stack of enclosing labels (innermost first). Used to determine if a labeled
    ///     break/continue should be handled by the current statement.
    /// </summary>
    private readonly Stack<Symbol> _labelStack = new();

    private readonly Stack<PendingClassFieldInitialization> _pendingClassFieldInitializers = new();

    /// <summary>
    ///     Tracks the active private name scopes (innermost first) so private member
    ///     lookups can be mapped to their class-specific brands.
    /// </summary>
    private readonly Stack<PrivateNameScope> _privateNameScopes = new();

    private readonly Stack<ScopeFrame> _scopeStack = new();
    private int _classFieldInitializerDepth;

    // Fast path for returns - avoids allocating ReturnCompletionSignal
    private JsValue _returnValue;

    /// <summary>
    ///     Scratch slot for JsValue to avoid struct copies in hot paths.
    ///     Used by JsValue.FromDoubleRef when the value isn't in the cache.
    /// </summary>
    public JsValue ScratchValue;

    /// <summary>
    ///     Enables per-context identifier binding caches when the current scope
    ///     is known to be free of dynamic scope features (direct eval / with).
    ///     Disabled by default for safety; set by the caller.
    /// </summary>
    internal bool AllowIdentifierCache { get; set; }

    /// <summary>
    ///     Realm-specific state (prototypes/constructors) for the current execution.
    /// </summary>
    public RealmState RealmState { get; } = realmState ?? throw new ArgumentNullException(nameof(realmState));

    /// <summary>
    ///     Indicates whether the current execution originated from script code or eval.
    /// </summary>
    public ExecutionKind ExecutionKind { get; } = executionKind;

    /// <summary>
    ///     True when the currently executing source code was parsed in strict mode.
    /// </summary>
    public bool IsStrictSource { get; set; }

    /// <summary>
    ///     The current control flow signal, if any.
    ///     Note: For returns, we use _isReturn/_returnValue directly to avoid allocation.
    /// </summary>
    public ICompletionSignal? CurrentSignal { get; private set; }

    /// <summary>
    ///     The yield slot index that produced the most recent suspension.
    /// </summary>
    public int LastYieldIndex { get; private set; } = -1;

    /// <summary>
    ///     Source start position of the last yield expression (for AST-evaluated yields).
    /// </summary>
    public int LastYieldSourceStart { get; set; } = -1;

    /// <summary>
    ///     Source end position of the last yield expression (for AST-evaluated yields).
    /// </summary>
    public int LastYieldSourceEnd { get; set; } = -1;

    /// <summary>
    ///     Tracks dynamic call depth to guard against uncontrolled recursion.
    /// </summary>
    public int CallDepth { get; set; }

    /// <summary>
    ///     Maximum allowed dynamic call depth before we bail out.
    /// </summary>
    public int MaxCallDepth { get; set; } = 1000;

    /// <summary>
    ///     Indicates whether the current execution context has an initialized
    ///     <c>this</c> binding (used for derived class constructor checks).
    /// </summary>
    public bool IsThisInitialized { get; private set; } = true;

    /// <summary>
    ///     The current source reference for error reporting.
    /// </summary>
    public SourceReference? SourceReference { get; set; }

    /// <summary>
    ///     True while evaluating a class field initializer (instance or static).
    ///     Used to apply PerformEval's extra early errors for initializers.
    /// </summary>
    public bool InClassFieldInitializer => _classFieldInitializerDepth > 0;

    /// <summary>
    ///     The innermost inferred function/class name hint (for name inference on
    ///     anonymous class/function expressions).
    /// </summary>
    public Symbol? CurrentFunctionNameHint => _functionNameHints.Count > 0 ? _functionNameHints.Peek() : null;

    /// <summary>
    ///     The value associated with the control flow (for Return, Throw, and Yield signals).
    /// </summary>
    public JsValue FlowValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (IsReturn)
            {
                return _returnValue;
            }

            return CurrentSignal switch
            {
                ThrowFlowCompletionSignal ts => ts.JsValue,
                YieldCompletionSignal ys => ys.JsValue,
                _ => JsValue.Undefined
            };
        }
    }

    /// <summary>
    ///     Returns true if evaluation should stop (any signal is present).
    /// </summary>
    public bool ShouldStopEvaluation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsReturn || CurrentSignal is not null;
    }

    /// <summary>
    ///     Returns true if the current signal is Return.
    /// </summary>
    public bool IsReturn
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        private set;
    }

    /// <summary>
    ///     Returns true if the current signal is Throw.
    /// </summary>
    public bool IsThrow => CurrentSignal is ThrowFlowCompletionSignal;

    /// <summary>
    ///     Controls whether await expressions should synchronously drain the microtask queue
    ///     to resolve promises. For non-blocking top-level await we defer draining until
    ///     the host resumes the job.
    /// </summary>
    public bool DrainAwaitMicrotasks { get; set; } = true;

    /// <summary>
    ///     When true, array pattern destructuring will NOT close iterators upon completion.
    ///     Used by generators to defer iterator close until the generator completes/returns.
    ///     The generator's CloseActiveIterators will handle cleanup.
    /// </summary>
    public bool InGeneratorContext { get; set; }

    /// <summary>
    ///     Returns true if the current signal is Yield.
    /// </summary>
    public bool IsYield => CurrentSignal is YieldCompletionSignal;

    /// <summary>
    ///     Returns true if the current signal is Break.
    /// </summary>
    public bool IsBreak => CurrentSignal is BreakCompletionSignal;

    /// <summary>
    ///     Returns true if the current signal is Continue.
    /// </summary>
    public bool IsContinue => CurrentSignal is ContinueCompletionSignal;

    /// <summary>
    ///     Returns true if the current signal is a pending await suspension.
    /// </summary>
    public bool IsPendingAwait => CurrentSignal is PendingAwaitCompletionSignal;

    public ScopeFrame CurrentScope => _scopeStack.Count > 0 ? _scopeStack.Peek() : ScopeFrame.Default;

    public PrivateNameScope? CurrentPrivateNameScope => _privateNameScopes.Count > 0 ? _privateNameScopes.Peek() : null;

    public object? LastConstructedThis { get; set; }

    /// <summary>
    /// Resolves an identifier using the appropriate path based on whether 'with' statements
    /// are in scope. This branches once instead of checking per-identifier access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue GetIdentifier(JsEnvironment environment, Symbol name)
    {
        return AllowIdentifierCache
            ? environment.GetIdentifierJsValueDirect(name, this)
            : environment.GetIdentifierJsValueWithScope(name, this);
    }

    public ImmutableArray<PrivateNameScope> CapturePrivateNameScopes()
    {
        // Stack enumerates from top to bottom; reverse to preserve outer-to-inner order.
        return [.._privateNameScopes.Reverse()];
    }

    public string? ResolvePrivateNameKey(string lexeme)
    {
        foreach (var scope in _privateNameScopes)
        {
            if (scope.TryGetKey(lexeme, out var key))
            {
                return key;
            }
        }

        return null;
    }

    public IDisposable PushScope(ScopeKind kind, ScopeMode mode)
    {
        var frame = new ScopeFrame(kind, mode);
        _scopeStack.Push(frame);
        return ScopeHandle.Rent(_scopeStack);
    }

    public void MarkThisUninitialized()
    {
        IsThisInitialized = false;
    }

    public void MarkThisInitialized()
    {
        IsThisInitialized = true;
    }

    /// <summary>
    ///     Throws if the current evaluation has been cancelled (e.g. timed out).
    /// </summary>
    public void ThrowIfCancellationRequested()
    {
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    ///     Pushes a label onto the label stack.
    /// </summary>
    public void PushLabel(Symbol label)
    {
        _labelStack.Push(label);
    }

    public IDisposable EnterPrivateNameScope(PrivateNameScope scope)
    {
        _privateNameScopes.Push(scope);
        return new StackPopHandle<PrivateNameScope>(_privateNameScopes);
    }

    public IDisposable EnterPrivateNameScopes(IReadOnlyList<PrivateNameScope> scopes)
    {
        foreach (var scope in scopes)
        {
            _privateNameScopes.Push(scope);
        }

        return new StackPopHandle<PrivateNameScope>(_privateNameScopes, scopes.Count);
    }

    public IDisposable EnterClassFieldInitializer()
    {
        _classFieldInitializerDepth++;
        return new ClassFieldInitializerHandle(this);
    }

    public IDisposable EnterFunctionNameHint(Symbol name)
    {
        _functionNameHints.Push(name);
        return new StackPopHandle<Symbol>(_functionNameHints);
    }

    /// <summary>
    ///     Pops a label from the label stack.
    /// </summary>
    public void PopLabel()
    {
        if (_labelStack.Count > 0)
        {
            _labelStack.Pop();
        }
    }

    /// <summary>
    ///     Sets the context to Return state with the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetReturn(JsValue value)
    {
        IsReturn = true;
        _returnValue = value;
    }

    /// <summary>
    ///     Sets the context to Break state.
    /// </summary>
    public void SetBreak(Symbol? label = null)
    {
        CurrentSignal = new BreakCompletionSignal(label);
    }

    /// <summary>
    ///     Sets the context to Continue state.
    /// </summary>
    public void SetContinue(Symbol? label = null)
    {
        CurrentSignal = new ContinueCompletionSignal(label);
    }

    /// <summary>
    ///     Sets the context to Throw state with the given value.
    /// </summary>
    public void SetThrow(JsValue value)
    {
        IsReturn = false; // Clear return state so FlowValue uses CurrentSignal
        CurrentSignal = new ThrowFlowCompletionSignal(value);
    }

    /// <summary>
    ///     Sets the context to PendingAwait state.
    /// </summary>
    public void SetPendingAwait()
    {
        IsReturn = false; // Clear return state so FlowValue uses CurrentSignal
        CurrentSignal = PendingAwaitCompletionSignal.Instance;
    }

    /// <summary>
    ///     Sets the context to Yield state with the given value.
    /// </summary>
    public void SetYield(JsValue value, int yieldIndex)
    {
        LastYieldIndex = yieldIndex;
        CurrentSignal = new YieldCompletionSignal(value);
    }

    /// <summary>
    ///     Clears the Continue signal only if it matches the given label (or has no label).
    ///     Returns true if the signal was cleared, false if it should propagate.
    /// </summary>
    public bool TryClearContinue(Symbol? label)
    {
        if (CurrentSignal is not ContinueCompletionSignal continueSignal)
        {
            return false;
        }

        // If the continue has no label, or if it matches the provided label, clear it
        if (continueSignal.Label is not null && (label is null || !ReferenceEquals(continueSignal.Label, label)))
        {
            return false;
        }

        CurrentSignal = null;
        return true;
        // Continue has a different label, let it propagate
    }

    /// <summary>
    ///     Clears the Break signal only if it matches the given label (or has no label).
    ///     Returns true if the signal was cleared, false if it should propagate.
    /// </summary>
    public bool TryClearBreak(Symbol? label)
    {
        if (CurrentSignal is not BreakCompletionSignal breakSignal)
        {
            return false;
        }

        // If the break has no label, or if it matches the provided label, clear it
        if (breakSignal.Label is not null && (label is null || !ReferenceEquals(breakSignal.Label, label)))
        {
            return false;
        }

        CurrentSignal = null;
        return true;
        // Break has a different label, let it propagate
    }

    /// <summary>
    ///     Clears the Return signal (used when a function consumes it).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearReturn()
    {
        IsReturn = false;
        _returnValue = default;
    }

    /// <summary>
    ///     Clears any control flow signal.
    /// </summary>
    public void Clear()
    {
        IsReturn = false;
        _returnValue = default;
        CurrentSignal = null;
    }

    /// <summary>
    ///     Saves the current completion state for later restoration (used by try-finally).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CompletionState SaveCompletionState()
    {
        return new CompletionState(IsReturn, _returnValue, CurrentSignal);
    }

    /// <summary>
    ///     Restores a previously saved completion state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestoreCompletionState(in CompletionState state)
    {
        IsReturn = state.IsReturn;
        _returnValue = state.ReturnValue;
        CurrentSignal = state.Signal;
    }

    /// <summary>
    ///     Called when context is rented from pool.
    /// </summary>
    void IRentable.Activate(ILogger? logger)
    {
        logger?.LogInformation("EvaluationContext.Activate");
    }

    /// <summary>
    ///     Resets the context for reuse. Clears all stacks and signals.
    /// </summary>
    void IRentable.Reset(ILogger? logger)
    {
        logger?.LogInformation("EvaluationContext.Reset");
        Reset();
    }

    /// <summary>
    ///     Resets the context for reuse. Clears all stacks and signals.
    /// </summary>
    internal void Reset()
    {
        _labelStack.Clear();
        _privateNameScopes.Clear();
        _scopeStack.Clear();
        _pendingClassFieldInitializers.Clear();
        _functionNameHints.Clear();
        _classFieldInitializerDepth = 0;
        AllowIdentifierCache = false;
        IsStrictSource = false;
        IsReturn = false;
        _returnValue = default;
        CurrentSignal = null;
        LastYieldIndex = -1;
        LastYieldSourceStart = -1;
        LastYieldSourceEnd = -1;
        CallDepth = 0;
        MaxCallDepth = 1000;
        IsThisInitialized = true;
        SourceReference = null;
        LastConstructedThis = null;
    }

    internal void PushClassFieldInitializer(PendingClassFieldInitialization initializer)
    {
        _pendingClassFieldInitializers.Push(initializer);
    }

    internal bool TryPopClassFieldInitializer(out PendingClassFieldInitialization initializer)
    {
        return _pendingClassFieldInitializers.TryPop(out initializer);
    }

    internal void RemovePendingClassFieldInitializer(object function)
    {
        if (_pendingClassFieldInitializers.Count == 0)
        {
            return;
        }

        if (ReferenceEquals(_pendingClassFieldInitializers.Peek().Constructor, function))
        {
            _pendingClassFieldInitializers.Pop();
        }
    }

    /// <summary>
    ///     Generic disposable handle that pops items from a stack on dispose.
    ///     Used for scope management (private names, function name hints, etc).
    /// </summary>
    private sealed class StackPopHandle<T>(Stack<T> stack, int count = 1) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            var remaining = count;
            while (remaining-- > 0 && stack.Count > 0)
            {
                stack.Pop();
            }

            _disposed = true;
        }
    }

    private sealed class ClassFieldInitializerHandle(EvaluationContext context) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (context._classFieldInitializerDepth > 0)
            {
                context._classFieldInitializerDepth--;
            }

            _disposed = true;
        }
    }

    private sealed class ScopeHandle : IDisposable, IRentable
    {
        private static readonly ObjectPool<ScopeHandle> Pool = new(64, static () => new ScopeHandle());

        private Stack<ScopeFrame>? _scopes;
        private bool _disposed;

        public static ScopeHandle Rent(Stack<ScopeFrame> scopes)
        {
            var handle = Pool.Rent();
            handle._scopes = scopes;
            handle._disposed = false;
            return handle;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_scopes?.Count > 0)
            {
                _scopes.Pop();
            }

            _disposed = true;
            Pool.Return(this);
        }

        void IRentable.Activate(ILogger? logger) { }

        void IRentable.Reset(ILogger? logger)
        {
            _scopes = null;
            _disposed = false;
        }
    }
}
