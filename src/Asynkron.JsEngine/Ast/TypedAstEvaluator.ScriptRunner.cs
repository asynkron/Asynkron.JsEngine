#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Runs an ExecutionPlan for a script (top-level code).
    /// This is a simplified runner that doesn't need generator/async machinery.
    /// The environment is already set up with hoisted declarations before calling Run().
    /// </summary>
    internal static class ScriptRunner
    {
        /// <summary>
        /// Runs a script execution plan to completion.
        /// </summary>
        /// <param name="plan">The execution plan to run.</param>
        /// <param name="environment">The pre-configured script environment (hoisting already done).</param>
        /// <param name="context">The evaluation context.</param>
        /// <returns>The completion value of the script.</returns>
        public static JsValue Run(
            ExecutionPlan plan,
            JsEnvironment environment,
            EvaluationContext context)
        {
            // NOTE: For scripts, we do NOT initialize slots because:
            // 1. Script hoisting (in ProgramNodeExtensions) already created dictionary-based
            //    bindings for var/let/const declarations
            // 2. Using slots would conflict with those dictionary bindings
            // This differs from function execution where slots are the primary storage mechanism.

            var programCounter = plan.EntryPoint;
            var resultValue = JsValue.Undefined;
            var tryStack = new Stack<TryFrame>();
            var loopStack = new Stack<LoopFrame>();

            while (programCounter >= 0 && programCounter < plan.Instructions.Length)
            {
                context.ThrowIfCancellationRequested();

                var currentIndex = programCounter;
                var instruction = plan.Instructions[programCounter];

                // Trace instruction execution when debug logging is enabled
                if (context.RealmState.Options.DebugMode)
                {
                    context.RealmState.Logger?.LogTrace(
                        "[Script IR:{PC,3}] {Instruction}",
                        programCounter,
                        ExecutionPlanPrinter.FormatInstruction(instruction));
                }

                switch (instruction.Kind)
                {
                    case InstructionKind.Statement:
                    {
                        var stmt = Unsafe.As<StatementInstruction>(instruction);
                        JsValue stmtResult;
                        try
                        {
                            stmtResult = stmt.Statement.EvaluateStatementJsValue(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            // Normalize ThrowSignal to context-based flow for IR exception handling
                            context.SetThrow(signal.ThrownValue);
                            stmtResult = JsValue.Undefined;
                        }
                        if (!stmtResult.IsUnit)
                        {
                            resultValue = stmtResult;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        // Scripts don't have return statements (syntax error at parse time)
                        // But we handle it gracefully just in case
                        if (context.IsReturn)
                        {
                            var returnValue = context.FlowValue;
                            context.ClearReturn();
                            return returnValue;
                        }

                        // Handle break/continue signals that escape from Statement instructions.
                        // This can happen when a labeled break/continue targets an outer loop
                        // that is handled by the IR runner, while the inner loop is AST-evaluated.
                        if (context.IsBreak || context.IsContinue)
                        {
                            // For now, fall back to AST for the entire script
                            var signal = context.IsBreak ? "Break" : "Continue";
                            context.Clear();
                            throw new NotSupportedException(
                                $"Script IR: {signal} signal escaped from Statement instruction. This script requires AST walking fallback.");
                        }

                        programCounter = stmt.Next;
                        continue;
                    }

                    case InstructionKind.EvaluateAndDiscard:
                    {
                        var eval = Unsafe.As<EvaluateAndDiscardInstruction>(instruction);
                        JsValue evalResult;
                        try
                        {
                            evalResult = eval.Expression.EvaluateExpression(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            evalResult = JsValue.Undefined;
                        }
                        if (!evalResult.IsUnit)
                        {
                            resultValue = evalResult;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        programCounter = eval.Next;
                        continue;
                    }

                    case InstructionKind.Expression:
                    {
                        var expr = Unsafe.As<ExpressionInstruction>(instruction);
                        JsValue exprResult;
                        try
                        {
                            exprResult = expr.Expression.EvaluateExpression(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            exprResult = JsValue.Undefined;
                        }
                        if (!exprResult.IsUnit)
                        {
                            resultValue = exprResult;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        programCounter = expr.Next;
                        continue;
                    }

                    case InstructionKind.SimpleVariableDeclaration:
                    {
                        var varDecl = Unsafe.As<SimpleVariableDeclarationInstruction>(instruction);
                        JsValue initValue;
                        try
                        {
                            initValue = varDecl.Initializer is not null
                                ? varDecl.Initializer.EvaluateExpression(environment, context)
                                : JsValue.Undefined;
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            initValue = JsValue.Undefined;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        // Handle variable declarations based on kind, matching ExecutionPlanRunner behavior
                        if (varDecl.VarKind == VariableKind.Var)
                        {
                            // For var: ensure binding exists in function scope, then assign if initializer present
                            // Script hoisting already created the binding, but we use EnsureFunctionScopedVarBinding
                            // for consistency and to handle edge cases
                            environment.EnsureFunctionScopedVarBinding(varDecl.TargetSymbol, context);
                            // Only assign if there's an initializer - per ES spec, `var x;` preserves hoisted value
                            if (varDecl.Initializer is not null)
                            {
                                // Try to assign to a blocked binding first (shadowed let/const in same scope)
                                if (!environment.TryAssignBlockedBindingJsValue(varDecl.TargetSymbol, initValue))
                                {
                                    // Use AssignJsValue (not DefineOrAssignJsValue) because for script-level var,
                                    // the value must also be set on the global object. AssignJsValue properly
                                    // handles this via the IsLexical check and globalObject.SetProperty().
                                    // Note: var bindings don't have TDZ so this won't throw ReferenceError.
                                    environment.AssignJsValue(varDecl.TargetSymbol, initValue);
                                }
                            }
                        }
                        else
                        {
                            // let/const - use DefineJsValue to properly initialize the TDZ binding
                            // that was created during hoisting. AssignJsValue would throw a ReferenceError
                            // because the binding is still marked as Uninitialized.
                            var isConst = varDecl.VarKind is VariableKind.Const or VariableKind.Using or VariableKind.AwaitUsing;
                            environment.DefineJsValue(varDecl.TargetSymbol, initValue,
                                isConst: isConst, isLexical: true, blocksFunctionScopeOverride: true);
                        }

                        programCounter = varDecl.Next;
                        continue;
                    }

                    case InstructionKind.IncrementSlot:
                    {
                        var incInst = Unsafe.As<IncrementSlotInstruction>(instruction);
                        // Fast path for ++/-- on identifiers
                        var currentValue = environment.GetIdentifierJsValueDirect(incInst.TargetSymbol, context);

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        JsValue newJsValue;
                        JsValue oldNumericValue; // For postfix: the numeric value before incrementing
                        if (currentValue.IsBigInt)
                        {
                            // BigInt arithmetic
                            var bigInt = (JsBigInt)currentValue.ObjectValue!;
                            oldNumericValue = currentValue; // BigInt is already numeric
                            var newBigInt = incInst.IsIncrement ? bigInt.Value + 1 : bigInt.Value - 1;
                            newJsValue = new JsBigInt(newBigInt);
                        }
                        else
                        {
                            // Convert to number if needed (fast path for already-number values)
                            var numValue = currentValue.IsNumber ? currentValue.NumberValue : currentValue.ToNumber();
                            oldNumericValue = JsValueCache.GetNumberJsValue(numValue);
                            // Apply increment or decrement
                            var newValue = incInst.IsIncrement ? numValue + 1.0 : numValue - 1.0;
                            newJsValue = JsValueCache.GetNumberJsValue(newValue);
                        }

                        // Update the binding - use AssignJsValue to walk up scope chain
                        // This may throw for const variables
                        try
                        {
                            environment.AssignJsValue(incInst.TargetSymbol, newJsValue);
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            if (!HandleThrow(signal.ThrownValue, tryStack, ref programCounter, ref environment))
                            {
                                throw;
                            }
                            continue;
                        }

                        // Capture result value: prefix returns new value, postfix returns old (numeric) value
                        resultValue = incInst.IsPrefix ? newJsValue : oldNumericValue;

                        programCounter = incInst.Next;
                        continue;
                    }

                    case InstructionKind.Branch:
                    {
                        var branch = Unsafe.As<BranchInstruction>(instruction);
                        JsValue conditionValue;
                        try
                        {
                            conditionValue = branch.Condition.EvaluateExpression(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            conditionValue = JsValue.Undefined;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        programCounter = conditionValue.IsTruthy ? branch.ConsequentIndex : branch.AlternateIndex;
                        continue;
                    }

                    case InstructionKind.Jump:
                    {
                        var jump = Unsafe.As<JumpInstruction>(instruction);
                        programCounter = jump.TargetIndex;
                        continue;
                    }

                    case InstructionKind.Return:
                    {
                        var ret = Unsafe.As<ReturnInstruction>(instruction);
                        // For scripts, an implicit return (no expression) should return the
                        // accumulated completion value, not undefined. This matches the ES spec
                        // ScriptEvaluation which returns the completion value of the last statement.
                        JsValue returnValue;
                        try
                        {
                            returnValue = ret.ReturnExpression is not null
                                ? ret.ReturnExpression.EvaluateExpression(environment, context)
                                : resultValue;
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                            returnValue = JsValue.Undefined;
                        }

                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }

                        // Handle pending finally blocks
                        if (tryStack.Count > 0 && TryHandlePendingFinally(tryStack, AbruptKind.Return, returnValue, ref programCounter))
                        {
                            continue;
                        }

                        return returnValue;
                    }

                    case InstructionKind.Throw:
                    {
                        var throwInst = Unsafe.As<ThrowInstruction>(instruction);
                        JsValue throwValue;
                        try
                        {
                            throwValue = throwInst.Expression.EvaluateExpression(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            // If evaluating the throw expression itself throws, use that value
                            throwValue = signal.ThrownValue;
                        }

                        if (context.IsThrow)
                        {
                            throwValue = context.FlowValue;
                            context.Clear();
                        }

                        if (!HandleThrow(throwValue, tryStack, ref programCounter, ref environment))
                        {
                            throw new ThrowSignal(throwValue);
                        }
                        continue;
                    }

                    case InstructionKind.PushEnvironment:
                    {
                        var pushEnv = Unsafe.As<PushEnvironmentInstruction>(instruction);
                        var newEnv = new JsEnvironment(environment, false, environment.IsStrict);
                        newEnv.ScopeId = pushEnv.ScopeId;

                        // Copy per-iteration bindings from previous environment
                        if (!pushEnv.PerIterationBindings.IsDefaultOrEmpty)
                        {
                            foreach (var binding in pushEnv.PerIterationBindings)
                            {
                                if (environment.TryGetJsValue(binding, out var value))
                                {
                                    newEnv.DefineJsValue(binding, value, isLexical: true);
                                }
                            }
                        }

                        // Initialize slots if needed
                        if (pushEnv.SlotCount > 0)
                        {
                            newEnv.InitializeSlots(pushEnv.SlotCount, pushEnv.ScopeId);
                            newEnv.SetSlotMap(pushEnv.SlotMap);
                        }

                        environment = newEnv;
                        programCounter = pushEnv.Next;
                        continue;
                    }

                    case InstructionKind.PopEnvironment:
                    {
                        var popEnv = Unsafe.As<PopEnvironmentInstruction>(instruction);
                        if (environment.ScopeId == popEnv.ScopeId && environment.Enclosing is not null)
                        {
                            environment = environment.Enclosing;
                        }
                        programCounter = popEnv.Next;
                        continue;
                    }

                    case InstructionKind.LoopEnter:
                    {
                        var loopEnter = Unsafe.As<LoopEnterInstruction>(instruction);
                        // Save current result and reset for loop body tracking.
                        // Per ES spec 13.7.3.6, loops have their own completion value that starts as undefined.
                        loopStack.Push(new LoopFrame(loopEnter.Label, loopEnter.BreakTarget, loopEnter.ContinueTarget, resultValue));
                        resultValue = JsValue.Undefined;
                        programCounter = loopEnter.Next;
                        continue;
                    }

                    case InstructionKind.LoopExit:
                    {
                        var loopExit = Unsafe.As<LoopExitInstruction>(instruction);
                        if (loopStack.Count > 0)
                        {
                            // Pop the frame. The current resultValue is the loop's completion value
                            // (either Undefined if body produced nothing, or the last body value).
                            // This becomes the statement's result per ES spec.
                            loopStack.Pop();
                        }
                        programCounter = loopExit.Next;
                        continue;
                    }

                    case InstructionKind.Break:
                    {
                        var breakInst = Unsafe.As<BreakInstruction>(instruction);
                        // For labeled breaks (crossing loop boundaries), fall back to AST for now
                        // TODO: Implement proper labeled break handling in IR
                        if (loopStack.Count > 0 && loopStack.Peek().BreakTarget != breakInst.TargetIndex)
                        {
                            throw new NotSupportedException(
                                "Script IR instruction 'Break' with label not yet supported. This script requires AST walking fallback.");
                        }
                        // Pop environments until we reach the target scope
                        while (breakInst.TargetScopeId >= 0 && environment.ScopeId != breakInst.TargetScopeId &&
                               environment.Enclosing is not null)
                        {
                            environment = environment.Enclosing;
                        }
                        // Pop the loop frame
                        if (loopStack.Count > 0)
                        {
                            loopStack.Pop();
                        }
                        programCounter = breakInst.TargetIndex;
                        continue;
                    }

                    case InstructionKind.Continue:
                    {
                        var continueInst = Unsafe.As<ContinueInstruction>(instruction);
                        // For labeled continues (crossing loop boundaries), fall back to AST for now
                        // TODO: Implement proper labeled continue handling in IR
                        if (loopStack.Count > 0 && loopStack.Peek().ContinueTarget != continueInst.TargetIndex)
                        {
                            throw new NotSupportedException(
                                "Script IR instruction 'Continue' with label not yet supported. This script requires AST walking fallback.");
                        }
                        // Pop environments until we reach the target scope
                        while (continueInst.TargetScopeId >= 0 && environment.ScopeId != continueInst.TargetScopeId &&
                               environment.Enclosing is not null)
                        {
                            environment = environment.Enclosing;
                        }
                        programCounter = continueInst.TargetIndex;
                        continue;
                    }

                    case InstructionKind.EnterTry:
                    {
                        var enterTry = Unsafe.As<EnterTryInstruction>(instruction);
                        tryStack.Push(new TryFrame(
                            enterTry.HandlerIndex,
                            enterTry.FinallyIndex,
                            enterTry.CatchSlotSymbol,
                            environment));
                        programCounter = enterTry.Next;
                        continue;
                    }

                    case InstructionKind.LeaveTry:
                    {
                        var leaveTry = Unsafe.As<LeaveTryInstruction>(instruction);
                        if (tryStack.Count > 0)
                        {
                            var frame = tryStack.Peek();
                            // If there's a finally, execute it
                            if (frame.FinallyIndex >= 0)
                            {
                                frame.PendingCompletion = (AbruptKind.None, JsValue.Undefined);
                                programCounter = frame.FinallyIndex;
                                continue;
                            }
                            tryStack.Pop();
                        }
                        programCounter = leaveTry.Next;
                        continue;
                    }

                    case InstructionKind.EndFinally:
                    {
                        var endFinally = Unsafe.As<EndFinallyInstruction>(instruction);
                        if (tryStack.Count > 0)
                        {
                            var frame = tryStack.Pop();
                            if (frame.PendingCompletion is { } pending)
                            {
                                switch (pending.Kind)
                                {
                                    case AbruptKind.Throw:
                                        if (!HandleThrow(pending.Value, tryStack, ref programCounter, ref environment))
                                        {
                                            throw new ThrowSignal(pending.Value);
                                        }
                                        continue;
                                    case AbruptKind.Return:
                                        if (tryStack.Count > 0 && TryHandlePendingFinally(tryStack, AbruptKind.Return, pending.Value, ref programCounter))
                                        {
                                            continue;
                                        }
                                        return pending.Value;
                                    case AbruptKind.Break:
                                    case AbruptKind.Continue:
                                        programCounter = (int)pending.Value.NumberValue;
                                        continue;
                                }
                            }
                        }
                        programCounter = endFinally.Next;
                        continue;
                    }

                    case InstructionKind.FunctionDeclaration:
                    {
                        // Function declarations are hoisted, so this is a no-op at runtime
                        var funcDecl = Unsafe.As<FunctionDeclarationInstruction>(instruction);
                        programCounter = funcDecl.Next;
                        continue;
                    }

                    case InstructionKind.ClassDeclaration:
                    {
                        var classDecl = Unsafe.As<ClassDeclarationInstruction>(instruction);
                        // Evaluate class declaration using existing evaluator
                        try
                        {
                            _ = classDecl.Declaration.EvaluateStatementJsValue(environment, context);
                        }
                        catch (ThrowSignal signal)
                        {
                            context.SetThrow(signal.ThrownValue);
                        }
                        if (context.IsThrow)
                        {
                            var thrown = context.FlowValue;
                            context.Clear();
                            if (!HandleThrow(thrown, tryStack, ref programCounter, ref environment))
                            {
                                throw new ThrowSignal(thrown);
                            }
                            continue;
                        }
                        programCounter = classDecl.Next;
                        continue;
                    }

                    // For unsupported instructions, fall back to AST evaluation via Statement
                    default:
                        throw new NotSupportedException(
                            $"Script IR instruction '{instruction.Kind}' not yet supported. " +
                            "This script requires AST walking fallback.");
                }
            }

            return resultValue;
        }

        private static bool HandleThrow(
            JsValue thrown,
            Stack<TryFrame> tryStack,
            ref int programCounter,
            ref JsEnvironment environment)
        {
            while (tryStack.Count > 0)
            {
                var frame = tryStack.Peek();

                // If there's a catch handler and we haven't gone through it yet
                if (frame.HandlerIndex >= 0 && frame.PendingCompletion is null)
                {
                    // Store the thrown value in catch slot if specified
                    if (frame.CatchSlotSymbol is not null)
                    {
                        environment.DefineJsValue(frame.CatchSlotSymbol, thrown, isLexical: true);
                    }
                    // Restore environment to try entry point
                    environment = frame.Environment;
                    programCounter = frame.HandlerIndex;
                    return true;
                }

                // If there's a finally block
                if (frame.FinallyIndex >= 0)
                {
                    frame.PendingCompletion = (AbruptKind.Throw, thrown);
                    environment = frame.Environment;
                    programCounter = frame.FinallyIndex;
                    return true;
                }

                // No handler in this frame, pop and try next
                tryStack.Pop();
            }

            return false;
        }

        private static bool TryHandlePendingFinally(
            Stack<TryFrame> tryStack,
            AbruptKind kind,
            JsValue value,
            ref int programCounter)
        {
            if (tryStack.Count == 0) return false;

            var frame = tryStack.Peek();
            if (frame.FinallyIndex >= 0)
            {
                frame.PendingCompletion = (kind, value);
                programCounter = frame.FinallyIndex;
                return true;
            }

            return false;
        }

        private static bool TryFindLoopTarget(
            Stack<LoopFrame> loopStack,
            Symbol? label,
            bool isBreak,
            out int target)
        {
            foreach (var frame in loopStack)
            {
                if (label is null || ReferenceEquals(frame.Label, label))
                {
                    target = isBreak ? frame.BreakTarget : frame.ContinueTarget;
                    return true;
                }
            }

            target = -1;
            return false;
        }

        private enum AbruptKind
        {
            None,
            Return,
            Throw,
            Break,
            Continue
        }

        private sealed class TryFrame(int handlerIndex, int finallyIndex, Symbol? catchSlotSymbol, JsEnvironment environment)
        {
            public int HandlerIndex { get; } = handlerIndex;
            public int FinallyIndex { get; } = finallyIndex;
            public Symbol? CatchSlotSymbol { get; } = catchSlotSymbol;
            public JsEnvironment Environment { get; } = environment;
            public (AbruptKind Kind, JsValue Value)? PendingCompletion { get; set; }
        }

        /// <summary>
        /// Tracks loop state including the saved result value before entering the loop.
        /// Per ES spec, loops have their own completion value which starts as undefined
        /// and is updated only if the loop body produces a value.
        /// </summary>
        private sealed class LoopFrame(Symbol? label, int breakTarget, int continueTarget, JsValue savedResultValue)
        {
            public Symbol? Label { get; } = label;
            public int BreakTarget { get; } = breakTarget;
            public int ContinueTarget { get; } = continueTarget;
            public JsValue SavedResultValue { get; } = savedResultValue;
        }
    }
}
