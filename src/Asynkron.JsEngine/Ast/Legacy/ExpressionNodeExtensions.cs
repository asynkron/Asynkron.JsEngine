#region

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Well-known symbol for storing yield resume state in the environment.
    /// Used when yields happen inside StatementInstruction (e.g., in destructuring defaults).
    /// </summary>
    private static readonly Symbol YieldResumeStateKey = Symbol.Intern("__yield_resume_state__");

    private static JsValue HandleIdentifierNotFound(Symbol name, EvaluationContext context)
    {
        var errorObject = StandardLibrary.CreateReferenceError(
            $"{name.Name} is not defined",
            context,
            context.RealmState);
        context.SetThrow(errorObject);
        return errorObject;
    }

    private static JsValue ResolveThisValue(JsEnvironment environment, EvaluationContext context)
    {
        if (!environment.IsThisInitializationKnownTrue(context))
        {
            throw StandardLibrary.ThrowReferenceError(
                "Must call super constructor in derived class before accessing 'this'",
                context,
                context.RealmState);
        }

        try
        {
            // Check if we're in an arrow function that has a lexical this environment.
            // If so, read `this` from the original owning environment, not the arrow's local copy.
            // This ensures that after super() updates the constructor's `this`, subsequent
            // reads of `this` inside the arrow function see the updated value.
            if (environment.TryFindBindingJsValue(Symbol.LexicalThisEnvironment, true, out _,
                    out var lexicalEnvValue) &&
                lexicalEnvValue.TryGetObject<JsEnvironment>(out var lexicalThisEnv))
            {
                return lexicalThisEnv.GetJsValue(Symbol.This);
            }

            return environment.GetJsValue(Symbol.This);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                       StringComparison.Ordinal))
        {
            var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
            throw new ThrowSignal(errorObject);
        }
    }

    /// <summary>
    ///     Evaluates an import.meta expression. Returns the import.meta object for the current module.
    /// </summary>
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateImportMeta(JsEnvironment environment, EvaluationContext context)
    {
        // Try to get the import.meta object from the environment
        // If running in module context, this should be set by the module loader
        if (environment.TryGetJsValue(Symbol.ImportMeta, out var importMeta))
        {
            return importMeta;
        }

        // Return a basic import.meta object with a url property
        var metaObject = new JsObject();
        metaObject.RealmState = context.RealmState;
        metaObject.SetPrototype(null);
        // Set a default URL if we can determine it from the environment
        metaObject.SetProperty("url", string.Empty);
        return (JsValue)metaObject;
    }

    private static (IJsEnvironmentAwareCallable? Constructor, IJsPropertyAccessor? Prototype) ResolveSuperclass(this ExpressionNode? extendsExpression, JsEnvironment environment, EvaluationContext context)
    {
        if (extendsExpression is null)
        {
            return (null, null);
        }

        var baseJsValue = EvaluateCachedExpressionProgram(
            extendsExpression,
            environment,
            context,
            "Dynamic class extends expression");
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (baseJsValue.IsNullOrUndefined)
        {
            return (null, null);
        }

        // Only objects can be constructors - extract ObjectValue directly
        var baseValue = baseJsValue.Kind == JsValueKind.Object ? baseJsValue.ObjectValue : null;

        if (!JsOps.IsConstructor(JsValue.FromObjectUnsafe(baseValue)))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value is not a constructor or null", context, context.RealmState));
        }

        // Proxy cannot be subclassed because its prototype is undefined.
        if (baseValue is IJsPropertyAccessor accessorWithMarker &&
            JsOps.TryGetPropertyValue(JsValue.FromObjectUnsafe(accessorWithMarker), "__proxyHasNoPrototype__",
                out var marker, context) &&
            JsOps.ToBoolean(marker))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState));
        }

        if (baseValue is not (IJsEnvironmentAwareCallable callable and IJsPropertyAccessor))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value is not a constructor or null", context, context.RealmState));
        }

        var hasPrototype = JsOps.TryGetPropertyValue(baseJsValue, "prototype", out var prototypeValue, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (!hasPrototype)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState));
        }

        // Per ES spec 15.7.14 step 7.c.iii: only null is acceptable, not undefined
        if (prototypeValue.IsNull)
        {
            return (callable, null);
        }

        if (prototypeValue.TryGetObject<IJsPropertyAccessor>(out var prototype))
        {
            return (callable, prototype);
        }

        throw new ThrowSignal(StandardLibrary.CreateTypeError(
            "Class extends value does not have a valid prototype",
            context,
            context.RealmState));
    }

    /// <summary>
    /// Ultra-thin hot path for expression evaluation - designed to be inlined.
    /// Uses explicit if statements instead of switch for minimal IL size.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateIdentifier(this IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        // `arguments` is an implicit binding; its slot isn't present in the analyzer's slot map,
        // so a cached slot hint can incorrectly point to an outer scope (e.g., a `var arguments`).
        // Always resolve it via normal binding lookup to ensure the per-call arguments object wins.
        if (ReferenceEquals(identifier.Name, Symbol.Arguments))
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var argumentsValue))
            {
                return argumentsValue;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (!context.AllowIdentifierCache)
        {
            if (environment.TryGetIdentifierJsValue(identifier.Name, context, out var value))
            {
                return value;
            }

            return HandleIdentifierNotFound(identifier.Name, context);
        }

        if (environment.TryReadIdentifierWithSlot(identifier, context, out var slotValue))
        {
            return slotValue;
        }

        // Slow path: identifier not found - create proper error
        // Compiled out when TRACE_IR_EXECUTION not defined
        ExecutionPlanPrinter.TraceLookup(
            context.RealmState.Logger,
            identifier.Name.Name,
            false,
            environment.Depth,
            environment.ScopeId,
            environment.GetHashCode(),
            $"idScope={identifier.ScopeId} slot={identifier.SlotIndex}");
        return HandleIdentifierNotFound(identifier.Name, context);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateExpression(this ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        context.RecordAstEvaluation(expression);
        return expression switch
        {
            // Explicit if statements generate less IL than switch expressions
            LiteralExpression literal => literal.Value,
            IdentifierExpression identifier => identifier.EvaluateIdentifier(environment, context),
            BinaryExpression binary => EvaluateCachedExpressionProgram(
                binary,
                environment,
                context,
                "Dynamic binary expression"),
            AssignmentExpression assignment => assignment.EvaluateAssignment(environment, context),
            UnaryExpression unary => unary.EvaluateUnary(environment, context),
            CallExpression call => call.EvaluateCall(environment, context),
            _ => expression.EvaluateExpressionSlow(environment, context)
        };
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDynamicExpressionOperand(
        this ExpressionNode expression,
        JsEnvironment environment,
        EvaluationContext context,
        string failureLabel)
    {
        return expression switch
        {
            AwaitExpression awaitExpression => awaitExpression.EvaluateAwait(environment, context),
            YieldExpression yieldExpression => yieldExpression.EvaluateYield(environment, context),
            _ => EvaluateCachedExpressionProgram(expression, environment, context, failureLabel)
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateAwait(this AwaitExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Async generators execute on the generator IR path via ExecutionPlanRunner.
        // When an await expression runs under that executor, the execution environment
        // carries a back-reference to the active generator instance so we can surface
        // pending promises instead of blocking. In that case the generator instance
        // is responsible for evaluating the awaited expression and managing resume.
        if (environment.TryGetObject<ExecutionPlanRunner>(Symbol.GeneratorInstanceSymbol, out var generator))
        {
            if (!ExpressionProgramCompiler.TryCompile(
                    expression.Expression,
                    out var awaitedProgram,
                    out var failureReason))
            {
                throw new NotSupportedException(
                    $"Async generator await operand could not be lowered to expression bytecode: {failureReason}");
            }

            return generator.EvaluateAwaitInGenerator(
                expression.GetAwaitStateKey(),
                awaitedProgram,
                environment,
                context);
        }

        var awaitedValue = expression.Expression.EvaluateDynamicExpressionOperand(
            environment,
            context,
            "Dynamic await operand");
        if (context.ShouldStopEvaluation)
        {
            return awaitedValue;
        }

        // Always await asynchronously: wrap non-promises with Promise.resolve and drive through scheduler.
        if (!awaitedValue.IsObject || !AwaitScheduler.IsPromiseLike(awaitedValue))
        {
            var promiseCtor = context.RealmState.PromiseConstructor;
            JsObject? wrappedPromise = null;

            if (promiseCtor is IJsPropertyAccessor accessor &&
                accessor.TryGetProperty("resolve", out var resolveValue) &&
                resolveValue.TryGetObject<IJsCallable>(out var resolveCallable))
            {
                var resolveResult = resolveCallable.Invoke(
                    new SingleValueArgs(awaitedValue),
                    JsValue.FromObjectUnsafe(promiseCtor));
                if (resolveResult.IsObject)
                {
                    wrappedPromise = resolveResult.AsObject();
                }
            }

            if (wrappedPromise is null)
            {
                // Fallback: create a resolved promise in the current realm.
                var engine = context.RealmState.Engine;
                var promise = engine?.CreateRealmPromise();
                promise?.Resolve(awaitedValue);
                wrappedPromise = promise?.JsObject;
            }

            awaitedValue = wrappedPromise is not null ? new JsValue(wrappedPromise) : awaitedValue;
        }

        var completed = AwaitScheduler.TryAwaitPromiseSync(
            awaitedValue,
            context,
            out var resolvedValue,
            context.DrainAwaitMicrotasks,
            blockUntilSettled: true);

        if (!completed)
        {
            if (!context.IsThrow)
            {
                throw new InvalidOperationException("Legacy await did not settle synchronously.");
            }

            return JsValue.Undefined;
        }

        return resolvedValue;
    }

    private static Symbol GetAwaitStateKey(this AwaitExpression expression)
    {
        return ((IAstCacheable<Symbol>)expression).GetOrCreateCache();
    }

    /// <summary>
    /// Sets the yield resume value in the environment so that the next call to EvaluateYield
    /// with a matching source position will return this value instead of yielding.
    /// </summary>
    internal static void SetYieldResumeValue(JsEnvironment environment, JsValue resumeValue, int yieldSourceStart,
        int yieldSourceEnd)
    {
        var state = new YieldResumeState
        {
            HasResumeValue = true,
            ResumeValue = resumeValue,
            YieldSourceStart = yieldSourceStart,
            YieldSourceEnd = yieldSourceEnd
        };

        if (environment.HasOwnBinding(YieldResumeStateKey))
        {
            environment.AssignJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state));
        }
        else
        {
            environment.DefineJsValue(YieldResumeStateKey, JsValue.FromObjectUnsafe(state), isLexicalBinding: true,
                canDelete: true);
        }
    }

    /// <summary>
    /// State for resuming from a yield that happened during AST evaluation (via StatementInstruction).
    /// </summary>
    internal sealed class YieldResumeState
    {
        /// <summary>
        /// When true, the yield has been resumed and ResumeValue should be returned.
        /// </summary>
        public bool HasResumeValue { get; set; }

        /// <summary>
        /// The value passed to iter.next(value) when resuming.
        /// </summary>
        public JsValue ResumeValue { get; set; }

        /// <summary>
        /// Source position of the yield expression that yielded.
        /// Used to match the correct yield on resume.
        /// </summary>
        public int YieldSourceStart { get; set; }

        public int YieldSourceEnd { get; set; }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateYield(this YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Most yield expressions should be lowered by GeneratorYieldLowerer and compiled to IR.
        // However, some yields (like those in destructuring default values) cannot be extracted
        // and are evaluated via StatementInstruction wrapping the containing for-of loop.
        // In this case, we signal a yield via the context so the caller can save state.

        if (expression.IsDelegated)
        {
            // yield* is more complex and should be handled by the IR interpreter.
            // If we reach here with yield*, something went wrong.
            throw new InvalidOperationException(
                "Delegated yield (yield*) expression encountered during AST evaluation. " +
                "This should have been lowered to IR by GeneratorYieldLowerer. " +
                $"Source: {expression.Source?.StartPosition}-{expression.Source?.EndPosition}");
        }

        // Check if we're resuming from a previous yield at this position.
        // If so, return the resume value instead of yielding again.
        if (environment.TryGetObject<YieldResumeState>(YieldResumeStateKey, out var resumeState) &&
            resumeState.HasResumeValue &&
            resumeState.YieldSourceStart == (expression.Source?.StartPosition ?? -1) &&
            resumeState.YieldSourceEnd == (expression.Source?.EndPosition ?? -1))
        {
            // Clear the resume state so future yields at this position work correctly
            resumeState.HasResumeValue = false;
            return resumeState.ResumeValue;
        }

        // Evaluate the yield operand if present
        var yieldedValue = JsValue.Undefined;
        if (expression.Expression is not null)
        {
            yieldedValue = expression.Expression.EvaluateDynamicExpressionOperand(
                environment,
                context,
                "Dynamic yield operand");
            if (context.ShouldStopEvaluation)
            {
                return yieldedValue;
            }
        }

        // Signal the yield via the context.
        // Use the source position to identify this yield for resume.
        context.SetYield(yieldedValue, expression.Source?.StartPosition ?? -1);

        // Store the yield position so the IR interpreter can set up resume state
        context.LastYieldSourceStart = expression.Source?.StartPosition ?? -1;
        context.LastYieldSourceEnd = expression.Source?.EndPosition ?? -1;

        // Return undefined; the actual resume value will be provided when the generator continues.
        // The caller (e.g., BindArrayPattern) will check context.IsYield and save state.
        return JsValue.Undefined;
    }

    private static bool IsParenthesizedIdentifierAssignment(AssignmentExpression expression)
    {
        if (expression.Source is null)
        {
            return false;
        }

        // Heuristic: if the identifier token is immediately preceded (ignoring
        // whitespace) by a '(', it came from a CoverParenthesizedExpression
        // and should not trigger SetFunctionName inference.
        var source = expression.Source.Source;
        var index = expression.Source.StartPosition - 1;
        while (index >= 0 && char.IsWhiteSpace(source, index))
        {
            index--;
        }

        return index >= 0 && source[index] == '(';
    }

    /// <summary>
    /// Core logic for evaluating compound assignment operators.
    /// Handles short-circuit evaluation for logical operators and applies binary operations.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static void EvaluateCompoundOperator(
        AssignmentExpression? assignment,
        BinaryExpression binary,
        JsValue leftJs,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.LogicalAnd:
                if (!leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
            case BinaryOperator.LogicalOr:
                if (leftJs.IsTruthy)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
            case BinaryOperator.NullishCoalescing:
                if (!leftJs.IsNullish)
                {
                    value = leftJs;
                    shouldAssign = false;
                    return;
                }

                value = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
                shouldAssign = !context.ShouldStopEvaluation;
                return;
        }

        var rightJs = EvaluateAssignmentRhsWithNameHintJsValue(assignment, binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return;
        }

        value = ApplyBinaryOperator(binary.Operator, leftJs, rightJs, context);
        shouldAssign = true;
    }

    /// <summary>
    /// Fast path for compound assignment using direct identifier access (avoids AssignmentReference).
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentDirectJsValue(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        Symbol target,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        var leftJs = context.GetIdentifier(environment, target);
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
        return true;
    }

    /// <summary>
    /// Slot-based compound assignment evaluation - fastest path for resolved identifiers.
    /// Only used when ScopeDepth=0 (local variables in the current function scope).
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentSlotBased(
        AssignmentExpression assignment,
        ExpressionNode candidate,
        IdentifierExpression targetIdentifier,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (!environment.TryReadIdentifierWithSlot(
                targetIdentifier,
                context,
                out var leftJs))
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (context.ShouldStopEvaluation)
        {
            value = leftJs;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
        return true;
    }

    /// <summary>
    /// Slot-based compound assignment evaluation using cached slot targets.
    /// Avoids resolving the identifier on every iteration.
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentCachedSlot(
        AssignmentExpression assignment,
        ExpressionNode candidate,
        EvaluationContext.CachedSlotTarget cached,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (!cached.Environment.TryReadSlotValue(cached.Name, cached.SlotIndex, context, out var leftJs))
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        if (context.ShouldStopEvaluation)
        {
            value = leftJs;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
        return true;
    }

    /// <summary>
    /// JsValue version of compound assignment evaluation that avoids boxing for numeric operations.
    /// </summary>
    private static bool TryEvaluateCompoundAssignmentJsValue(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue value,
        out bool shouldAssign)
    {
        if (candidate is not BinaryExpression binary)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return false;
        }

        var leftJs = reference.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            value = JsValue.Undefined;
            shouldAssign = false;
            return true;
        }

        EvaluateCompoundOperator(assignment, binary, leftJs, environment, context, out value, out shouldAssign);
        return true;
    }

    /// <summary>
    /// Tries to evaluate a compound assignment and apply it to the reference.
    /// Combines TryEvaluateCompoundAssignmentJsValue with the common post-processing pattern.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    internal static bool TryApplyCompoundAssignment(
        AssignmentExpression? assignment,
        ExpressionNode candidate,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue result)
    {
        if (!TryEvaluateCompoundAssignmentJsValue(assignment, candidate, reference, environment, context,
                out var compoundValue, out var shouldAssign))
        {
            result = JsValue.Undefined;
            return false;
        }

        if (!context.ShouldStopEvaluation && shouldAssign)
        {
            reference.SetValue(compoundValue);
        }

        result = compoundValue;
        return true;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateAssignmentRhsWithNameHintJsValue(
        AssignmentExpression? assignment,
        ExpressionNode rhs,
        JsEnvironment environment,
        EvaluationContext context)
    {
        using var functionNameHint = ShouldApplyAssignmentNameHint(assignment, rhs)
            ? context.EnterFunctionNameHint(assignment!.Target)
            : null;

        var jsValue = rhs.EvaluateDynamicExpressionOperand(
            environment,
            context,
            "Dynamic assignment right-hand side");
        if (context.ShouldStopEvaluation)
        {
            return jsValue;
        }

        if (assignment is not null &&
            jsValue.ObjectValue is IFunctionNameTarget nameTarget &&
            rhs.IsAnonymousFunctionDefinitionNode() &&
            !IsParenthesizedIdentifierAssignment(assignment))
        {
            nameTarget.EnsureHasName(assignment.Target.Name);
        }

        return jsValue;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool ShouldApplyAssignmentNameHint(AssignmentExpression? assignment, ExpressionNode rhs)
    {
        return assignment is not null &&
               rhs.IsAnonymousFunctionDefinitionNode() &&
               !IsParenthesizedIdentifierAssignment(assignment);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateAssignment(this AssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var target = expression.Target;
        var hasWithObjectInChain = environment.HasWithObjectInChain();

        // Check for immutable binding (e.g., named function expression name)
        // Per ECMAScript spec, in strict mode throw TypeError, in non-strict mode silently ignore
        if (expression.IsImmutableTarget)
        {
            // Still need to evaluate RHS for potential side effects
            var rhsValue = EvaluateAssignmentRhsWithNameHintJsValue(
                expression,
                expression.Value,
                environment,
                context);
            if (context.ShouldStopEvaluation)
            {
                return rhsValue;
            }

            // Find the binding's environment to check its strictness
            // The binding's environment strictness determines the behavior, not the current execution context
            var immutableBindingEnv = expression.ScopeId >= 0 && environment.ScopeId == expression.ScopeId
                ? environment
                : expression.ScopeId >= 0
                    ? environment.FindByScopeId(expression.ScopeId)
                    : environment.GetFunctionScope();
            var isStrictBinding = immutableBindingEnv?.IsStrict ?? environment.IsStrict;

            if (isStrictBinding)
            {
                var error = StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{expression.Target.Name}'.", context, context.RealmState);
                context.SetThrow(error);
                return JsValue.Undefined;
            }

            // Non-strict mode: silently ignore the assignment, return the evaluated value
            return rhsValue;
        }

        // Fast path: slot-based assignment using ScopeId to find the declaring environment.
        // This enables O(1) slot access for variables in any scope (local or closure).
        if (!hasWithObjectInChain &&
            expression is { SlotIndex: >= 0, ScopeId: >= 0 })
        {
            var targetIdentifier = expression.TargetIdentifier ??
                                   new IdentifierExpression(
                                       expression.Source,
                                       expression.Target,
                                       expression.ScopeDepth,
                                       expression.SlotIndex,
                                       expression.ScopeId);

            if (expression.IsCompoundAssignment)
            {
                // Try slot-based compound assignment (fastest path)
                if (TryEvaluateCompoundAssignmentSlotBased(
                        expression,
                        expression.Value,
                        targetIdentifier,
                        environment,
                        context,
                        out var compoundJsValue,
                        out var shouldAssignCompound))
                {
                    if (context.ShouldStopEvaluation)
                    {
                        return compoundJsValue;
                    }

                    if (shouldAssignCompound)
                    {
                        environment.TryWriteIdentifierWithSlot(targetIdentifier, compoundJsValue, context);
                    }

                    return compoundJsValue;
                }
                // If slot-based compound fails, fall through to other compound handlers below
            }
            else
            {
                // Simple slot-based assignment (not compound)
                var slotValueJs =
                    EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return slotValueJs;
                }

                environment.TryWriteIdentifierWithSlot(targetIdentifier, slotValueJs, context);
                return slotValueJs;
            }
        }

        // Pre-check for const/immutable bindings using scope analysis (before fast paths that may bypass flags)
        // Only applies to simple identifier targets.
        if (environment.TryFindBindingJsValue(target, allowUninitialized: true,
                out var precheckBindingEnv, out _))
        {
            if (precheckBindingEnv.TryGetSlotIndex(target, out var idx))
            {
                ref var slot = ref precheckBindingEnv.GetSlotByIndex(idx);
                if (!slot.IsUninitialized)
                {
                    var isStrictContext = precheckBindingEnv.IsStrict || context.CurrentScope.IsStrict;
                    if (slot.IsConst ||
                        (slot.IsImmutableBinding && isStrictContext))
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Assignment to constant variable '{target.Name}'.",
                            realm: context.RealmState));
                    }
                }
            }
        }

        // Fast path for compound assignments on simple identifiers
        // This avoids creating AssignmentReference structs entirely.
        // IMPORTANT: Only use this fast path for non-dynamic scopes (see comment below for simple assignments).
        if (!hasWithObjectInChain &&
            expression is { IsCompoundAssignment: true, SlotIndex: >= 0, ScopeId: >= 0 } &&
            TryEvaluateCompoundAssignmentDirectJsValue(expression, expression.Value, target,
                environment, context, out var compoundJsValue2, out var shouldAssignCompound2))
        {
            if (context.ShouldStopEvaluation)
            {
                return compoundJsValue2;
            }

            if (shouldAssignCompound2)
            {
                var slotEnvironment = environment.ScopeId == expression.ScopeId
                    ? environment
                    : environment.FindByScopeId(expression.ScopeId) ?? environment;
                slotEnvironment.SetIdentifierJsValue(target, compoundJsValue2, context);
            }

            return compoundJsValue2;
        }

        // Fast path for simple identifier assignments (not compound)
        // This avoids creating AssignmentReference structs entirely.
        // IMPORTANT: Only use this fast path for non-dynamic scopes!
        // Dynamic scopes (with eval/with) require resolving the reference BEFORE
        // evaluating the RHS, per ES spec 13.15.2. The fast path evaluates RHS first
        // which breaks code like: with(scope) { x = (delete scope.x, 2); }
        if (!hasWithObjectInChain &&
            expression is { IsCompoundAssignment: false, SlotIndex: >= 0, ScopeId: >= 0 })
        {
            // Find the environment that owns this slot. Slot indices are scoped to the declaring environment,
            // so we must not blindly write to the current environment if ScopeId differs (e.g., class name slots).
            var slotEnvironment = environment.ScopeId == expression.ScopeId
                ? environment
                : environment.FindByScopeId(expression.ScopeId) ?? environment;

            var targetValueJs =
                EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, slotEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return targetValueJs;
            }

            try
            {
                slotEnvironment.SetIdentifierJsValue(target, targetValueJs, context);
                return targetValueJs;
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                           StringComparison.Ordinal))
            {
                return expression.HandleReferenceError(ex, environment, context);
            }
        }

        // Runtime slot lookup: try to find slot index from environment's SlotMap
        // This avoids the expensive ResolveIdentifierDirect fallback for variables
        // declared in the current function scope when AST nodes weren't pre-stamped.
        if (!hasWithObjectInChain &&
            environment.TryGetSlotIndex(target, out var runtimeSlotIndex))
        {
            // Found slot - check TDZ first
            ref var tdzdCheckSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
            if (tdzdCheckSlot.IsUninitialized && tdzdCheckSlot.IsLexical)
            {
                throw new ThrowSignal(StandardLibrary.CreateReferenceError(
                    $"Cannot access '{target.Name}' before initialization",
                    context, context.RealmState));
            }

            if (expression.IsCompoundAssignment && expression.Value is BinaryExpression binary)
            {
                // Read current value from slot (already TDZ-checked above)
                var currentValue = environment.GetSlotRef(runtimeSlotIndex);

                // Short-circuit for logical operators
                switch (binary.Operator)
                {
                    case BinaryOperator.LogicalAnd:
                        if (!currentValue.IsTruthy)
                        {
                            return currentValue; // No assignment needed
                        }

                        break;
                    case BinaryOperator.LogicalOr:
                        if (currentValue.IsTruthy)
                        {
                            return currentValue; // No assignment needed
                        }

                        break;
                    case BinaryOperator.NullishCoalescing:
                        if (!currentValue.IsNullish)
                        {
                            return currentValue; // No assignment needed
                        }

                        break;
                }

                var compoundRhsValue = EvaluateAssignmentRhsWithNameHintJsValue(
                    expression,
                    binary.Right,
                    environment,
                    context);
                if (context.ShouldStopEvaluation)
                {
                    return compoundRhsValue;
                }

                // Apply compound operation using shared helper
                var compoundResult = ApplyBinaryOperator(binary.Operator, currentValue, compoundRhsValue, context);

                // Check const before assignment - const always throws per ES spec
                ref var compoundSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
                if (compoundSlot.IsConst && !compoundSlot.IsUninitialized)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{target.Name}'.",
                        realm: context.RealmState));
                }

                if (compoundSlot.IsImmutableBinding)
                {
                    if (environment.IsStrict || context.CurrentScope.IsStrict)
                    {
                        throw new ThrowSignal(StandardLibrary.CreateTypeError(
                            $"Assignment to constant variable '{target.Name}'.",
                            realm: context.RealmState));
                    }

                    // Sloppy mode: ignore write but still return the computed value
                    return compoundResult;
                }

                compoundSlot.Value = compoundResult;

                // For non-lexical bindings (var) in global scope, also update the global object
                if (!compoundSlot.IsLexical && environment.IsGlobalFunctionScope)
                {
                    var globalObject = environment.GetRootGlobalObject();
                    globalObject?.SetProperty(expression.Target.Name, compoundResult);
                }

                return compoundResult;
            }

            // Simple assignment
            var rhsValue =
                EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return rhsValue;
            }

            // Check const before assignment - const always throws per ES spec
            ref var simpleSlot = ref environment.GetSlotByIndex(runtimeSlotIndex);
            if (simpleSlot.IsConst && !simpleSlot.IsUninitialized)
            {
                throw new ThrowSignal(StandardLibrary.CreateTypeError(
                    $"Assignment to constant variable '{target.Name}'.",
                    realm: context.RealmState));
            }

            if (simpleSlot.IsImmutableBinding)
            {
                if (environment.IsStrict || context.CurrentScope.IsStrict)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        $"Assignment to constant variable '{target.Name}'.",
                        realm: context.RealmState));
                }

                return rhsValue;
            }

            simpleSlot.Value = rhsValue;

            // For non-lexical bindings (var) in global scope, also update the global object
            if (!simpleSlot.IsLexical && environment.IsGlobalFunctionScope)
            {
                var globalObject = environment.GetRootGlobalObject();
                globalObject?.SetProperty(expression.Target.Name, rhsValue);
            }

            return rhsValue;
        }

        if (!hasWithObjectInChain &&
            context.TryResolveAssignmentSlot(expression, environment, out var cachedSlot))
        {
            if (expression.IsCompoundAssignment &&
                TryEvaluateCompoundAssignmentCachedSlot(expression, expression.Value, cachedSlot, environment,
                    context,
                    out var cachedCompoundValue,
                    out var cachedShouldAssign))
            {
                if (context.ShouldStopEvaluation)
                {
                    return cachedCompoundValue;
                }

                if (cachedShouldAssign)
                {
                    if (!cachedSlot.Environment.TryWriteSlotValue(
                            cachedSlot.Name,
                            cachedSlot.SlotIndex,
                            cachedCompoundValue,
                            context))
                    {
                        environment.SetIdentifierJsValue(cachedSlot.Name, cachedCompoundValue, context);
                    }
                }

                return cachedCompoundValue;
            }

            var cachedValue =
                EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return cachedValue;
            }

            if (!cachedSlot.Environment.TryWriteSlotValue(cachedSlot.Name, cachedSlot.SlotIndex, cachedValue,
                    context))
            {
                environment.SetIdentifierJsValue(cachedSlot.Name, cachedValue, context);
            }

            return cachedValue;
        }

        // Fallback to the AssignmentReference path for other cases
        var reference = AssignmentReferenceResolver.ResolveIdentifierDirect(
            expression.Target, environment, context);

        // Spec: GetValue on an unresolvable Reference (compound assignments must read first)
        // must throw ReferenceError in both strict and non-strict mode.
        if (expression.IsCompoundAssignment && reference.IsUnresolvable)
        {
            throw StandardLibrary.ThrowReferenceError(
                $"{expression.Target.Name} is not defined",
                context,
                context.RealmState);
        }

        // Use JsValue version of the compound assignment to avoid boxing
        if (expression.IsCompoundAssignment &&
            TryApplyCompoundAssignment(expression, expression.Value, reference, environment, context,
                out var refCompoundJsValue))
        {
            return refCompoundJsValue;
        }

        // Use JsValue version to avoid boxing
        var valueJs = EvaluateAssignmentRhsWithNameHintJsValue(expression, expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return valueJs;
        }

        try
        {
            reference.SetValue(valueJs);
            return valueJs;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                       StringComparison.Ordinal))
        {
            return expression.HandleReferenceError(ex, environment, context);
        }
    }

    private static JsValue HandleReferenceError(this AssignmentExpression _,
        InvalidOperationException ex,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var errorValue = (JsValue)ex.Message;

        // If a ReferenceError constructor is available, use it to
        // create a proper JS error instance so user code can catch
        // and inspect it.
        if (environment.TryGetObject<IJsCallable>(Symbol.ReferenceErrorIdentifier, out var callable))
        {
            errorValue = callable.Invoke(new SingleValueArgs((JsValue)ex.Message), JsValue.Undefined);
        }

        context.SetThrow(errorValue);
        return errorValue;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateMember(this MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Fast-path well-known symbol properties so expressions like
        // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
        // values that can be used as keys (e.g. o[Symbol.iterator]).
        if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
            string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
            expression.Property is LiteralExpression { Value.IsString: true } symbolPropLit)
        {
            var symbolProp = symbolPropLit.Value.AsString();
            return symbolProp switch
            {
                "iterator" => (JsValue)Symbols.Iterator,
                "asyncIterator" => (JsValue)Symbols.AsyncIterator,
                "toStringTag" => (JsValue)Symbols.ToStringTag,
                _ => expression.EvaluateDefaultMember(environment, context)
            };
        }

        return expression.EvaluateDefaultMember(environment, context);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateDefaultMember(this MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is ThisExpression && !environment.IsThisInitializationKnownTrue(context))
        {
            throw StandardLibrary.ThrowReferenceError(
                "Must call super constructor in derived class before accessing 'this'",
                context,
                context.RealmState);
        }

        if (expression.Target is SuperExpression)
        {
            var (memberValue, _) = expression.ResolveSuperMember(environment, context);
            return context.ShouldStopEvaluation ? JsValue.Undefined : memberValue;
        }

        var targetJs = EvaluateCachedExpressionProgram(
            expression.Target,
            environment,
            context,
            "Dynamic member target");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (expression.IsOptional && targetJs.IsNullOrUndefined)
        {
            return JsValue.Undefined;
        }

        if (targetJs.IsNullOrUndefined && HasOptionalChaining(expression.Target))
        {
            return JsValue.Undefined;
        }

        if (targetJs.IsNullOrUndefined)
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState);
            context.SetThrow(error);
            return JsValue.Undefined;
        }

        string? propertyName;
        if (expression is { IsComputed: false, Property: LiteralExpression { Value.IsString: true } literalProp })
        {
            propertyName = literalProp.Value.AsString();
        }
        else
        {
            var propertyValueJs = EvaluateCachedExpressionProgram(
                expression.Property,
                environment,
                context,
                "Dynamic member property");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            propertyName = JsOps.GetRequiredPropertyName(propertyValueJs, context);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        if (expression.IsComputed || !propertyName.IsPrivateName())
        {
            if (JsOps.TryGetPropertyValue(targetJs, propertyName, out var directValue, context))
            {
                return context.ShouldStopEvaluation
                    ? JsValue.Undefined
                    : directValue;
            }

            return JsValue.Undefined;
        }

        var handle = PropertyHandle.Resolve(
            targetJs,
            propertyName,
            context,
            context.CurrentScope.IsStrict,
            !expression.IsComputed);
        var value = handle.GetJsValue();
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return value;
    }

    private static (JsValue Value, SuperBinding Binding) ResolveSuperMember(this MemberExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (!environment.IsThisInitializationKnownTrue(context))
        {
            throw environment.CreateSuperReferenceError(context);
        }

        var binding = environment.ExpectSuperBinding(context);
        if (binding.Prototype is null)
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null (reading from super)",
                context,
                context.RealmState);
            context.SetThrow(error);
            return (JsValue.Undefined, binding);
        }

        var propertyValueJs = EvaluateCachedExpressionProgram(
            expression.Property,
            environment,
            context,
            "Dynamic super member property");
        if (context.ShouldStopEvaluation)
        {
            return (JsValue.Undefined, binding);
        }

        var propertyName = JsOps.GetRequiredPropertyName(propertyValueJs, context);
        if (context.ShouldStopEvaluation)
        {
            return (JsValue.Undefined, binding);
        }

        if (!binding.TryGetProperty(propertyName, out var value))
        {
            return (JsValue.Undefined, binding);
        }

        return (value, binding);
    }

    /// <summary>
    /// Hot path for call expressions - handles simple SyncFunctionInvoker calls without Activity overhead.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateCall(this CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        // Ultra-fast path for simple identifier calls to SyncFunctionInvokers (e.g., fib(n-1))
        // This is the most common case in recursive benchmarks
        if (
            expression is { IsOptional: false, Callee: IdentifierExpression calleeId, Arguments.Length: <= 2 })
        {
            // If the identifier was NOT statically resolved (SlotIndex < 0), it might be
            // in a dynamic scope (with/eval). Check for 'with' environment - if found, we need
            // to use the with-object as 'this', which the fast path doesn't handle.
            // When SlotIndex >= 0, the identifier was statically resolved, so we know
            // it's NOT coming from a 'with' binding and can skip this check.
            // IMPORTANT: Use HasWithObjectInChain() instead of TryResolveWithBinding() to avoid
            // triggering proxy 'has' and 'get' traps just to detect if we need the slow path.
            // The slow path's EvaluateCallTarget will do the actual with-binding resolution.
            if (calleeId.SlotIndex < 0 && environment.HasWithObjectInChain())
            {
                // Fall through to slow path which handles 'with' bindings correctly
                return expression.EvaluateCallSlow(environment, context);
            }

            // Check if all arguments are simple (no spread)
            var hasSpread = false;
            foreach (var arg in expression.Arguments)
            {
                if (arg.IsSpread)
                {
                    hasSpread = true;
                    break;
                }
            }

            if (!hasSpread)
            {
                // Fast slot-based lookup for the function
                JsValue calleeValue;
                if (calleeId is { SlotIndex: >= 0, ScopeId: >= 0 })
                {
                    calleeValue = environment.TryReadIdentifierWithSlot(
                        calleeId,
                        context,
                        out var slotCallee)
                        ? slotCallee
                        : context.GetIdentifier(environment, calleeId.Name);
                }
                else
                {
                    calleeValue = context.GetIdentifier(environment, calleeId.Name);
                }

                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                // Fast path for SyncFunctionInvoker only
                if (calleeValue.TryGetObject<SyncFunctionInvoker>(out var typedFunc) &&
                    !typedFunc.IsClassConstructor)
                {
                    if (++context.CallDepth > context.MaxCallDepth)
                    {
                        throw new InvalidOperationException(
                            $"Exceeded maximum call depth of {context.MaxCallDepth}.");
                    }

                    // Evaluate arguments and call specialized invoke - avoids array allocation
                    JsValue result;
                    switch (expression.Arguments.Length)
                    {
                        case 0:
                            result = typedFunc.InvokeWithContext([], JsValue.Undefined, context, JsValue.Undefined);
                            break;
                        case 1:
                            var arg0 = EvaluateCachedExpressionProgram(
                                expression.Arguments[0].Expression,
                                environment,
                                context,
                                "Dynamic call argument");
                            if (context.ShouldStopEvaluation)
                            {
                                context.CallDepth--;
                                return JsValue.Undefined;
                            }

                            // Use environment reuse optimization when eligible
                            result = expression.CanReuseCallerEnvironment
                                ? typedFunc.InvokeWithContext1Reuse(arg0, JsValue.Undefined, context, environment)
                                : typedFunc.InvokeWithContext1(arg0, JsValue.Undefined, context);
                            break;
                        default: // 2
                            var a0 = EvaluateCachedExpressionProgram(
                                expression.Arguments[0].Expression,
                                environment,
                                context,
                                "Dynamic call argument");
                            if (context.ShouldStopEvaluation)
                            {
                                context.CallDepth--;
                                return JsValue.Undefined;
                            }

                            var a1 = EvaluateCachedExpressionProgram(
                                expression.Arguments[1].Expression,
                                environment,
                                context,
                                "Dynamic call argument");
                            if (context.ShouldStopEvaluation)
                            {
                                context.CallDepth--;
                                return JsValue.Undefined;
                            }

                            result = typedFunc.InvokeWithContext2(a0, a1, JsValue.Undefined, context);
                            break;
                    }

                    context.CallDepth--;
                    return result;
                }
            }
        }

        // Fall through to slow path for complex cases
        return expression.EvaluateCallSlow(environment, context);
    }

    /// <summary>
    /// Slow path for complex call expressions - handles optional chaining, super calls, spread arguments, etc.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateCallSlow(this CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        // Fast-path for plain Map/Set method calls - bypasses prototype lookup and host function machinery
        if (expression.TryFastPathMapSetCall(environment, context, out var fastResult))
        {
            return fastResult;
        }

        var (callee, thisValue, skippedOptional) = expression.Callee.EvaluateCallTarget(environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            context.RealmState.Logger?.LogInformation(
                "EvaluateCall short-circuit callee={CalleeType} stopped={Stopped} optionalSkipped={Skipped}",
                expression.Callee.GetType().Name,
                context.ShouldStopEvaluation,
                skippedOptional);
            return JsValue.Undefined;
        }

        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        if (expression.IsOptional && callee.IsNullOrUndefined)
        {
            context.CallDepth--;
            return JsValue.Undefined;
        }

        // Per ES spec 15.7.14, default derived constructors forward arguments directly
        // without invoking the iterator protocol. Check if we're in such a context.
        var isDefaultDerivedConstructorSuperCall = expression.Callee is SuperExpression &&
                                                   environment.IsDefaultDerivedConstructor;

        var hasSpread = false;
        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                hasSpread = true;
                break;
            }
        }

        IReadOnlyList<JsValue> frozenArguments;
        object?[]? pooledArgsArray = null; // Track if we used a pooled object array
        JsValue[]? pooledJsValueArray = null; // Track if we used a pooled JsValue array
        if (!hasSpread)
        {
            var argCount = expression.Arguments.Length;
            switch (argCount)
            {
                case 0:
                    frozenArguments = [];
                    break;
                case 1:
                    {
                        var v0 = EvaluateCachedExpressionProgram(
                            expression.Arguments[0].Expression,
                            environment,
                            context,
                            "Dynamic call argument");
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        // Use pooled JsValue[] directly - avoid boxing via ToObject()
                        var jsArr = JsValueCache.RentJsValueArray(1);
                        jsArr[0] = v0;
                        pooledJsValueArray = jsArr;
                        frozenArguments = jsArr;
                        break;
                    }
                case 2:
                    {
                        var v0 = EvaluateCachedExpressionProgram(
                            expression.Arguments[0].Expression,
                            environment,
                            context,
                            "Dynamic call argument");
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        var v1 = EvaluateCachedExpressionProgram(
                            expression.Arguments[1].Expression,
                            environment,
                            context,
                            "Dynamic call argument");
                        if (context.ShouldStopEvaluation)
                        {
                            context.CallDepth--;
                            return JsValue.Undefined;
                        }

                        // Use pooled JsValue[] directly - avoid boxing via ToObject()
                        var jsArr = JsValueCache.RentJsValueArray(2);
                        jsArr[0] = v0;
                        jsArr[1] = v1;
                        pooledJsValueArray = jsArr;
                        frozenArguments = jsArr;
                        break;
                    }
                default:
                    {
                        // Use pooled JsValue[] for small argument counts (3-4)
                        var jsArgsArray = argCount <= 4
                            ? JsValueCache.RentJsValueArray(argCount)
                            : new JsValue[argCount];

                        if (argCount <= 4)
                        {
                            pooledJsValueArray = jsArgsArray;
                        }

                        for (var i = 0; i < argCount; i++)
                        {
                            jsArgsArray[i] = EvaluateCachedExpressionProgram(
                                expression.Arguments[i].Expression,
                                environment,
                                context,
                                "Dynamic call argument");
                            if (context.ShouldStopEvaluation)
                            {
                                if (pooledJsValueArray is not null)
                                {
                                    JsValueCache.ReturnJsValueArray(pooledJsValueArray);
                                }

                                context.CallDepth--;
                                return JsValue.Undefined;
                            }
                        }

                        frozenArguments = jsArgsArray;
                        break;
                    }
            }
        }
        else
        {
            var argsBuilder = ImmutableArray.CreateBuilder<JsValue>(expression.Arguments.Length);
            foreach (var argument in expression.Arguments)
            {
                if (argument.IsSpread)
                {
                    var spreadValueJs = EvaluateCachedExpressionProgram(
                        argument.Expression,
                        environment,
                        context,
                        "Dynamic call spread argument");
                    if (context.ShouldStopEvaluation)
                    {
                        context.CallDepth--;
                        return JsValue.Undefined;
                    }

                    // For default derived constructor super calls, bypass the iterator protocol
                    // and directly iterate array items per ES spec 15.7.14.
                    if (isDefaultDerivedConstructorSuperCall &&
                        spreadValueJs.TryGetObject<JsArray>(out var jsArray))
                    {
                        foreach (var item in jsArray.Items)
                        {
                            // Items is already IReadOnlyList<JsValue>, no wrapping needed
                            argsBuilder.Add(item);
                        }
                    }
                    else
                    {
                        argsBuilder.AddRange(EnumerateSpread(spreadValueJs, context));
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        context.CallDepth--;
                        return JsValue.Undefined;
                    }

                    continue;
                }

                argsBuilder.Add(EvaluateCachedExpressionProgram(
                    argument.Expression,
                    environment,
                    context,
                    "Dynamic call argument"));
                if (context.ShouldStopEvaluation)
                {
                    context.CallDepth--;
                    return JsValue.Undefined;
                }
            }

            frozenArguments = FreezeArguments(argsBuilder);
        }

        if (!callee.TryGetObject<IJsCallable>(out var callable))
        {
            // Special-case Function.prototype.apply / call patterns such as
            // Object.prototype.hasOwnProperty.apply(target, args).
            if (expression.Callee is MemberExpression member)
            {
                if (thisValue.TryGetObject<IJsCallable>(out var targetFunction) &&
                    member.Property is LiteralExpression { Value.IsString: true } propLit)
                {
                    var propertyName = propLit.Value.AsString();
                    if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                    {
                        return targetFunction.InvokeWithApply(expression.Arguments, environment, context);
                    }

                    if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                    {
                        return targetFunction.InvokeWithCall(expression.Arguments, environment, context);
                    }
                }

                // Fallback for patterns like `obj.formatArgs.call(this, ...)`
                // where `formatArgs` is a callable copied onto `obj` but the
                // `.call` helper is missing or not modeled. In that case we
                // invoke the underlying function directly with the provided
                // `this` value and arguments instead of throwing.
                if (member is
                    {
                        Property: LiteralExpression { Value.IsString: true } callLit, Target: MemberExpression
                        {
                            Property: LiteralExpression { Value.IsString: true } formatArgsLit
                        } inner
                    } && string.Equals(callLit.Value.AsString(), "call", StringComparison.Ordinal) &&
                    string.Equals(formatArgsLit.Value.AsString(), "formatArgs", StringComparison.Ordinal))
                {
                    var targetJs = EvaluateCachedExpressionProgram(
                        inner.Target,
                        environment,
                        context,
                        "Dynamic call fallback target");
                    if (context.ShouldStopEvaluation)
                    {
                        return JsValue.Undefined;
                    }

                    if (JsOps.TryGetPropertyValue(targetJs, "formatArgs", out var innerValueJs) &&
                        innerValueJs.TryGetObject<IJsCallable>(out var innerFunction))
                    {
                        return innerFunction.InvokeWithCall(expression.Arguments, environment, context);
                    }
                }
            }

            var calleeObj = callee.Kind == JsValueKind.Object ? callee.ObjectValue : null;
            var typeName = calleeObj?.GetType().Name ?? callee.Kind.ToString();
            var sourceInfo = context.GetSourceInfo(expression.Source);
            var symbolName = calleeObj is Symbol sym ? sym.Name : null;
            var symbolSuffix = symbolName is null ? string.Empty : $" (symbol '{symbolName}')";
            var calleeDescription = expression.Callee.DescribeCallee();
            var thisObj = thisValue.Kind == JsValueKind.Object ? thisValue.ObjectValue : null;
            context.RealmState.Logger?.LogInformation(
                "[EvaluateCall] Non-callable callee={Callee} type={Type} thisValueType={ThisType}{SymbolSuffix}{SourceInfo}",
                calleeDescription,
                typeName,
                thisObj?.GetType().Name ?? thisValue.Kind.ToString(),
                symbolSuffix,
                sourceInfo);
            var error = StandardLibrary.CreateTypeError(
                $"Attempted to call a non-callable value '{calleeDescription}' of type '{typeName}'{symbolSuffix}.",
                context,
                context.RealmState);
            context.SetThrow(error);
            context.CallDepth--;
            return JsValue.Undefined;
        }

        // Class constructors cannot be invoked without 'new' (except via super() call)
        if (callable is SyncFunctionInvoker { IsClassConstructor: true } classConstructor &&
            expression.Callee is not SuperExpression)
        {
            var error = StandardLibrary.CreateTypeError(
                "Class constructor cannot be invoked without 'new'",
                context,
                classConstructor.RealmState);
            context.SetThrow(error);
            context.CallDepth--;
            return JsValue.Undefined;
        }

        var isAsyncCallable = callable is SyncFunctionInvoker { IsAsyncLike: true };

        IJsEnvironmentAwareCallable? envAwareHandle = null;
        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
            envAwareHandle = envAware;
        }

        IEvaluationContextAwareCallable? contextAwareHandle = null;
        if (callable is IEvaluationContextAwareCallable contextAware)
        {
            contextAware.CallingContext = context;
            contextAwareHandle = contextAware;
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var callResult = JsValue.Undefined;
        var newTargetForCall = JsValue.Undefined;
        if (expression.Callee is SuperExpression &&
            environment.TryGetJsValue(Symbol.NewTarget, out var inheritedNewTarget))
        {
            newTargetForCall = inheritedNewTarget;
        }

        SuperBinding? superBindingForCall = null;
        if (expression.Callee is SuperExpression)
        {
            superBindingForCall = environment.ExpectSuperBinding(context);
            // Per ES spec 12.3.5.1 SuperCall:
            // After ArgumentListEvaluation, check if the super constructor is actually a constructor.
            // If IsConstructor(func) is false, throw a TypeError exception.
            if (!JsOps.IsConstructor(JsValue.FromObjectUnsafe(callable)))
            {
                var error = StandardLibrary.CreateTypeError(
                    "Super constructor is not a constructor",
                    context,
                    context.RealmState);
                context.SetThrow(error);
                context.CallDepth--;
                return JsValue.Undefined;
            }
        }

        EvalHostFunction? evalHost = null;
        if (callable is EvalHostFunction evalHostFunction)
        {
            evalHost = evalHostFunction;
            var isDirectEvalCall = expression is
            { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } } &&
                                   thisValue.IsUndefined &&
                                   ReferenceEquals(evalHostFunction.Engine, environment.RealmState?.Engine);
            evalHost.IsDirectCall = isDirectEvalCall;
            evalHost.InClassFieldInitializer = context.InClassFieldInitializer;
        }

        JsEnvironment? thisInitializationEnvironment = null;
        var thisInitializationValue = JsValue.Undefined;
        if (expression.Callee is SuperExpression)
        {
            // First check if we're in an arrow function that has captured a lexical this environment.
            // Arrow functions store a reference to the original constructor's environment so super()
            // can update the correct `this` binding.
            if (environment.TryFindBindingJsValue(Symbol.LexicalThisEnvironment, true, out _,
                    out var lexicalEnvValue) &&
                lexicalEnvValue.TryGetObject<JsEnvironment>(out var lexicalThisEnv))
            {
                thisInitializationEnvironment = lexicalThisEnv;
                if (lexicalThisEnv.TryGetJsValue(Symbol.ThisInitialized, out var lexicalInitValue))
                {
                    thisInitializationValue = lexicalInitValue;
                }
            }
            // Otherwise, prefer the environment that owns the current `this` binding; the [[ThisInitialized]]
            // marker is defined alongside it for derived constructors.
            else if (environment.TryFindBindingJsValue(Symbol.This, true, out var thisEnv, out _))
            {
                thisInitializationEnvironment = thisEnv.ResolveConstructorThisEnvironment();
                if (thisInitializationEnvironment.TryGetJsValue(Symbol.ThisInitialized, out var initValue))
                {
                    thisInitializationValue = initValue;
                }
            }

            if (thisInitializationEnvironment is null &&
                environment.TryFindBindingJsValue(Symbol.ThisInitialized, true, out var foundEnv,
                    out var foundValue))
            {
                thisInitializationEnvironment = foundEnv;
                thisInitializationValue = foundValue;
            }
        }

        try
        {
            if (expression.Callee is SuperExpression)
            {
                var realm = context.RealmState ?? throw new InvalidOperationException("Realm is required.");
                var newTargetCallable = newTargetForCall.TryGetObject<IJsCallable>(out var nt)
                    ? nt
                    : callable;
                callResult = ReflectHelper.Construct(callable, frozenArguments, newTargetCallable, realm);
            }
            else
            {
                callResult = callable switch
                {
                    SyncFunctionInvoker typedFunction => typedFunction.InvokeWithContext(frozenArguments, thisValue,
                        context,
                        newTargetForCall),
                    HostFunction hostFunction => hostFunction.InvokeWithContext(frozenArguments, thisValue, context,
                        newTargetForCall),
                    _ => callable.Invoke(frozenArguments, thisValue)
                };
            }

            if (expression.Callee is SuperExpression)
            {
                var callResultObj = callResult.Kind == JsValueKind.Object ? callResult.ObjectValue : null;
                var thisAfterSuper = callResultObj;
                if (callResultObj is not JsObject && callResultObj is not IJsObjectLike)
                {
                    thisAfterSuper = thisValue.Kind == JsValueKind.Object ? thisValue.ObjectValue : null;
                }

                context?.RealmState?.Logger?.LogInformation(
                    "Super call produced this type={Type}",
                    thisAfterSuper?.GetType().Name ?? "null");

                if (thisInitializationEnvironment is not null)
                {
                    var alreadyInitialized = thisInitializationValue.IsUndefined
                        ? thisInitializationEnvironment.TryGetJsValue(Symbol.ThisInitialized, out var initValue)
                            ? initValue
                            : JsValue.Undefined
                        : thisInitializationValue;

                    if (!alreadyInitialized.IsUndefined)
                    {
                        context?.RealmState?.Logger?.LogInformation(
                            "Super call pre-check thisInit env={Env} value={Value}",
                            thisInitializationEnvironment.GetHashCode(),
                            alreadyInitialized.Kind == JsValueKind.Object
                                ? alreadyInitialized.ObjectValue
                                : alreadyInitialized.Kind.ToString());
                        if (JsOps.ToBoolean(alreadyInitialized))
                        {
                            throw StandardLibrary.ThrowReferenceError(
                                "Super constructor may only be called once.", context, context?.RealmState);
                        }
                    }
                }

                var targetEnvironment = thisInitializationEnvironment ?? environment;
                var hasThisBinding = targetEnvironment.HasBinding(Symbol.This);
                string beforeType;
                try
                {
                    targetEnvironment.TryGetJsValue(Symbol.This, out var existingThis);
                    beforeType = existingThis.Kind == JsValueKind.Object
                        ? existingThis.ObjectValue?.GetType().Name ?? "null"
                        : existingThis.Kind.ToString();
                }
                catch (Exception ex)
                {
                    beforeType = ex.GetType().Name;
                }

                context?.RealmState?.Logger?.LogInformation(
                    "Super assigning this (hasBinding={HasBinding}, beforeType={BeforeType})",
                    hasThisBinding,
                    beforeType);
                targetEnvironment.AssignJsValue(Symbol.This, JsValue.FromObjectUnsafe(thisAfterSuper));
                if (!ReferenceEquals(environment, targetEnvironment))
                {
                    environment.AssignJsValue(Symbol.This, JsValue.FromObjectUnsafe(thisAfterSuper));
                }
                try
                {
                    targetEnvironment.TryGetJsValue(Symbol.This, out var afterThis);
                    context?.RealmState?.Logger?.LogInformation("Super assigned this now type={Type}",
                        afterThis.Kind == JsValueKind.Object
                            ? afterThis.ObjectValue?.GetType().Name ?? "null"
                            : afterThis.Kind.ToString());
                }
                catch (Exception ex)
                {
                    context?.RealmState?.Logger?.LogInformation("Super assigned this lookup failed {ErrorType}",
                        ex.GetType().Name);
                }

                if (targetEnvironment.TryGetObject<SuperBinding>(Symbol.Super, out var binding))
                {
                    var constructorForSuper = superBindingForCall?.Constructor ?? binding.Constructor;
                    var prototypeForSuper = superBindingForCall?.Prototype ?? binding.Prototype;
                    targetEnvironment.AssignJsValue(Symbol.Super,
                        JsValue.FromObjectUnsafe(new SuperBinding(constructorForSuper, prototypeForSuper,
                            JsValue.FromObjectUnsafe(thisAfterSuper), true)));
                }

                context!.MarkThisInitialized();
                targetEnvironment.SetThisInitializationStatus(context.IsThisInitialized);

                if (thisAfterSuper is IJsObjectLike initializedThis &&
                    context.TryPopClassFieldInitializer(out var pendingInitializer) &&
                    pendingInitializer.Constructor is SyncFunctionInvoker pendingConstructor)
                {
                    pendingConstructor.InitializeInstance(
                        initializedThis,
                        pendingInitializer.Environment,
                        context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (context.IsThrow)
                        {
                            var thrownDuringInitialization = context.FlowValue;
                            context.Clear();
                            throw new ThrowSignal(thrownDuringInitialization);
                        }

                        return context.FlowValue;
                    }
                }
            }
        }
        catch (ThrowSignal signal)
        {
            var thrownObj = signal.ThrownValue.Kind == JsValueKind.Object ? signal.ThrownValue.ObjectValue : null;
            context.RealmState.Logger?.LogInformation(
                "EvaluateCall caught ThrowSignal type={Type} calleeType={CalleeType}",
                thrownObj?.GetType().Name ?? signal.ThrownValue.Kind.ToString(),
                callable.GetType().Name);
            if (isAsyncCallable)
            {
                context.Clear();
                callResult = CreateRejectedPromise(signal.ThrownValue, environment);
            }
            else
            {
                context.SetThrow(signal.ThrownValue);
                return signal.ThrownValue;
            }
        }
        catch (Exception ex) when (isAsyncCallable)
        {
            // Any synchronous failure while invoking an async function should surface
            // as a rejected promise rather than throwing out of the call.
            context.Clear();
            callResult = CreateRejectedPromise(JsValue.FromObjectUnsafe(ex), environment);
        }
        finally
        {
            if (evalHost is not null)
            {
                evalHost.IsDirectCall = false;
                evalHost.InClassFieldInitializer = false;
            }

            context.CallDepth--;

            debugFunction?.CurrentJsEnvironment = null;
            debugFunction?.CurrentContext = null;

            envAwareHandle?.CallingJsEnvironment = null;
            contextAwareHandle?.CallingContext = null;

            // Return pooled argument arrays
            if (pooledArgsArray is not null)
            {
                JsValueCache.ReturnArgumentArray(pooledArgsArray);
            }

            if (pooledJsValueArray is not null)
            {
                JsValueCache.ReturnJsValueArray(pooledJsValueArray);
            }
        }

        switch (isAsyncCallable)
        {
            // If an async callable left a pending throw signal (e.g., default parameter TDZ),
            // translate it into a rejected promise and clear the signal so it does not
            // escape to the caller's context.
            case true when context.IsThrow:
                {
                    var reason = context.FlowValue;
                    context.Clear();
                    return CreateRejectedPromise(reason, environment);
                }
            case true:
                // Async functions should never propagate a throw signal; ensure the
                // calling context stays clear.
                context.Clear();
                break;
        }

        return callResult;
    }

    /// <summary>
    ///     Fast-path for plain Map/Set method calls.
    ///     Bypasses prototype lookup, host function creation, and argument array allocation.
    /// </summary>
    [MethodImpl(JsEngineConstants.Inlining)]
    private static bool TryFastPathMapSetCall(
        this CallExpression callExpr,
        JsEnvironment environment,
        EvaluationContext context,
        out JsValue result)
    {
        result = JsValue.Unit;

        // Only handle simple member expression calls like map.set(), map.get(), etc.
        if (callExpr.Callee is not MemberExpression { IsComputed: false, IsOptional: false } member)
        {
            return false;
        }

        // IMPORTANT: Only use fast path for simple identifier targets (e.g., `myMap.set(...)`)
        // If we evaluate a complex target expression (like `getMap().set(...)`) and it's NOT
        // a Map/Set, the normal path would evaluate it again, causing double execution!
        if (member.Target is not IdentifierExpression)
        {
            return false;
        }

        // Get the method name
        var methodName = member.Property switch
        {
            IdentifierExpression id => id.Name.Name,
            LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
            _ => null
        };

        if (methodName is null)
        {
            return false;
        }

        // Evaluate the target (map or set instance) - safe because it's just an identifier lookup
        var targetJs = EvaluateCachedExpressionProgram(
            member.Target,
            environment,
            context,
            "Dynamic Map/Set call target");
        if (context.ShouldStopEvaluation)
        {
            result = JsValue.Undefined;
            return true;
        }

        // Fast-path for JsMap
        if (targetJs.TryGetObject<JsMap>(out var map) && map.IsPlain)
        {
            // Check for spread arguments - fall back to normal path if any
            var hasSpread = false;
            foreach (var arg in callExpr.Arguments)
            {
                if (arg.IsSpread)
                {
                    hasSpread = true;
                    break;
                }
            }

            if (!hasSpread)
            {
                result = methodName switch
                {
                    "set" when callExpr.Arguments.Length >= 2 => FastMapSet(map, callExpr.Arguments,
                        environment, context),
                    "get" when callExpr.Arguments.Length >= 1 => FastMapGet(map, callExpr.Arguments,
                        environment, context),
                    "has" when callExpr.Arguments.Length >= 1 => FastMapHas(map, callExpr.Arguments,
                        environment, context),
                    "delete" when callExpr.Arguments.Length >= 1 => FastMapDelete(map,
                        callExpr.Arguments, environment, context),
                    "clear" => FastMapClear(map),
                    _ => JsValue
                        .Unit // Fall back to normal path for other methods (size is a property, not a method)
                };

                if (!result.IsUnit)
                {
                    return true;
                }
            }
        }

        // Fast-path for JsSet
        if (targetJs.TryGetObject<JsSet>(out var set) && set.IsPlain)
        {
            // Check for spread arguments - fall back to normal path if any
            var hasSpread = false;
            foreach (var arg in callExpr.Arguments)
            {
                if (arg.IsSpread)
                {
                    hasSpread = true;
                    break;
                }
            }

            if (!hasSpread)
            {
                result = methodName switch
                {
                    "add" when callExpr.Arguments.Length >= 1 => FastSetAdd(set, callExpr.Arguments,
                        environment, context),
                    "has" when callExpr.Arguments.Length >= 1 => FastSetHas(set, callExpr.Arguments,
                        environment, context),
                    "delete" when callExpr.Arguments.Length >= 1 => FastSetDelete(set,
                        callExpr.Arguments, environment, context),
                    "clear" => FastSetClear(set),
                    _ => JsValue
                        .Unit // Fall back to normal path for other methods (size is a property, not a method)
                };

                if (!result.IsUnit)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---- Map fast-path helpers ----

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastMapSet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var key = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Map.set key");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var value = EvaluateCachedExpressionProgram(
            args[1].Expression,
            env,
            ctx,
            "Dynamic Map.set value");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        map.Set(key, value);
        return JsValue.FromObjectUnsafe((IJsObjectLike)map);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastMapGet(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var key = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Map.get key");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return map.Get(key);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastMapHas(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var key = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Map.has key");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return map.Has(key) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastMapDelete(JsMap map, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var key = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Map.delete key");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return map.Delete(key) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastMapClear(JsMap map)
    {
        map.Clear();
        return JsValue.Undefined;
    }

    // ---- Set fast-path helpers ----

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastSetAdd(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var value = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Set.add value");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        set.Add(value);
        return JsValue.FromObjectUnsafe(set);
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastSetHas(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var value = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Set.has value");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return set.Has(value) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastSetDelete(JsSet set, ImmutableArray<CallArgument> args, JsEnvironment env,
        EvaluationContext ctx)
    {
        var value = EvaluateCachedExpressionProgram(
            args[0].Expression,
            env,
            ctx,
            "Dynamic Set.delete value");
        if (ctx.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return set.Delete(value) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(JsEngineConstants.Inlining)]
    private static JsValue FastSetClear(JsSet set)
    {
        set.Clear();
        return JsValue.Undefined;
    }

    private static JsValue InvokeWithApply(this IJsCallable targetFunction, ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var thisArg = JsValue.Undefined;
        if (callArguments.Length > 0)
        {
            thisArg = EvaluateCachedExpressionProgram(
                callArguments[0].Expression,
                environment,
                context,
                "Dynamic Function.apply thisArg");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        var argsBuilder = ImmutableArray.CreateBuilder<JsValue>();
        if (callArguments.Length > 1)
        {
            var argsArrayJs = EvaluateCachedExpressionProgram(
                callArguments[1].Expression,
                environment,
                context,
                "Dynamic Function.apply arguments");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            argsBuilder.AddRange(EnumerateSpread(argsArrayJs, context));

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        if (targetFunction is SyncFunctionInvoker typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static JsValue InvokeWithCall(this IJsCallable targetFunction, ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var thisArg = JsValue.Undefined;
        var argsBuilder = ImmutableArray.CreateBuilder<JsValue>();

        for (var i = 0; i < callArguments.Length; i++)
        {
            var argValue = EvaluateCachedExpressionProgram(
                callArguments[i].Expression,
                environment,
                context,
                "Dynamic Function.call argument");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (i == 0)
            {
                thisArg = argValue;
            }
            else
            {
                argsBuilder.Add(argValue);
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        if (targetFunction is SyncFunctionInvoker typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    /// <summary>
    /// Slow path for less common expression types. Marked NoInlining to keep hot path small.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateExpressionSlow(this ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Second tier of common expressions
        switch (expression)
        {
            case UnaryExpression unary:
                return unary.EvaluateUnary(environment, context);
            case AssignmentExpression assignment:
                return assignment.EvaluateAssignment(environment, context);
            case MemberExpression member:
                return member.EvaluateMember(environment, context);
            case CallExpression call:
                return call.EvaluateCall(environment, context);
        }

        // Slowest path - set source reference and optionally trace
        context.SourceReference = expression.Source;

        return expression switch
        {
            RegexLiteralExpression regex => new JsValue(RegExpHelper.CreateRegExpLiteral(
                regex.Pattern,
                regex.Flags,
                context.RealmState)),
            ConditionalExpression conditional => EvaluateConditionalExpression(
                conditional,
                environment,
                context),
            FunctionExpression functionExpression => JsValue.FromObjectUnsafe(
                functionExpression.CreateFunctionValue(
                    environment,
                    context,
                    planSeed: FunctionLiteralDescriptor.Create(functionExpression).PlanSeed)),
            DestructuringAssignmentExpression destructuringAssignment => EvaluateCachedExpressionProgram(
                destructuringAssignment,
                environment,
                context,
                "Dynamic destructuring assignment"),
            PropertyAssignmentExpression propertyAssignment => EvaluateCachedExpressionProgram(
                propertyAssignment,
                environment,
                context,
                "Dynamic property assignment"),
            IndexAssignmentExpression indexAssignment => EvaluateCachedExpressionProgram(
                indexAssignment,
                environment,
                context,
                "Dynamic index assignment"),
            SequenceExpression sequence => EvaluateCachedExpressionProgram(
                sequence,
                environment,
                context,
                "Dynamic sequence expression"),
            NewExpression newExpression => EvaluateCachedExpressionProgram(
                newExpression,
                environment,
                context,
                "Dynamic new expression"),
            NewTargetExpression => environment.TryGetJsValue(Symbol.NewTarget, out var newTarget)
                ? newTarget
                : JsValue.Undefined,
            ImportMetaExpression => EvaluateImportMeta(environment, context),
            ArrayExpression array => EvaluateArrayExpression(
                array,
                environment,
                context),
            ObjectExpression obj => EvaluateObjectExpression(
                obj,
                environment,
                context),
            ClassExpression classExpression => EvaluateClassExpressionValue(
                classExpression,
                environment,
                context),
            DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
            TemplateLiteralExpression template => EvaluateTemplateLiteralExpression(
                template,
                environment,
                context),
            TaggedTemplateExpression taggedTemplate => taggedTemplate.EvaluateTaggedTemplate(environment, context),
            AwaitExpression awaitExpression => awaitExpression.EvaluateAwait(environment, context),
            YieldExpression yieldExpression => yieldExpression.EvaluateYield(environment, context),
            ThisExpression => ResolveThisValue(environment, context),
            SuperExpression => throw new InvalidOperationException(
                $"Super is not available in this context.{context.GetSourceInfo(expression.Source)}"),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateConditionalExpression(
        ConditionalExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateCachedExpressionProgram(
            expression.Test,
            environment,
            context,
            "Dynamic conditional test");
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return test.IsTruthy
            ? EvaluateCachedExpressionProgram(
                expression.Consequent,
                environment,
                context,
                "Dynamic conditional consequent")
            : EvaluateCachedExpressionProgram(
                expression.Alternate,
                environment,
                context,
                "Dynamic conditional alternate");
    }

    private static string GetTypeofStringValue(in JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.BigInt => "bigint",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.Object => GetTypeofStringForObject(value.ObjectValue),
            _ => "undefined"
        };
    }

    private static string GetTypeofStringForObject(object? obj)
    {
        if (obj is IIsHtmlDda)
        {
            return "undefined";
        }

        if (obj is JsProxy proxy)
        {
            return proxy.IsCallableTarget() ? "function" : "object";
        }

        return obj is IJsCallable ? "function" : "object";
    }

    private static JsValue BitwiseNotValue(in JsValue operand, EvaluationContext context)
    {
        return BitwiseNotJsValue(in operand, context);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateClassExpressionValue(
        ClassExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var inferredName = expression.Name ?? context.CurrentFunctionNameHint;
        return expression.Definition.CreateClassValue(environment, context, inferredName);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateArrayExpression(
        ArrayExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray(context.RealmState);
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValueJs = EvaluateCachedExpressionProgram(
                    element.Expression!,
                    environment,
                    context,
                    "Dynamic array spread expression");
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValueJs, context))
                {
                    array.Push(item);
                }

                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                continue;
            }

            if (element.Expression is null)
            {
                array.PushHole();
            }
            else
            {
                array.Push(EvaluateCachedExpressionProgram(
                    element.Expression,
                    environment,
                    context,
                    "Dynamic array element expression"));
            }

            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        return JsValue.FromJsArray(array);
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateObjectExpression(
        ObjectExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        obj.RealmState = context.RealmState;
        if (context.RealmState.ObjectPrototype is { } objectProto)
        {
            obj.SetPrototype(objectProto);
        }

        foreach (var member in expression.Members)
        {
            switch (member.Kind)
            {
                case ObjectMemberKind.Property:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : EvaluateCachedExpressionProgram(
                                member.Value,
                                environment,
                                context,
                                "Dynamic object property value");

                        if (!member.IsComputed &&
                            string.Equals(name, "__proto__", StringComparison.Ordinal) &&
                            member.Parameter is null)
                        {
                            if (valueJs.IsNull)
                            {
                                obj.SetPrototype(null);
                            }
                            else if (valueJs.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
                            {
                                obj.SetPrototype(protoAccessor);
                            }

                            break;
                        }

                        if (valueJs.ObjectValue is IFunctionNameTarget nameTarget &&
                            member.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                        {
                            nameTarget.EnsureHasName(BuildFunctionNameDisplay(name));
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Method:
                    {
                        var function = member.Function!;
                        var callable = function.CreateFunctionValue(
                            environment,
                            context,
                            false,
                            planSeed: FunctionLiteralDescriptor.Create(function).PlanSeed);
                        if (callable is SyncFunctionInvoker typed)
                        {
                            typed.SetHomeObject(obj);
                            typed.DisableConstruction();
                        }
                        else if (callable is SyncGeneratorInvoker generatorFactory)
                        {
                            generatorFactory.SetHomeObject(obj);
                            generatorFactory.DisableConstruction();
                        }
                        else if (callable is AsyncGeneratorFunctionInvoker asyncGeneratorFactory)
                        {
                            asyncGeneratorFactory.SetHomeObject(obj);
                            asyncGeneratorFactory.DisableConstruction();
                        }

                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (callable is IFunctionNameTarget nameTarget)
                        {
                            nameTarget.EnsureHasName(BuildFunctionNameDisplay(name));
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                Value = callable,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Getter:
                    {
                        var function = member.Function!;
                        var planSeed = FunctionLiteralDescriptor.Create(function).PlanSeed;
                        var getter = function.CreateFunctionValue(
                            environment,
                            context,
                            false,
                            planSeed: planSeed) as SyncFunctionInvoker
                                     ?? throw new InvalidOperationException(
                                         "Object getter must materialize as a sync function.");
                        getter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        getter.EnsureHasName($"get {BuildFunctionNameDisplay(name)}");

                        obj.DefineAccessorProperty(name, getter, null);
                        break;
                    }
                case ObjectMemberKind.Setter:
                    {
                        var function = member.Function!;
                        var planSeed = FunctionLiteralDescriptor.Create(function).PlanSeed;
                        var setter = function.CreateFunctionValue(
                            environment,
                            context,
                            false,
                            planSeed: planSeed) as SyncFunctionInvoker
                                     ?? throw new InvalidOperationException(
                                         "Object setter must materialize as a sync function.");
                        setter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        setter.EnsureHasName($"set {BuildFunctionNameDisplay(name)}");

                        obj.DefineAccessorProperty(name, null, setter);
                        break;
                    }
                case ObjectMemberKind.Field:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : EvaluateCachedExpressionProgram(
                                member.Value,
                                environment,
                                context,
                                "Dynamic object field value");
                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Spread:
                    {
                        var spreadValueJs = EvaluateCachedExpressionProgram(
                            member.Value!,
                            environment,
                            context,
                            "Dynamic object spread value");
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (spreadValueJs.IsNullOrUndefined)
                        {
                            break;
                        }

                        if (spreadValueJs.ObjectValue is IIsHtmlDda)
                        {
                            break;
                        }

                        if (spreadValueJs.ObjectValue is IDictionary<string, object?> dictionary and not JsObject)
                        {
                            foreach (var kvp in dictionary)
                            {
                                obj.DefineProperty(kvp.Key,
                                    new PropertyDescriptor
                                    {
                                        Value = kvp.Value,
                                        Writable = true,
                                        Enumerable = true,
                                        Configurable = true
                                    });
                            }

                            break;
                        }

                        var accessor = spreadValueJs.ObjectValue is IJsPropertyAccessor propertyAccessor
                            ? propertyAccessor
                            : ToObjectForDestructuringJsValue(spreadValueJs, context);

                        foreach (var key in accessor.GetOwnPropertyKeysInOrder(includeSymbols: true, includeNonEnumerable: true))
                        {
                            var desc = accessor.GetOwnPropertyDescriptor(key);
                            if (desc is not { Enumerable: true })
                            {
                                continue;
                            }

                            var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                                ? val
                                : JsValue.Undefined;
                            obj.DefineProperty(key,
                                new PropertyDescriptor
                                {
                                    JsValue = spreadPropertyValue,
                                    Writable = true,
                                    Enumerable = true,
                                    Configurable = true
                                });
                        }

                        break;
                    }
            }
        }

        return new JsValue(obj);
    }

    private static string BuildFunctionNameDisplay(this string propertyName)
    {
        if (JsSymbol.TryGetByInternalKey(propertyName, out var symbol))
        {
            return symbol!.Description is null ? string.Empty : $"[{symbol.Description}]";
        }

        return propertyName;
    }

    private static string ResolveObjectMemberName(
        ObjectMember member,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            var keyValue = EvaluateCachedExpressionProgram(
                keyExpression,
                environment,
                context,
                "Dynamic object member name");
            if (context.ShouldStopEvaluation)
            {
                return string.Empty;
            }

            var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
            return context.ShouldStopEvaluation ? string.Empty : propertyName;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        var propertyNameFromKey = JsOps.GetRequiredPropertyName(JsValue.FromObjectUnsafe(member.Key), context);
        return context.ShouldStopEvaluation ? string.Empty : propertyNameFromKey;
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateUnary(
        this UnaryExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression is
            {
                Operator: UnaryOperator.Delete,
                Operand: MemberExpression { IsOptional: true }
            })
        {
            return expression.Operand.EvaluateDelete(environment, context) ? JsValue.True : JsValue.False;
        }

        return EvaluateCachedExpressionProgram(
            expression,
            environment,
            context,
            "Dynamic unary expression");
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTemplateLiteralExpression(
        TemplateLiteralExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in expression.Parts)
        {
            if (part.Text is not null)
            {
                builder.Append(part.Text);
                continue;
            }

            if (part.Expression is null)
            {
                continue;
            }

            var valueJs = EvaluateCachedExpressionProgram(
                part.Expression,
                environment,
                context,
                "Dynamic template literal expression");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            builder.Append(valueJs.ToJsString());
        }

        return new JsValue(builder.ToString());
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static JsValue EvaluateTaggedTemplate(
        this TaggedTemplateExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        var (tagValue, thisValue, skippedOptional) = expression.Tag.EvaluateCallTarget(environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return JsValue.Undefined;
        }

        if (!tagValue.TryGetObject<IJsCallable>(out var callable))
        {
            var error = StandardLibrary.CreateTypeError(
                "Tag in tagged template must be a function.",
                context,
                environment.RealmState);
            throw new ThrowSignal(error);
        }

        // Per ES spec 13.2.8.4 GetTemplateObject, template objects are cached by parse node.
        var realmState = context.RealmState;
        JsArray templateObject;
        if (realmState.TemplateObjectCache.TryGetValue(expression, out var cachedTemplate))
        {
            templateObject = (JsArray)cachedTemplate;
        }
        else
        {
            var stringsArrayValueJs = EvaluateCachedExpressionProgram(
                expression.StringsArray,
                environment,
                context,
                "Dynamic tagged template cooked strings");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (!stringsArrayValueJs.TryGetObject<JsArray>(out var stringsArray))
            {
                throw new InvalidOperationException("Tagged template strings array is invalid.");
            }

            var rawStringsArrayValueJs = EvaluateCachedExpressionProgram(
                expression.RawStringsArray,
                environment,
                context,
                "Dynamic tagged template raw strings");
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            if (!rawStringsArrayValueJs.TryGetObject<JsArray>(out var rawStringsArray))
            {
                throw new InvalidOperationException("Tagged template raw strings array is invalid.");
            }

            templateObject = (JsArray)stringsArray.CreateTemplateObject(rawStringsArray);
            realmState?.TemplateObjectCache[expression] = templateObject;
        }

        var arguments = ImmutableArray.CreateBuilder<JsValue>(expression.Expressions.Length + 1);
        arguments.Add(JsValue.FromJsArray(templateObject));

        foreach (var expr in expression.Expressions)
        {
            arguments.Add(EvaluateCachedExpressionProgram(
                expr,
                environment,
                context,
                "Dynamic tagged template argument"));
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }
        }

        DebugAwareHostFunction? debugFunction = null;
        if (callable is DebugAwareHostFunction debugAware)
        {
            debugFunction = debugAware;
            debugFunction.CurrentJsEnvironment = environment;
            debugFunction.CurrentContext = context;
        }

        var frozenArguments = FreezeArguments(arguments);

        try
        {
            return InvokeCallableJsValue(
                callable,
                frozenArguments,
                thisValue,
                context,
                environment);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }
        finally
        {
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }
        }
    }

    private static string DescribeCallee(this ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression id => id.Name.Name,
            MemberExpression member => $"{member.Target.DescribeCallee()}.{member.Property.DescribeMemberName()}",
            CallExpression call => $"{call.Callee.DescribeCallee()}(...)",
            _ => expression.GetType().Name
        };
    }

    private static bool IsAnonymousFunctionDefinition(this ExpressionNode expression)
    {
        return expression.IsAnonymousFunctionDefinitionNode();
    }

    private static bool IsAnonymousFunctionDefinitionNode(this ExpressionNode node)
    {
        // Per ES spec, sequence expressions (comma operator) do not qualify for name inference
        // e.g., `const x = (0, function() {})` should not infer name
        if (node is SequenceExpression)
        {
            return false;
        }

        return node switch
        {
            FunctionExpression func => func.Name is null,
            ClassExpression classExpression => classExpression.Name is null,
            _ => false
        };
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static (JsValue Callee, JsValue thisValue, bool SkippedOptional) EvaluateCallTarget(this ExpressionNode callee, JsEnvironment environment,
        EvaluationContext context)
    {
        switch (callee)
        {
            case SuperExpression superExpression:
                {
                    var logger = environment.RealmState?.Logger;
                    logger?.LogInformation(
                        "Super call target env={Env} hasThisInit={HasThisInit} hasSuper={HasSuper}",
                        environment.GetHashCode(),
                        environment.HasBinding(Symbol.ThisInitialized),
                        environment.HasBinding(Symbol.Super));
                    var binding = environment.ExpectSuperBinding(context);

                    // Per ES spec 12.3.5.1 SuperCall, the super constructor should be looked up
                    // dynamically via GetSuperConstructor() which gets activeFunction.[[Prototype]].
                    // For a constructor, the active function is available via NewTarget when it's
                    // a constructor being invoked via 'new'.
                    var dynamicSuperConstructor = environment.ResolveSuperConstructorForCall(binding);
                    if (environment.TryGetObject<IJsObjectLike>(Symbol.NewTarget, out var activeFunction))
                    {
                        logger?.LogInformation(
                            "Super call: dynamic lookup newTargetType={NewTargetType} protoType={ProtoType}",
                            activeFunction.GetType().Name,
                            dynamicSuperConstructor?.GetType().Name ?? "null");
                    }

                    if (dynamicSuperConstructor is null)
                    {
                        throw new InvalidOperationException(
                            $"Super constructor is not available in this context.{context.GetSourceInfo(superExpression.Source)}");
                    }

                    var superThis = binding.ThisValue.IsUninitialized
                        ? JsValue.Undefined
                        : binding.ThisValue;

                    return (JsValue.FromObjectUnsafe(dynamicSuperConstructor), superThis, false);
                }
            case MemberExpression { Target: SuperExpression } member:
                {
                    var (memberValue, binding) = member.ResolveSuperMember(environment, context);
                    return context.ShouldStopEvaluation
                        ? (JsValue.Undefined, thisValue: binding.ThisValue, true)
                        : (memberValue, thisValue: binding.ThisValue, false);
                }
            case MemberExpression member:
                {
                    var targetJs = EvaluateCachedExpressionProgram(
                        member.Target,
                        environment,
                        context,
                        "Dynamic call target member target");
                    if (context.ShouldStopEvaluation
                        || (member.IsOptional && targetJs.IsNullOrUndefined)
                        || (targetJs.IsNullOrUndefined && HasOptionalChaining(member.Target)))
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    if (targetJs.IsNullOrUndefined)
                    {
                        var error = StandardLibrary.CreateTypeError(
                            "Cannot read properties of null or undefined",
                            context,
                            context.RealmState);
                        context.SetThrow(error);
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    string propertyName;
                    if (member.IsComputed)
                    {
                        var propertyJs = EvaluateCachedExpressionProgram(
                            member.Property,
                            environment,
                            context,
                            "Dynamic call target computed property");
                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }

                        propertyName = JsOps.GetRequiredPropertyName(propertyJs, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }
                    }
                    else
                    {
                        propertyName = member.Property switch
                        {
                            IdentifierExpression id => id.Name.Name,
                            LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
                            _ => JsOps.GetRequiredPropertyName(
                                EvaluateCachedExpressionProgram(
                                    member.Property,
                                    environment,
                                    context,
                                    "Dynamic call target property"),
                                context)
                        };
                    }

                    if (member.IsComputed || !propertyName.IsPrivateName())
                    {
                        if (targetJs.TryGetObject<IJsPropertyAccessor>(out var accessor))
                        {
                            try
                            {
                                if (accessor.TryGetProperty(propertyName, targetJs, out var directJsValue))
                                {
                                    return (directJsValue, targetJs, false);
                                }
                            }
                            catch (ThrowSignal signal)
                            {
                                context.SetThrow(signal.ThrownValue);
                                return (JsValue.Undefined, JsValue.Undefined, true);
                            }
                        }

                        if (JsOps.TryGetPropertyValue(targetJs, propertyName, out var directValue, context))
                        {
                            if (context.ShouldStopEvaluation)
                            {
                                return (JsValue.Undefined, JsValue.Undefined, true);
                            }

                            return (directValue, targetJs, false);
                        }

                        if (context.ShouldStopEvaluation)
                        {
                            return (JsValue.Undefined, JsValue.Undefined, true);
                        }

                        return (JsValue.Undefined, targetJs, false);
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs,
                        propertyName,
                        context,
                        context.CurrentScope.IsStrict,
                        !member.IsComputed);
                    var value = handle.GetJsValue();
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    return (value, targetJs, false);
                }
            case IdentifierExpression identifier
                when environment.TryResolveWithBinding(identifier.Name, context, out var withBinding):
                try
                {
                    var withValue = JsEnvironment.GetWithBindingValueJsValue(withBinding);
                    return (withValue, JsValue.FromObjectUnsafe(withBinding.BindingObject), false);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:",
                                                               StringComparison.Ordinal))
                {
                    // Convert to JavaScript ReferenceError so it can be caught by JavaScript try-catch
                    var errorObject = StandardLibrary.CreateReferenceError(ex.Message, context, context.RealmState);
                    throw new ThrowSignal(errorObject);
                }

            // Fast path: use slot-based lookup when available
            case IdentifierExpression identifier:
                {
                    if (identifier is { SlotIndex: >= 0, ScopeId: >= 0 })
                    {
                        if (environment.TryReadIdentifierWithSlot(
                                identifier.Name,
                                identifier.ScopeId,
                                identifier.SlotIndex,
                                context,
                                out var slotCallee))
                        {
                            return (slotCallee, JsValue.Undefined, false);
                        }
                    }

                    // Fallback: dictionary-based lookup
                    var reference = environment.ResolveIdentifierAssignmentReference(identifier.Name, context);
                    if (reference.IsUnresolvable)
                    {
                        var error = StandardLibrary.CreateReferenceError(
                            $"{identifier.Name.Name} is not defined",
                            context,
                            context.RealmState);
                        context.SetThrow(error);
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    var calleeValue = AssignmentReferenceResolver.ReadIdentifierValue(reference.GetJsValue, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return (JsValue.Undefined, JsValue.Undefined, true);
                    }

                    return (calleeValue, JsValue.Undefined, false);
                }
            default:
                {
                    var directCallee = EvaluateCachedExpressionProgram(
                        callee,
                        environment,
                        context,
                        "Dynamic direct callee");
                    return (directCallee, JsValue.Undefined, false);
                }
        }
    }

    [Obsolete("This AST evaluation method is quarantined. Prefer IR execution via ExecutionPlanRunner.")]
    private static bool EvaluateDelete(this ExpressionNode operand, JsEnvironment environment, EvaluationContext context)
    {
        switch (operand)
        {
            case MemberExpression member:
                {
                    if (member.Target is SuperExpression)
                    {
                        throw StandardLibrary.ThrowReferenceError(
                            "Cannot delete property on super reference",
                            context,
                            context.RealmState);
                    }

                    var targetJs = EvaluateCachedExpressionProgram(
                        member.Target,
                        environment,
                        context,
                        "Dynamic delete member target");
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var propertyValueJs = EvaluateCachedExpressionProgram(
                        member.Property,
                        environment,
                        context,
                        "Dynamic delete member property");
                    if (context.ShouldStopEvaluation)
                    {
                        return false;
                    }

                    var handle = PropertyHandle.Resolve(
                        targetJs,
                        propertyValueJs,
                        context,
                        context.CurrentScope.IsStrict,
                        !member.IsComputed);
                    return handle.Delete();
                }
            case IdentifierExpression when context.CurrentScope.IsStrict:
                throw StandardLibrary.ThrowSyntaxError(
                    "Delete of an unqualified identifier is not allowed in strict mode.",
                    context,
                    context.RealmState);
            case IdentifierExpression identifier:
                {
                    var outcome = environment.DeleteBinding(identifier.Name);
                    return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
                }
            default:
                _ = operand.EvaluateDynamicExpressionOperand(
                    environment,
                    context,
                    "Dynamic delete operand");
                return true;
        }
    }

    private static string DescribeMemberName(this ExpressionNode property)
    {
        return property switch
        {
            LiteralExpression { Value.IsString: true } lit => lit.Value.AsString(),
            IdentifierExpression id => id.Name.Name,
            _ => property.GetType().Name
        };
    }
}
