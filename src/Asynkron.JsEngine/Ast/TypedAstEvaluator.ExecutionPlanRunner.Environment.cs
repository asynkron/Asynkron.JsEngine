#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private JsEnvironment CreateExecutionEnvironment()
        {
            var description = _function.Name is { } name
                ? $"function* {name.Name}"
                : "generator function";

            var hasParameterExpressions = _function.HasParameterExpressions();
            var hoistPlan = ((IAstCacheable<HoistPlan>)_function.Body).GetOrCreateCache();
            var lexicalNamesRaw = hoistPlan.LexicalNames;
            var lexicalNames = lexicalNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(lexicalNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var catchParameterNamesRaw = hoistPlan.CatchParameterNames;
            var catchParameterNames = catchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(catchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var simpleCatchParameterNamesRaw = hoistPlan.SimpleCatchParameterNames;
            var simpleCatchParameterNames = simpleCatchParameterNamesRaw.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(simpleCatchParameterNamesRaw, ReferenceEqualityComparer<Symbol>.Instance);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);

            var parameterNames = ((IAstCacheable<FunctionParameterNamesPlan>)_function).GetOrCreateCache()
                .ParameterNames;
            var blockedFunctionVarNames = bodyLexicalNames.Count == 0
                ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                : new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var parameterName in parameterNames)
            {
                blockedFunctionVarNames.Add(parameterName);
            }

            JsEnvironment parameterEnvironment;
            JsEnvironment varEnvironment;
            var functionEnvironment = new JsEnvironment(_closure, true, _isStrict, _function.Source,
                description);
            if (hasParameterExpressions)
            {
                parameterEnvironment = new JsEnvironment(functionEnvironment, false, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);

                varEnvironment = new JsEnvironment(parameterEnvironment, true, _isStrict, _function.Source,
                    description);
            }
            else
            {
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var executionEnvironment = new JsEnvironment(varEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // Initialize slots for generator-internal variables (iterator states, values, etc.) FIRST.
            // This must happen BEFORE hoisting lexical bindings because the IR uses 0-based slot indices.
            // Plan slots get indices 0, 1, 2... and hoisted lexical bindings get subsequent indices.
            // This enables O(1) slot-based access instead of dictionary lookups.
            // Use the plan's RootScopeId for all execution plan slots.
            if (_plan is { SlotCount: > 0, SlotSymbols.IsDefaultOrEmpty: false })
            {
                // Ensure we allocate enough slots to cover:
                // - Internal plan slots (SlotSymbols.Length)
                // - Root slot map entries (indices can be sparse)
                // - Explicit RootSlotCount from analysis (if present)
                var rootSlotMap = _plan.SafeRootSlotMap;
                var mapMax = rootSlotMap.Count > 0 ? rootSlotMap.Values.Max() + 1 : 0;
                var requiredSlots = Math.Max(Math.Max(_plan.RootSlotCount, _plan.SlotSymbols.Length), mapMax);
                if (requiredSlots == 0)
                {
                    requiredSlots = _plan.SlotCount;
                }

                var scopeLexicals = _plan.SafeScopeLexicalBindings;
                var rootLexicals = _plan.SafeRootLexicalBindings;
                if (rootLexicals.Count == 0 && scopeLexicals.TryGetValue(_plan.RootScopeId, out var fromRoot))
                {
                    rootLexicals = fromRoot;
                }

                executionEnvironment.ResetSlotLayoutForPlan(
                    requiredSlots,
                    rootSlotMap,
                    rootLexicals,
                    _plan.SlotSymbols,
                    _plan.LayoutId,
                    _plan.RootScopeId);
            }

            // ES2024 9.2.12 FunctionDeclarationInstantiation step 34-35:
            // Create TDZ bindings for lexical declarations (let/const) in the function environment.
            // This must happen BEFORE the body is evaluated so that closures that reference these
            // variables will find them in TDZ state and throw ReferenceError if accessed before initialization.
            // NOTE: We use TopLevelLexicalNames which excludes for-loop/for-of initializer variables
            // (those create their own per-iteration environments and should NOT be in function TDZ).
            // These bindings are added AFTER plan slots so they don't conflict with 0-based IR indices.
            var topLevelLexicalNames = hoistPlan.TopLevelLexicalNames;
            var lexicalDeclarationKinds = hoistPlan.LexicalDeclarationKinds;
            foreach (var lexicalName in topLevelLexicalNames)
            {
                if (!executionEnvironment.HasBinding(lexicalName))
                {
                    var isConst = lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                    executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isLexicalBinding: true,
                        blocksFunctionScopeOverride: true, isConst: isConst);
                }
            }

            // Store YieldResumeContext reference in the environment for yield expressions
            var yieldState = YieldStateRef;

            var generatorContext = _realmState.CreateContext(
                ScopeKind.Function,
                DetermineGeneratorScopeMode());

            var boundThis = _thisValue;
            if (!_isStrict)
            {
                if (boundThis.IsNullish)
                {
                    boundThis = _realmState.Engine?.GlobalObject is { } go ? new JsValue(go) : JsValue.Undefined;
                }

                if (boundThis.IsNull)
                {
                    boundThis = new JsValue(new JsObject { RealmState = _realmState });
                }
                else if (!boundThis.TryGetObject<IJsPropertyAccessor>(out _) &&
                         !boundThis.IsNullish &&
                         !boundThis.TryGetObject<IIsHtmlDda>(out _))
                {
                    boundThis = JsValue.FromObjectUnsafe(ToObjectForDestructuringJsValue(boundThis, generatorContext));
                }
            }

            functionEnvironment.DefineJsValue(Symbol.This, boundThis);

            // For arrow functions with captured lexical this environment, define LexicalThisEnvironment
            // so super() calls can update the correct this binding in the original constructor
            if (_function.IsArrow && _lexicalThisEnvironment is not null)
            {
                functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                    JsValue.FromObjectUnsafe(_lexicalThisEnvironment));
            }

            // Define new.target for non-arrow functions so inner arrow functions can access it lexically
            if (!_function.IsArrow)
            {
                var newTargetValue = _newTarget.IsUndefined ? JsValue.Undefined : _newTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            functionEnvironment.DefineJsValue(Symbol.YieldResumeContextSymbol,
                JsValue.FromObjectUnsafe(yieldState.ResumeContext));
            functionEnvironment.DefineJsValue(Symbol.GeneratorInstanceSymbol, JsValue.FromObjectUnsafe(this));

            var superPrototype = _homeObject?.Prototype;
            if (superPrototype is null && boundThis.TryGetObject<JsObject>(out var thisObj))
            {
                superPrototype = thisObj.Prototype;
            }

            if (superPrototype is not null)
            {
                var superBinding = new SuperBinding(null, superPrototype, boundThis, true);
                functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(superBinding));
            }

            var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
                _callable,
                _isStrict);
            parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                isLexicalBinding: false);
            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
            {
                functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexicalBinding: false);
            }

            if (_function.Name is { } functionName && !_hasFunctionNameEnvironment)
            {
                parameterEnvironment.DefineJsValue(functionName, JsValue.FromObjectUnsafe(_callable), true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            // ES spec order: bind parameters FIRST, then hoist function declarations
            // Function declarations should override parameter bindings with the same name
            _function.BindFunctionParameters(_arguments, parameterEnvironment, generatorContext);
            if (generatorContext.IsThrow)
            {
                var thrown = generatorContext.FlowValue;
                generatorContext.Clear();
                throw new ThrowSignal(thrown);
            }

            SyncParameterSlotsToPlan(executionEnvironment, parameterEnvironment, parameterNames);

            _function.Body.HoistVarDeclarations(executionEnvironment, generatorContext,
                lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            if (generatorContext.IsThrow)
            {
                var thrown = generatorContext.FlowValue;
                generatorContext.Clear();
                throw new ThrowSignal(thrown);
            }

            if (generatorContext.IsReturn)
            {
                generatorContext.ClearReturn();
            }

            return executionEnvironment;
        }

        private static void SyncParameterSlotsToPlan(
            JsEnvironment executionEnvironment,
            JsEnvironment parameterEnvironment,
            ImmutableArray<Symbol> parameterNames)
        {
            if (parameterNames.IsDefaultOrEmpty || executionEnvironment._slots is null)
            {
                return;
            }

            foreach (var name in parameterNames)
            {
                if (!executionEnvironment.TryGetSlotIndex(name, out var slotIndex))
                {
                    continue;
                }

                var value = parameterEnvironment.GetJsValue(name);
                executionEnvironment.SetSlotDirect(slotIndex, value);
            }
        }

        private JsEnvironment EnsureExecutionEnvironment()
        {
            if (_executionEnvironment is not null)
            {
                return _executionEnvironment;
            }

            _executionEnvironment = CreateExecutionEnvironment();
            LogRootScopeIdOnce();

            // Initialize and populate flat slots for the root scope and closure chain
            if (_plan is null || _plan.FlatSlotCount <= 0 || _flatSlots is not null)
            {
                return _executionEnvironment;
            }

            _flatSlots = new JsVariable[_plan.FlatSlotCount];
            PopulateFlatSlotsForScope(_plan.RootScopeId, _executionEnvironment);

            // Walk closure chain to populate flat slots for captured variables
            var closureEnv = _executionEnvironment.Enclosing;
            while (closureEnv is not null)
            {
                PopulateFlatSlotsForScope(closureEnv.ScopeId, closureEnv);
                closureEnv = closureEnv.Enclosing;
            }

            return _executionEnvironment;
        }

        private void LogRootScopeIdOnce()
        {
            if (_rootScopeLogged || _realmState.Logger is null || _plan is null)
            {
                return;
            }

            _realmState.Logger.LogInformation(
                "ExecutionPlanRunner scopeId={RootScopeId}",
                JsEnvironment.FormatScopeIdForLog(_plan.RootScopeId));
            _rootScopeLogged = true;
        }

        internal EvaluationContext EnsureEvaluationContext()
        {
            if (_context is null)
            {
                _context = _realmState.CreateContext(
                    ScopeKind.Function,
                    DetermineGeneratorScopeMode());
            }
            else
            {
                _context.Clear();
            }

            _context.AllowIdentifierCache = _allowIdentifierCache;
            ApplyPrivateNameScopes();

            return _context;
        }

        private void ApplyPrivateNameScopes()
        {
            if (_privateScopesApplied || _context is null)
            {
                return;
            }

            if (!_capturedPrivateNameScopes.IsDefaultOrEmpty)
            {
                _context.EnterPrivateNameScopes(_capturedPrivateNameScopes);
            }

            if (_privateNameScope is not null)
            {
                _context.EnterPrivateNameScope(_privateNameScope);
            }

            _privateScopesApplied = true;
        }

        private ScopeMode DetermineGeneratorScopeMode()
        {
            return _isStrict ? ScopeMode.Strict : ScopeMode.Sloppy;
        }
    }
}
