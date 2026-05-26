#region

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Compatibility overloads remain for dynamic/resume seams; not proof of direct runner AST fallback.

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
            // Track active catch parameters while hoisting (Annex B.3.5/B.3.3.3); start empty.
            var catchParameterNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
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
            var needsArgumentsBinding = !_function.IsArrow && NeedsArgumentsBinding(_function);
            HashSet<Symbol>? blockedFunctionVarNames = null;
            if (!_isStrict)
            {
                var catchParameterNamesRaw = hoistPlan.CatchParameterNames;
                blockedFunctionVarNames = bodyLexicalNames.Count == 0
                    ? new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance)
                    : new HashSet<Symbol>(bodyLexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
                foreach (var parameterName in parameterNames)
                {
                    blockedFunctionVarNames.Add(parameterName);
                }

                // B.3.5: non-simple catch parameters (destructured) block AnnexB hoisting.
                foreach (var cn in catchParameterNamesRaw)
                {
                    if (!simpleCatchParameterNames.Contains(cn))
                    {
                        blockedFunctionVarNames.Add(cn);
                    }
                }

                // Annex B legacy var-style hoisting is skipped when the function has
                // parameter expressions. Block all AnnexB block-function names.
                if (hasParameterExpressions)
                {
                    foreach (var blockedName in CollectAnnexBBlockFunctionNames(_function.Body))
                    {
                        blockedFunctionVarNames.Add(blockedName);
                    }
                }
            }

            // Per spec step 22.f: when argumentsObjectNeeded, "arguments" blocks AnnexB hoisting
            var argumentsIsParam = parameterNames.Contains(Symbol.Arguments);
            var argumentsInBodyLex = bodyLexicalNames.Contains(Symbol.Arguments) &&
                                     !simpleCatchParameterNames.Contains(Symbol.Arguments);
            var canSkipForBodyDecl = !hasParameterExpressions && argumentsInBodyLex;
            var argumentsObjectNeededBySpec = !argumentsIsParam && !canSkipForBodyDecl;
            var needsArgumentsObject = !_function.IsArrow && argumentsObjectNeededBySpec && needsArgumentsBinding;
            if (needsArgumentsObject)
            {
                blockedFunctionVarNames?.Add(Symbol.Arguments);
            }

            JsEnvironment parameterEnvironment;
            JsEnvironment varEnvironment;
            var functionEnvironment = JsEnvironment.CreateInstance(_closure, true, _isStrict, _function.Source,
                description);
            functionEnvironment.IsArrowFunctionEnvironment = _function.IsArrow;
            if (hasParameterExpressions)
            {
                parameterEnvironment = JsEnvironment.CreateInstance(functionEnvironment, false, _isStrict, _function.Source,
                    description, isParameterEnvironment: true);
                parameterEnvironment.IsArrowFunctionEnvironment = _function.IsArrow;

                varEnvironment = JsEnvironment.CreateInstance(parameterEnvironment, true, _isStrict, _function.Source,
                    description);
                varEnvironment.IsArrowFunctionEnvironment = _function.IsArrow;
            }
            else
            {
                parameterEnvironment = functionEnvironment;
                varEnvironment = functionEnvironment;
            }

            var isArrowFunction = _function.IsArrow;
            var isDerivedClassConstructor = _callable is SyncFunctionInvoker { IsDerivedClassConstructor: true };
            functionEnvironment.InitializeSlotsWithCapacity(0,
                ComputeFunctionEnvironmentMinimumCapacity(
                    parameterNames.Length,
                    needsArgumentsObject,
                    hasParameterExpressions,
                    isArrowFunction,
                    isDerivedClassConstructor));
            if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
            {
                parameterEnvironment.InitializeSlotsWithCapacity(0,
                    ComputeParameterEnvironmentMinimumCapacity(
                        parameterNames.Length,
                        needsArgumentsObject));
            }

            var executionEnvironment = JsEnvironment.CreateInstance(varEnvironment, false, _isStrict,
                _function.Source, description, isBodyEnvironment: true);
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);

            // Store names that block Annex B.3.3 function-scope hoisting so runtime
            // HandleFunctionDeclaration can skip the var-binding update for these names.
            if (blockedFunctionVarNames is { Count: > 0 })
            {
                varEnvironment.SetAnnexBBlockedNames(blockedFunctionVarNames);
            }

            // Initialize slots for generator-internal variables (iterator states, values, etc.) FIRST.
            // This must happen BEFORE hoisting lexical bindings because the IR uses 0-based slot indices.
            // Plan slots get indices 0, 1, 2... and hoisted lexical bindings get subsequent indices.
            // This enables O(1) slot-based access instead of dictionary lookups.
            // Use the plan's RootScopeId for all execution plan slots.
            if (_plan?.ActivationSlots is { } activationSlots)
            {
                var requiredSlots = activationSlots.SlotCount;
                if (requiredSlots <= 0 && activationSlots.SlotNames.Length > 0)
                {
                    requiredSlots = activationSlots.SlotNames[^1].SlotIndex + 1;
                }

                var scopeLexicals = _plan.SafeScopeLexicalBindings;
                var rootLexicals = _plan.SafeRootLexicalBindings;
                if (rootLexicals.Count == 0 && scopeLexicals.TryGetValue(activationSlots.ScopeId, out var fromRoot))
                {
                    rootLexicals = fromRoot;
                }

                executionEnvironment.ResetSlotLayoutForPlan(
                    requiredSlots,
                    activationSlots.SlotMap,
                    rootLexicals,
                    _plan.SlotSymbols,
                    activationSlots.LayoutId,
                    activationSlots.ScopeId,
                    activationSlots.SlotNames);
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
                var isConst = lexicalDeclarationKinds.TryGetValue(lexicalName, out var c) && c;
                executionEnvironment.DefineJsValue(lexicalName, JsValue.Uninitialized, isConst: isConst,
isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            // Store YieldResumeContext reference in the environment for yield expressions
            var yieldState = YieldStateRef;

            var generatorContext = _realmState.CreateContext(
                ScopeKind.Function,
                DetermineGeneratorScopeMode());
            generatorContext.InGeneratorContext = _isGenerator;
            using var capturedPrivateScopes = !_capturedPrivateNameScopes.IsDefaultOrEmpty
                ? generatorContext.EnterPrivateNameScopes(_capturedPrivateNameScopes)
                : null;
            using var privateScope = _privateNameScope is not null
                ? generatorContext.EnterPrivateNameScope(_privateNameScope)
                : null;

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

            var arrowThisInitialized = true;
            if (isArrowFunction)
            {
                var lexicalThis = _thisValue;
                if (_lexicalThisEnvironment is not null &&
                    _lexicalThisEnvironment.TryFindBindingJsValue(Symbol.This, true, out _, out var lexicalEnvThis))
                {
                    lexicalThis = lexicalEnvThis;
                }

                arrowThisInitialized = !lexicalThis.IsUninitialized;
                boundThis = arrowThisInitialized ? lexicalThis : JsValue.Undefined;
            }

            var thisValueForEnvironment = isArrowFunction
                ? boundThis
                : isDerivedClassConstructor
                    ? JsValue.Uninitialized
                    : boundThis;
            var thisInitialized = isArrowFunction ? arrowThisInitialized : !isDerivedClassConstructor;

            functionEnvironment.DefineJsValue(Symbol.This, thisValueForEnvironment);
            functionEnvironment.SetThisInitializationStatus(thisInitialized);
            if (thisInitialized)
            {
                generatorContext.MarkThisInitialized();
            }
            else
            {
                generatorContext.MarkThisUninitialized();
            }

            // For arrow functions with captured lexical this environment, define LexicalThisEnvironment
            // so super() calls can update the correct this binding in the original constructor
            if (isArrowFunction && _lexicalThisEnvironment is not null)
            {
                functionEnvironment.DefineJsValue(Symbol.LexicalThisEnvironment,
                    JsValue.FromObjectUnsafe(_lexicalThisEnvironment));
            }

            // Define new.target for non-arrow functions so inner arrow functions can access it lexically
            if (!isArrowFunction)
            {
                var newTargetValue = _currentNewTarget.IsUndefined ? JsValue.Undefined : _currentNewTarget;
                functionEnvironment.DefineJsValue(Symbol.NewTarget, newTargetValue, true, isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
                functionEnvironment.DefineJsValue(Symbol.ActiveFunction, JsValue.FromObjectUnsafe(_callable), true,
                    isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            functionEnvironment.DefineJsValue(Symbol.YieldResumeContextSymbol,
                JsValue.FromObjectUnsafe(yieldState.ResumeContext));
            functionEnvironment.DefineJsValue(Symbol.GeneratorInstanceSymbol, JsValue.FromObjectUnsafe(this));

            var superPrototype = (_homeObject as IPrototypeAccessorProvider)?.PrototypeAccessor ??
                                 _homeObject?.Prototype;
            superPrototype ??= _superPrototype;
            if (superPrototype is null && boundThis.TryGetObject<JsObject>(out var thisObj))
            {
                superPrototype = thisObj.PrototypeAccessor ?? thisObj.Prototype;
            }

            var superConstructor = _superConstructor ?? superPrototype as IJsEnvironmentAwareCallable;
            if (superConstructor is not null || superPrototype is not null)
            {
                var superBinding = new SuperBinding(
                    superConstructor,
                    superPrototype,
                    isArrowFunction
                        ? boundThis
                        : isDerivedClassConstructor
                            ? JsValue.Undefined
                            : boundThis,
                    thisInitialized);
                functionEnvironment.DefineJsValue(Symbol.Super, JsValue.FromObjectUnsafe(superBinding));
            }

            // Per ES spec 10.2.11 step 18: Arrow functions don't have their own arguments object.
            // They inherit `arguments` from the lexically enclosing function.
            if (needsArgumentsObject)
            {
                var argumentsObject = _function.CreateArgumentsObject(_arguments, executionEnvironment, _realmState,
                    _callable,
                    _isStrict);
                executionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexicalBinding: false);
                parameterEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                    isLexicalBinding: false);
                if (!ReferenceEquals(parameterEnvironment, functionEnvironment))
                {
                    functionEnvironment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject),
                        isLexicalBinding: false);
                }
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

            if (((IAstCacheable<HoistableDeclarationsPlan>)_function.Body).GetOrCreateCache().HasHoistableDeclarations)
            {
                simpleCatchParameterNames.Clear();
                _function.Body.HoistVarDeclarations(executionEnvironment, generatorContext,
                    lexicalNames: lexicalNames,
                    catchParameterNames: catchParameterNames,
                    simpleCatchParameterNames: simpleCatchParameterNames);
            }

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

        private int ComputeFunctionEnvironmentMinimumCapacity(
            int parameterCount,
            bool needsArgumentsObject,
            bool hasParameterExpressions,
            bool isArrowFunction,
            bool isDerivedClassConstructor)
        {
            var capacity = 2; // this + this-initialized marker.

            if (!isArrowFunction)
            {
                capacity += 2; // new.target + active function.
            }

            capacity += 2; // yield resume context + runner instance.

            if (isArrowFunction && _lexicalThisEnvironment is not null)
            {
                capacity++;
            }

            if (_homeObject is not null ||
                _superConstructor is not null ||
                _superPrototype is not null ||
                isDerivedClassConstructor)
            {
                capacity++;
            }

            if (!hasParameterExpressions)
            {
                if (needsArgumentsObject)
                {
                    capacity++;
                }

                if (_function.Name is not null && !_hasFunctionNameEnvironment)
                {
                    capacity++;
                }

                capacity += parameterCount;
            }

            return capacity;
        }

        private int ComputeParameterEnvironmentMinimumCapacity(
            int parameterCount,
            bool needsArgumentsObject)
        {
            var capacity = parameterCount;
            if (needsArgumentsObject)
            {
                capacity++;
            }

            if (_function.Name is not null && !_hasFunctionNameEnvironment)
            {
                capacity++;
            }

            return capacity;
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
            if (_executionEnvironment is null)
            {
                _executionEnvironment = CreateExecutionEnvironment();
                LogRootScopeIdOnce();
            }

            EnsureFlatSlotsInitialized(_executionEnvironment);
            return _executionEnvironment;
        }

        private void EnsureFlatSlotsInitialized(JsEnvironment executionEnvironment)
        {
            // Initialize and populate flat slots for the root scope and closure chain
            if (_plan is null || _plan.FlatSlotCount <= 0 || _flatSlots is not null)
            {
                return;
            }

            _flatSlots = new JsVariable[_plan.FlatSlotCount];
            AssertFlatSlotsInitialized();
            PopulateFlatSlotsForScope(_plan.RootScopeId, executionEnvironment);

            // Walk closure chain to populate flat slots for captured variables
            var closureEnv = executionEnvironment.Enclosing;
            while (closureEnv is not null)
            {
                PopulateFlatSlotsForScope(closureEnv.ScopeId, closureEnv);
                closureEnv = closureEnv.Enclosing;
            }
        }

        internal JsEnvironment GetOrCreateExecutionEnvironmentForInternalUse()
        {
            return EnsureExecutionEnvironment();
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
                _context.InGeneratorContext = _isGenerator;
            }
            else
            {
                _context.Clear();
                _context.InGeneratorContext = _isGenerator;
                // Ensure the scope frame reflects the function's strictness.
                // The AST path may have popped the initial scope frame, or Clear()
                // may have been called after previous use. Re-push to guarantee
                // context.CurrentScope.IsStrict matches _isStrict.
                if (_context.CurrentScope.IsStrict != _isStrict)
                {
                    _context.PushScope(ScopeKind.Function, DetermineGeneratorScopeMode());
                }
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

        private static HashSet<Symbol> CollectAnnexBBlockFunctionNames(BlockStatement body)
        {
            var result = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);
            foreach (var statement in body.Statements)
            {
                CollectFromStatement(statement, result, body.IsStrict, inBlockScope: false);
            }

            return result;

            static void CollectFromStatement(StatementNode statement, HashSet<Symbol> sink, bool isStrict, bool inBlockScope)
            {
                while (true)
                {
                    switch (statement)
                    {
                        case FunctionDeclaration functionDeclaration:
                            if (!isStrict && inBlockScope &&
                                !functionDeclaration.Function.IsAsync &&
                                !functionDeclaration.Function.WasAsync &&
                                !functionDeclaration.Function.IsGenerator)
                            {
                                sink.Add(functionDeclaration.Name);
                            }

                            return;
                        case BlockStatement block:
                            foreach (var inner in block.Statements)
                            {
                                CollectFromStatement(inner, sink, isStrict, inBlockScope: true);
                            }

                            return;
                        case IfStatement ifStatement:
                            CollectFromStatement(ifStatement.Then, sink, isStrict, inBlockScope: true);
                            if (ifStatement.Else is { } elseBranch)
                            {
                                statement = elseBranch;
                                continue;
                            }

                            return;
                        case WhileStatement whileStatement:
                            statement = whileStatement.Body;
                            continue;
                        case DoWhileStatement doWhileStatement:
                            statement = doWhileStatement.Body;
                            continue;
                        case WithStatement withStatement:
                            statement = withStatement.Body;
                            continue;
                        case ForStatement forStatement:
                            statement = forStatement.Body;
                            continue;
                        case ForEachStatement forEachStatement:
                            statement = forEachStatement.Body;
                            continue;
                        case SwitchStatement switchStatement:
                            foreach (var switchCase in switchStatement.Cases)
                            {
                                CollectFromStatement(switchCase.Body, sink, isStrict, inBlockScope: true);
                            }

                            return;
                        case TryStatement tryStatement:
                            CollectFromStatement(tryStatement.TryBlock, sink, isStrict, inBlockScope: true);
                            if (tryStatement.Catch?.Body is { } catchBody)
                            {
                                CollectFromStatement(catchBody, sink, isStrict, inBlockScope: true);
                            }

                            if (tryStatement.Finally is { } finallyBlock)
                            {
                                CollectFromStatement(finallyBlock, sink, isStrict, inBlockScope: true);
                            }

                            return;
                        case LabeledStatement labeledStatement:
                            statement = labeledStatement.Statement;
                            continue;
                        default:
                            return;
                    }
                }
            }
        }
    }
}
