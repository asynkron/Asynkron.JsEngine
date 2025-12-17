using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine;

/// <summary>
///     Represents a saved completion state for try-finally handling.
/// </summary>
public readonly struct CompletionState(bool isReturn, JsValue returnValue, ICompletionSignal? signal)
{
    public bool IsReturn { get; } = isReturn;
    public JsValue ReturnValue { get; } = returnValue;
    public ICompletionSignal? Signal { get; } = signal;

    /// <summary>
    ///     Returns true if there's any completion to restore.
    /// </summary>
    public bool HasCompletion => IsReturn || Signal is not null;
}

/// <summary>
///     Tracks the current control flow state during evaluation using typed signals.
///     Used as an alternative to exception-based control flow.
/// </summary>
public sealed class EvaluationContext(
    RealmState realmState,
    CancellationToken cancellationToken = default,
    ExecutionKind executionKind = ExecutionKind.Script)
{
    /// <summary>
    ///     Stack of enclosing labels (innermost first). Used to determine if a labeled
    ///     break/continue should be handled by the current statement.
    /// </summary>
    private readonly Stack<Symbol> _labelStack = new();

    /// <summary>
    ///     Tracks the active private name scopes (innermost first) so private member
    ///     lookups can be mapped to their class-specific brands.
    /// </summary>
    private readonly Stack<PrivateNameScope> _privateNameScopes = new();
    private readonly Stack<ScopeFrame> _scopeStack = new();
    private readonly Stack<PendingClassFieldInitialization> _pendingClassFieldInitializers = new();
    private readonly Stack<Symbol> _functionNameHints = new();
    private int _classFieldInitializerDepth;

    /// <summary>
    ///     Enables per-context identifier binding caches when the current scope
    ///     is known to be free of dynamic scope features (direct eval / with).
    ///     Disabled by default for safety; set by the caller.
    /// </summary>
    internal bool AllowIdentifierCache { get; set; }

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

    /// <summary>
    ///     Realm-specific state (prototypes/constructors) for the current execution.
    /// </summary>
    public RealmState RealmState { get; } = realmState ?? throw new ArgumentNullException(nameof(realmState));

    public IJsEngineOptions Options => RealmState.Options;

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

    // Fast path for returns - avoids allocating ReturnCompletionSignal
    private bool _isReturn;
    private JsValue _returnValue;

    /// <summary>
    ///     The yield slot index that produced the most recent suspension.
    /// </summary>
    public int LastYieldIndex { get; private set; } = -1;

    /// <summary>
    ///     Tracks dynamic call depth to guard against uncontrolled recursion.
    /// </summary>
    public int CallDepth { get; set; }

    /// <summary>
    ///     Scratch slot for JsValue to avoid struct copies in hot paths.
    ///     Used by JsValue.FromDoubleRef when the value isn't in the cache.
    /// </summary>
    public JsValue ScratchValue;

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
    ///     Returns the current innermost label, or null if not in a labeled context.
    /// </summary>
    public Symbol? CurrentLabel => _labelStack.Count > 0 ? _labelStack.Peek() : null;

    /// <summary>
    ///     The value associated with the control flow (for Return, Throw, and Yield signals).
    /// </summary>
    public JsValue FlowValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_isReturn) return _returnValue;
            return CurrentSignal switch
            {
                ThrowFlowCompletionSignal ts => ts.JsValue,
                YieldCompletionSignal ys => ys.JsValue,
                _ => JsValue.Undefined,
            };
        }
    }

    /// <summary>
    ///     Returns true if evaluation should stop (any signal is present).
    /// </summary>
    public bool ShouldStopEvaluation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isReturn || CurrentSignal is not null;
    }

    /// <summary>
    ///     Returns true if the current signal is Return.
    /// </summary>
    public bool IsReturn
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isReturn;
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
    ///     The generator's CloseActiveArrayPatternIterators will handle cleanup.
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

    public ScopeFrame CurrentScope => _scopeStack.Count > 0 ? _scopeStack.Peek() : ScopeFrame.Default;

    public PrivateNameScope? CurrentPrivateNameScope => _privateNameScopes.Count > 0 ? _privateNameScopes.Peek() : null;

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
        return new ScopeHandle(_scopeStack);
    }

    public void MarkThisUninitialized()
    {
        IsThisInitialized = false;
    }

    public void MarkThisInitialized()
    {
        IsThisInitialized = true;
    }

    public object? LastConstructedThis { get; set; }

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
        return new PrivateNameScopeHandle(_privateNameScopes);
    }

    public IDisposable EnterPrivateNameScopes(IReadOnlyList<PrivateNameScope> scopes)
    {
        foreach (var scope in scopes)
        {
            _privateNameScopes.Push(scope);
        }

        return new PrivateNameScopeHandle(_privateNameScopes, scopes.Count);
    }

    public IDisposable EnterClassFieldInitializer()
    {
        _classFieldInitializerDepth++;
        return new ClassFieldInitializerHandle(this);
    }

    public IDisposable EnterFunctionNameHint(Symbol name)
    {
        _functionNameHints.Push(name);
        return new FunctionNameHintHandle(_functionNameHints);
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
    ///     Checks if a label is in the current label stack.
    /// </summary>
    public bool IsLabelInScope(Symbol label)
    {
        return _labelStack.Contains(label);
    }

    /// <summary>
    ///     Sets the context to Return state with the given value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetReturn(JsValue value)
    {
        _isReturn = true;
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
        _isReturn = false;  // Clear return state so FlowValue uses CurrentSignal
        CurrentSignal = new ThrowFlowCompletionSignal(value);
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
    ///     Sets the context to Yield state with the given value and original iterator result object.
    ///     Used by yield* to preserve the original iterator result properties (like done being absent).
    /// </summary>
    public void SetYieldWithIteratorResult(JsValue value, int yieldIndex, IJsObjectLike? iteratorResultObject)
    {
        LastYieldIndex = yieldIndex;
        CurrentSignal = new YieldCompletionSignal(value, iteratorResultObject);
    }

    /// <summary>
    ///     Clears the Continue signal (used when a loop consumes it).
    /// </summary>
    public void ClearContinue()
    {
        if (CurrentSignal is ContinueCompletionSignal)
        {
            CurrentSignal = null;
        }
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
        _isReturn = false;
        _returnValue = default;
    }

    /// <summary>
    ///     Clears any control flow signal.
    /// </summary>
    public void Clear()
    {
        _isReturn = false;
        _returnValue = default;
        CurrentSignal = null;
    }

    /// <summary>
    ///     Saves the current completion state for later restoration (used by try-finally).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CompletionState SaveCompletionState()
    {
        return new CompletionState(_isReturn, _returnValue, CurrentSignal);
    }

    /// <summary>
    ///     Restores a previously saved completion state.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestoreCompletionState(in CompletionState state)
    {
        _isReturn = state.IsReturn;
        _returnValue = state.ReturnValue;
        CurrentSignal = state.Signal;
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
        _isReturn = false;
        _returnValue = default;
        CurrentSignal = null;
        LastYieldIndex = -1;
        CallDepth = 0;
        MaxCallDepth = 1000;
        IsThisInitialized = true;
        SourceReference = null;
        LastConstructedThis = null;
    }

    private sealed class PrivateNameScopeHandle(Stack<PrivateNameScope> scopes, int count = 1) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            var remaining = count;
            while (remaining-- > 0 && scopes.Count > 0)
            {
                scopes.Pop();
            }

            _disposed = true;
        }
    }

    private sealed class ClassFieldInitializerHandle : IDisposable
    {
        private readonly EvaluationContext _context;
        private bool _disposed;

        public ClassFieldInitializerHandle(EvaluationContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_context._classFieldInitializerDepth > 0)
            {
                _context._classFieldInitializerDepth--;
            }

            _disposed = true;
        }
    }

    private sealed class FunctionNameHintHandle(Stack<Symbol> hints) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (hints.Count > 0)
            {
                hints.Pop();
            }

            _disposed = true;
        }
    }

    private sealed class ScopeHandle(Stack<ScopeFrame> scopes) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (scopes.Count > 0)
            {
                scopes.Pop();
            }

            _disposed = true;
        }
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
}

