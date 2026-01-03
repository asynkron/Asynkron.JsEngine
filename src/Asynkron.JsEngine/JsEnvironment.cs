#region

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine;

public sealed class JsEnvironment : IRentable
{
    private const int MaxDepth = 1_000;
    internal static readonly object Uninitialized = new();

    private Dictionary<Symbol, List<Action<JsValue>>>? _bindingObservers;
    private HashSet<Symbol>? _bodyLexicalNames;
    private SourceReference? _creatingSource;
    private string? _description;
    internal bool _hasThisValue;
    private Dictionary<Symbol, ResolvedIdentifierBinding>? _identifierBindingCache;
    private bool _inheritStrictness;
    private bool _isStrictEffective;

    private bool _isDefaultDerivedConstructor;
    private HashSet<Symbol>? _simpleCatchParameters;

    /// <summary>
    /// Slot-based storage for variable access. Each slot contains name, value, and flags.
    /// Uses linear scan for name lookup - fast for typical JS scopes (1-10 bindings).
    /// </summary>
    internal JsSlot[]? _slots;

    /// <summary>
    /// Number of slots currently in use (may be less than array length due to pooling).
    /// </summary>
    internal int _slotCount;

    /// <summary>
    /// Tracks the expected slot layout identity for pooled environments.
    /// Used to detect when a pooled environment needs its slots rebuilt.
    /// </summary>
    internal int LayoutId { get; private set; } = -1;

    /// <summary>
    /// Fast storage for 'this' binding in slot-only environments (InvokeSimpleFast).
    /// Only valid when _hasThisValue is true.
    /// </summary>
    internal JsValue _thisValue;

    private bool _treatAsGlobalFunctionScope;

    private JsEnvironment? _varEnvironmentOverride;

    private IJsObjectLike? _withObject;

    public JsEnvironment(
        JsEnvironment? enclosing = null,
        bool isFunctionScope = false,
        bool isStrict = false,
        SourceReference? creatingSource = null,
        string? description = null,
        IJsObjectLike? withObject = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false,
        bool treatAsGlobalFunctionScope = false,
        bool inheritStrictness = true)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        _creatingSource = creatingSource;
        _description = description;
        IsStrictLocal = isStrict;
        _withObject = withObject;
        IsParameterEnvironment = isParameterEnvironment;
        IsBodyEnvironment = isBodyEnvironment;
        _treatAsGlobalFunctionScope = treatAsGlobalFunctionScope;
        _inheritStrictness = inheritStrictness;
        // Cache effective strictness to avoid O(n) chain traversal on each access
        _isStrictEffective = isStrict || (inheritStrictness && (enclosing?.IsStrict ?? false));
        RealmState = enclosing?.RealmState;
        ModulePath = enclosing?.ModulePath;
        IsAsyncModule = enclosing?.IsAsyncModule ?? false;

