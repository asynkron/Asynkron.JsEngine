#region

using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;

#endregion

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
            if (instruction.Declaration is { } funcDecl)
            {
                // Pass skipInternalNameBinding: true so the function doesn't create an internal
                // const binding for its name (the binding is handled by environment.Define below).
                var functionValue = funcDecl.Function.CreateFunctionValue(environment, ctx,
                    skipInternalNameBinding: true);
                environment.DefineJsValue(
                    funcDecl.Name,
                    JsValue.FromObjectUnsafe(functionValue),
                    true,
                    isLexicalBinding: true,
                    blocksFunctionScopeOverride: true);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleClassDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ClassDeclarationInstruction>(instr);
            var classValue = instruction.Declaration.Definition.CreateClassValue(
                environment, context, instruction.Declaration.Name);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingClassResult, environment))
            {
                returnValue = pendingClassResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var classThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, classThrown, environment))
                {
                    returnValue = default;
                    return InstructionResult.Continue;
                }

                runner.TryCatchStateRef.TryStack.Clear();
                throw new ThrowSignal(classThrown);
            }

            environment.DefineJsValue(instruction.Declaration.Name, classValue,
                isLexicalBinding: true, blocksFunctionScopeOverride: true);

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleSimpleVariableDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<SimpleVariableDeclarationInstruction>(instr);
            var isAnonymousFunctionDefinition = instruction.Initializer is not null &&
                ExpressionNode.IsAnonymousFunctionDefinitionNode(instruction.Initializer);

            using var functionNameHint = isAnonymousFunctionDefinition
                ? context.EnterFunctionNameHint(instruction.TargetSymbol)
                : null;

            var varValue = instruction.Initializer?.EvaluateExpression(environment, context)
                           ?? JsValue.Undefined;

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingVarResult, environment))
            {
                returnValue = pendingVarResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var varThrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, varThrown, environment))
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

            if (context.IsReturn)
            {
                var varReturnValue = context.FlowValue;
                context.ClearReturn();
                if (!runner.HandleAbruptCompletion(AbruptKind.Return, varReturnValue, environment))
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

            if (context.IsYield)
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

            if (instruction.VarKind == VariableKind.Var)
            {
                environment.EnsureFunctionScopedVarBinding(instruction.TargetSymbol, context);
                if (instruction.Initializer is not null)
                {
                    if (!environment.TryAssignBlockedBinding(instruction.TargetSymbol, varValue))
                    {
                        if (instruction.IsScriptLevel)
                        {
                            environment.AssignJsValue(instruction.TargetSymbol, varValue);
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
                var isConst = instruction.VarKind == VariableKind.Const;
#pragma warning disable CS0162
                if (JsEngineConstants.TraceIrExecution && runner._realmState.Logger is not null)
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
#pragma warning restore CS0162
                environment.DefineJsValue(instruction.TargetSymbol, varValue,
                    isConst, isLexicalBinding: true, blocksFunctionScopeOverride: true);
            }

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }

        private static InstructionResult HandleComplexVariableDeclaration(
            ExecutionPlanRunner runner,
            ExecutionInstruction instr,
            ref JsEnvironment environment,
            EvaluationContext context,
            out JsValue returnValue)
        {
            var instruction = Unsafe.As<ComplexVariableDeclarationInstruction>(instr);

            // Evaluate the variable declaration using the standard AST evaluator
            // which handles destructuring patterns, rest elements, default values, etc.
            instruction.Declaration.EvaluateStatementJsValue(environment, context);

            if (runner._isAsync && runner.TryHandlePendingAwait(context, out var pendingResult, environment))
            {
                returnValue = pendingResult;
                return InstructionResult.Return;
            }

            if (context.IsThrow)
            {
                var thrown = context.FlowValue;
                context.Clear();
                if (runner.HandleAbruptCompletion(AbruptKind.Throw, thrown, environment))
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

            if (context.IsReturn)
            {
                var returnVal = context.FlowValue;
                context.ClearReturn();
                if (!runner.HandleAbruptCompletion(AbruptKind.Return, returnVal, environment))
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

            if (context.IsYield)
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

            runner._programCounter = instruction.Next;
            returnValue = default;
            return InstructionResult.Continue;
        }
    }
}