public enum ScopeKind
{
    Program,
    Function,
    Block,
}

public enum ScopeMode
{
    Strict,
    Sloppy,
}

public readonly record struct ScopeFrame(ScopeKind Kind, ScopeMode Mode)
{
    public bool IsStrict => Mode == ScopeMode.Strict;
    public static ScopeFrame Default { get; } = new(ScopeKind.Program, ScopeMode.Strict);
}

public sealed class PrivateNameScope
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, PrivateNameScope> _scopes = new();
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public PrivateNameScope()
    {
        _scopes[_id] = this;
    }

    public object BrandToken { get; } = new();

    public bool TryGetKey(string lexeme, out string key)
    {
        return _map.TryGetValue(lexeme, out key!);
    }

    public string GetKey(string lexeme)
    {
        if (_map.TryGetValue(lexeme, out var key))
        {
            return key;
        }

        key = $"{lexeme}@{_id}";
        _map[lexeme] = key;
        return key;
    }

    public static bool TryResolveScope(string key, out PrivateNameScope? scope)
    {
        scope = null;
        var separator = key.LastIndexOf('@');
        if (separator < 0)
        {
            return false;
        }

        if (!int.TryParse(key.AsSpan(separator + 1), out var id))
        {
            return false;
        }

        return _scopes.TryGetValue(id, out scope);
    }
}

internal readonly record struct PendingClassFieldInitialization(
    object Constructor,
    JsEnvironment Environment);