        Depth = (Enclosing?.Depth ?? 0) + 1;
        if (Depth > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Exceeded maximum environment depth of {MaxDepth}. Possible unbounded recursion detected.");
        }
    }

    private static void ValidateScopeId(int scopeId)
    {
        if (scopeId == 0 && EngineFeatureFlags.ThrowOnZeroScopeId)
        {
            throw new InvalidOperationException(
                "JsEnvironment initialized with ScopeId=0; scope analysis likely missing or incorrect.");
        }
    }

    public string Description => _description ?? string.Empty;

    /// <summary>
    /// Unique ID for this scope, used to match variables to their declaring environment.
    /// -1 means not set (use fallback to dictionary lookup).
    /// </summary>
    internal int ScopeId { get; set; } = -1;

    internal static string FormatScopeIdForLog(int scopeId)
    {
        return scopeId >= 0
            ? scopeId.ToString(CultureInfo.InvariantCulture)
            : "unknown";
    }

    internal RealmState? RealmState { get; private set; }
    internal bool IsAsyncModule { get; set; }
    internal string? ModulePath { get; set; }

    /// <summary>
    ///     Depth of the environment chain (0 for the root/global).
    /// </summary>
    public int Depth { get; private set; }

    private bool IsStrictLocal { get; set; }

    /// <summary>
    ///     Returns true if this environment or any enclosing environment is in strict mode.
    ///     Cached at creation time to avoid O(n) chain traversal.
    /// </summary>
    public bool IsStrict => _isStrictEffective;

    internal bool IsObjectEnvironment => _withObject is not null;

    internal bool IsParameterEnvironment { get; private set; }

    internal bool IsBodyEnvironment { get; private set; }

    /// <summary>
    ///     When true, indicates this environment belongs to an arrow function.
    ///     Arrow functions don't have their own 'arguments' binding, so the eval
    ///     restriction on declaring 'arguments' in direct eval inside functions
    ///     with non-simple parameters doesn't apply.
    /// </summary>
    internal bool IsArrowFunctionEnvironment { get; set; }

    internal bool IsFunctionScope { get; private set; }

    /// <summary>
    ///     When true, indicates this environment belongs to a default derived constructor
    ///     where argument spreading should bypass the iterator protocol per ES spec 15.7.14.
    ///     Walks up the enclosing chain to find the flag if not set locally.
    /// </summary>
    internal bool IsDefaultDerivedConstructor
    {
        get => _isDefaultDerivedConstructor || (Enclosing?.IsDefaultDerivedConstructor ?? false);
        set => _isDefaultDerivedConstructor = value;
    }

    internal JsEnvironment? Enclosing { get; private set; }

    internal bool IsGlobalFunctionScope => _treatAsGlobalFunctionScope || (IsFunctionScope && Enclosing is null);

    /// <summary>
    /// Checks if this environment or any of its enclosing environments has a with object.
    /// Used to determine if identifier lookups need to check with bindings.
    /// </summary>
    internal bool HasWithObjectInChain()
    {
        var current = this;
        while (current is not null)
        {
            if (current._withObject is not null)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal void SetRealmState(RealmState realmState)
    {
        RealmState = realmState;
    }

    /// <summary>
    ///     Resets the environment for reuse from a pool.
    ///     Sets new enclosing and clears all bindings.
    /// </summary>
    internal void Reset(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null,
        bool isParameterEnvironment = false,
        bool isBodyEnvironment = false)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        IsStrictLocal = isStrict;
        _creatingSource = creatingSource;
        _description = description;
        ClearSlots();
        _identifierBindingCache?.Clear();
        _bindingObservers?.Clear();
        _bodyLexicalNames?.Clear();
        _simpleCatchParameters?.Clear();
        _isDefaultDerivedConstructor = false;
        _varEnvironmentOverride = null;
        _withObject = null;
        IsParameterEnvironment = isParameterEnvironment;
        IsBodyEnvironment = isBodyEnvironment;
        IsArrowFunctionEnvironment = false;
        _treatAsGlobalFunctionScope = false;
        _inheritStrictness = true;
        // Cache effective strictness - inheritStrictness is always true in Reset
        _isStrictEffective = isStrict || (enclosing?.IsStrict ?? false);
        RealmState = enclosing?.RealmState;
        ModulePath = enclosing?.ModulePath;
        IsAsyncModule = enclosing?.IsAsyncModule ?? false;
        Depth = (enclosing?.Depth ?? 0) + 1;
        // Reset slot-based state - return slots array to pool
        JsSlotArrayPool.Return(_slots);
        _slots = null;
        _slotCount = 0;
        _thisValue = default;
        _hasThisValue = false;
        ScopeId = -1;
        LayoutId = -1;
    }

    /// <summary>
    ///     Called when environment is rented from pool.
    ///     Implements IRentable.Activate().
    /// </summary>
    void IRentable.Activate(ILogger? logger)
    {
        logger?.LogInformation("JsEnvironment.Activate description={Description}", _description);
    }

    /// <summary>
    ///     Resets the environment to a neutral state for pool return.
    ///     Implements IRentable.Reset().
    /// </summary>
    void IRentable.Reset(ILogger? logger)
    {
        logger?.LogInformation("JsEnvironment.Reset description={Description}", _description);
        Reset(null, false, false);
    }

    /// <summary>
    ///     Resets the environment for direct reuse in recursive calls.
    ///     Unlike Reset(), this KEEPS the slots array to avoid allocation.
    ///     Only use this when reusing an environment for the SAME function.
    /// </summary>
    internal void ResetForReuse(
        JsEnvironment? enclosing,
        bool isFunctionScope,
        bool isStrict,
        SourceReference? creatingSource = null,
        string? description = null)
    {
        Enclosing = enclosing;
        IsFunctionScope = isFunctionScope;
        IsStrictLocal = isStrict;
        _creatingSource = creatingSource;
        _description = description;
        ClearSlots();
        _identifierBindingCache?.Clear();
        _bindingObservers?.Clear();
        _bodyLexicalNames?.Clear();
        _simpleCatchParameters?.Clear();
        _isDefaultDerivedConstructor = false;
        _varEnvironmentOverride = null;
        _withObject = null;
        IsParameterEnvironment = false;
        IsBodyEnvironment = false;
        _treatAsGlobalFunctionScope = false;
        _inheritStrictness = true;
        // Cache effective strictness - inheritStrictness is always true in ResetForReuse
        _isStrictEffective = isStrict || (enclosing?.IsStrict ?? false);
        RealmState = enclosing?.RealmState;
        ModulePath = enclosing?.ModulePath;
        IsAsyncModule = enclosing?.IsAsyncModule ?? false;
        Depth = (enclosing?.Depth ?? 0) + 1;
        // Keep _slots for reuse - InitializeSlots will reuse if same size
        // Note: _slotCount is NOT reset here - InitializeSlots will set it
        _thisValue = default;
        _hasThisValue = false;
        ScopeId = -1;
        LayoutId = -1;

        if (Depth > 5000)
        {
            throw new NotSupportedException("Recursion depth exceeded maximum of 5000");
        }
    }

    /// <summary>
    /// Defines a binding with a JsValue directly, avoiding boxing for primitives.
    /// </summary>
    public void DefineJsValue(
        Symbol name,
        JsValue value,
        bool isConst = false,
        bool isGlobalConstant = false,
        bool isLexicalBinding = true,
        bool blocksFunctionScopeOverride = false,
        bool canDelete = false,
        bool isImmutableBinding = false)
    {
        // Check for existing slot first
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot))
        {
            // Upgrade flags on existing slot so pre-initialized layouts (InitializeSlots)
            // can still become const/immutable/global-constant bindings.
            var flags = slot.Flags;
            if (isConst) flags |= SlotFlags.Const;
            if (isGlobalConstant) flags |= SlotFlags.GlobalConstant;
            if (isLexicalBinding) flags |= SlotFlags.Lexical;
            if (blocksFunctionScopeOverride) flags |= SlotFlags.BlocksFunctionScopeOverride;
            if (canDelete) flags |= SlotFlags.CanDelete;
            if (isImmutableBinding) flags |= SlotFlags.ImmutableBinding;
            slot.Flags = flags;

            // Can't overwrite global constants
            if (slot.IsGlobalConstant)
            {
                return;
            }

            // Check for special binding (async export) stored in slot - use flag for fast detection
            if (slot.HasSpecialBinding)
            {
                ((ISpecialBinding)slot.Value.ObjectValue!).SetJsValue(value);
                if (_bindingObservers is not null)
                {
                    NotifyBindingObservers(name, value);
                }
                return;
            }

            // Can't overwrite const unless it's a lexical override
            if (slot.IsConst)
            {
                if (slot.IsUninitialized || slot.Value.IsUninitialized)
                {
                    // First-time initialization for a TDZ slot
                    slot.Value = value;
                    if (value.IsUninitialized)
                    {
                        slot.Flags |= SlotFlags.Uninitialized;
                    }
                    else
                    {
                        slot.Flags &= ~SlotFlags.Uninitialized;
                    }
                    return;
                }

                if (isLexicalBinding && blocksFunctionScopeOverride)
                {
                    var isUninit = value.IsUninitialized;
                    slot = new JsSlot(name, value, BuildSlotFlags(isConst, isGlobalConstant, isLexicalBinding,
                        blocksFunctionScopeOverride, canDelete, isImmutableBinding, isUninit));
                }
                return;
            }

            slot.Value = value;
            // Upgrade lexical flags if needed
            if (isLexicalBinding) slot.Flags |= SlotFlags.Lexical;
            if (blocksFunctionScopeOverride) slot.Flags |= SlotFlags.BlocksFunctionScopeOverride;
            if (value.IsUninitialized) slot.Flags |= SlotFlags.Uninitialized;
            // Clear uninitialized flag when setting an initialized value (TDZ completion)
            if (!value.IsUninitialized) slot.Flags &= ~SlotFlags.Uninitialized;

            if (_bindingObservers is not null)
            {
                NotifyBindingObservers(name, value);
            }
            return;
        }

        // Create new slot - detect if value is uninitialized for TDZ support
        var isUninitialized = value.IsUninitialized;
        DefineSlot(name, value, BuildSlotFlags(isConst, isGlobalConstant, isLexicalBinding,
            blocksFunctionScopeOverride, canDelete, isImmutableBinding, isUninitialized));

        if (_bindingObservers is not null)
        {
            NotifyBindingObservers(name, value);
        }
    }

    /// <summary>
    /// Defines or assigns a value in a single slot lookup.
    /// Use this when you don't know if the binding exists yet and want to avoid
    /// the overhead of HasBinding + Assign/Define pattern.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DefineOrAssignJsValue(Symbol name, JsValue value)
    {
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot))
        {
            // Const bindings always throw TypeError on reassignment per ES spec
            if (slot.IsConst && !slot.IsUninitialized)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{name.Name}'.",
                    realm: RealmState));
            }

            // Global constants (like undefined, NaN) are immutable
            if (slot.IsGlobalConstant)
            {
                return;
            }

            slot.SetValueAndClearTdz(value);
        }
        else
        {
            // Slot doesn't exist - create it as a mutable lexical binding
            DefineSlot(name, value, SlotFlags.Lexical);
        }
    }

    /// <summary>
    /// Fast path for defining function parameters. Assumes:
    /// - Environment is fresh (no existing bindings)
    /// - Not defining GlobalConstants
    /// - No binding observers
    /// Use only for function parameter binding in fresh environments.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DefineParameterFast(Symbol name, JsValue value)
    {
        DefineSlot(name, value, SlotFlags.None);
    }

    internal void DefineExportPromiseBinding(Symbol name, JsPromise promise, bool isLexical, bool isConst)
    {
        if (HasBindingLocal(name))
        {
            return;
        }

        // Store async export binding directly in slot - use HasSpecialBinding flag for fast detection
        var flags = SlotFlags.HasSpecialBinding;
        if (isConst) flags |= SlotFlags.Const;
        if (isLexical) flags |= SlotFlags.Lexical;
        DefineSlot(name, JsValue.FromObjectUnsafe(new AsyncExportBinding(promise, isConst)), flags);
    }

    /// <summary>
    /// Defines an import binding that indirectly references a binding in another module's environment.
    /// Import bindings are immutable - they always read from the source module.
    /// </summary>
    internal void DefineImportBinding(Symbol localName, JsEnvironment sourceEnvironment, Symbol bindingName)
    {
        // Store import binding directly in slot - use HasSpecialBinding flag for fast detection
        DefineSlot(localName, JsValue.FromObjectUnsafe(new ImportBindingWrapper(sourceEnvironment, bindingName)),
            SlotFlags.Const | SlotFlags.Lexical | SlotFlags.ImmutableBinding | SlotFlags.HasSpecialBinding);
    }

    public void DefineFunctionScoped(
        Symbol name,
        JsValue value,
        bool hasInitializer,
        bool isFunctionDeclaration = false,
        bool? globalFunctionConfigurable = null,
        EvaluationContext? context = null,
        bool blocksFunctionScopeOverride = false,
        bool? globalVarConfigurable = null,
        bool canDelete = false)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        var scope = GetFunctionScope();
        var isGlobalScope = scope.IsGlobalFunctionScope;
        JsObject? globalThis = null;
        PropertyDescriptor? existingDescriptor = null;
        var existingGlobalValue = JsValue.Undefined;
        var hasLooseGlobalValue = false;
        var allowDelete = canDelete || (context is { ExecutionKind: ExecutionKind.Eval } && !isGlobalScope);
        if (isGlobalScope)
        {
            globalThis = scope.GetRootGlobalObject();
            if (globalThis is not null)
            {
                existingDescriptor = globalThis.GetOwnPropertyDescriptor(name.Name);
                if (existingDescriptor is not null)
                {
                    if (globalThis.TryGetProperty(name.Name, out var jsValue))
                    {
                        existingGlobalValue = jsValue;
                    }
                }
                else if (globalThis.TryGetValue(name.Name, out var looseValue))
                {
                    // Use TryGetValue instead of TryGetProperty to avoid invoking
                    // inherited accessors like Object.prototype.__proto__
                    existingGlobalValue = JsValue.FromObjectUnsafe(looseValue);
                    hasLooseGlobalValue = true;
                }
            }
        }

        var isRestrictedGlobal = isGlobalScope &&
                                 existingDescriptor is { Configurable: false } descriptor &&
                                 (!descriptor.IsDataDescriptor || !descriptor.Writable);

        if (isGlobalScope && isFunctionDeclaration)
        {
            if (isRestrictedGlobal)
            {
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }

            var canDeclareFunction = existingDescriptor switch
            {
                null => globalThis?.IsExtensible != false,
                { Configurable: true } => true,
                _ => existingDescriptor is { IsAccessorDescriptor: false, Writable: true, Enumerable: true }
            };

            if (!canDeclareFunction)
            {
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }
        }

        if (isGlobalScope &&
            !isFunctionDeclaration &&
            existingDescriptor is null &&
            globalThis?.IsExtensible == false)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot declare global variable '{name.Name}' on a non-extensible global object.",
                context,
                context?.RealmState);
        }

        // Per ES spec GlobalDeclarationInstantiation step 6:
        // If envRec.HasLexicalDeclaration(name) is true, throw a SyntaxError.
        // This check must happen before checking _values because lexical bindings
        // may be tracked separately in bodyLexicalNames across script evaluations.
        // We need to check both the current scope AND enclosing scopes because
        // strict scripts create wrapper environments around the global scope.
        if (isGlobalScope && HasGlobalLexicalName(scope, name))
        {
            throw StandardLibrary.ThrowSyntaxError(
                $"Identifier '{name.Name}' has already been declared",
                context,
                context?.RealmState);
        }

        var allowConfigurableGlobalBinding =
            context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
        var varBindingConfigurable = globalVarConfigurable ?? allowConfigurableGlobalBinding;

        ref var existing = ref scope.TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref existing))
        {
            // Also, check existing lexical bindings in the local scope
            if (isGlobalScope && existing.IsLexical)
            {
                throw StandardLibrary.ThrowSyntaxError(
                    $"Identifier '{name.Name}' has already been declared",
                    context,
                    context?.RealmState);
            }

            if (existing.IsConst || existing.IsGlobalConstant)
            {
                return;
            }

            if (blocksFunctionScopeOverride)
            {
                existing.Flags |= SlotFlags.BlocksFunctionScopeOverride;
            }

            if (!hasInitializer)
            {
                return;
            }

            existing.Value = value;
            if (!isGlobalScope || globalThis is null)
            {
                return;
            }

            if (!isFunctionDeclaration)
            {
                globalThis.SetProperty(name.Name, value);
                return;
            }

            var configurable = globalFunctionConfigurable ?? allowConfigurableGlobalBinding;
            if (existingDescriptor is null)
            {
                if (!globalThis.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            JsValue = value, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot declare global function '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }
            else if (existingDescriptor.Configurable)
            {
                if (!globalThis.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            JsValue = value, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot redeclare global function '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }
            else
            {
                // Existing non-configurable property: update value only (CreateGlobalFunctionBinding step 6).
                if (!globalThis.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor { JsValue = value }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot update global function binding for '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }

            return;
        }

        var initialValue = value;
        var shouldWriteGlobal = true;

        if (isGlobalScope && !hasInitializer && (existingDescriptor is not null || hasLooseGlobalValue))
        {
            initialValue = existingGlobalValue;
            shouldWriteGlobal = false;
        }

        var slotFlags = SlotFlags.None;
        if (blocksFunctionScopeOverride) slotFlags |= SlotFlags.BlocksFunctionScopeOverride;
        if (allowDelete) slotFlags |= SlotFlags.CanDelete;
        scope.DefineSlot(name, initialValue, slotFlags);
        if (!isGlobalScope || globalThis is null || !shouldWriteGlobal)
        {
            return;
        }

        if (isFunctionDeclaration)
        {
            var configurable = globalFunctionConfigurable ?? allowConfigurableGlobalBinding;
            if (existingDescriptor is null)
            {
                if (!globalThis!.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            JsValue = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot declare global function '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }
            else if (existingDescriptor.Configurable)
            {
                if (!globalThis!.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            JsValue = initialValue, Writable = true, Enumerable = true, Configurable = configurable
                        }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot redeclare global function '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }
            else
            {
                // Existing non-configurable property: update value only (CreateGlobalFunctionBinding step 6).
                if (!globalThis!.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor { JsValue = initialValue }))
                {
                    throw StandardLibrary.ThrowTypeError(
                        $"Cannot update global function binding for '{name.Name}'.",
                        context,
                        context?.RealmState);
                }
            }

            return;
        }

        if (existingDescriptor is null)
        {
            if (!globalThis!.TryDefineProperty(
                    name.Name,
                    new PropertyDescriptor
                    {
                        JsValue = initialValue,
                        Writable = true,
                        Enumerable = true,
                        Configurable = varBindingConfigurable
                    }))
            {
                throw StandardLibrary.ThrowTypeError(
                    $"Cannot declare global variable '{name.Name}'.",
                    context,
                    context?.RealmState);
            }
        }
        else
        {
            globalThis.SetProperty(name.Name, initialValue);
        }
    }


    /// <summary>
    /// Gets a binding value as JsValue, avoiding boxing for primitives.
    /// Throws ReferenceError if binding doesn't exist or is uninitialized.
    /// </summary>
    public JsValue GetJsValue(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        while (current is not null && hops++ < maxLookupDepth)
        {
            // Fast path for 'this' in slot-only environments (InvokeSimpleFast)
            if (Equals(name, Symbol.This) && current._hasThisValue)
            {
                return current._thisValue;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                if (slot.IsUninitialized)
                {
                    var realm = current.RealmState ?? current.Enclosing?.RealmState;
                    throw StandardLibrary.ThrowReferenceError(
                        $"Cannot access '{name.Name}' before initialization",
                        realm: realm);
                }

                // Check for special binding (import/export) - use flag for fast detection
                if (slot.HasSpecialBinding)
                {
                    return ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
                }

                var slotValue = slot.Value;
                if (!current.IsGlobalFunctionScope ||
                    slot.IsLexical)
                {
                    return slotValue;
                }

                var globalObject = current.GetRootGlobalObject();
                if (globalObject is not null &&
                    globalObject.TryGetProperty(name.Name, out var globalValue))
                {
                    return globalValue;
                }

                return slotValue;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                return current._varEnvironmentOverride.GetJsValue(name);
            }

            if (current._withObject is not null && TryGetFromWithJsValue(current._withObject, name, out var withValue))
            {
                return withValue;
            }

            current = current.Enclosing;
        }

        if (!IsGlobalFunctionScope)
        {
            throw StandardLibrary.ThrowReferenceError(
                $"ReferenceError: {name.Name} is not defined",
                realm: RealmState ?? Enclosing?.RealmState);
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            return propertyValue;
        }

        throw StandardLibrary.ThrowReferenceError(
            $"ReferenceError: {name.Name} is not defined",
            realm: RealmState ?? Enclosing?.RealmState);
    }

    internal bool IsConstBinding(Symbol name)
    {
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot))
        {
            return slot.IsConst || slot.IsGlobalConstant;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            return false;
        }

        return Enclosing?.IsConstBinding(name) ?? false;
    }

    internal bool HasBinding(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            // Fast path for 'this' in slot-only environments (InvokeSimpleFast)
            if (Equals(name, Symbol.This) && current._hasThisValue)
            {
                return true;
            }

            if (current.FindSlotIndex(name) >= 0)
            {
                return true;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                return true;
            }

            var next = current.Enclosing;
            if (ReferenceEquals(next, current))
            {
                break;
            }

            current = next;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasOwnBinding(Symbol name)
    {
        return FindSlotIndex(name) >= 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasOwnLexicalBinding(Symbol name)
    {
        var index = FindSlotIndex(name);
        return index >= 0 && _slots![index].IsLexical;
    }

    /// <summary>
    /// Gets all binding symbols defined in this environment (not including parent environments).
    /// </summary>
    internal IEnumerable<Symbol> GetBindingSymbols()
    {
        if (_slots is null || _slotCount == 0) return [];
        return _slots.Take(_slotCount).Select(s => s.Name);
    }

    internal bool TryAssignBlockedBinding(Symbol name, JsValue value)
    {
        var current = this;
        var passedFunctionBoundary = false;
        while (current is not null)
        {
            if (current.IsFunctionScope)
            {
                if (passedFunctionBoundary)
                {
                    break;
                }

                passedFunctionBoundary = true;
            }

            // ES2019: Skip catch parameters - var declarations should hoist to function scope
            // Per ECMAScript, catch parameters should NOT block var declarations from reaching function scope
            if (current.IsSimpleCatchParameter(name))
            {
                current = current.Enclosing;
                continue;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot) && slot.BlocksFunctionScopeOverride)
            {
                slot.SetValueAndClearTdz(value);
                current.NotifyBindingObservers(name, value);
                if (!current.IsGlobalFunctionScope)
                {
                    return true;
                }

                var globalObject = current.GetRootGlobalObject();
                globalObject?.SetProperty(name.Name, value);

                return true;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                break;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal bool TryAssignBlockedBindingJsValue(Symbol name, JsValue value)
    {
        var current = this;
        var passedFunctionBoundary = false;
        while (current is not null)
        {
            if (current.IsFunctionScope)
            {
                if (passedFunctionBoundary)
                {
                    break;
                }

                passedFunctionBoundary = true;
            }

            // ES2019: Skip catch parameters - var declarations should hoist to function scope
            // Per ECMAScript, catch parameters should NOT block var declarations from reaching function scope
            if (current.IsSimpleCatchParameter(name))
            {
                current = current.Enclosing;
                continue;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot) && slot.BlocksFunctionScopeOverride)
            {
                slot.SetValueAndClearTdz(value);
                current.NotifyBindingObservers(name, value);
                if (!current.IsGlobalFunctionScope)
                {
                    return true;
                }

                var globalObject = current.GetRootGlobalObject();
                globalObject?.SetProperty(name.Name, value);

                return true;
            }

            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                break;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal bool TryResolveWithBinding(
        Symbol name,
        EvaluationContext context,
        out ObjectEnvironmentBinding binding)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        var isStrictReference = IsStrict || context.CurrentScope.IsStrict || context.IsStrictSource;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._withObject is not null &&
                TryResolveObjectBinding(
                    current._withObject,
                    name,
                    out var propertyName,
                    out var allowMissingAssignment))
            {
                binding = new ObjectEnvironmentBinding(
                    current._withObject,
                    propertyName,
                    isStrictReference,
                    allowMissingAssignment);
                return true;
            }

            if (current.FindSlotIndex(name) >= 0)
            {
                break;
            }

            current = current.Enclosing;
        }

        binding = default;
        return false;
    }

    /// <summary>
    /// Resolves a with binding for WRITE operations without checking unscopables.
    /// Per ES spec, unscopables only affect reads (GetBindingValue), not writes (SetMutableBinding).
    /// </summary>
    private bool TryResolveWithBindingForWrite(
        Symbol name,
        EvaluationContext context,
        out ObjectEnvironmentBinding binding)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        var isStrictReference = IsStrict || context.CurrentScope.IsStrict || context.IsStrictSource;

        while (current is not null && hops++ < maxLookupDepth)
        {
            // For writes, only check if property exists - DON'T check unscopables
            if (current._withObject is not null && HasWithPropertyForAssignment(current._withObject, name))
            {
                binding = new ObjectEnvironmentBinding(
                    current._withObject,
                    name.Name,
                    isStrictReference,
                    false);
                return true;
            }

            if (current.FindSlotIndex(name) >= 0)
            {
                break;
            }

            current = current.Enclosing;
        }

        binding = default;
        return false;
    }

    internal AssignmentReference ResolveIdentifierAssignmentReference(Symbol name, EvaluationContext context)
    {
        var strictContext = context.CurrentScope.IsStrict;

        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return AssignmentReference.ForDeclarativeBinding(cached, name, context, strictContext);
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return AssignmentReference.ForDeclarativeBinding(cachedBinding, name, context, strictContext);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return AssignmentReference.ForGlobalBinding(globalBinding, context);
        }

        return AssignmentReference.ForUnresolvable(name, context, strictContext, this);
    }

    /// <summary>
    /// Direct identifier resolution that returns JsValue, avoiding boxing for primitives.
    /// This is the slow path that checks for 'with' statement bindings.
    /// </summary>
    internal JsValue GetIdentifierJsValueWithScope(Symbol name, EvaluationContext context)
    {
        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return cached.ReadJsValue(context);
        }

        if (TryResolveWithBinding(name, context, out var withBinding))
        {
            return GetWithBindingValueJsValue(withBinding);
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return cachedBinding.ReadJsValue(context);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return GetWithBindingValueJsValue(globalBinding);
        }

        // Identifier not found - throw ReferenceError via ThrowSignal so JavaScript can catch it
        throw new ThrowSignal(StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState));
    }

    /// <summary>
    /// Fast path identifier resolution - no 'with' statement check.
    /// Only use when AllowIdentifierCache is true (no with/eval in scope).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue GetIdentifierJsValueDirect(Symbol name, EvaluationContext context)
    {
        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            return cached.ReadJsValue(context);
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            return cachedBinding.ReadJsValue(context);
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            return GetWithBindingValueJsValue(globalBinding);
        }

        // Identifier not found - throw ReferenceError via ThrowSignal so JavaScript can catch it
        throw new ThrowSignal(StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState));
    }

    /// <summary>
    /// Tries to resolve an identifier and return its value as JsValue.
    /// Returns false if the identifier is not found (instead of throwing).
    /// This is the fast path for identifier evaluation in hot loops.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetIdentifierJsValue(Symbol name, EvaluationContext context, out JsValue value)
    {
        // Ultra-fast path: check the current environment first for local variables
        // Most identifier lookups in functions are for local variables/parameters
        ref var localSlot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref localSlot))
        {
            if (localSlot.IsUninitialized)
            {
                // TDZ violation - throw ReferenceError
                throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                    $"Cannot access '{name.Name}' before initialization",
                    context,
                    context.RealmState));
            }

            // Check for special binding (import/export) - use flag for fast detection
            if (localSlot.HasSpecialBinding)
            {
                value = ((ISpecialBinding)localSlot.Value.ObjectValue!).GetJsValue();
                return true;
            }

            var slotValue = localSlot.Value;
            // For non-lexical bindings in global scope, read from global object
            // to ensure changes via this.x are visible when accessing x directly.
            // This mirrors the logic in ReadResolvedBindingJsValue.
            if (IsGlobalFunctionScope && !localSlot.IsLexical)
            {
                var globalObject = GetRootGlobalObject();
                if (globalObject is not null && globalObject.TryGetProperty(name.Name, out var globalValue))
                {
                    value = globalValue; // globalValue is already JsValue - don't box via FromObject!
                    return true;
                }
            }

            value = slotValue;
            return true;
        }

        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            value = cached.ReadJsValue(context);
            return true;
        }

        // Fast path: skip TryResolveWithBinding when AllowIdentifierCache is true (no with/eval in scope)
        if (!context.AllowIdentifierCache && TryResolveWithBinding(name, context, out var withBinding))
        {
            value = GetWithBindingValueJsValue(withBinding);
            return true;
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            value = cachedBinding.ReadJsValue(context);
            return true;
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            value = GetWithBindingValueJsValue(globalBinding);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Slot-aware identifier read. Attempts to use the provided scope/slot hint, then falls back to regular resolution.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadIdentifierWithSlot(
        Symbol name,
        int scopeId,
        int slotIndex,
        EvaluationContext context,
        out JsValue value)
    {
        value = default;
        var realmState = context.RealmState;
        var shouldLogSlots = realmState.Options.DebugMode;
        var logger = shouldLogSlots ? realmState.Logger : null;

        if (TryValidateSlotTarget(name, scopeId, slotIndex, shouldLogSlots, logger, out var targetEnv,
                out var slots))
        {
            ref var slot = ref slots![slotIndex];
            if (slot.IsUninitialized)
            {
                var errorObject = StandardLibrary.CreateReferenceError(
                    $"Cannot access '{name.Name}' before initialization",
                    context,
                    context.RealmState);
                value = errorObject;
                context.SetThrow(value);
                return true;
            }

            value = slot.Value;
            if (shouldLogSlots)
            {
                logger?.LogTrace(
                    "Identifier slot read hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                    targetEnv!.GetHashCode(),
                    name.Name,
                    FormatScopeIdForLog(targetEnv.ScopeId),
                    slotIndex,
                    value.Kind);
            }

            return true;
        }

        if (TryGetIdentifierJsValue(name, context, out var resolved))
        {
            value = resolved;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Overload for convenience when call sites already have the identifier node.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadIdentifierWithSlot(IdentifierExpression identifier, EvaluationContext context,
        out JsValue value)
    {
        return TryReadIdentifierWithSlot(identifier.Name, identifier.ScopeId, identifier.SlotIndex, context, out value);
    }

    /// <summary>
    /// Slot-aware identifier write. Uses slot hint when possible; otherwise falls back to normal write resolution.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteIdentifierWithSlot(
        Symbol name,
        int scopeId,
        int slotIndex,
        JsValue value,
        EvaluationContext context)
    {
        var realmState = context.RealmState;
        var shouldLogSlots = realmState.Options.DebugMode;
        var logger = shouldLogSlots ? realmState.Logger : null;

        if (TryValidateSlotTarget(name, scopeId, slotIndex, shouldLogSlots, logger, out var targetEnv, out var slots))
        {
            ref var currentSlot = ref slots![slotIndex];

            // TDZ check: if the slot is uninitialized, this is a TDZ violation.
            if (currentSlot.IsUninitialized)
            {
                throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                    $"Cannot access '{name.Name}' before initialization",
                    context,
                    context.RealmState));
            }

            // Check for const assignment
            if (currentSlot.IsConst)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Cannot reassign constant '{name.Name}'.",
                    realm: context.RealmState));
            }

            // Check for special binding (import/export) - use flag for fast detection
            if (currentSlot.HasSpecialBinding)
            {
                ((ISpecialBinding)currentSlot.Value.ObjectValue!).SetJsValue(value);
            }
            else
            {
                currentSlot.Value = value;
            }

            // For non-lexical bindings (var) in global scope, also update the global object
            // This mirrors the behavior of AssignJsValue which syncs with the global object
            if (!currentSlot.IsLexical && targetEnv!.IsGlobalFunctionScope)
            {
                var globalObject = targetEnv.GetRootGlobalObject();
                globalObject?.SetProperty(name.Name, value);
            }

            if (shouldLogSlots)
            {
                logger?.LogInformation(
                    "Identifier slot write hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                    targetEnv!.GetHashCode(),
                    name.Name,
                    FormatScopeIdForLog(scopeId),
                    slotIndex,
                    value.Kind);
            }

            targetEnv?.NotifyBindingObservers(name, value);
            return true;
        }

        SetIdentifierJsValue(name, value, context);
        return true;
    }

    /// <summary>
    /// Overload for convenience when call sites already have the identifier node.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryWriteIdentifierWithSlot(IdentifierExpression identifier, JsValue value, EvaluationContext context)
    {
        return TryWriteIdentifierWithSlot(identifier.Name, identifier.ScopeId, identifier.SlotIndex, value, context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadSlotValue(Symbol name, int slotIndex, EvaluationContext context, out JsValue value)
    {
        var shouldLogSlots = context.RealmState.Options.DebugMode;
        var logger = shouldLogSlots ? context.RealmState.Logger : null;

        var slots = _slots;
        if (slots is null || (uint)slotIndex >= (uint)slots.Length)
        {
            value = default;
            return false;
        }

        ref var slot = ref slots[slotIndex];
        if (slot.IsUninitialized)
        {
            var errorValue = StandardLibrary.CreateReferenceError(
                $"Cannot access '{name.Name}' before initialization",
                context,
                context.RealmState);
            value = errorValue;
            context.SetThrow(value);
            return true;
        }

        value = slot.Value;

        if (shouldLogSlots)
        {
            logger?.LogInformation(
                "Identifier slot read hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                GetHashCode(),
                name.Name,
                FormatScopeIdForLog(ScopeId),
                slotIndex,
                value.Kind);
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryWriteSlotValue(Symbol name, int slotIndex, JsValue value, EvaluationContext context)
    {
        var slots = _slots;
        if (slots is null || (uint)slotIndex >= (uint)slots.Length)
        {
            return false;
        }

        ref var slot = ref slots[slotIndex];
        if (slot.IsUninitialized)
        {
            throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                $"Cannot access '{name.Name}' before initialization",
                context,
                context.RealmState));
        }

        // Const bindings always throw TypeError on reassignment per ES spec,
        // regardless of strict mode
        if (slot.IsConst && !slot.IsUninitialized)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                $"Assignment to constant variable '{name.Name}'.",
                realm: context.RealmState));
        }

        if (slot.IsImmutableBinding)
        {
            if (context.CurrentScope.IsStrict || IsStrict)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{name.Name}'.",
                    realm: context.RealmState));
            }

            return true;
        }

        // Check for special binding (import/export) - use flag for fast detection
        if (slot.HasSpecialBinding)
        {
            ((ISpecialBinding)slot.Value.ObjectValue!).SetJsValue(value);
        }
        else
        {
            slot.SetValueAndClearTdz(value);
        }

        // For non-lexical bindings (var) in global scope, also update the global object
        // This mirrors the behavior of AssignJsValue which syncs with the global object
        if (!slot.IsLexical && IsGlobalFunctionScope)
        {
            var globalObject = GetRootGlobalObject();
            globalObject?.SetProperty(name.Name, value);
        }

        return true;
    }

    /// <summary>
    /// Direct identifier assignment that avoids creating AssignmentReference structs.
    /// This is the fast path for simple identifier assignments in loops.
    /// </summary>
    internal void SetIdentifierJsValue(Symbol name, JsValue value, EvaluationContext context)
    {
        var isStrictContext = context.CurrentScope.IsStrict;

        if (TryGetCachedDeclarativeBinding(name, context, out var cached))
        {
            cached.WriteJsValue(value, isStrictContext);
            return;
        }

        // For writes, use TryResolveWithBindingForWrite which skips unscopables check.
        // Per ES spec, unscopables only affects reads, not writes.
        if (!context.AllowIdentifierCache && TryResolveWithBindingForWrite(name, context, out var withBinding))
        {
            if (isStrictContext && IsStrictRestrictedName(name))
            {
                throw new ThrowSignal(StandardLibrary.CreateSyntaxError(
                    "Assignment to eval or arguments is not allowed in strict mode.", context,
                    context.RealmState));
            }

            if (!TrySetWithBindingValueJsValue(withBinding, value, context.RealmState))
            {
                AssignJsValue(name, value);
            }

            return;
        }

        if (TryLocateBinding(name, out var bindingEnvironment, out _))
        {
            var cachedBinding = new ResolvedIdentifierBinding(bindingEnvironment, name);
            CacheDeclarativeBinding(name, cachedBinding, context);
            cachedBinding.WriteJsValue(value, isStrictContext);
            return;
        }

        if (TryResolveGlobalObjectBinding(name, context, out var globalBinding))
        {
            TrySetWithBindingValueJsValue(globalBinding, value, context.RealmState);
            return;
        }

        AssignUnresolvableJsValue(name, value, isStrictContext, context, this);
    }

    private static bool IsStrictRestrictedName(Symbol name)
    {
        // Use reference equality since Symbols are interned
        return ReferenceEquals(name, Symbol.Eval) || ReferenceEquals(name, Symbol.Arguments);
    }

    private bool TryGetCachedDeclarativeBinding(
        Symbol name,
        EvaluationContext context,
        out ResolvedIdentifierBinding binding)
    {
        if (context.AllowIdentifierCache && _identifierBindingCache is not null)
        {
            return _identifierBindingCache.TryGetValue(name, out binding);
        }

        binding = default;
        return false;
    }

    private void CacheDeclarativeBinding(
        Symbol name,
        ResolvedIdentifierBinding binding,
        EvaluationContext context)
    {
        if (!context.AllowIdentifierCache)
        {
            return;
        }

        _identifierBindingCache ??=
            new Dictionary<Symbol, ResolvedIdentifierBinding>(ReferenceEqualityComparer<Symbol>.Instance);
        _identifierBindingCache[name] = binding;
    }

    internal static object ReadUnresolvable(Symbol name)
    {
        throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined");
    }

    internal static JsValue ReadUnresolvableJsValue(Symbol name)
    {
        throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined");
    }

    internal static void AssignUnresolvableJsValue(Symbol name, JsValue value, bool isStrictContext,
        EvaluationContext context, JsEnvironment? environment = null)
    {
        var realm = environment?.RealmState ?? environment?.Enclosing?.RealmState ?? context.RealmState;
        if (isStrictContext)
        {
            context.RealmState?.Logger?.LogInformation(
                "AssignUnresolvable strict throw name={Name} scopeStrict={ScopeStrict} functionScopeStrict={FnStrict} env={Env}",
                name.Name,
                context.CurrentScope.IsStrict,
                environment?.GetFunctionScope().IsStrict ?? false,
                environment?.GetHashCode() ?? 0);
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (environment is null)
        {
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        var globalScope = environment;
        while (globalScope.Enclosing is not null)
        {
            globalScope = globalScope.Enclosing;
        }

        var globalObject = environment.GetRootGlobalObject();
        if (globalObject is null)
        {
            globalScope.DefineJsValue(name, value, isLexicalBinding: false, canDelete: true);
            return;
        }

        // Sloppy assignment to an unresolvable reference creates a new
        // configurable property on the global object rather than a declarative
        // binding so that `delete` can remove it (ES2024 9.1.1.3.4 SetMutableBinding).
        globalObject.SetProperty(name.Name, value);
        context.RealmState?.Logger?.LogInformation(
            "Sloppy assignment created unresolvable binding name={Name} valueKind={ValueKind}",
            name.Name,
            value.Kind);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryLocateBinding(
        Symbol name,
        out JsEnvironment bindingEnvironment,
        out int slotIndex)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            var idx = current.FindSlotIndex(name);
            if (idx >= 0)
            {
                bindingEnvironment = current;
                slotIndex = idx;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryLocateBinding(name, out bindingEnvironment, out slotIndex))
            {
                return true;
            }

            current = current.Enclosing;
        }

        bindingEnvironment = null!;
        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// Direct binding value read for fast accumulator patterns.
    /// Assumes the binding exists in this environment (caller verified via TryLocateBindingInternal).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsValue GetBindingValueDirect(Symbol name)
    {
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot))
        {
            if (slot.HasSpecialBinding)
            {
                return ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
            }
            return slot.Value;
        }
        return JsValue.Undefined;
    }

    /// <summary>
    /// Direct binding value write for fast accumulator patterns.
    /// Assumes the binding exists in this environment and is mutable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetBindingValueDirect(Symbol name, JsValue value)
    {
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot))
        {
            if (slot.HasSpecialBinding)
            {
                ((ISpecialBinding)slot.Value.ObjectValue!).SetJsValue(value);
            }
            else
            {
                slot.SetValueAndClearTdz(value);
            }
        }
    }

    /// <summary>
    /// Internal method to locate a binding's environment for fast accumulator patterns.
    /// Returns just the environment, not the binding itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryLocateBindingInternal(Symbol name, out JsEnvironment? bindingEnvironment)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current.HasBindingLocal(name))
            {
                bindingEnvironment = current;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryLocateBindingInternal(name, out bindingEnvironment))
            {
                return true;
            }

            current = current.Enclosing;
        }

        bindingEnvironment = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryResolveGlobalObjectBinding(
        Symbol name,
        EvaluationContext context,
        out ObjectEnvironmentBinding binding)
    {
        binding = default;
        var globalObject = GetRootGlobalObject();
        if (globalObject is null)
        {
            // Debug: Log when global object is null
            RealmState?.Logger?.LogWarning(
                "TryResolveGlobalObjectBinding: globalObject is null for name={Name}",
                name.Name);
            return false;
        }

        if (!HasProperty(globalObject, name.Name))
        {
            // Debug: Log when property is not found
            RealmState?.Logger?.LogWarning(
                "TryResolveGlobalObjectBinding: property not found name={Name}",
                name.Name);
            return false;
        }

        var isStrictReference = IsStrict || context.CurrentScope.IsStrict || context.IsStrictSource;
        binding = new ObjectEnvironmentBinding(globalObject, name.Name, isStrictReference, false);
        return true;
    }

    internal bool HasLexicalBinding(Symbol name)
    {
        ref var slot = ref TryGetSlotRef(name);
        if (!Unsafe.IsNullRef(ref slot) && slot.IsLexical)
        {
            return true;
        }

        return Enclosing?.HasLexicalBinding(name) ?? false;
    }

    internal bool HasBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null)
        {
            if (current._withObject is null && current.HasBindingLocal(name))
            {
                return true;
            }

            if (current is { IsFunctionScope: true, IsParameterEnvironment: false })
            {
                break;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal bool HasRestrictedGlobalProperty(Symbol name)
    {
        var scope = GetFunctionScope();
        if (!scope.IsGlobalFunctionScope)
        {
            return false;
        }

        var descriptor = scope.GetGlobalOwnPropertyDescriptor(name, out var globalObject);
        if (globalObject is null)
        {
            return false;
        }

        return descriptor?.Configurable == false;
    }

    internal PropertyDescriptor? GetGlobalOwnPropertyDescriptor(Symbol name, out JsObject? globalObject)
    {
        globalObject = GetRootGlobalObject();
        return globalObject?.GetOwnPropertyDescriptor(name.Name);
    }

    internal bool HasLexicalBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current?.IsFunctionScope == false)
        {
            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot) && slot.IsLexical)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal void SetBodyLexicalNames(HashSet<Symbol> names)
    {
        _bodyLexicalNames = names;
    }

    /// <summary>
    /// Merges the given lexical names into the existing set of body lexical names.
    /// This is used during global script evaluation to preserve lexical bindings
    /// from previous evalScript calls.
    /// </summary>
    internal void MergeBodyLexicalNames(HashSet<Symbol> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        if (_bodyLexicalNames is null)
        {
            _bodyLexicalNames = new HashSet<Symbol>(names, ReferenceEqualityComparer<Symbol>.Instance);
        }
        else
        {
            _bodyLexicalNames.UnionWith(names);
        }
    }

    internal bool HasBodyLexicalName(Symbol name)
    {
        return _bodyLexicalNames?.Contains(name) == true;
    }

    /// <summary>
    /// Checks if the given name has a var declaration in this environment or the global object.
    /// Used by GlobalDeclarationInstantiation step 5.a to detect lexical declarations that
    /// conflict with existing var declarations.
    /// </summary>
    public bool HasVarDeclaration(Symbol name)
    {
        // Check if there's a non-lexical binding in slots
        ref var slot = ref TryGetSlotRef(name);
        if (Unsafe.IsNullRef(ref slot) || slot.IsLexical)
        {
            return false;
        }

        // Non-strict direct eval creates deletable global var bindings (configurable properties)
        // which should not block future lexical declarations in GlobalDeclarationInstantiation.
        return !slot.CanDelete || !IsGlobalFunctionScope;
    }

    /// <summary>
    /// Checks if the given name is a lexical declaration in the global environment.
    /// This checks the current scope's body lexical names and traverses enclosing scopes.
    /// Used by GlobalDeclarationInstantiation to detect var/function declarations that
    /// conflict with existing let/const/class bindings.
    /// </summary>
    public bool HasGlobalLexicalDeclaration(Symbol name)
    {
        // Check the current scope and all enclosing scopes for the lexical name
        var current = this;
        while (current is not null)
        {
            if (current.HasBodyLexicalName(name))
            {
                return true;
            }

            // Also check if there's a lexical binding in slots
            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot) && slot.IsLexical)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    /// <summary>
    /// Checks if the given name is a lexical binding in the global environment chain.
    /// This traverses the entire chain from the given scope up to the root global scope
    /// to check for lexical bindings that might have been created in strict script wrappers.
    /// </summary>
    private static bool HasGlobalLexicalName(JsEnvironment scope, Symbol name)
    {
        var current = scope;
        while (current is not null)
        {
            if (current.HasBodyLexicalName(name))
            {
                return true;
            }

            // Also check if there's a lexical binding in slots
            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot) && slot.IsLexical)
            {
                return true;
            }

            // Continue to enclosing scopes (important for strict script wrappers)
            current = current.Enclosing;
        }

        return false;
    }

    internal void SetSimpleCatchParameters(HashSet<Symbol> names)
    {
        _simpleCatchParameters = names;
    }

    internal bool IsSimpleCatchParameter(Symbol name)
    {
        return _simpleCatchParameters?.Contains(name) == true;
    }

    /// <summary>
    /// Tries to get a binding value from this environment only (no chain traversal).
    /// Returns false for uninitialized bindings instead of throwing.
    /// Used for scanning environments for active iterators.
    /// </summary>
    internal bool TryGetJsValueLocalSafe(Symbol name, out JsValue value)
    {
        try
        {
            // If slots have been cleared (e.g., pooled environment reset), treat as missing binding.
            if (_slots is null || _slotCount == 0)
            {
                value = default;
                return false;
            }

            ref var slot = ref TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                // Return false for uninitialized bindings instead of throwing
                if (slot.IsUninitialized)
                {
                    value = default;
                    return false;
                }

                // Import bindings may throw if the source binding isn't defined yet
                // (e.g., self-importing module with *default* export not yet evaluated)
                try
                {
                    if (slot.HasSpecialBinding)
                    {
                        if (slot.Value.ObjectValue is null)
                        {
                            value = default;
                            return false;
                        }

                        value = ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
                    }
                    else
                    {
                        value = slot.Value;
                    }

                    return true;
                }
                catch (InvalidOperationException)
                {
                    // Source binding not yet available
                    value = default;
                    return false;
                }
            }

            value = default;
            return false;
        }
        catch (NullReferenceException)
        {
            // If the environment has been torn down (pooled and reset), treat as no binding.
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Tries to get a binding value as JsValue, avoiding boxing for primitives.
    /// </summary>
    public bool TryGetJsValue(Symbol name, out JsValue value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            // Fast path for 'this' in slot-only environments (InvokeSimpleFast)
            if (Equals(name, Symbol.This) && current._hasThisValue)
            {
                value = current._thisValue;
                return true;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                // Check IsUninitialized before reading
                if (slot.IsUninitialized)
                {
                    var realm = current.RealmState ?? current.Enclosing?.RealmState;
                    throw StandardLibrary.ThrowReferenceError(
                        $"Cannot access '{name.Name}' before initialization",
                        realm: realm);
                }

                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        value = globalValue; // globalValue is already JsValue - don't box via FromObject!
                        return true;
                    }
                }

                if (slot.HasSpecialBinding)
                {
                    value = ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
                }
                else
                {
                    value = slot.Value;
                }
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryGetJsValue(name, out value))
            {
                return true;
            }

            if (current._withObject is not null && TryGetFromWithJsValue(current._withObject, name, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            value = propertyValue; // propertyValue is already JsValue - don't box via FromObject!
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Tries to get a binding's value and its const status.
    /// Used when copying bindings to preserve const semantics (e.g., per-iteration loop environments).
    /// </summary>
    internal bool TryGetJsValueWithConst(Symbol name, out JsValue value, out bool isConst)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            ref var slot = ref current.TryGetSlotRef(name);
                if (!Unsafe.IsNullRef(ref slot))
                {
                    if (slot.IsUninitialized)
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            $"Cannot access '{name.Name}' before initialization",
                            realm: current.RealmState ?? current.Enclosing?.RealmState);
                    }

                if (slot.HasSpecialBinding)
                {
                    value = ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
                }
                else
                {
                    value = slot.Value;
                }
                isConst = slot.IsConst;
                return true;
            }

            current = current.Enclosing;
        }

        value = default;
        isConst = false;
        return false;
    }

    /// <summary>
    /// Tries to get a binding value as a specific object type, avoiding boxing for primitives.
    /// Combines TryGetJsValue with TryGetObject for cleaner code.
    /// </summary>
    public bool TryGetObject<T>(Symbol name, [NotNullWhen(true)] out T? obj) where T : class
    {
        if (TryGetJsValue(name, out var value) && value.TryGetObject(out obj))
        {
            return true;
        }

        obj = null;
        return false;
    }

    /// <summary>
    /// JsValue version of TryFindBinding that avoids boxing. Returns JsValue.Uninitialized
    /// for uninitialized bindings when allowUninitialized is true.
    /// </summary>
    internal bool TryFindBindingJsValue(Symbol name, bool allowUninitialized, out JsEnvironment environment,
        out JsValue value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            // Fast path for 'this' stored via _hasThisValue (used by InvokeSimpleFast)
            if (ReferenceEquals(name, Symbol.This) && current._hasThisValue)
            {
                environment = current;
                value = current._thisValue;
                return true;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                if (slot.IsUninitialized && !allowUninitialized)
                {
                    throw StandardLibrary.ThrowReferenceError(
                        $"Cannot access '{name.Name}' before initialization",
                        realm: current.RealmState ?? current.Enclosing?.RealmState);
                }

                environment = current;
                if (slot.HasSpecialBinding)
                {
                    value = ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
                }
                else
                {
                    value = slot.Value;
                }
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                current._varEnvironmentOverride.TryFindBindingJsValue(name, allowUninitialized, out environment,
                    out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        environment = null!;
        value = JsValue.Undefined;
        return false;
    }

    /// <summary>
    /// JsValue overload that avoids boxing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AssignJsValue(Symbol name, JsValue value)
    {
        var isStrictContext = IsStrict;
        AssignInternal(name, value, isStrictContext);
    }

    private void AssignInternal(Symbol name, JsValue value, bool isStrictContext)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            JsObject? globalObject = null;
            if (current.IsGlobalFunctionScope)
            {
                globalObject = current.GetRootGlobalObject();
            }

            var realm = current.RealmState ?? current.Enclosing?.RealmState;

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                // TDZ check: uninitialized lexical bindings should throw ReferenceError
                if (slot.IsUninitialized && slot.IsLexical && !Equals(name, Symbol.This))
                {
                    throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null,
                        realm);
                }

                if (slot.IsConst)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Cannot reassign constant '{name.Name}'.",
                        realm: realm));
                }

                if (slot.IsImmutableBinding)
                {
                    // Immutable bindings (named function expression names) throw in strict mode
                    // but silently fail in non-strict mode
                    if (isStrictContext)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Cannot reassign constant '{name.Name}'.",
                            realm: realm));
                    }

                    return;
                }

                if (!slot.IsGlobalConstant)
                {
                    // Check for special binding (import/export) - use flag for fast detection
                    if (slot.HasSpecialBinding)
                    {
                        ((ISpecialBinding)slot.Value.ObjectValue!).SetJsValue(value);
                    }
                    else
                    {
                        slot.SetValueAndClearTdz(value);
                    }
                    if (!slot.IsLexical)
                    {
                        globalObject?.SetProperty(name.Name, value);
                    }

                    current.NotifyBindingObservers(name, value);
                    return;
                }

                // Handle case where the value is already a boxed JsValue
                if (isStrictContext)
                {
                    throw new ThrowSignal(
                        StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable",
                            realm: realm));
                }

                return;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current)
            {
                current._varEnvironmentOverride.AssignInternal(name, value, isStrictContext);
                return;
            }

            // For with objects, check if the property exists but DON'T check unscopables.
            // Per ES spec, unscopables only affect reads (GetBindingValue), not writes (SetMutableBinding).
            if (current._withObject is not null && HasWithPropertyForAssignment(current._withObject, name))
            {
                if (current._withObject is JsObject withObject)
                {
                    AssignmentReferenceResolver.AssignObjectProperty(withObject, name.Name, value, isStrictContext,
                        null,
                        realm);
                }
                else
                {
                    current._withObject.SetProperty(name.Name, value);
                }

                return;
            }

            if (current.Enclosing is null)
            {
                // Reached the global scope without finding the variable
                if (globalObject?.GetOwnPropertyDescriptor(name.Name) is not null)
                {
                    AssignmentReferenceResolver.AssignObjectProperty(globalObject, name.Name, value, isStrictContext,
                        null,
                        realm);
                    return;
                }

                // In strict mode, assignment to an undefined variable is an error
                // In non-strict mode, create the variable as a global
                var functionScope = current.GetFunctionScope();
                if (functionScope.HasBodyLexicalName(name))
                {
                    RealmState?.Logger?.LogInformation(
                        "AssignUnresolvable blocked by body lexical name={Name} strict={Strict} env={Env}",
                        name.Name, isStrictContext, GetHashCode());
                    throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null,
                        realm);
                }

                if (isStrictContext)
                {
                    // Use ReferenceError message format
                    throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null,
                        realm);
                }

                // Non-strict mode: Create the variable in the global scope (this environment)
                current.DefineJsValue(name, value);
                globalObject?.SetProperty(name.Name, value);
                return;
            }

            current = current.Enclosing;
        }
    }

    internal DeleteBindingResult DeleteBinding(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            // For with objects, check if the property exists but DON'T check unscopables.
            // Per ES spec, unscopables only affect reads, not writes/deletes.
            if (current._withObject is not null && HasWithPropertyForAssignment(current._withObject, name))
            {
                return current._withObject.Delete(name.Name)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            ref var slot = ref current.TryGetSlotRef(name);
            if (!Unsafe.IsNullRef(ref slot))
            {
                return current.TryDeleteDeclarativeBinding(name, ref slot)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            current = current.Enclosing;
        }

        var globalObject = GetRootGlobalObject();
        var descriptor = globalObject?.GetOwnPropertyDescriptor(name.Name);
        if (descriptor == null)
        {
            return DeleteBindingResult.NotFound;
        }

        if (!descriptor.Configurable)
        {
            return DeleteBindingResult.NotDeletable;
        }

        globalObject!.Delete(name.Name);
        return DeleteBindingResult.Deleted;
    }

    private bool TryDeleteDeclarativeBinding(Symbol name, ref JsSlot slot)
    {
        if (slot.IsLexical || slot.IsConst || slot.IsGlobalConstant || slot.BlocksFunctionScopeOverride)
        {
            return false;
        }

        if (slot.CanDelete)
        {
            RemoveSlot(name);
            return true;
        }

        if (!IsFunctionScope)
        {
            return false;
        }

        if (Enclosing is not null)
        {
            // Function scopes (including parameters) cannot remove declarative bindings.
            return false;
        }

        var globalObject = GetRootGlobalObject();
        if (globalObject is null)
        {
            return false;
        }

        var descriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
        if (descriptor?.Configurable == false)
        {
            return false;
        }

        globalObject.Delete(name.Name);
        RemoveSlot(name);
        return true;
    }

    /// <summary>
    /// Removes a slot by name (used for delete operation).
    /// </summary>
    private void RemoveSlot(Symbol name)
    {
        var slots = _slots;
        if (slots is null) return;

        var count = _slotCount;
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(slots[i].Name, name))
            {
                // Move last slot to this position (swap-remove)
                if (i < count - 1)
                {
                    slots[i] = slots[count - 1];
                }
                slots[count - 1] = default;
                _slotCount = count - 1;
                return;
            }
        }
    }

    internal JsObject? GetRootGlobalObject()
    {
        var current = this;
        var hops = 0;
        const int maxDepth = 10_000;
        while (current.Enclosing is not null && hops++ < maxDepth)
        {
            current = current.Enclosing;
        }

        ref var slot = ref current.TryGetSlotRef(Symbol.This);
        if (!Unsafe.IsNullRef(ref slot))
        {
            var slotValue = slot.HasSpecialBinding
                ? ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue()
                : slot.Value;
            if (slotValue.TryGetObject<JsObject>(out var globalObject))
            {
                return globalObject;
            }
        }

        // Debug: Log when we can't find Symbol.This
        // This helps diagnose "JSON is not defined" type errors
        if (RealmState?.Logger is { } logger)
        {
            var slot0Name = current._slots?[0].Name;
            var isRefEqual = ReferenceEquals(slot0Name, Symbol.This);
            logger.LogWarning(
                "GetRootGlobalObject: Could not find Symbol.This. SlotCount={SlotCount}, Hops={Hops}, " +
                "Slot0NameRef={Slot0Ref}, SymbolThisRef={ThisRef}, RefEqual={RefEqual}, " +
                "Slot0NameStr={Slot0Str}, SymbolThisStr={ThisStr}",
                current._slotCount, hops,
                slot0Name?.GetHashCode() ?? -1, Symbol.This.GetHashCode(), isRefEqual,
                slot0Name?.Name ?? "(null)", Symbol.This.Name);
            // Log first few slots
            for (var i = 0; i < current._slotCount && i < 5; i++)
            {
                var slotName = current._slots?[i].Name;
                logger.LogWarning(
                    "  Slot[{Index}] = '{Name}' (hash={Hash})",
                    i, slotName?.Name ?? "(null)", slotName?.GetHashCode() ?? -1);
            }
        }

        return null;
    }


    private static bool IsBlockedByUnscopables(IJsObjectLike target, string name, out bool touchedUnscopables)
    {
        touchedUnscopables = false;
        var key = SymbolKeys.Unscopables;
        if (!target.TryGetProperty(key, out var unscopables))
        {
            return false;
        }

        touchedUnscopables = true;
        return unscopables.TryGetObject<IJsPropertyAccessor>(out var accessor) &&
               accessor.TryGetProperty(name, out var blocked) && blocked.IsTruthy;
    }

    private static bool TryGetFromWithJsValue(IJsObjectLike target, Symbol name, out JsValue value)
    {
        var propertyName = name.Name;
        if (string.IsNullOrEmpty(propertyName) || !HasProperty(target, propertyName) ||
            IsBlockedByUnscopables(target, propertyName, out _))
        {
            value = JsValue.Undefined;
            return false;
        }

        if (target.TryGetProperty(propertyName, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        if (target.TryGetProperty(propertyName, JsValue.FromObjectUnsafe(target), out var receiverValue))
        {
            value = receiverValue;
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    private static bool HasVisibleWithBinding(IJsObjectLike target, Symbol name)
    {
        return TryResolveObjectBinding(target, name, out _, out _);
    }

    /// <summary>
    /// Checks if the with object has a property WITHOUT checking unscopables.
    /// Used for assignment - per ES spec, unscopables only affects reads, not writes.
    /// </summary>
    private static bool HasWithPropertyForAssignment(IJsObjectLike target, Symbol name)
    {
        var propertyName = name.Name;
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        return HasProperty(target, propertyName);
    }

    private static bool TryResolveObjectBinding(
        IJsObjectLike target,
        Symbol name,
        out string propertyName,
        out bool allowMissingAssignment)
    {
        propertyName = name.Name;
        allowMissingAssignment = false;
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        if (!HasProperty(target, propertyName))
        {
            return false;
        }

        if (target is JsObject candidate)
        {
            candidate.GetOwnPropertyDescriptor(propertyName);
        }

        if (IsBlockedByUnscopables(target, propertyName, out _))
        {
            return false;
        }

        return true;
    }

    private static bool HasProperty(IJsObjectLike target, string name)
    {
        if (target is JsProxy proxy)
        {
            return proxy.HasProperty(name);
        }

        if (target is JsObject jsObject && jsObject.HasProperty(name))
        {
            return true;
        }

        if (target.GetOwnPropertyDescriptor(name) is not null)
        {
            return true;
        }

        var depth = 0;
        var prototypeAccessor =
            (target as IPrototypeAccessorProvider)?.PrototypeAccessor ?? target.Prototype;

        while (prototypeAccessor is not null && depth++ < JsEngineConstants.MaxPrototypeChainDepth)
        {
            if (prototypeAccessor is not JsObject protoObj)
            {
                if (prototypeAccessor is IJsObjectLike objectLike)
                {
                    if (objectLike.GetOwnPropertyDescriptor(name) is not null)
                    {
                        return true;
                    }

                    prototypeAccessor =
                        (objectLike as IPrototypeAccessorProvider)?.PrototypeAccessor ?? objectLike.Prototype;
                    continue;
                }

                if (prototypeAccessor.TryGetProperty(name, out _))
                {
                    return true;
                }

                if (prototypeAccessor is IPrototypeAccessorProvider provider)
                {
                    prototypeAccessor = provider.PrototypeAccessor;
                    continue;
                }

                break;
            }

            if (protoObj.HasProperty(name))
            {
                return true;
            }

            prototypeAccessor = protoObj.PrototypeAccessor ?? protoObj.Prototype;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static JsValue GetWithBindingValueJsValue(in ObjectEnvironmentBinding binding)
    {
        var propertyName = binding.PropertyName;
        if (HasProperty(binding.BindingObject, propertyName))
        {
            return JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(binding.BindingObject), propertyName,
                out var value)
                ? value
                : JsValue.Undefined;
        }

        if (!binding.IsStrictReference)
        {
            return JsValue.Undefined;
        }

        var realm = (binding.BindingObject as JsObject)?.RealmState;
        throw StandardLibrary.ThrowReferenceError($"ReferenceError: {propertyName} is not defined",
            realm: realm);
    }

    internal static bool TrySetWithBindingValueJsValue(in ObjectEnvironmentBinding binding, JsValue value,
        RealmState? realm = null)
    {
        var propertyName = binding.PropertyName;
        var bindingObject = binding.BindingObject;
        var stillExists = HasProperty(bindingObject, propertyName);
        if (!stillExists && binding is { AllowMissingAssignment: false, IsStrictReference: true })
        {
            realm ??= (bindingObject as JsObject)?.RealmState;
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {propertyName} is not defined",
                realm: realm);
        }

        if (bindingObject is JsObject jsObject)
        {
            AssignmentReferenceResolver.AssignObjectProperty(
                jsObject,
                propertyName,
                value,
                binding.IsStrictReference,
                null,
                realm ?? jsObject.RealmState,
                bindingObject);
            return true;
        }

        JsOps.AssignPropertyValueByNameJsValue(JsValue.FromObjectUnsafe(bindingObject), propertyName, value);
        if (bindingObject is not IPropertyDefinitionHost definitionHost)
        {
            return true;
        }

        var ownDescriptor = bindingObject.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor?.IsDataDescriptor != true)
        {
            return true;
        }

        var descriptorClone = ownDescriptor.Clone();
        descriptorClone.JsValue = value;
        definitionHost.TryDefineProperty(propertyName, descriptorClone);

        return true;
    }

    internal void AddBindingObserver(Symbol symbol, Action<JsValue> observer)
    {
        _bindingObservers ??= new Dictionary<Symbol, List<Action<JsValue>>>(ReferenceEqualityComparer<Symbol>.Instance);
        if (!_bindingObservers.TryGetValue(symbol, out var list))
        {
            list = [];
            _bindingObservers[symbol] = list;
        }

        list.Add(observer);
    }

    private void NotifyBindingObservers(Symbol symbol, JsValue value)
    {
        if (_bindingObservers is null || !_bindingObservers.TryGetValue(symbol, out var observers))
        {
            return;
        }

        foreach (var observer in observers)
        {
            observer(value);
        }
    }

    internal bool HasFunctionScopedBinding(Symbol name)
    {
        var scope = GetFunctionScope();
        ref var slot = ref scope.TryGetSlotRef(name);
        return !Unsafe.IsNullRef(ref slot) && !slot.IsLexical;
    }

    internal JsEnvironment GetFunctionScope()
    {
        if (_varEnvironmentOverride is not null)
        {
            return _varEnvironmentOverride.GetFunctionScope();
        }

        var current = this;
        while (!current.IsFunctionScope)
        {
            current = current.Enclosing
                      ?? throw new InvalidOperationException("Unable to locate function scope for var declaration.");
        }

        return current;
    }

    internal JsEnvironment GetVarEnvironment()
    {
        return _varEnvironmentOverride ?? GetFunctionScope();
    }

    internal void SetVarEnvironment(JsEnvironment environment)
    {
        _varEnvironmentOverride = environment;
    }

    /// <summary>
    ///     Gets all variables from this environment and all enclosing environments.
    ///     Used for debugging purposes.
    /// </summary>
    /// <remarks>
    /// Intentionally returns object? for debugging/inspection purposes.
    /// This API is not on the hot path and boxing here is acceptable.
    /// Do not migrate to JsValue - debuggers and inspectors expect object?.
    /// </remarks>
#pragma warning disable CS0618 // ToObject() is intentional for debug API
    public Dictionary<string, object?> GetAllVariables()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        // Traverse up the scope chain
        var current = this;
        while (current is not null)
        {
            // Add variables from current scope (only if not already present from inner scope)
            var slots = current._slots;
            if (slots is not null)
            {
                for (var i = 0; i < current._slotCount; i++)
                {
                    ref var slot = ref slots[i];
                    if (slot.Name is null) continue;

                    // Skip uninitialized TDZ bindings - they exist but can't be accessed
                    if (slot.IsUninitialized) continue;

                    if (!result.ContainsKey(slot.Name.Name))
                    {
                        var value = slot.HasSpecialBinding
                            ? ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue()
                            : slot.Value;
                        result[slot.Name.Name] = value.ToObject();
                    }
                }
            }

            current = current.Enclosing;
        }

        return result;
    }

    /// <summary>
    ///     Builds environment chain information for debugging.
    ///     Shows ScopeId, slots, and dictionary variables for each environment.
    /// </summary>
    /// <remarks>
    /// Uses object? internally for debugging/inspection purposes.
    /// This API is not on the hot path and boxing here is acceptable.
    /// Do not migrate to JsValue - debuggers and inspectors expect object?.
    /// </remarks>
    public List<EnvironmentInfo> BuildEnvironmentChain()
    {
        var chain = new List<EnvironmentInfo>();
        var current = this;
        var iterations = 0;
        const int maxIterations = 100;

        while (current is not null && iterations < maxIterations)
        {
            iterations++;

            // Collect slot variables (both named and by index)
            var dictVars = new Dictionary<string, object?>(StringComparer.Ordinal);
            var slotVars = new Dictionary<int, object?>();
            if (current._slots is not null)
            {
                for (var i = 0; i < current._slotCount; i++)
                {
                    ref var slot = ref current._slots[i];
                    var value = slot.HasSpecialBinding
                        ? ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue()
                        : slot.Value;
                    slotVars[i] = value.ToObject();
                    if (slot.Name is not null)
                    {
                        dictVars[slot.Name.Name] = value.ToObject();
                    }
                }
            }

            chain.Add(new EnvironmentInfo(
                current.ScopeId,
                current._slots is not null,
                current._slotCount,
                dictVars,
                slotVars,
                current._description
            ));

            current = current.Enclosing;
        }

        return chain;
    }
#pragma warning restore CS0618

    /// <summary>
    ///     Builds a call stack by traversing the enclosing environment chain
    ///     and collecting information about the S-expressions that created each environment.
    /// </summary>
    public List<CallStackFrame> BuildCallStack()
    {
        var frames = new List<CallStackFrame>();
        var current = this;
        var iterations = 0;
        const int maxIterations = 100; // Prevent infinite loops

        while (current is not null && iterations < maxIterations)
        {
            iterations++;

            // Always add a frame if we have any identifying information
            if (current._creatingSource is not null || current._description is not null)
            {
                var operationType = DetermineOperationTypeFromDescription(current._description);
                var description = current._description ?? operationType;
                frames.Add(new CallStackFrame(
                    operationType,
                    description,
                    current._creatingSource,
                    current.Depth
                ));
            }

            // Follow the enclosing chain (lexical scope chain)
            current = current.Enclosing;
        }

        return frames;
    }

    private static string DetermineOperationTypeFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return "unknown";
        }

        var trimmed = description.TrimStart();
        var separators = new[] { ' ', '-', ':' };
        var separatorIndex = trimmed.IndexOfAny(separators);
        var firstToken = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;

        return string.IsNullOrEmpty(firstToken)
            ? "unknown"
            : firstToken.ToLowerInvariant();
    }

    internal readonly struct ResolvedIdentifierBinding
    {
        private readonly JsEnvironment _environment;
        private readonly Symbol _name;

        internal ResolvedIdentifierBinding(JsEnvironment environment, Symbol name)
        {
            _environment = environment;
            _name = name;
        }

        /// <summary>
        /// Reads the binding value as JsValue, avoiding boxing for primitives.
        /// </summary>
        internal JsValue ReadJsValue(EvaluationContext context)
        {
            ref var slot = ref _environment.TryGetSlotRef(_name);
            if (Unsafe.IsNullRef(ref slot))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            if (slot.IsUninitialized)
            {
                throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                    $"Cannot access '{_name.Name}' before initialization",
                    context,
                    context.RealmState));
            }

            if (slot.HasSpecialBinding)
            {
                return ((ISpecialBinding)slot.Value.ObjectValue!).GetJsValue();
            }

            // For global scope non-lexical bindings, check global object
            if (_environment.IsGlobalFunctionScope && !slot.IsLexical)
            {
                var globalObject = _environment.GetRootGlobalObject();
                if (globalObject is not null && globalObject.TryGetProperty(_name.Name, out var globalValue))
                {
                    return globalValue;
                }
            }

            return slot.Value;
        }

        /// <summary>
        /// Writes the binding value as JsValue, avoiding boxing for primitives.
        /// This is safe for async contexts where the original context may be stale.
        /// </summary>
        internal void WriteJsValue(JsValue value, bool isStrictContext)
        {
            ref var slot = ref _environment.TryGetSlotRef(_name);
            if (Unsafe.IsNullRef(ref slot))
            {
                throw new InvalidOperationException($"Binding for {_name.Name} not found");
            }

            if (slot.IsUninitialized && slot.IsLexical)
            {
                throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                    $"Cannot access '{_name.Name}' before initialization",
                    null,
                    _environment.RealmState));
            }

            // Const bindings always throw TypeError on reassignment per ES spec,
            // regardless of strict mode
            if (slot.IsConst && !slot.IsUninitialized)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{_name.Name}'.",
                    realm: _environment.RealmState));
            }

            if (slot.IsImmutableBinding)
            {
                if (isStrictContext)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{_name.Name}'.",
                        realm: _environment.RealmState));
                }

                return;
            }

            if (slot.HasSpecialBinding)
            {
                ((ISpecialBinding)slot.Value.ObjectValue!).SetJsValue(value);
            }
            else
            {
                slot.SetValueAndClearTdz(value);
            }

            // For non-lexical bindings (var) in global scope, also update the global object
            // This mirrors the behavior of AssignJsValue which syncs with the global object
            if (!slot.IsLexical && _environment.IsGlobalFunctionScope)
            {
                var globalObject = _environment.GetRootGlobalObject();
                globalObject?.SetProperty(_name.Name, value);
            }

            _environment.NotifyBindingObservers(_name, value);
        }
    }

    [Flags]
    private enum BindingFlags : byte
    {
        None = 0,
        IsConst = 1,
        IsGlobalConstant = 2,
        IsLexical = 4,
        BlocksFunctionScopeOverride = 8,
        CanDelete = 16,
        IsImmutableBinding = 32,
        HasSpecialBinding = 64 // When set, _value holds ISpecialBinding
    }

    /// <summary>
    /// A binding in the environment. This is a struct to avoid per-binding heap allocations.
    /// Regular values are stored in _jsValue to avoid boxing primitives.
    /// For special bindings (async exports, imports), the HasSpecialBinding flag is set
    /// and _specialBinding holds an ISpecialBinding instance.
    /// </summary>
    private struct Binding : IEquatable<Binding>
    {
        private JsValue _jsValue;
        private readonly ISpecialBinding? _specialBinding; // Only used when HasSpecialBinding flag is set
        private BindingFlags _flags;

        public Binding(
            JsValue value,
            bool isConst,
            bool isGlobalConstant,
            bool isLexical,
            bool blocksFunctionScopeOverride,
            bool canDelete,
            bool isImmutableBinding = false)
        {
            _jsValue = value;
            _specialBinding = null;
            _flags = BindingFlags.None;
            if (isConst)
            {
                _flags |= BindingFlags.IsConst;
            }

            if (isGlobalConstant)
            {
                _flags |= BindingFlags.IsGlobalConstant;
            }

            if (isLexical)
            {
                _flags |= BindingFlags.IsLexical;
            }

            if (blocksFunctionScopeOverride)
            {
                _flags |= BindingFlags.BlocksFunctionScopeOverride;
            }

            if (canDelete)
            {
                _flags |= BindingFlags.CanDelete;
            }

            if (isImmutableBinding)
            {
                _flags |= BindingFlags.IsImmutableBinding;
            }
        }

        private Binding(ISpecialBinding special, BindingFlags flags)
        {
            _jsValue = default;
            _specialBinding = special;
            _flags = flags | BindingFlags.HasSpecialBinding;
        }

        public readonly bool Equals(Binding other)
        {
            return _jsValue.Equals(other._jsValue) && Equals(_specialBinding, other._specialBinding) &&
                   _flags == other._flags;
        }

        public override readonly bool Equals(object? obj)
        {
            return obj is Binding other && Equals(other);
        }

        public override readonly int GetHashCode()
        {
            return HashCode.Combine(_jsValue, _specialBinding, (int)_flags);
        }
    }

    /// <summary>
    /// Interface for special bindings that need custom get/set behavior.
    /// </summary>
    private interface ISpecialBinding
    {
        bool IsConst { get; }
        JsValue GetJsValue();
        void SetJsValue(JsValue value);
    }

    private sealed class AsyncExportBinding(JsPromise promise, bool isConst) : ISpecialBinding
    {
        private bool _resolved;
        private JsValue _resolvedValue;

        public JsValue GetJsValue()
        {
            return _resolved ? _resolvedValue : JsValue.FromJsObject(promise.JsObject);
        }

        public void SetJsValue(JsValue value)
        {
            if (value.IsUninitialized || _resolved)
            {
                return;
            }

            _resolved = true;
            _resolvedValue = value;
            promise.Resolve(value);
        }

        public bool IsConst { get; } = isConst;
    }

    /// <summary>
    /// An import binding that proxies reads to the source module's environment.
    /// Import bindings are immutable (assignment throws TypeError).
    /// </summary>
    private sealed class ImportBindingWrapper(JsEnvironment sourceEnvironment, Symbol bindingName) : ISpecialBinding
    {
        private JsEnvironment SourceEnvironment { get; } = sourceEnvironment;
        private Symbol BindingName { get; } = bindingName;

        public JsValue GetJsValue()
        {
            return SourceEnvironment.GetJsValue(BindingName);
        }

        public void SetJsValue(JsValue value)
        {
            throw new InvalidOperationException("TypeError: Cannot assign to import binding");
        }

        public bool IsConst => true;
    }

    #region Slot-based Variable Access

    /// <summary>
    /// Clears all slot values without deallocating the array.
    /// Used during Reset/ResetForReuse.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearSlots()
    {
        var slots = _slots;
        if (slots is null) return;
        var count = _slotCount;
        for (var i = 0; i < count; i++)
        {
            slots[i] = default;
        }
        _slotCount = 0;
    }

    /// <summary>
    /// Initializes slot storage for this environment.
    /// Internal use only - prefer Initialize() for public API.
    /// </summary>
    /// <param name="slotCount">Number of slots needed for this scope.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void InitializeSlots(int slotCount)
    {
        _slotCount = slotCount;
        if (slotCount <= 0)
        {
            return;
        }

        // Reuse existing array if it's big enough, otherwise rent from pool
        if (_slots is null || _slots.Length < slotCount)
        {
            // Return old array to pool before getting new one
            JsSlotArrayPool.Return(_slots);
            _slots = JsSlotArrayPool.Rent(slotCount);
        }

        // Clear all slots (set to empty)
        Array.Clear(_slots, 0, slotCount);
    }

    /// <summary>
    /// Initializes slot storage and scope ID for this environment.
    /// Internal use only - prefer Initialize() for public API.
    /// </summary>
    /// <param name="slotCount">Number of slots needed for this scope.</param>
    /// <param name="scopeId">Unique ID for this scope from scope analysis.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void InitializeSlots(int slotCount, int scopeId)
    {
        ValidateScopeId(scopeId);
        ScopeId = scopeId;

        // If we already have slots (from hoisting), preserve them and ensure capacity
        // This is important for scripts where hoisted declarations are stored in slots
        // before the execution plan internal slots are needed.
        var existingCount = _slotCount;
        if (existingCount > 0)
        {
            // Ensure we have capacity for existing slots + new slots
            var neededCapacity = existingCount + slotCount;
            if (_slots is null || _slots.Length < neededCapacity)
            {
                var oldSlots = _slots;
                _slots = JsSlotArrayPool.Rent(neededCapacity);
                if (oldSlots is not null)
                {
                    Array.Copy(oldSlots, _slots, existingCount);
                    JsSlotArrayPool.Return(oldSlots);
                }
            }
            // Clear only the new slots (after existing ones)
            if (slotCount > 0)
            {
                Array.Clear(_slots, existingCount, slotCount);
            }
            // Keep _slotCount as the sum of existing + new
            _slotCount = neededCapacity;
            return;
        }

        _slotCount = slotCount;
        if (slotCount <= 0)
        {
            return;
        }

        // Reuse existing array if it's big enough, otherwise rent from pool
        if (_slots is null || _slots.Length < slotCount)
        {
            // Return old array to pool before getting new one
            JsSlotArrayPool.Return(_slots);
            _slots = JsSlotArrayPool.Rent(slotCount);
        }

        // Clear all slots (set to empty) - only when starting fresh
        Array.Clear(_slots, 0, slotCount);
    }

    /// <summary>
    /// Ensures the slot layout matches the expected plan layout.
    /// If the layoutId or capacity does not match, the slots are rebuilt.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ResetSlotLayoutForPlan(
        int requiredSlots,
        ImmutableDictionary<Symbol, int> slotMap,
        ImmutableHashSet<Symbol> lexicalBindings,
        ImmutableArray<Symbol> slotSymbols,
        int layoutId,
        int scopeId)
    {
        var needsRebuild = LayoutId != layoutId || _slots is null || _slots.Length < requiredSlots ||
                           _slotCount < requiredSlots;
        if (needsRebuild)
        {
            JsSlotArrayPool.Return(_slots);
            _slots = null;
        }

        InitializeSlots(requiredSlots, scopeId);

        if (!slotMap.IsEmpty)
        {
            SetSlotMap(slotMap);
        }
        else if (!slotSymbols.IsDefaultOrEmpty)
        {
            var builder = ImmutableDictionary.CreateBuilder<Symbol, int>(ReferenceEqualityComparer<Symbol>.Instance);
            for (var i = 0; i < slotSymbols.Length; i++)
            {
                builder[slotSymbols[i]] = i;
            }

            SetSlotMap(builder.ToImmutable());
        }

        if (!lexicalBindings.IsEmpty)
        {
            MarkSlotsLexicalUninitialized(lexicalBindings);
        }

        LayoutId = layoutId;
    }

    /// <summary>
    /// Initializes all scope-related metadata for this environment using a slot map.
    /// This converts from the old ImmutableDictionary-based approach.
    /// </summary>
    /// <param name="scopeId">Unique ID for this scope from scope analysis.</param>
    /// <param name="slotMap">Mapping from symbol names to slot indices.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Initialize(int scopeId, System.Collections.Immutable.ImmutableDictionary<Symbol, int> slotMap)
    {
        ValidateScopeId(scopeId);
        ScopeId = scopeId;
        var count = slotMap.Count;
        _slotCount = count;

        if (count <= 0)
        {
            return;
        }

        // Ensure capacity
        if (_slots is null || _slots.Length < count)
        {
            JsSlotArrayPool.Return(_slots);
            _slots = JsSlotArrayPool.Rent(count);
        }

        // Populate slots from slotMap - each entry maps Symbol -> index
            foreach (var (symbol, index) in slotMap)
            {
                if (index >= 0 && index < count)
                {
                    _slots[index] = new JsSlot(symbol, JsValue.Undefined, SlotFlags.None);
                }
            }
        }

    /// <summary>
    /// Sets slot names from a slot map. Used for compatibility with old API.
    /// Preserves existing slots that already have names (e.g., Symbol.This from GlobalEnvironment).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetSlotMap(System.Collections.Immutable.ImmutableDictionary<Symbol, int> slotMap)
    {
        var count = slotMap.Count;

        // Ensure we have capacity
        if (count > 0)
        {
            if (_slots is null || _slots.Length < count)
            {
                // Preserve existing slots when reallocating
                var oldSlots = _slots;
                var existingCount = _slotCount;
                _slots = JsSlotArrayPool.Rent(count);

                if (oldSlots is not null && existingCount > 0)
                {
                    // Copy existing slots to preserve their names and values
                    Array.Copy(oldSlots, _slots, Math.Min(existingCount, count));
                    JsSlotArrayPool.Return(oldSlots);
                }
                else
                {
                    Array.Clear(_slots, 0, count);
                }
            }

            // Set slot names from the map, but only for slots that don't already have names.
            // This prevents overwriting existing bindings like Symbol.This on the GlobalEnvironment.
            foreach (var (symbol, index) in slotMap)
            {
                if (index >= 0 && index < _slots.Length && _slots[index].Name is null)
                {
                    _slots[index] = new JsSlot(symbol, JsValue.Undefined, SlotFlags.None);
                }
            }

            _slotCount = Math.Max(_slotCount, count);
        }
    }

    /// <summary>
    /// Marks the provided symbols as lexical/uninitialized in the current slots (TDZ).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MarkSlotsLexicalUninitialized(IEnumerable<Symbol> symbols)
    {
        if (_slots is null)
        {
            return;
        }

        foreach (var symbol in symbols)
        {
            var index = FindSlotIndex(symbol);
            if (index < 0 || index >= _slotCount)
            {
                continue;
            }

            ref var slot = ref _slots[index];
            slot.Flags |= SlotFlags.Lexical | SlotFlags.Uninitialized | SlotFlags.BlocksFunctionScopeOverride;
            // Preserve existing value; TDZ enforced via Uninitialized flag
        }
    }

    /// <summary>
    /// Finds the slot index for a given symbol name using linear scan.
    /// Returns -1 if not found. Fast for typical JS scopes (1-10 bindings).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int FindSlotIndex(Symbol name)
    {
        var slots = _slots;
        if (slots is null)
        {
            return -1;
        }

        var count = _slotCount;
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(slots[i].Name, name))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Tries to get the slot index for a symbol from this environment.
    /// Uses linear scan over slot names.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetSlotIndex(Symbol name, out int slotIndex)
    {
        slotIndex = FindSlotIndex(name);
        return slotIndex >= 0;
    }

    /// <summary>
    /// Gets a reference to a slot by index. Caller must ensure index is valid.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref JsSlot GetSlotByIndex(int index)
    {
        return ref _slots![index];
    }

    /// <summary>
    /// Defines a new slot with the given name, value, and flags.
    /// Returns the index of the new slot.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int DefineSlot(Symbol name, JsValue value, SlotFlags flags)
    {
        var index = _slotCount;

        // Ensure we have capacity
        if (_slots is null || index >= _slots.Length)
        {
            GrowSlots();
        }

        _slots![index] = new JsSlot(name, value, flags);
        _slotCount = index + 1;
        return index;
    }

    /// <summary>
    /// Grows the slots array when capacity is exceeded.
    /// </summary>
    private void GrowSlots()
    {
        var newCapacity = _slots is null ? 4 : _slots.Length * 2;
        var newSlots = JsSlotArrayPool.Rent(newCapacity);

        if (_slots is not null)
        {
            Array.Copy(_slots, newSlots, _slotCount);
            JsSlotArrayPool.Return(_slots);
        }

        _slots = newSlots;
    }

    /// <summary>
    /// Gets a reference to an existing slot by name, or adds a new slot if not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref JsSlot GetOrDefineSlotRef(Symbol name, JsValue value, SlotFlags flags)
    {
        var index = FindSlotIndex(name);
        if (index >= 0)
        {
            return ref _slots![index];
        }

        index = DefineSlot(name, value, flags);
        return ref _slots![index];
    }

    /// <summary>
    /// Tries to get a reference to an existing slot by name.
    /// Returns Unsafe.NullRef if not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref JsSlot TryGetSlotRef(Symbol name)
    {
        var index = FindSlotIndex(name);
        if (index >= 0)
        {
            ref var slot = ref _slots![index];
            var realmState = RealmState;
            if (realmState?.Options.DebugMode == true)
            {
                realmState.Logger?.LogTrace(
                    "Identifier slot read hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                    GetHashCode(),
                    slot.Name.Name,
                    FormatScopeIdForLog(ScopeId),
                    index,
                    slot.Value.Kind);
            }

            return ref slot;
        }

        return ref Unsafe.NullRef<JsSlot>();
    }

    /// <summary>
    /// Checks if a binding exists in this environment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool HasBindingLocal(Symbol name)
    {
        return FindSlotIndex(name) >= 0;
    }

    /// <summary>
    /// Builds SlotFlags from individual boolean parameters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SlotFlags BuildSlotFlags(
        bool isConst,
        bool isGlobalConstant,
        bool isLexical,
        bool blocksFunctionScopeOverride,
        bool canDelete,
        bool isImmutableBinding = false,
        bool uninitialized = false)
    {
        var flags = SlotFlags.None;
        if (isConst) flags |= SlotFlags.Const;
        if (isGlobalConstant) flags |= SlotFlags.GlobalConstant;
        if (isLexical) flags |= SlotFlags.Lexical;
        if (blocksFunctionScopeOverride) flags |= SlotFlags.BlocksFunctionScopeOverride;
        if (canDelete) flags |= SlotFlags.CanDelete;
        if (isImmutableBinding) flags |= SlotFlags.ImmutableBinding;
        if (uninitialized) flags |= SlotFlags.Uninitialized;
        return flags;
    }

    /// <summary>
    /// Finds the environment in the chain that has the specified scope ID.
    /// Returns null if not found (caller should fall back to dictionary lookup).
    /// </summary>
    /// <param name="scopeId">The scope ID to find.</param>
    /// <returns>The environment with matching ScopeId, or null if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal JsEnvironment? FindByScopeId(int scopeId)
    {
        var env = this;
        while (env is not null)
        {
            if (env.ScopeId == scopeId)
            {
                return env;
            }

            env = env.Enclosing;
        }

        return null;
    }

    /// <summary>
    /// Gets a variable value by slot index. This is the fast path for resolved identifiers.
    /// </summary>
    /// <param name="scopeDepth">How many function scopes up (0 = this scope).</param>
    /// <param name="slotIndex">Index into the slots array.</param>
    /// <returns>The value at the specified slot.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JsValue GetSlot(int scopeDepth, int slotIndex)
    {
        var env = this;
        while (scopeDepth > 0)
        {
            env = env.Enclosing!;
            // Only count function scopes for depth
            if (env.IsFunctionScope)
            {
                scopeDepth--;
            }
        }

        return env._slots![slotIndex].Value;
    }

    /// <summary>
    /// Sets a variable value by slot index. This is the fast path for resolved identifiers.
    /// </summary>
    /// <param name="scopeDepth">How many function scopes up (0 = this scope).</param>
    /// <param name="slotIndex">Index into the slots array.</param>
    /// <param name="value">The value to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSlot(int scopeDepth, int slotIndex, JsValue value)
    {
        var env = this;
        while (scopeDepth > 0)
        {
            env = env.Enclosing!;
            // Only count function scopes for depth
            if (env.IsFunctionScope)
            {
                scopeDepth--;
            }
        }

        env.SetSlotDirect(slotIndex, value);
    }

    /// <summary>
    /// Sets a variable value by slot index in the current environment.
    /// Caller must ensure slotIndex is valid and slots are initialized.
    /// </summary>
    /// <param name="slotIndex">Index into the slots array.</param>
    /// <param name="value">The value to set.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetSlot(int slotIndex, JsValue value)
    {
        SetSlotDirect(slotIndex, value);
    }

    /// <summary>
    /// Checks if this environment has slot storage initialized.
    /// </summary>
    public bool HasSlots => _slots is not null && _slotCount > 0;

    /// <summary>
    /// Gets a direct reference to a slot's value. This enables zero-overhead read/write
    /// when the caller has already resolved the target environment.
    /// WARNING: Caller must ensure slotIndex is valid and _slots is initialized.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref JsValue GetSlotRef(int slotIndex)
    {
        ref var slot = ref _slots![slotIndex];
        var realmState = RealmState;
        if (realmState?.Options.DebugMode == true)
        {
            realmState.Logger?.LogTrace(
                "Identifier slot read hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                GetHashCode(),
                slot.Name.Name,
                ScopeId,
                slotIndex,
                slot.Value.Kind);
        }

        return ref slot.Value;
    }

    /// <summary>
    /// Sets a slot value directly without any lookups.
    /// Used for fast per-iteration binding copies in generators.
    /// WARNING: Caller must ensure slotIndex is valid and _slots is initialized.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetSlotDirect(int slotIndex, JsValue value)
    {
        ref var slot = ref _slots![slotIndex];
        slot.Value = value;
        // Clearing Uninitialized makes the slot readable (TDZ satisfied) after copy.
        slot.Flags &= ~SlotFlags.Uninitialized;
        if (_bindingObservers is not null)
        {
            NotifyBindingObservers(slot.Name, value);
        }
    }

    /// <summary>
    /// Resolves an identifier to its target environment and slot index.
    /// Returns true if the identifier can be accessed via slots, false if dictionary fallback is needed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryResolveSlot(Symbol name, int scopeId, int slotIndex, out JsEnvironment? targetEnv)
    {
        if (scopeId < 0 || slotIndex < 0)
        {
            targetEnv = null;
            return false;
        }

        targetEnv = (ScopeId == scopeId) ? this : FindByScopeId(scopeId);
        var slots = targetEnv?._slots;
        if (targetEnv is null || slots is null || slotIndex >= targetEnv._slotCount)
        {
            targetEnv = null;
            return false;
        }

        ref var slot = ref slots[slotIndex];
        if (!ReferenceEquals(slot.Name, name))
        {
            targetEnv = null;
            return false;
        }

        return true;
    }

    private bool TryValidateSlotTarget(
        Symbol name,
        int scopeId,
        int slotIndex,
        bool shouldLogSlots,
        ILogger? logger,
        [NotNullWhen(true)] out JsEnvironment? targetEnv,
        out JsSlot[]? slots)
    {
        targetEnv = null;
        slots = null;

        if (scopeId < 0 || slotIndex < 0)
        {
            return false;
        }

        var resolvedEnv = ScopeId == scopeId ? this : FindByScopeId(scopeId);
        var resolvedSlots = resolvedEnv?._slots;
        if (resolvedEnv is null || resolvedSlots is null || slotIndex >= resolvedEnv._slotCount)
        {
            if (shouldLogSlots)
            {
                logger?.LogInformation(
                    "Identifier slot fallback name={Name} slotScope={ScopeId} slot={Slot} reason=env_or_index_mismatch",
                    name.Name,
                    FormatScopeIdForLog(scopeId),
                    slotIndex);
            }

            return false;
        }

        ref var slot = ref resolvedSlots[slotIndex];
        if (!ReferenceEquals(slot.Name, name))
        {
            if (shouldLogSlots)
            {
                logger?.LogInformation(
                    "Identifier slot fallback name={Name} slotScope={ScopeId} slot={Slot} reason=name_mismatch actualName={ActualName}",
                    name.Name,
                    FormatScopeIdForLog(scopeId),
                    slotIndex,
                    slot.Name?.Name ?? "<null>");
            }

            return false;
        }

        if (shouldLogSlots)
        {
            logger?.LogInformation(
                "Identifier slot read/write validated env={Env} name={Name} slotScope={ScopeId} slot={Slot}",
                resolvedEnv.GetHashCode(),
                name.Name,
                FormatScopeIdForLog(scopeId),
                slotIndex);
            logger?.LogTrace(
                "Identifier slot read hit env={Env} name={Name} slotScope={ScopeId} slot={Slot} valueKind={Kind}",
                resolvedEnv.GetHashCode(),
                name.Name,
                FormatScopeIdForLog(scopeId),
                slotIndex,
                slot.Value.Kind);
        }

        targetEnv = resolvedEnv;
        slots = resolvedSlots;
        return true;
    }

    #endregion
}
