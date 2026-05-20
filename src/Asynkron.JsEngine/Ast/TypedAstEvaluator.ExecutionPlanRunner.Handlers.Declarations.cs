#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.StdLib;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private sealed partial class ExecutionPlanRunner
    {
        private static InstructionResult HandleFunctionDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext ctx,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<FunctionDeclarationInstruction>(instr);

            // Block-scoped function declarations need to be instantiated at runtime.
            // Function-scoped declarations are hoisted (handled during function entry).
            if (instruction.Descriptor is { } funcDecl)
            {
                // Async/generator declarations stay block-scoped for ordinary sloppy switch/catch
                // Annex B paths, but eval declaration environments preserve existing eval semantics.
                var suppressAnnexBVarUpdate = (funcDecl.Function.IsAsync || funcDecl.Function.WasAsync ||
                                               funcDecl.Function.IsGenerator) &&
                                              !environment.IsEvalDeclarationEnvironment;

                // Pass skipInternalNameBinding: true so the function doesn't create an internal
                // const binding for its name (the binding is handled by environment.Define below).
                var functionValue = funcDecl.Function.CreateFunctionValue(environment, ctx,
                    skipInternalNameBinding: true,
                    planSeed: funcDecl.PlanSeed);
                var fnValueJs = JsValue.FromObjectUnsafe(functionValue);

                // Function declarations target the variable environment, which may be different
                // from the nearest function scope for sloppy eval.
                var varEnvironment = environment.GetVarEnvironment();
                var isAtVarEnvironment = ReferenceEquals(varEnvironment, environment) ||
                                         environment.IsEvalDeclarationEnvironment;

                var isHoistedUndefinedBinding = false;
                if (!suppressAnnexBVarUpdate && isAtVarEnvironment && !ctx.CurrentScope.IsStrict &&
                    varEnvironment.HasFunctionScopedBinding(funcDecl.Name))
                {
                    var existingValue = varEnvironment.GetBindingValueDirect(funcDecl.Name);
                    if (existingValue.IsUndefined)
                    {
                        isHoistedUndefinedBinding = true;
                    }
                }

                if (isAtVarEnvironment)
                {
                    if (isHoistedUndefinedBinding && !environment.IsAnnexBBlocked(funcDecl.Name))
                    {
                        varEnvironment.AssignJsValue(funcDecl.Name, fnValueJs);

                        if (varEnvironment.IsGlobalFunctionScope)
                        {
                            var globalThis = varEnvironment.GetRootGlobalObject();
                            globalThis?.SetProperty(funcDecl.Name.Name, fnValueJs);
                        }
                    }
                    // Function-scoped declarations were initialized during entry/instantiation.
                    // Runtime evaluation is a no-op unless we are filling a hoisted undefined
                    // binding for an Annex B branch-scoped function.
                }
                else
                {
                    // Use the var environment's strictness (not ctx.CurrentScope.IsStrict)
                    // to determine Annex B applicability. The evaluation context's scope
                    // mode may reflect the outer script's strict mode while the function
                    // itself is sloppy, causing incorrect blocking decisions.
                    var isBlocked = !varEnvironment.IsStrict &&
                                    (environment.IsAnnexBBlocked(funcDecl.Name) ||
                                     HasEnclosingLexicalBinding(environment.Enclosing, funcDecl.Name));

                    // Block-scoped: create lexical binding in the current block environment.
                    // Per B.3.3.1/B.3.3.2: when AnnexB hoisting is blocked (parameter/lexical
                    // conflict), only create the binding if we're in a real block environment,
                    // not the function body environment. In if-branches without an explicit block,
                    // the function declaration runs in the body environment -- skip the binding
                    // to avoid shadowing the parameter/lexical.
                    if (!isBlocked || !environment.IsBodyEnvironment)
                    {
                        environment.DefineJsValue(
                            funcDecl.Name,
                            fnValueJs,
                            isLexicalBinding: true,
                            blocksFunctionScopeOverride: true);
                    }

                    // For hoisted functions in sloppy mode, also update the var-scoped binding.
                    // This implements Annex B semantics where block-scoped functions also
                    // update the outer binding visible to the surrounding eval/function body.
                    if (!suppressAnnexBVarUpdate && !varEnvironment.IsStrict && !isBlocked)
                    {
                        // Update the var-scoped binding in the function scope.
                        ref var varBinding = ref varEnvironment.TryGetSlotRef(funcDecl.Name);
                        if (!Unsafe.IsNullRef(ref varBinding) && !varBinding.IsLexical)
                        {
                            varEnvironment.AssignJsValue(funcDecl.Name, fnValueJs);
                        }
                        else if (Unsafe.IsNullRef(ref varBinding))
                        {
                            varEnvironment.DefineFunctionScoped(
                                funcDecl.Name,
                                fnValueJs,
                                hasInitializer: true,
                                isFunctionDeclaration: true,
                                context: ctx);
                        }

                        // Also update any intermediate environments between the block and
                        // the function scope (e.g. the body environment) that have a slot
                        // for this name. The IR plan may allocate Annex B hoisted function
                        // names in the body environment for O(1) slot access; without this
                        // update, the IR read path sees the stale outer function value.
                        UpdateIntermediateVarBindings(
                            environment.Enclosing, varEnvironment, funcDecl.Name, fnValueJs);
                        runner.UpdateFlatSlotsForBinding(funcDecl.Name, fnValueJs);
                    }
                }
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;

            /// <summary>
            /// Checks if there's a non-catch lexical binding for the name in any enclosing
            /// scope between the current environment and the function scope. This implements
            /// the "would produce early errors" check for Annex B.3.3.
            /// Per B.3.5, simple catch parameters do not block var hoisting.
            /// Only block-level lexical bindings (let/const/class/function declarations with
            /// BlocksFunctionScopeOverride) are checked. Catch parameter bindings (which also
            /// have IsLexical but NOT BlocksFunctionScopeOverride) are excluded.
            /// </summary>
            static bool HasEnclosingLexicalBinding(JsEnvironment? start, Symbol name)
            {
                var current = start;
                while (current is not null)
                {
                    if (current.IsFunctionScope)
                    {
                        break;
                    }

                    if (current.IsBodyEnvironment || current.IsSimpleCatchParameter(name))
                    {
                        current = current.Enclosing;
                        continue;
                    }

                    ref var slot = ref current.TryGetSlotRef(name);
                    if (!Unsafe.IsNullRef(ref slot) && slot.IsLexical && slot.BlocksFunctionScopeOverride)
                    {
                        return true;
                    }

                    current = current.Enclosing;
                }

                return false;
            }

            /// <summary>
            /// Updates non-lexical slots for <paramref name="name"/> in environments between
            /// <paramref name="start"/> and <paramref name="stop"/> (exclusive). This ensures
            /// that body environments whose slots were pre-allocated by the IR plan receive the
            /// Annex B function value so that direct slot reads return the correct value.
            /// </summary>
            static void UpdateIntermediateVarBindings(
                JsEnvironment? start, JsEnvironment stop, Symbol name, JsValue value)
            {
                var current = start;
                while (current is not null && !ReferenceEquals(current, stop))
                {
                    ref var slot = ref current.TryGetSlotRef(name);
                    if (!Unsafe.IsNullRef(ref slot))
                    {
                        slot.Value = value;
                    }

                    current = current.Enclosing;
                }
            }
        }

        private void UpdateFlatSlotsForBinding(Symbol name, JsValue value)
        {
            if (_flatSlots is null)
            {
                return;
            }

            for (var i = 0; i < _flatSlots.Length; i++)
            {
                var variable = _flatSlots[i];
                if (!variable.IsValid)
                {
                    continue;
                }

                ref var slot = ref variable.Environment.GetSlotByIndex(variable.SlotIndex);
                if (ReferenceEquals(slot.Name, name))
                {
                    variable.Write(value);
                }
            }
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleClassDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ClassDeclarationInstruction>(instr);
            var classValue = instruction.Descriptor.ProgramCache.CreateClassValue(
                environment, context, instruction.Descriptor.Name);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingClassResult, environment))
            {
                returnValue = pendingClassResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleClassDeclarationThrowSlow(runner, context, out returnValue);
            }

            environment.DefineJsValue(instruction.Descriptor.Name, classValue,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleClassDeclarationThrowSlow(
            ExecutionPlanRunner runner,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var classThrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, classThrown))
            {
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(classThrown);
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleSimpleVariableDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<SimpleVariableDeclarationInstruction>(instr);
            var hasInitializer = instruction.InitializerProgram is not null || instruction.AwaitedProgram is not null;
            var isAnonymousFunctionDefinition = instruction.AllowNameInference;
            var isLexicalDeclaration = instruction.VarKind != VariableKind.Var;
            var isConstDeclaration = instruction.VarKind == VariableKind.Const;
            AssignmentReference? preResolvedVarReference = null;

            using var functionNameHint = isAnonymousFunctionDefinition
                ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                : null;

            if (isLexicalDeclaration && hasInitializer)
            {
                environment.DefineJsValue(instruction.TargetSymbol, JsValue.Uninitialized,
                    isConstDeclaration, isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }
            else if (instruction.VarKind == VariableKind.Var && hasInitializer)
            {
                // Per ES variable declaration evaluation, ResolveBinding happens
                // before the initializer. This matters for with/proxy side effects
                // that can change the lookup result during initializer evaluation.
                environment.EnsureFunctionScopedVarBinding(instruction.TargetSymbol, context);
                preResolvedVarReference = AssignmentReferenceResolver.ResolveIdentifierDirect(
                    instruction.TargetSymbol,
                    environment,
                    context);
            }

            var varValue = instruction.AwaitedProgram is { } awaitedProgram
                ? runner.EvaluateAwaitInGenerator(
                    instruction.AwaitStateKey!,
                    awaitedProgram,
                    environment,
                    context)
                : instruction.InitializerProgram is { } initializerProgram
                    ? runner.EvaluateExpressionProgram(initializerProgram, environment, context)
                    : JsValue.Undefined;

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingVarResult, environment))
            {
                returnValue = pendingVarResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleSimpleVariableDeclarationThrowSlow(runner, instruction, context, out returnValue);
            }

            if (context.IsReturn)
            {
                return HandleSimpleVariableDeclarationReturnSlow(runner, instruction, context, out returnValue);
            }

            if (context.IsYield)
            {
                return HandleSimpleVariableDeclarationYieldSlow(runner, ref environment, context, out returnValue);
            }

            if (instruction.VarKind == VariableKind.Var)
            {
                environment.EnsureFunctionScopedVarBinding(instruction.TargetSymbol, context);
                if (hasInitializer)
                {
                    if (!environment.TryAssignBlockedBinding(instruction.TargetSymbol, varValue))
                    {
                        if (preResolvedVarReference is { } resolvedReference)
                        {
                            resolvedReference.SetValue(varValue);
                        }
                        else
                        {
                            // Assign to the function-scoped binding even if we're currently in a child scope.
                            environment.AssignJsValue(instruction.TargetSymbol, varValue);
                        }
                    }
                }
            }
            else
            {
                environment.DefineJsValue(instruction.TargetSymbol, varValue,
                    isConstDeclaration, isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            // Compiled out when TRACE_IR_EXECUTION not defined
            if (instruction.VarKind != VariableKind.Var)
            {
                ExecutionPlanPrinter.TraceDefine(
                    runner._realmState.Logger,
                    instruction.VarKind.ToString(),
                    instruction.TargetSymbol.Name,
                    varValue.ToString() ?? "?",
                    environment.Depth,
                    environment.ScopeId,
                    environment.GetHashCode());
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleSimpleVariableDeclarationThrowSlow(
            ExecutionPlanRunner runner,
            SimpleVariableDeclarationInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var varThrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, varThrown))
            {
                if (runner._programCounter == runner._currentInstructionIndex)
                {
                    runner._programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(varThrown);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleSimpleVariableDeclarationReturnSlow(
            ExecutionPlanRunner runner,
            SimpleVariableDeclarationInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var varReturnValue = context.FlowValue;
            context.ClearReturn();
            if (!runner.HandleAbruptCompletion(AbruptKind.Return, varReturnValue))
            {
                returnValue = runner.CompleteReturn(varReturnValue);
                return InstructionResult.Return;
            }

            if (runner._programCounter == runner._currentInstructionIndex)
            {
                runner._programCounter = instruction.Next;
            }
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleSimpleVariableDeclarationYieldSlow(
            ExecutionPlanRunner runner,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var varYieldedValue = context.FlowValue;
            var varIteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
            runner.RecordYield(context, environment);
            context.Clear();
            runner._state = GeneratorState.Suspended;
            returnValue = varIteratorResultObject is not null
                ? JsValue.FromObjectUnsafe(varIteratorResultObject)
                : CreateIteratorResult(varYieldedValue, false);
            return InstructionResult.Return;
        }

        [MethodImpl(JsEngineConstants.Inlining)]
        private static InstructionResult HandleBindingVariableDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<BindingVariableDeclarationInstruction>(instr);
            var hasInitializer = instruction.InitializerProgram is not null || instruction.AwaitedProgram is not null;
            var bindingValue = instruction.AwaitedProgram is { } awaitedProgram
                ? runner.EvaluateAwaitInGenerator(
                    instruction.AwaitStateKey!,
                    awaitedProgram,
                    environment,
                    context)
                : instruction.InitializerProgram is { } initializerProgram
                    ? runner.EvaluateExpressionProgram(initializerProgram, environment, context)
                    : JsValue.Undefined;

            if (!context.ShouldStopEvaluation)
            {
                if (instruction.VarKind is VariableKind.Using or VariableKind.AwaitUsing)
                {
                    if (!bindingValue.IsNullOrUndefined && !bindingValue.TryGetObject<IJsObjectLike>(out _))
                    {
                        throw StandardLibrary.ThrowTypeError(
                            "using declarations require an object value",
                            context,
                            context.RealmState);
                    }

                    // Register the resource for disposal when this scope exits
                    var isAwaitUsing = instruction.VarKind == VariableKind.AwaitUsing;
                    environment.RegisterDisposable(bindingValue, isAwaitUsing, context.RealmState!);
                }

                var mode = instruction.VarKind switch
                {
                    VariableKind.Var => BindingMode.DefineVar,
                    VariableKind.Let => BindingMode.DefineLet,
                    VariableKind.Const => BindingMode.DefineConst,
                    VariableKind.Using => BindingMode.DefineConst,
                    VariableKind.AwaitUsing => BindingMode.DefineConst,
                    _ => throw new ArgumentOutOfRangeException(nameof(instruction.VarKind), instruction.VarKind, null)
                };

                runner.ApplyBindingTargetProgram(
                    instruction.TargetProgram,
                    bindingValue,
                    environment,
                    context,
                    mode,
                    hasInitializer,
                    allowNameInference: false);
            }

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingResult, environment))
            {
                returnValue = pendingResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                return HandleBindingVariableDeclarationThrowSlow(runner, instruction, context, out returnValue);
            }

            if (context.IsReturn)
            {
                return HandleBindingVariableDeclarationReturnSlow(runner, instruction, context, out returnValue);
            }

            if (context.IsYield)
            {
                return HandleBindingVariableDeclarationYieldSlow(runner, ref environment, context, out returnValue);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleBindingVariableDeclarationThrowSlow(
            ExecutionPlanRunner runner,
            BindingVariableDeclarationInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var thrown = context.FlowValue;
            context.Clear();
            if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown))
            {
                if (runner._programCounter == runner._currentInstructionIndex)
                {
                    runner._programCounter = instruction.Next;
                }
                returnValue = default;
                return InstructionResult.Continue;
            }

            runner.TryCatchStateRef.TryStack.Clear();
            throw new ThrowSignal(thrown);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleBindingVariableDeclarationReturnSlow(
            ExecutionPlanRunner runner,
            BindingVariableDeclarationInstruction instruction,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var returnVal = context.FlowValue;
            context.ClearReturn();
            if (!runner.HandleAbruptCompletion(AbruptKind.Return, returnVal))
            {
                returnValue = runner.CompleteReturn(returnVal);
                return InstructionResult.Return;
            }

            if (runner._programCounter == runner._currentInstructionIndex)
            {
                runner._programCounter = instruction.Next;
            }
            returnValue = default;
            return InstructionResult.Continue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static InstructionResult HandleBindingVariableDeclarationYieldSlow(
            ExecutionPlanRunner runner,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var yieldedValue = context.FlowValue;
            var iteratorResultObject = (context.CurrentSignal as YieldCompletionSignal)?.IteratorResultObject;
            runner.RecordYield(context, environment);
            context.Clear();
            runner._state = GeneratorState.Suspended;
            returnValue = iteratorResultObject is not null
                ? JsValue.FromObjectUnsafe(iteratorResultObject)
                : CreateIteratorResult(yieldedValue, false);
            return InstructionResult.Return;
        }

    }
}
