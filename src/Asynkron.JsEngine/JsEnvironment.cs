using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine;

public sealed class JsEnvironment
{
    private const int MaxDepth = 1_000;
    internal static readonly object Uninitialized = new();
    private readonly SourceReference? _creatingSource;
    private readonly bool _inheritStrictness;
    private readonly string? _description;
    private readonly bool _treatAsGlobalFunctionScope;

    private readonly Dictionary<Symbol, Binding> _values = new();
    private readonly IJsObjectLike? _withObject;
    private Dictionary<Symbol, List<Action<object?>>>? _bindingObservers;
    private HashSet<Symbol>? _bodyLexicalNames;
    private HashSet<Symbol>? _simpleCatchParameters;
    private HashSet<Symbol>? _annexBFunctionNames;
    private HashSet<Symbol>? _annexBApplicableFunctions;

    internal RealmState? RealmState { get; private set; }
    private JsEnvironment? _varEnvironmentOverride;

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
        RealmState = enclosing?.RealmState;

        Depth = (Enclosing?.Depth ?? -1) + 1;
        if (Depth > MaxDepth)
        {
            throw new InvalidOperationException(
                $"Exceeded maximum environment depth of {MaxDepth}. Possible unbounded recursion detected.");
        }
    }

    /// <summary>
    ///     Depth of the environment chain (0 for the root/global).
    /// </summary>
    public int Depth { get; }

    private bool IsStrictLocal { get; }

    /// <summary>
    ///     Returns true if this environment or any enclosing environment is in strict mode.
    /// </summary>
    public bool IsStrict => IsStrictLocal || (_inheritStrictness && (Enclosing?.IsStrict ?? false));

    internal bool IsObjectEnvironment => _withObject is not null;

    internal bool IsParameterEnvironment { get; }

    internal bool IsBodyEnvironment { get; }

    internal bool IsFunctionScope { get; }

    internal JsEnvironment? Enclosing { get; }

    internal void SetRealmState(RealmState realmState)
    {
        RealmState = realmState;
    }

    internal bool IsGlobalFunctionScope => _treatAsGlobalFunctionScope || (IsFunctionScope && Enclosing is null);

    public void Define(
        Symbol name,
        object? value,
        bool isConst = false,
        bool isGlobalConstant = false,
        bool isLexical = true,
        bool blocksFunctionScopeOverride = false,
        bool canDelete = false)
    {
        if (_values.TryGetValue(name, out var existing) && existing.IsGlobalConstant)
        {
            return;
        }

        if (_values.TryGetValue(name, out var binding))
        {
            if (binding.IsConst || binding.IsGlobalConstant)
            {
                // Generators can execute flattened blocks without recreating the
                // lexical environment per iteration, which would normally allow
                // a fresh const/let binding each time. If we see a lexical
                // redeclaration request, replace the binding so loop iterations
                // can observe the new value instead of sticking with the first.
                if (isLexical && blocksFunctionScopeOverride)
                {
                    _values[name] = new Binding(value, isConst, isGlobalConstant, isLexical,
                        blocksFunctionScopeOverride, canDelete);
                }

                return;
            }

            binding.Value = value;
            binding.UpgradeLexical(isLexical, blocksFunctionScopeOverride);
            NotifyBindingObservers(name, value);
            return;
        }

        _values[name] = new Binding(value, isConst, isGlobalConstant, isLexical, blocksFunctionScopeOverride,
            canDelete);
        NotifyBindingObservers(name, value);
    }

    public void DefineFunctionScoped(
        Symbol name,
        object? value,
        bool hasInitializer,
        bool isFunctionDeclaration = false,
        bool? globalFunctionConfigurable = null,
        EvaluationContext? context = null,
        bool blocksFunctionScopeOverride = false,
        bool? globalVarConfigurable = null,
        bool allowExistingGlobalFunctionRedeclaration = false,
        bool isAnnexBFunction = false,
        bool canDelete = false)
    {
        // `var` declarations are hoisted to the nearest function/global scope, so we skip block environments here.
        LogRealm("DefineFunctionScoped name={Name} funcDecl={FuncDecl} hasInit={HasInit} allowDelete={AllowDelete}",
            name.Name, isFunctionDeclaration, hasInitializer, canDelete);
        var scope = GetFunctionScope();
        var isGlobalScope = scope.IsGlobalFunctionScope;
        var wasTrackedAnnexBFunction = scope._annexBFunctionNames?.Contains(name) == true;
        JsObject? globalThis = null;
        PropertyDescriptor? existingDescriptor = null;
        object? existingGlobalValue = null;
        var hasLooseGlobalValue = false;
        var allowDelete = canDelete || context is { ExecutionKind: ExecutionKind.Eval } && !isGlobalScope;
        if (isGlobalScope)
        {
            globalThis = scope.GetRootGlobalObject();
            if (globalThis is not null)
            {
                existingDescriptor = globalThis.GetOwnPropertyDescriptor(name.Name);
                if (existingDescriptor is not null)
                {
                    globalThis.TryGetProperty(name.Name, out existingGlobalValue);
                }
                else if (globalThis.TryGetProperty(name.Name, out var looseValue))
                {
                    existingGlobalValue = looseValue;
                    hasLooseGlobalValue = true;
                }
            }
        }

        var canDeclareFunction = true;
        var isRestrictedGlobal = isGlobalScope &&
                                 existingDescriptor is { Configurable: false } descriptor &&
                                 (!descriptor.IsDataDescriptor || !descriptor.Writable);

        if (isGlobalScope && isFunctionDeclaration)
        {
            if (isRestrictedGlobal)
            {
                LogRealm("DefineFunctionScoped restricted global function name={Name}", name.Name);
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }

            canDeclareFunction = existingDescriptor switch
            {
                null => globalThis?.IsExtensible != false,
                { Configurable: true } => true,
                _ => !existingDescriptor.IsAccessorDescriptor &&
                     existingDescriptor.Writable &&
                     existingDescriptor.Enumerable
            };

            if (!canDeclareFunction)
            {
                LogRealm("DefineFunctionScoped cannot declare global function name={Name} existingConfig={Config} writable={Writable} enumerable={Enumerable}",
                    name.Name, existingDescriptor.Configurable, existingDescriptor.Writable, existingDescriptor.Enumerable);
                throw StandardLibrary.ThrowTypeError("Cannot redeclare non-configurable global function",
                    context, context?.RealmState);
            }
        }

        if (isGlobalScope &&
            !isFunctionDeclaration &&
            existingDescriptor is null &&
            globalThis is not null &&
            !globalThis.IsExtensible)
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot declare global variable '{name.Name}' on a non-extensible global object.",
                context,
                context?.RealmState);
        }

        if (scope._values.TryGetValue(name, out var existing))
        {
            if (existing.IsConst || existing.IsGlobalConstant)
            {
                TrackAnnexBBinding();
                return;
            }

            if (blocksFunctionScopeOverride)
            {
                existing.UpgradeLexical(existing.IsLexical, true);
            }

            if (existing.BlocksFunctionScopeOverride)
            {
                if (hasInitializer)
                {
                    existing.Value = value;
                    if (isGlobalScope && globalThis is not null)
                    {
                        globalThis.SetProperty(name.Name, value);
                    }
                }

                return;
            }

            if (hasInitializer)
            {
                existing.Value = value;
                LogRealm("DefineFunctionScoped reuse existing binding name={Name} varEnvOverride={VarOverride} isGlobal={IsGlobal} isFunctionDecl={FuncDecl} hasInitializer={HasInit}",
                    name.Name, _varEnvironmentOverride is not null, isGlobalScope, isFunctionDeclaration, hasInitializer);
                if (isGlobalScope && globalThis is not null)
                {
                    globalThis.SetProperty(name.Name, value);
                }
            }

            TrackAnnexBBinding();
            return;
        }

        var allowConfigurableGlobalBinding =
            context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
        var varBindingConfigurable = globalVarConfigurable ?? allowConfigurableGlobalBinding;

        var initialValue = value;
        var shouldWriteGlobal = true;

        if (isGlobalScope && !hasInitializer && (existingDescriptor is not null || hasLooseGlobalValue))
        {
            initialValue = existingGlobalValue;
            shouldWriteGlobal = false;
        }

        scope._values[name] = new Binding(initialValue, false, false, false, blocksFunctionScopeOverride, allowDelete);
        TrackAnnexBBinding();
        if (isGlobalScope && globalThis is not null && shouldWriteGlobal)
        {
            if (isFunctionDeclaration)
            {
                var configurable = globalFunctionConfigurable ?? allowConfigurableGlobalBinding;
                if (existingDescriptor is null)
                {
                    if (!globalThis.TryDefineProperty(
                            name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
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
                            Value = initialValue, Writable = true, Enumerable = true, Configurable = configurable
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
                        new PropertyDescriptor
                        {
                            Value = initialValue
                        }))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            $"Cannot update global function binding for '{name.Name}'.",
                            context,
                            context?.RealmState);
                    }
                }
            }
            else
            {
                if (existingDescriptor is null)
                {
                    if (!globalThis.TryDefineProperty(
                        name.Name,
                        new PropertyDescriptor
                        {
                            Value = initialValue,
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
        }

        void TrackAnnexBBinding()
        {
            if (!isGlobalScope)
            {
                return;
            }

            if (isAnnexBFunction)
            {
                if (context is { ExecutionKind: ExecutionKind.Eval })
                {
                    scope._annexBFunctionNames?.Remove(name);
                    return;
                }

                scope._annexBFunctionNames ??=
                    new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
                scope._annexBFunctionNames.Add(name);
                return;
            }

            scope._annexBFunctionNames?.Remove(name);
        }
    }

    public object? Get(Symbol name)
    {
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);
        return GetInternal(name, visited);
    }

    private object? GetInternal(Symbol name, HashSet<JsEnvironment> visited)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope &&
                    !binding.IsLexical)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        return globalValue;
                    }
                }

                return binding.Value;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride))
            {
                return current._varEnvironmentOverride.GetInternal(name, visited);
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out var withValue))
            {
                return withValue;
            }

            current = current.Enclosing;
        }

        if (IsGlobalFunctionScope)
        {
            var rootGlobal = GetRootGlobalObject();
            if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
            {
                return propertyValue;
            }
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    internal object? GetDeclarative(Symbol name)
    {
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);
        return GetDeclarativeInternal(name, visited);
    }

    private object? GetDeclarativeInternal(Symbol name, HashSet<JsEnvironment> visited)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;
        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope &&
                    !binding.IsLexical)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        return globalValue;
                    }
                }

                return binding.Value;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride))
            {
                return current._varEnvironmentOverride.GetDeclarativeInternal(name, visited);
            }

            current = current.Enclosing;
        }

        var rootGlobal = GetRootGlobalObject();
        if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
        {
            return propertyValue;
        }

        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    internal bool IsConstBinding(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding))
        {
            return binding.IsConst || binding.IsGlobalConstant;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            return false;
        }

        return Enclosing?.IsConstBinding(name) ?? false;
    }

    internal bool HasBinding(Symbol name)
    {
        if (_values.ContainsKey(name))
        {
            return true;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            return true;
        }

        return Enclosing?.HasBinding(name) ?? false;
    }

    internal bool HasOwnBinding(Symbol name)
    {
        return _values.ContainsKey(name);
    }

    internal bool HasOwnLexicalBinding(Symbol name)
    {
        return _values.TryGetValue(name, out var binding) && binding.IsLexical;
    }

    internal bool TryAssignBlockedBinding(Symbol name, object? value)
    {
        var current = this;
        while (current is not null)
        {
            if (current._values.TryGetValue(name, out var binding) && binding.BlocksFunctionScopeOverride)
            {
                binding.Value = value;
                current.NotifyBindingObservers(name, value);
                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    globalObject?.SetProperty(name.Name, value);
                }

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
        var isStrictReference = context.CurrentScope.IsStrict;

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

            if (current._values.ContainsKey(name))
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
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);

        if (TryLocateBinding(name, visited, out var bindingEnvironment, out var binding))
        {
            return new AssignmentReference(
                () => AssignmentReferenceResolver.ReadIdentifierValue(
                    () => ReadResolvedBindingValue(bindingEnvironment, binding, name), context),
                newValue =>
                {
                    WriteResolvedBindingValue(bindingEnvironment, binding, name, newValue, strictContext);
                });
        }

        return new AssignmentReference(
            () => AssignmentReferenceResolver.ReadIdentifierValue(
                () => ReadUnresolvable(name), context),
            newValue => AssignUnresolvable(name, newValue, strictContext, context));
    }

    private static object? ReadResolvedBindingValue(JsEnvironment bindingEnvironment, Binding binding, Symbol name)
    {
        if (ReferenceEquals(binding.Value, Uninitialized))
        {
            throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
        }

        if (bindingEnvironment.IsGlobalFunctionScope && !binding.IsLexical)
        {
            var globalObject = bindingEnvironment.GetRootGlobalObject();
            if (globalObject is not null && globalObject.TryGetProperty(name.Name, out var globalValue))
            {
                return globalValue;
            }
        }

        return binding.Value;
    }

    private void WriteResolvedBindingValue(
        JsEnvironment bindingEnvironment,
        Binding binding,
        Symbol name,
        object? value,
        bool isStrictContext)
    {
        var realm = bindingEnvironment.RealmState ?? bindingEnvironment.Enclosing?.RealmState;

        if (ReferenceEquals(binding.Value, Uninitialized) &&
            binding.IsLexical &&
            !Symbol.Equals(name, Symbol.This))
        {
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (binding.IsConst)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                $"Cannot reassign constant '{name.Name}'.", realm: realm));
        }

        if (binding.IsGlobalConstant)
        {
            if (isStrictContext)
            {
                throw new ThrowSignal(
                    StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable", realm: realm));
            }

            return;
        }

        binding.Value = value;
        if (!binding.IsLexical && bindingEnvironment.IsGlobalFunctionScope)
        {
            bindingEnvironment.GetRootGlobalObject()?.SetProperty(name.Name, value);
        }

        bindingEnvironment.NotifyBindingObservers(name, value);
    }

    private static object ReadUnresolvable(Symbol name)
    {
        throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
    }

    private void AssignUnresolvable(Symbol name, object? value, bool isStrictContext, EvaluationContext context)
    {
        var realm = RealmState ?? Enclosing?.RealmState;
        if (isStrictContext)
        {
            context.RealmState?.Logger?.LogInformation(
                "AssignUnresolvable strict throw name={Name} scopeStrict={ScopeStrict} functionScopeStrict={FnStrict} env={Env}",
                name.Name,
                context.CurrentScope.IsStrict,
                GetFunctionScope().IsStrict,
                GetHashCode());
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        var globalScope = this;
        while (globalScope.Enclosing is not null)
        {
            globalScope = globalScope.Enclosing;
        }

        if (!globalScope._values.ContainsKey(name))
        {
            globalScope.Define(name, value, isLexical: false, canDelete: true);
        }
        else
        {
            globalScope.AssignInternal(name, value, isStrictContext, new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance));
        }

        var globalObject = GetRootGlobalObject();
        globalObject?.SetProperty(name.Name, value);
        LogRealm("Assign created global via sloppy assignment name={Name} valueType={ValueType}", name.Name,
            value?.GetType().Name ?? "null");
        context.RealmState?.Logger?.LogInformation(
            "Sloppy assignment created unresolvable binding name={Name} valueType={ValueType}",
            name.Name,
            value?.GetType().Name ?? "null");
    }

    private bool TryLocateBinding(
        Symbol name,
        HashSet<JsEnvironment> visited,
        out JsEnvironment bindingEnvironment,
        out Binding binding)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out binding))
            {
                bindingEnvironment = current;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride) &&
                current._varEnvironmentOverride.TryLocateBinding(name, visited, out bindingEnvironment, out binding))
            {
                return true;
            }

            current = current.Enclosing;
        }

        bindingEnvironment = null!;
        binding = null!;
        return false;
    }

    internal bool HasLexicalBinding(Symbol name)
    {
        if (_values.TryGetValue(name, out var binding) && binding.IsLexical)
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
            if (current._withObject is null && current._values.ContainsKey(name))
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

        if (!scope._values.TryGetValue(Symbol.This, out var thisBinding) ||
            thisBinding.Value is not JsObject globalObject)
        {
            return false;
        }

        var descriptor = globalObject.GetOwnPropertyDescriptor(name.Name);
        if (descriptor is not null && !descriptor.Configurable)
        {
            return true;
        }

        return scope._annexBFunctionNames is not null && scope._annexBFunctionNames.Contains(name);
    }

    internal PropertyDescriptor? GetGlobalOwnPropertyDescriptor(Symbol name, out JsObject? globalObject)
    {
        globalObject = GetRootGlobalObject();
        return globalObject?.GetOwnPropertyDescriptor(name.Name);
    }

    internal bool HasLexicalBindingBeforeFunctionScope(Symbol name)
    {
        var current = this;
        while (current is not null && !current.IsFunctionScope)
        {
            if (current._values.TryGetValue(name, out var binding) &&
                binding.IsLexical)
            {
                return true;
            }

            current = current.Enclosing;
        }

        return false;
    }

    internal void MarkAnnexBApplicableFunction(Symbol name)
    {
        _annexBApplicableFunctions ??=
            new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
        _annexBApplicableFunctions.Add(name);
    }

    internal bool IsAnnexBApplicableFunction(Symbol name)
    {
        return _annexBApplicableFunctions is not null && _annexBApplicableFunctions.Contains(name);
    }

    internal void SetBodyLexicalNames(HashSet<Symbol> names)
    {
        _bodyLexicalNames = names;
    }

    internal bool HasBodyLexicalName(Symbol name)
    {
        return _bodyLexicalNames is not null && _bodyLexicalNames.Contains(name);
    }

    internal void SetSimpleCatchParameters(HashSet<Symbol> names)
    {
        _simpleCatchParameters = names;
    }

    internal bool IsSimpleCatchParameter(Symbol name)
    {
        return _simpleCatchParameters is not null && _simpleCatchParameters.Contains(name);
    }

    public bool TryGet(Symbol name, out object? value)
    {
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        value = globalValue;
                        return true;
                    }
                }

                value = binding.Value;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride) &&
                current._varEnvironmentOverride.TryGetInternal(name, visited, out value))
            {
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        if (IsGlobalFunctionScope)
        {
            var rootGlobal = GetRootGlobalObject();
            if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
            {
                value = propertyValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    private bool TryGetInternal(Symbol name, HashSet<JsEnvironment> visited, out object? value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                if (current.IsGlobalFunctionScope)
                {
                    var globalObject = current.GetRootGlobalObject();
                    if (globalObject is not null &&
                        globalObject.TryGetProperty(name.Name, out var globalValue))
                    {
                        value = globalValue;
                        return true;
                    }
                }

                value = binding.Value;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride) &&
                current._varEnvironmentOverride.TryGetInternal(name, visited, out value))
            {
                return true;
            }

            if (current._withObject is not null && TryGetFromWith(current._withObject, name, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        if (IsGlobalFunctionScope)
        {
            var rootGlobal = GetRootGlobalObject();
            if (rootGlobal is not null && rootGlobal.TryGetProperty(name.Name, out var propertyValue))
            {
                value = propertyValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    internal bool TryFindBinding(Symbol name, out JsEnvironment environment, out object? value)
    {
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                environment = current;
                value = binding.Value;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride) &&
                current._varEnvironmentOverride.TryFindBindingInternal(name, visited, out environment, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        environment = null!;
        value = null;
        return false;
    }

    private bool TryFindBindingInternal(Symbol name, HashSet<JsEnvironment> visited, out JsEnvironment environment,
        out object? value)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (!visited.Add(current))
            {
                current = current.Enclosing;
                continue;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                if (ReferenceEquals(binding.Value, Uninitialized))
                {
                    throw new InvalidOperationException($"ReferenceError: {name.Name} is not defined");
                }

                environment = current;
                value = binding.Value;
                return true;
            }

            if (current._varEnvironmentOverride is not null &&
                current._varEnvironmentOverride != current &&
                !visited.Contains(current._varEnvironmentOverride) &&
                current._varEnvironmentOverride.TryFindBindingInternal(name, visited, out environment, out value))
            {
                return true;
            }

            current = current.Enclosing;
        }

        environment = null!;
        value = null;
        return false;
    }

    public void Assign(Symbol name, object? value)
    {
        // Remember if we're in strict mode at the call site
        var isStrictContext = IsStrict;
        var visited = new HashSet<JsEnvironment>(ReferenceEqualityComparer.Instance);
        AssignInternal(name, value, isStrictContext, visited);
    }

    private void AssignInternal(Symbol name, object? value, bool isStrictContext,
        HashSet<JsEnvironment> visited)
    {
        if (!visited.Add(this))
        {
            if (Enclosing is not null)
            {
                Enclosing.AssignInternal(name, value, isStrictContext, visited);
            }
            return;
        }

        JsObject? globalObject = null;
        if (IsGlobalFunctionScope)
        {
            globalObject = GetRootGlobalObject();
        }
        var realm = RealmState ?? Enclosing?.RealmState;

        if (_values.TryGetValue(name, out var binding))
        {
            if (ReferenceEquals(binding.Value, Uninitialized) &&
                binding.IsLexical &&
                !Symbol.Equals(name, Symbol.This))
            {
                throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
            }

            if (binding.IsConst)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError($"Cannot reassign constant '{name.Name}'.",
                    realm: realm));
            }

            if (binding.IsGlobalConstant)
            {
                if (isStrictContext)
                {
                    throw new ThrowSignal(
                        StandardLibrary.CreateTypeError($"ReferenceError: {name.Name} is not writable",
                            realm: realm));
                }

                return;
            }

            binding.Value = value;
            if (!binding.IsLexical)
            {
                globalObject?.SetProperty(name.Name, value);
            }
            NotifyBindingObservers(name, value);
            return;
        }

        if (_varEnvironmentOverride is not null &&
            _varEnvironmentOverride != this &&
            !visited.Contains(_varEnvironmentOverride))
        {
            _varEnvironmentOverride.AssignInternal(name, value, isStrictContext, visited);
            return;
        }

        if (_withObject is not null && HasVisibleWithBinding(_withObject, name))
        {
            if (_withObject is JsObject withObject)
            {
                AssignmentReferenceResolver.AssignObjectProperty(withObject, name.Name, value, isStrictContext, null,
                    realm);
            }
            else
            {
                _withObject.SetProperty(name.Name, value);
            }

            return;
        }

        if (Enclosing is not null)
        {
            Enclosing.AssignInternal(name, value, isStrictContext, visited);
            return;
        }

        if (globalObject is not null && globalObject.GetOwnPropertyDescriptor(name.Name) is not null)
        {
            AssignmentReferenceResolver.AssignObjectProperty(globalObject, name.Name, value, isStrictContext, null,
                realm);
            return;
        }

        // Reached the global scope without finding the variable
        // In strict mode, assignment to undefined variable is an error
        // In non-strict mode, create the variable as a global
        var functionScope = GetFunctionScope();
        if (functionScope.HasBodyLexicalName(name) && functionScope.HasOwnLexicalBinding(name))
        {
            RealmState?.Logger?.LogInformation(
                "AssignUnresolvable blocked by body lexical name={Name} strict={Strict} env={Env}",
                name.Name, isStrictContext, GetHashCode());
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        if (isStrictContext)
        {
            // Use ReferenceError message format
            throw StandardLibrary.ThrowReferenceError($"ReferenceError: {name.Name} is not defined", null, realm);
        }

        // Non-strict mode: Create the variable in the global scope (this environment)
        Define(name, value);
        LogRealm("Assign created global via sloppy assignment name={Name} valueType={ValueType}", name.Name,
            value?.GetType().Name ?? "null");
        globalObject?.SetProperty(name.Name, value);
    }

    internal DeleteBindingResult DeleteBinding(Symbol name)
    {
        var current = this;
        var hops = 0;
        const int maxLookupDepth = 10_000;

        while (current is not null && hops++ < maxLookupDepth)
        {
            if (current._withObject is not null && HasVisibleWithBinding(current._withObject, name))
            {
                return current._withObject.Delete(name.Name)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            if (current._values.TryGetValue(name, out var binding))
            {
                return current.TryDeleteDeclarativeBinding(name, binding)
                    ? DeleteBindingResult.Deleted
                    : DeleteBindingResult.NotDeletable;
            }

            current = current.Enclosing;
        }

        var globalObject = GetRootGlobalObject();
        var descriptor = globalObject?.GetOwnPropertyDescriptor(name.Name);
        if (descriptor != null)
        {
            if (!descriptor.Configurable)
            {
                return DeleteBindingResult.NotDeletable;
            }

            globalObject.Delete(name.Name);
            return DeleteBindingResult.Deleted;
        }

        return DeleteBindingResult.NotFound;
    }

    private bool TryDeleteDeclarativeBinding(Symbol name, Binding binding)
    {
        if (binding.IsLexical || binding.IsConst || binding.IsGlobalConstant || binding.BlocksFunctionScopeOverride)
        {
            return false;
        }

        if (binding.CanDelete)
        {
            _values.Remove(name);
            return true;
        }

        if (IsFunctionScope)
        {
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
            if (descriptor is not null && !descriptor.Configurable)
            {
                return false;
            }

            globalObject.Delete(name.Name);
            _values.Remove(name);
            return true;
        }

        return false;
    }

    private JsObject? GetRootGlobalObject()
    {
        var current = this;
        var hops = 0;
        const int maxDepth = 10_000;
        while (current.Enclosing is not null && hops++ < maxDepth)
        {
            current = current.Enclosing;
        }

        if (current._values.TryGetValue(Symbol.This, out var thisBinding) &&
            thisBinding.Value is JsObject globalObject)
        {
            return globalObject;
        }

        return null;
    }

    private void LogRealm(string message, params object?[] args)
    {
        RealmState?.Logger?.LogInformation(message, args);
    }

    private static bool IsBlockedByUnscopables(IJsObjectLike target, string name, out bool touchedUnscopables)
    {
        touchedUnscopables = false;
        var unscopablesSymbol = TypedAstSymbol.For("Symbol.unscopables");
        var key = $"@@symbol:{unscopablesSymbol.GetHashCode()}";
        if (target.TryGetProperty(key, out var unscopables))
        {
            touchedUnscopables = true;
            if (unscopables is IJsPropertyAccessor accessor &&
                JsOps.TryGetPropertyValue(accessor, name, out var blocked) && JsOps.ToBoolean(blocked))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFromWith(IJsObjectLike target, Symbol name, out object? value)
    {
        var propertyName = name.Name;
        if (string.IsNullOrEmpty(propertyName))
        {
            value = null;
            return false;
        }

        if (!HasProperty(target, propertyName))
        {
            value = null;
            return false;
        }

        if (IsBlockedByUnscopables(target, propertyName, out _))
        {
            value = null;
            return false;
        }

        if (target.TryGetProperty(propertyName, out var propertyValue))
        {
            value = propertyValue;
            return true;
        }

        return target.TryGetProperty(propertyName, target, out value);
    }

    private static bool HasVisibleWithBinding(IJsObjectLike target, Symbol name)
    {
        return TryResolveObjectBinding(target, name, out _, out _);
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

        JsObject? jsObject = null;
        PropertyDescriptor? originalDescriptor = null;
        if (target is JsObject candidate)
        {
            jsObject = candidate;
            originalDescriptor = candidate.GetOwnPropertyDescriptor(propertyName);
        }

        var touchedUnscopables = false;
        if (IsBlockedByUnscopables(target, propertyName, out touchedUnscopables))
        {
            return false;
        }

        if (touchedUnscopables && jsObject is not null && originalDescriptor is not null)
        {
            var currentDescriptor = jsObject.GetOwnPropertyDescriptor(propertyName);
            if (currentDescriptor is null)
            {
                allowMissingAssignment = true;
            }
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

        var prototype = target.Prototype;
        while (prototype is not null)
        {
            if (prototype.HasProperty(name))
            {
                return true;
            }

            prototype = prototype.Prototype;
        }

        return false;
    }

    internal static object? GetWithBindingValue(in ObjectEnvironmentBinding binding)
    {
        var propertyName = binding.PropertyName;
        if (!HasProperty(binding.BindingObject, propertyName))
        {
            if (binding.IsStrictReference)
            {
                throw new InvalidOperationException($"ReferenceError: {propertyName} is not defined");
            }

            return Symbol.Undefined;
        }

        return JsOps.TryGetPropertyValue(binding.BindingObject, propertyName, out var value)
            ? value
            : Symbol.Undefined;
    }

    internal static bool TrySetWithBindingValue(in ObjectEnvironmentBinding binding, object? value)
    {
        var propertyName = binding.PropertyName;
        var bindingObject = binding.BindingObject;
        var stillExists = HasProperty(bindingObject, propertyName);
        if (!stillExists && !binding.AllowMissingAssignment)
        {
            if (binding.IsStrictReference)
            {
                throw new InvalidOperationException($"ReferenceError: {propertyName} is not defined");
            }
        }

        JsOps.AssignPropertyValueByName(bindingObject, propertyName, value);

        var ownDescriptor = bindingObject.GetOwnPropertyDescriptor(propertyName);
        if (ownDescriptor is null)
        {
            return true;
        }

        if (bindingObject is not IPropertyDefinitionHost definitionHost)
        {
            return true;
        }

        if (!ownDescriptor.IsDataDescriptor)
        {
            return true;
        }

        var descriptorClone = ownDescriptor.Clone();
        descriptorClone.Value = value;
        definitionHost.TryDefineProperty(propertyName, descriptorClone);
        return true;
    }

    internal void AddBindingObserver(Symbol symbol, Action<object?> observer)
    {
        _bindingObservers ??= new Dictionary<Symbol, List<Action<object?>>>(ReferenceEqualityComparer<Symbol>.Instance);
        if (!_bindingObservers.TryGetValue(symbol, out var list))
        {
            list = [];
            _bindingObservers[symbol] = list;
        }

        list.Add(observer);
    }

    private void NotifyBindingObservers(Symbol symbol, object? value)
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
        return scope._values.TryGetValue(name, out var binding) && !binding.IsLexical;
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
    public Dictionary<string, object?> GetAllVariables()
    {
        var result = new Dictionary<string, object?>();

        // Traverse up the scope chain
        var current = this;
        while (current is not null)
        {
            // Add variables from current scope (only if not already present from inner scope)
            foreach (var kvp in current._values)
            {
                if (!result.ContainsKey(kvp.Key.Name))
                {
                    result[kvp.Key.Name] = kvp.Value.Value;
                }
            }

            current = current.Enclosing;
        }

        return result;
    }

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

    private sealed class Binding(
        object? value,
        bool isConst,
        bool isGlobalConstant,
        bool isLexical,
        bool blocksFunctionScopeOverride,
        bool canDelete)
    {
        public object? Value { get; set; } = value;

        public bool IsConst { get; } = isConst;

        public bool IsGlobalConstant { get; } = isGlobalConstant;

        public bool IsLexical { get; private set; } = isLexical;

        public bool BlocksFunctionScopeOverride { get; private set; } = blocksFunctionScopeOverride;

        public bool CanDelete { get; private set; } = canDelete;

        public void UpgradeLexical(bool isLexical, bool blocksFunctionScopeOverride)
        {
            if (isLexical)
            {
                IsLexical = true;
            }

            if (blocksFunctionScopeOverride)
            {
                BlocksFunctionScopeOverride = true;
            }
        }
    }
}

internal enum DeleteBindingResult
{
    NotFound,
    Deleted,
    NotDeletable
}

internal readonly record struct ObjectEnvironmentBinding(
    IJsObjectLike BindingObject,
    string PropertyName,
    bool IsStrictReference,
    bool AllowMissingAssignment);
