using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

namespace Asynkron.JsEngine.Ast;

public interface ICallableMetadata
{
    bool IsArrowFunction { get; }
}

/// <summary>
///     Proof-of-concept evaluator that executes the new typed AST directly instead of walking cons cells.
///     The goal is to showcase the recommended shape: a dedicated evaluator with explicit pattern matching
///     rather than virtual methods on the node hierarchy. Only a focused subset of JavaScript semantics is
///     implemented for now so the skeleton stays approachable.
/// </summary>
public static partial class TypedAstEvaluator
{
    private const string GeneratorBrandPropertyName = "__generator_brand__";
    private static readonly Symbol YieldTrackerSymbol = Symbol.Intern("__yieldTracker__");
    private static readonly Symbol YieldResumeContextSymbol = Symbol.Intern("__yieldResume__");
    private static readonly Symbol GeneratorPendingCompletionSymbol = Symbol.Intern("__generatorPending__");
    private static readonly Symbol GeneratorInstanceSymbol = Symbol.Intern("__generatorInstance__");

    private static readonly string IteratorSymbolPropertyName =
        $"@@symbol:{TypedAstSymbol.For("Symbol.iterator").GetHashCode()}";

    private static readonly object GeneratorBrandMarker = new();
    private static readonly object EmptyCompletion = new();

    private enum HoistPass
    {
        Functions,
        Vars
    }

    extension(ProgramNode program)
    {
        public object? EvaluateProgram(JsEnvironment environment,
            RealmState realmState,
            CancellationToken cancellationToken = default,
            ExecutionKind executionKind = ExecutionKind.Script,
            bool createStrictEnvironment = true)
        {
            var context = realmState.CreateContext(
                ScopeKind.Program,
                program.IsStrict ? ScopeMode.Strict : ScopeMode.Sloppy,
                skipAnnexBInstantiation: false,
                cancellationToken: cancellationToken,
                executionKind: executionKind,
                pushScope: false);
            context.SourceReference = program.Source;
            context.IsStrictSource = program.IsStrict;
            var executionEnvironment = program.IsStrict && createStrictEnvironment
                ? new JsEnvironment(environment, true, true)
                : environment;
            if (program.IsStrict && !executionEnvironment.IsStrict)
            {
                executionEnvironment = new JsEnvironment(executionEnvironment, true, true);
            }

            var programMode = executionEnvironment.IsStrict
                ? ScopeMode.Strict
                : (context.Options.EnableAnnexBFunctionExtensions ? ScopeMode.SloppyAnnexB : ScopeMode.Sloppy);
            using var programScope = context.PushScope(ScopeKind.Program, programMode);

            // Hoist var and function declarations in the program body so that
            // function declarations like `function formatArgs(...) {}` are
            // available before earlier statements that reference them.
            var programBlock = new BlockStatement(program.Source, program.Body, program.IsStrict);
            var lexicalNames = CollectLexicalNames(programBlock);
            var catchParameterNames = CollectCatchParameterNames(programBlock);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(programBlock);
            var bodyLexicalNames = lexicalNames.Count == 0
                ? lexicalNames
                : new HashSet<Symbol>(lexicalNames, ReferenceEqualityComparer<Symbol>.Instance);
            bodyLexicalNames.ExceptWith(simpleCatchParameterNames);
            context.BlockedFunctionVarNames = bodyLexicalNames;
            executionEnvironment.SetBodyLexicalNames(bodyLexicalNames);
            var functionScope = executionEnvironment.GetFunctionScope();
            if (functionScope.IsGlobalFunctionScope)
            {
                foreach (var blockedName in bodyLexicalNames)
                {
                    if (functionScope.HasRestrictedGlobalProperty(blockedName))
                    {
                        throw StandardLibrary.ThrowSyntaxError(
                            $"Cannot redeclare var-scoped binding '{blockedName.Name}' with lexical declaration",
                            context,
                            context.RealmState);
                    }
                }
            }

            HoistVarDeclarations(programBlock, executionEnvironment, context, lexicalNames: lexicalNames,
                catchParameterNames: catchParameterNames,
                simpleCatchParameterNames: simpleCatchParameterNames);

            var result = EmptyCompletion;
            foreach (var statement in program.Body)
            {
                context.ThrowIfCancellationRequested();
                var completion = EvaluateStatement(statement, executionEnvironment, context);
                if (!ReferenceEquals(completion, EmptyCompletion))
                {
                    result = completion;
                }

                if (context.ShouldStopEvaluation)
                {
                    break;
                }
            }


            if (context.IsThrow)
            {
                throw new ThrowSignal(context.FlowValue);
            }

            return ReferenceEquals(result, EmptyCompletion) ? Symbol.Undefined : result;
        }
    }

    extension(StatementNode statement)
    {
        private object? EvaluateStatement(JsEnvironment environment,
            EvaluationContext context,
            Symbol? activeLabel = null)
        {
            context.SourceReference = statement.Source;
            context.ThrowIfCancellationRequested();

            return statement switch
            {
                BlockStatement block => EvaluateBlock(block, environment, context),
                ExpressionStatement expressionStatement => EvaluateExpression(expressionStatement.Expression, environment,
                    context),
                ReturnStatement returnStatement => EvaluateReturn(returnStatement, environment, context),
                ThrowStatement throwStatement => EvaluateThrow(throwStatement, environment, context),
                VariableDeclaration declaration => EvaluateVariableDeclaration(declaration, environment, context),
                FunctionDeclaration functionDeclaration => EvaluateFunctionDeclaration(functionDeclaration, environment,
                    context),
                IfStatement ifStatement => EvaluateIf(ifStatement, environment, context),
                WhileStatement whileStatement => EvaluateWhile(whileStatement, environment, context, activeLabel),
                DoWhileStatement doWhileStatement => EvaluateDoWhile(doWhileStatement, environment, context, activeLabel),
                ForStatement forStatement => EvaluateFor(forStatement, environment, context, activeLabel),
                ForEachStatement forEachStatement => EvaluateForEach(forEachStatement, environment, context, activeLabel),
                BreakStatement breakStatement => EvaluateBreak(breakStatement, context),
                ContinueStatement continueStatement => EvaluateContinue(continueStatement, context),
                LabeledStatement labeledStatement => EvaluateLabeled(labeledStatement, environment, context),
                TryStatement tryStatement => EvaluateTry(tryStatement, environment, context),
                SwitchStatement switchStatement => EvaluateSwitch(switchStatement, environment, context, activeLabel),
                ClassDeclaration classDeclaration => EvaluateClass(classDeclaration, environment, context),
                WithStatement withStatement => EvaluateWith(withStatement, environment, context),
                EmptyStatement => EmptyCompletion,
                _ => throw new NotSupportedException(
                    $"Typed evaluator does not yet support '{statement.GetType().Name}'.")
            };
        }
    }

    extension(BlockStatement block)
    {
        private object? EvaluateBlock(JsEnvironment environment,
            EvaluationContext context,
            bool skipAnnexBFunctionInstantiation = false)
        {
            var scope = new JsEnvironment(environment, false, block.IsStrict);
            var result = EmptyCompletion;

            var currentMode = context.CurrentScope.Mode;
            var allowAnnexB = currentMode == ScopeMode.SloppyAnnexB &&
                              !scope.IsStrict &&
                              !block.IsStrict;
            var mode = scope.IsStrict
                ? ScopeMode.Strict
                : (allowAnnexB ? ScopeMode.SloppyAnnexB : ScopeMode.Sloppy);
            using var scopeHandle = context.PushScope(
                ScopeKind.Block,
                mode,
                skipAnnexBFunctionInstantiation);

            var currentFrame = context.CurrentScope;
            if (currentFrame is { AllowAnnexB: true, SkipAnnexBInstantiation: false })
            {
                InstantiateAnnexBBlockFunctions(block, scope, context);
            }

            foreach (var statement in block.Statements)
            {
                context.ThrowIfCancellationRequested();
                var completion = EvaluateStatement(statement, scope, context);
                var shouldStop = context.ShouldStopEvaluation;
                var shouldCapture =
                    !ReferenceEquals(completion, EmptyCompletion) &&
                    (!shouldStop ||
                     context.IsReturn ||
                     context.IsThrow ||
                     context.IsYield ||
                     context.IsBreak ||
                     context.IsContinue);

                if (shouldCapture)
                {
                    result = completion;
                }

                if (shouldStop)
                {
                    break;
                }
            }

            return result;
        }
    }

    extension(BlockStatement block)
    {
        private void InstantiateAnnexBBlockFunctions(JsEnvironment blockEnvironment,
            EvaluationContext context)
        {
            var frame = context.CurrentScope;
            if (!frame.AllowAnnexB || frame.SkipAnnexBInstantiation)
            {
                return;
            }

            var functionScope = blockEnvironment.GetFunctionScope();
            var lexicalNames = CollectLexicalNames(block);
            var simpleCatchParameterNames = CollectSimpleCatchParameterNames(block);

            foreach (var statement in block.Statements)
            {
                if (statement is not FunctionDeclaration functionDeclaration)
                {
                    continue;
                }

                var hasNonCatchLexical = lexicalNames.Contains(functionDeclaration.Name) &&
                                         !simpleCatchParameterNames.Contains(functionDeclaration.Name);
                var shouldCreateVarBinding = !hasNonCatchLexical &&
                                             !functionScope.HasBodyLexicalName(functionDeclaration.Name);
                var blockedByParameters = context.BlockedFunctionVarNames is { } blocked &&
                                          blocked.Contains(functionDeclaration.Name);
                var hasLexicalBeforeFunctionScope =
                    blockEnvironment.HasBindingBeforeFunctionScope(functionDeclaration.Name);
                var hasBlockingLexicalBeforeFunctionScope = hasLexicalBeforeFunctionScope &&
                                                            !simpleCatchParameterNames.Contains(functionDeclaration.Name) &&
                                                            !IsSimpleCatchParameterBinding(blockEnvironment,
                                                                functionDeclaration.Name);
                var bindingExists =
                    hasLexicalBeforeFunctionScope ||
                    functionScope.HasBodyLexicalName(functionDeclaration.Name) ||
                    (functionScope.IsGlobalFunctionScope && functionScope.HasOwnLexicalBinding(functionDeclaration.Name));

                var functionValue = CreateFunctionValue(functionDeclaration.Function, blockEnvironment, context);

                blockEnvironment.Define(functionDeclaration.Name, functionValue, isLexical: true,
                    blocksFunctionScopeOverride: true);

                var skipVarUpdateForExistingGlobal = false;
                if (bindingExists && functionScope.IsGlobalFunctionScope)
                {
                    try
                    {
                        if (functionScope.TryGet(functionDeclaration.Name, out var existingValue) &&
                            !ReferenceEquals(existingValue, Symbol.Undefined))
                        {
                            skipVarUpdateForExistingGlobal = true;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore lookup failures (e.g., uninitialized); allow update in that case.
                    }
                }

                if (!shouldCreateVarBinding || blockedByParameters || skipVarUpdateForExistingGlobal ||
                    hasBlockingLexicalBeforeFunctionScope)
                {
                    continue;
                }

                var hasFunctionBinding = functionScope.HasFunctionScopedBinding(functionDeclaration.Name);
                if (!bindingExists || hasFunctionBinding)
                {
                    functionScope.DefineFunctionScoped(
                        functionDeclaration.Name,
                        Symbol.Undefined,
                        hasInitializer: false,
                        isFunctionDeclaration: true,
                        globalFunctionConfigurable: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                        context,
                        blocksFunctionScopeOverride: true);
                }
            }
        }
    }

    extension(JsEnvironment environment)
    {
        private bool IsSimpleCatchParameterBinding(Symbol name)
        {
            try
            {
                if (environment.TryFindBinding(name, out var bindingEnvironment, out _) &&
                    !bindingEnvironment.IsFunctionScope &&
                    bindingEnvironment.IsSimpleCatchParameter(name))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Ignore lookup failures such as TDZ reads.
            }

            return false;
        }
    }

    extension(JsEnvironment environment)
    {
        private bool HasBlockingLexicalBeforeFunctionScope(Symbol name)
        {
            var current = environment;
            var skippedOwnBinding = false;
            while (current?.IsFunctionScope == false)
            {
                if (current.HasOwnLexicalBinding(name))
                {
                    if (!skippedOwnBinding)
                    {
                        skippedOwnBinding = true;
                    }
                    else if (!current.IsSimpleCatchParameter(name))
                    {
                        return true;
                    }
                }

                current = current.Enclosing;
            }

            return false;
        }
    }

    extension(ReturnStatement statement)
    {
        private object? EvaluateReturn(JsEnvironment environment,
            EvaluationContext context)
        {
            var value = statement.Expression is null
                ? null
                : EvaluateExpression(statement.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return value;
            }

            context.SetReturn(value);
            return value;
        }
    }

    extension(ThrowStatement statement)
    {
        private object? EvaluateThrow(JsEnvironment environment, EvaluationContext context)
        {
            var value = EvaluateExpression(statement.Expression, environment, context);
            context.SetThrow(value);
            return value;
        }
    }

    extension(WithStatement statement)
    {
        private object? EvaluateWith(JsEnvironment environment, EvaluationContext context)
        {
            var objValue = EvaluateExpression(statement.Object, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return objValue;
            }

            if (!TryConvertToWithBindingObject(objValue, context, out var withObject))
            {
                return Symbol.Undefined;
            }

            var withEnv = new JsEnvironment(environment, false, context.CurrentScope.IsStrict, statement.Source, "with",
                withObject);
            var completion = EvaluateStatement(statement.Body, withEnv, context);

            return ReferenceEquals(completion, EmptyCompletion) ?
                Symbol.Undefined :
                completion;
        }
    }

    extension(BreakStatement statement)
    {
        private object EvaluateBreak(EvaluationContext context)
        {
            context.SetBreak(statement.Label);
            return EmptyCompletion;
        }
    }

    extension(ContinueStatement statement)
    {
        private object EvaluateContinue(EvaluationContext context)
        {
            context.SetContinue(statement.Label);
            return EmptyCompletion;
        }
    }

    private static bool TryConvertToWithBindingObject(
        object? value,
        EvaluationContext context,
        out IJsObjectLike? bindingObject)
    {
        switch (value)
        {
            case IJsObjectLike objectLike:
                bindingObject = objectLike;
                return true;
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
            {
                var error = StandardLibrary.CreateTypeError("Cannot convert undefined or null to object", context,
                    context.RealmState);
                context.SetThrow(error);
                bindingObject = null;
                return false;
            }
            default:
            {
                var converted = ToObjectForDestructuring(value, context);
                if (context.IsThrow)
                {
                    bindingObject = null;
                    return false;
                }

                bindingObject = converted;
                return true;
            }
        }
    }

    private static object? EvaluateIf(IfStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var test = EvaluateExpression(statement.Condition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var branch = IsTruthy(test) ? statement.Then : statement.Else;
        if (branch is null)
        {
            return Symbol.Undefined;
        }

        if (branch is BlockStatement block)
        {
            return EvaluateBlock(block, environment, context);
        }

        var branchScope = new JsEnvironment(environment, false, context.CurrentScope.IsStrict);
        return EvaluateStatement(branch, branchScope, context);
    }

    private static object? EvaluateWhile(WhileStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize while loop.");
        }

        return EvaluateLoopPlan(plan, environment, context, loopLabel);
    }

    private static object? EvaluateDoWhile(DoWhileStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize do/while loop.");
        }

        return EvaluateLoopPlan(plan, environment, context, loopLabel);
    }

    private static object? EvaluateFor(ForStatement statement, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        var isStrict = IsStrictBlock(statement.Body);
        if (!LoopNormalizer.TryNormalize(statement, isStrict, out var plan, out _))
        {
            throw new NotSupportedException("Failed to normalize for loop.");
        }

        var loopEnvironment = new JsEnvironment(environment, creatingSource: statement.Source, description: "for-loop");
        return EvaluateLoopPlan(plan, loopEnvironment, context, loopLabel);
    }

    private static object? EvaluateForEach(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        if (statement.Kind == ForEachKind.AwaitOf)
        {
            return EvaluateForAwaitOf(statement, environment, context, loopLabel);
        }

        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        // In JavaScript, `for...in` requires an object value; iterating
        // over `null` or `undefined` throws a TypeError. Treat other
        // non-object values as errors as well so engine bugs surface
        // as JavaScript throws rather than host exceptions.
        if (statement.Kind == ForEachKind.In &&
            iterable is not IJsObjectLike &&
            iterable is not JsObject &&
            iterable is not JsArray &&
            iterable is not string &&
            iterable is not null &&
            !ReferenceEquals(iterable, Symbol.Undefined))
        {
            throw new ThrowSignal("Cannot iterate properties of non-object value.");
        }

        var loopEnvironment =
            new JsEnvironment(environment, creatingSource: statement.Source, description: "for-each-loop");
        object? lastValue = Symbol.Undefined;

        if (statement.Kind == ForEachKind.Of &&
            TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
        {
            var plan = IteratorDriverFactory.CreatePlan(statement,
                statement.Body is BlockStatement b
                    ? b
                    : new BlockStatement(statement.Source, [statement.Body], IsStrictBlock(statement.Body)));
            return ExecuteIteratorDriver(plan, iterator, null, loopEnvironment, environment, context, loopLabel);
        }

        var values = statement.Kind switch
        {
            ForEachKind.In => EnumeratePropertyKeys(iterable),
            ForEachKind.Of => EnumerateValues(iterable),
            _ => throw new ArgumentOutOfRangeException()
        };

        foreach (var value in values)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            var iterationEnvironment = statement.DeclarationKind is VariableKind.Let or VariableKind.Const
                ? new JsEnvironment(loopEnvironment, creatingSource: statement.Source,
                    description: "for-each-iteration")
                : loopEnvironment;

            AssignLoopBinding(statement.Target, value, iterationEnvironment, environment, context,
                statement.DeclarationKind);

            lastValue = EvaluateStatement(statement.Body, iterationEnvironment, context);

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }
        }

        return ReferenceEquals(lastValue, EmptyCompletion) ? Symbol.Undefined : lastValue;
    }

    private static object? EvaluateForAwaitOf(ForEachStatement statement, JsEnvironment environment,
        EvaluationContext context, Symbol? loopLabel)
    {
        var iterable = EvaluateExpression(statement.Iterable, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var loopEnvironment =
            new JsEnvironment(environment, creatingSource: statement.Source, description: "for-await-of loop");
        object? lastValue = Symbol.Undefined;

        if (TryGetIteratorFromProtocols(iterable, out var iterator))
        {
            var plan = IteratorDriverFactory.CreatePlan(statement,
                statement.Body is BlockStatement b
                    ? b
                    : new BlockStatement(statement.Source, [statement.Body], IsStrictBlock(statement.Body)));
            return ExecuteIteratorDriver(plan, iterator!, null, loopEnvironment, environment, context, loopLabel);
        }

        var values = EnumerateValues(iterable);
        foreach (var value in values)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            if (IsPromiseLike(value))
            {
                throw new NotSupportedException(
                    "for await...of in this context must be lowered via the async CPS/iterator helpers; promise-valued iteration values are not supported in the direct evaluator.");
            }

            AssignLoopBinding(statement.Target, value, loopEnvironment, environment, context,
                statement.DeclarationKind);
            lastValue = EvaluateStatement(statement.Body, loopEnvironment, context);

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }
        }

        return lastValue;
    }

    // Per ECMA-262 §7.4.1/§7.4.2 (GetIterator / GetAsyncIterator) via @@iterator/@@asyncIterator.
    private static bool TryGetIteratorFromProtocols(object? iterable, out JsObject? iterator)
    {
        iterator = null;
        if (iterable is not IJsPropertyAccessor accessor)
        {
            return false;
        }

        if (TryInvokeSymbolMethod(accessor, iterable, "Symbol.asyncIterator", out var asyncIterator) &&
            asyncIterator is JsObject asyncObj)
        {
            iterator = asyncObj;
            return true;
        }

        if (TryInvokeSymbolMethod(accessor, iterable, "Symbol.iterator", out var iteratorValue) &&
            iteratorValue is JsObject iteratorObj)
        {
            iterator = iteratorObj;
            return true;
        }

        return false;
    }

    private static bool TryInvokeSymbolMethod(IJsPropertyAccessor target, object? thisArg, string symbolName,
        out object? result)
    {
        var symbol = TypedAstSymbol.For(symbolName);
        var hashedName = $"@@symbol:{symbol.GetHashCode()}";

        if (TryGetCallable(hashedName, out var callable) ||
            TryGetCallable(symbolName, out callable) ||
            TryGetCallable(symbol.ToString(), out callable))
        {
            result = callable!.Invoke([], thisArg);
            return true;
        }

        result = null;
        return false;

        bool TryGetCallable(string propertyName, out IJsCallable? callable)
        {
            if (target.TryGetProperty(propertyName, out var candidate) && candidate is IJsCallable found)
            {
                callable = found;
                return true;
            }

            callable = null;
            return false;
        }
    }

    private static object? InvokeIteratorNext(JsObject iterator, object? sendValue = null, bool hasSendValue = false)
    {
        if (!iterator.TryGetProperty("next", out var nextValue) || nextValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Iterator must expose a 'next' method.");
        }

        var args = hasSendValue ? new[] { sendValue } : Array.Empty<object?>();
        return callable.Invoke(args, iterator);
    }

    private static bool TryInvokeIteratorMethod(
        JsObject iterator,
        string methodName,
        object? argument,
        EvaluationContext context,
        out object? result,
        bool hasArgument = true)
    {
        result = null;
        if (!iterator.TryGetProperty(methodName, out var methodValue))
        {
            return false;
        }

        if (methodValue is null)
        {
            return false;
        }

        if (methodValue is not IJsCallable callable)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator method is not callable", context,
                context.RealmState));
        }

        var args = hasArgument ? new[] { argument } : Array.Empty<object?>();
        result = callable.Invoke(args, iterator);
        return true;
    }

    private static void IteratorClose(JsObject iterator, EvaluationContext context, bool preserveExistingThrow = false)
    {
        try
        {
            if (TryInvokeIteratorMethod(
                    iterator,
                    "return",
                    Symbol.Undefined,
                    context,
                    out var closeResult,
                    hasArgument: false))
            {
                if (closeResult is not JsObject returnObject)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError("Iterator.return() must return an object",
                        context, context.RealmState));
                }

                if (IsPromiseLike(returnObject))
                {
                    AwaitScheduler.TryAwaitPromiseSync(returnObject, context, out _);
                }
            }
        }
        catch (ThrowSignal) when (preserveExistingThrow || context.IsThrow)
        {
            // Preserve the original abrupt completion per IteratorClose when completion is already throw.
        }
    }

    private static object? ExecuteIteratorDriver(IteratorDriverPlan plan,
        JsObject iterator,
        IEnumerator<object?>? enumerator,
        JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment,
        EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = Symbol.Undefined;
        var iteratorDone = false;

        var state = new IteratorDriverState
        {
            IteratorObject = iterator,
            Enumerator = enumerator,
            IsAsyncIterator = plan.Kind == IteratorDriverKind.Await
        };

        while (!context.ShouldStopEvaluation)
        {
            context.ThrowIfCancellationRequested();

            object? nextResult = null;
            if (state.IteratorObject is not null)
            {
                nextResult = InvokeIteratorNext(state.IteratorObject);
            }
            else if (state.Enumerator is not null)
            {
                if (!state.Enumerator.MoveNext())
                {
                    break;
                }

                nextResult = state.Enumerator.Current;
            }

            if (nextResult is JsObject resultObj)
            {
                var done = resultObj.TryGetProperty("done", out var doneValue) &&
                           doneValue is bool and true;
                if (done)
                {
                    iteratorDone = true;
                    break;
                }

                var value = resultObj.TryGetProperty("value", out var yielded)
                    ? yielded
                    : Symbol.Undefined;

                var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(plan.Target, value, iterationEnvironment, outerEnvironment, context,
                    plan.DeclarationKind);
                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
            }
            else
            {
                if (state.IteratorObject is not null)
                {
                    throw new ThrowSignal(StandardLibrary.CreateTypeError(
                        "Iterator.next() did not return an object", context, context.RealmState));
                }

                // Enumerator path (non-object next)
                var iterationEnvironment = plan.DeclarationKind is VariableKind.Let or VariableKind.Const
                    ? new JsEnvironment(loopEnvironment, creatingSource: plan.Body.Source,
                        description: "for-each-iteration")
                    : loopEnvironment;

                AssignLoopBinding(plan.Target, nextResult, iterationEnvironment, outerEnvironment, context,
                    plan.DeclarationKind);
                lastValue = EvaluateStatement(plan.Body, iterationEnvironment, context, loopLabel);
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }
        }

        if (state.IteratorObject is not null && !iteratorDone)
        {
            IteratorClose(state.IteratorObject, context, preserveExistingThrow: context.IsThrow);
        }

        return lastValue;
    }

    private static bool IsPromiseLike(object? candidate)
    {
        return AwaitScheduler.IsPromiseLike(candidate);
    }

    // WAITING ON FULL ASYNC/AWAIT + ASYNC GENERATOR IR SUPPORT:
    // This helper synchronously blocks on promise resolution using TaskCompletionSource.
    // It keeps async/await and async iteration usable for now but must be replaced by
    // a non-blocking, event-loop-integrated continuation model once the async IR
    // pipeline is in place.
    private static bool TryAwaitPromise(object? candidate, EvaluationContext context, out object? resolvedValue)
    {
        return AwaitScheduler.TryAwaitPromiseSync(candidate, context, out resolvedValue);
    }

    private static void AssignLoopBinding(BindingTarget target, object? value, JsEnvironment loopEnvironment,
        JsEnvironment outerEnvironment, EvaluationContext context, VariableKind? declarationKind)
    {
        if (declarationKind is null)
        {
            AssignBindingTarget(target, value, outerEnvironment, context);
            return;
        }

        switch (declarationKind)
        {
            case VariableKind.Var:
                DefineOrAssignVar(target, value, loopEnvironment, context);
                break;
            case VariableKind.Let:
            case VariableKind.Const:
                DefineBindingTarget(target, value, loopEnvironment, context,
                    declarationKind == VariableKind.Const);
                CollectSymbolsFromBinding(target, context.BlockedFunctionVarNames);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static IEnumerable<object?> EnumeratePropertyKeys(object? value)
    {
        switch (value)
        {
            case JsArray array:
            {
                for (var i = 0; i < array.Items.Count; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case string s:
            {
                for (var i = 0; i < s.Length; i++)
                {
                    yield return i.ToString(CultureInfo.InvariantCulture);
                }

                yield break;
            }

            case IJsObjectLike accessor:
            {
                foreach (var key in accessor.GetOwnPropertyNames())
                {
                    var desc = accessor.GetOwnPropertyDescriptor(key);
                    if (desc is { Enumerable: false })
                    {
                        continue;
                    }

                    yield return key;
                }

                yield break;
            }
        }

        throw new InvalidOperationException("Cannot iterate properties of non-object value.");
    }

    private static IEnumerable<object?> EnumerateValues(object? value)
    {
        switch (value)
        {
            case JsArray array:
                foreach (var item in array.Items)
                {
                    yield return item;
                }

                yield break;
            case string s:
                foreach (var ch in s)
                {
                    yield return ch.ToString();
                }

                yield break;
            case IEnumerable<object?> enumerable:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
        }

        throw new InvalidOperationException("Value is not iterable.");
    }

    private static object? EvaluateTry(TryStatement statement, JsEnvironment environment, EvaluationContext context)
    {
        var result = EvaluateBlock(statement.TryBlock, environment, context);
        if (context.IsThrow && statement.Catch is not null)
        {
            var thrownValue = context.FlowValue;
            context.Clear();
            var catchEnv = new JsEnvironment(environment, creatingSource: statement.Catch.Body.Source,
                description: "catch");
            if (statement.Catch.Binding is IdentifierBinding identifierBinding)
            {
                catchEnv.SetSimpleCatchParameters(
                    new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance) { identifierBinding.Name });
            }
            DefineBindingTarget(statement.Catch.Binding, thrownValue, catchEnv, context, false);
            result = EvaluateBlock(statement.Catch.Body, catchEnv, context);
        }

        if (statement.Finally is not null)
        {
            var savedSignal = context.CurrentSignal;

            GeneratorPendingCompletion? pending = null;
            var isGenerator = IsGeneratorContext(environment);
            if (isGenerator && savedSignal is not null)
            {
                pending = GetGeneratorPendingCompletion(environment);
                switch (savedSignal)
                {
                    case ThrowFlowSignal throwSignal:
                        pending.HasValue = true;
                        pending.IsThrow = true;
                        pending.IsReturn = false;
                        pending.Value = throwSignal.Value;
                        break;
                    case ReturnSignal returnSignal:
                        pending.HasValue = true;
                        pending.IsThrow = false;
                        pending.IsReturn = true;
                        pending.Value = returnSignal.Value;
                        break;
                }
            }

            context.Clear();
            _ = EvaluateBlock(statement.Finally, environment, context);
            if (context.CurrentSignal is null)
            {
                if (isGenerator && pending?.HasValue == true)
                {
                    if (pending.IsThrow)
                    {
                        context.SetThrow(pending.Value);
                    }
                    else if (pending.IsReturn)
                    {
                        context.SetReturn(pending.Value);
                    }

                    pending.HasValue = false;
                    pending.IsThrow = false;
                    pending.IsReturn = false;
                    pending.Value = null;
                }
                else
                {
                    RestoreSignal(context, savedSignal);
                }
            }
        }

        return result;
    }

    private static object? EvaluateSwitch(SwitchStatement statement, JsEnvironment environment,
        EvaluationContext context,
        Symbol? targetLabel)
    {
        var discriminant = EvaluateExpression(statement.Discriminant, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        object? lastValue = Symbol.Undefined;
        var hasMatched = false;

        foreach (var switchCase in statement.Cases)
        {
            if (!hasMatched)
            {
                if (switchCase.Test is null)
                {
                    hasMatched = true;
                }
                else
                {
                    var test = EvaluateExpression(switchCase.Test, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return lastValue;
                    }

                    hasMatched = StrictEquals(discriminant, test);
                }

                if (!hasMatched)
                {
                    continue;
                }
            }

            lastValue = EvaluateBlock(switchCase.Body, environment, context);
            if (context.TryClearBreak(targetLabel))
            {
                break;
            }

            if (context.IsReturn || context.IsThrow)
            {
                break;
            }
        }

        return lastValue;
    }

    private static object? EvaluateClass(ClassDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        var constructorValue = CreateClassValue(declaration.Definition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return EmptyCompletion;
        }

        environment.Define(declaration.Name, constructorValue, isLexical: true, blocksFunctionScopeOverride: true);
        return EmptyCompletion;
    }

    private static object? EvaluateLoopPlan(LoopPlan plan, JsEnvironment environment, EvaluationContext context,
        Symbol? loopLabel)
    {
        object? lastValue = Symbol.Undefined;

        if (!plan.LeadingStatements.IsDefaultOrEmpty)
        {
            foreach (var statement in plan.LeadingStatements)
            {
                lastValue = EvaluateStatement(statement, environment, context, loopLabel);
                if (context.ShouldStopEvaluation)
                {
                    return NormalizeLoopCompletion(lastValue);
                }
            }
        }

        while (true)
        {
            context.ThrowIfCancellationRequested();

            if (!plan.ConditionAfterBody)
            {
                if (!ExecuteCondition(plan, environment, context))
                {
                    break;
                }
            }

            lastValue = EvaluateStatement(plan.Body, environment, context, loopLabel);
            if (context.IsReturn || context.IsThrow)
            {
                break;
            }

            if (context.TryClearContinue(loopLabel))
            {
                if (!ExecutePostIteration(plan, environment, context, ref lastValue))
                {
                    break;
                }

                if (plan.ConditionAfterBody && !ExecuteCondition(plan, environment, context))
                {
                    break;
                }

                continue;
            }

            if (context.TryClearBreak(loopLabel))
            {
                break;
            }

            if (context.ShouldStopEvaluation)
            {
                break;
            }

            if (!ExecutePostIteration(plan, environment, context, ref lastValue))
            {
                break;
            }

            if (plan.ConditionAfterBody)
            {
                if (!ExecuteCondition(plan, environment, context))
                {
                    break;
                }
            }
        }

        return NormalizeLoopCompletion(lastValue);
    }

    private static bool ExecuteCondition(LoopPlan plan, JsEnvironment environment, EvaluationContext context)
    {
        if (!plan.ConditionPrologue.IsDefaultOrEmpty)
        {
            foreach (var statement in plan.ConditionPrologue)
            {
                _ = EvaluateStatement(statement, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return false;
                }
            }
        }

        var test = EvaluateExpression(plan.Condition, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        return IsTruthy(test);
    }

    private static bool ExecutePostIteration(LoopPlan plan, JsEnvironment environment, EvaluationContext context,
        ref object? lastValue)
    {
        if (plan.PostIteration.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var statement in plan.PostIteration)
        {
            lastValue = EvaluateStatement(statement, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }
        }

        return true;
    }

    private static object? NormalizeLoopCompletion(object? completion)
    {
        return ReferenceEquals(completion, EmptyCompletion) ? Symbol.Undefined : completion;
    }

    private static bool IsStrictBlock(StatementNode statement)
    {
        return statement is BlockStatement { IsStrict: true };
    }

    private static object? EvaluateClassExpression(ClassExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return CreateClassValue(expression.Definition, environment, context);
    }

    private static object? CreateClassValue(ClassDefinition definition, JsEnvironment environment,
        EvaluationContext context)
    {
        using var classScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, skipAnnexBInstantiation: true);
        var (superConstructor, superPrototype) = ResolveSuperclass(definition.Extends, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var constructorValue = EvaluateExpression(definition.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (constructorValue is not IJsEnvironmentAwareCallable constructor ||
            constructorValue is not IJsPropertyAccessor constructorAccessor)
        {
            throw new InvalidOperationException("Class constructor must be callable.");
        }

        var realm = context.RealmState;
        var prototype = EnsurePrototype(constructorAccessor, realm);
        if (superPrototype is not null)
        {
            prototype.SetPrototype(superPrototype);
        }

        var privateNameScope = CreatePrivateNameScope(definition);
            if (constructorValue is TypedFunction typedFunction)
            {
                typedFunction.SetSuperBinding(superConstructor, superPrototype);
                var instanceFields = definition.Fields.Where(field => !field.IsStatic).ToImmutableArray();
                typedFunction.SetInstanceFields(instanceFields);
                typedFunction.SetIsClassConstructor(superConstructor is not null);
                typedFunction.SetPrivateNameScope(privateNameScope);
                if (privateNameScope is not null)
                {
                    typedFunction.AddPrivateBrand(privateNameScope.BrandToken);
                }
            }

        if (superConstructor is not null)
        {
            constructorAccessor.SetProperty("__proto__", superConstructor);
            if (constructorAccessor is IJsObjectLike ctorObject)
            {
                ctorObject.SetPrototype(superConstructor);
            }
        }
        else if (constructorAccessor is IJsObjectLike { Prototype: null } baseCtor &&
                 realm.FunctionPrototype is not null)
        {
            baseCtor.SetPrototype(realm.FunctionPrototype);
        }

        prototype.SetProperty("constructor", constructorValue);

        AssignClassMembers(definition.Members, constructorAccessor, prototype, superConstructor, superPrototype,
            environment, context, privateNameScope);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var staticFields = definition.Fields.Where(field => field.IsStatic).ToImmutableArray();
        InitializeStaticFields(staticFields, constructorAccessor, environment, context, privateNameScope);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        return constructorValue;
    }

    private static (IJsEnvironmentAwareCallable? Constructor, JsObject? Prototype) ResolveSuperclass(
        ExpressionNode? extendsExpression, JsEnvironment environment, EvaluationContext context)
    {
        if (extendsExpression is null)
        {
            return (null, null);
        }

        var baseValue = EvaluateExpression(extendsExpression, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (baseValue is null)
        {
            return (null, null);
        }

        if (baseValue is not IJsEnvironmentAwareCallable callable ||
            baseValue is not IJsPropertyAccessor accessor)
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                "Class extends value is not a constructor or null", context, context.RealmState));
        }

        if (!TryGetPropertyValue(baseValue, "prototype", out var prototypeValue) ||
            prototypeValue is not JsObject prototype)
        {
            prototype = new JsObject();
            accessor.SetProperty("prototype", prototype);
        }

        return (callable, prototype);
    }

    private static JsObject EnsurePrototype(IJsPropertyAccessor constructor, RealmState realm)
    {
        if (constructor.TryGetProperty("prototype", out var prototypeValue) && prototypeValue is JsObject prototype)
        {
            if (prototype.Prototype is null && realm.ObjectPrototype is not null)
            {
                prototype.SetPrototype(realm.ObjectPrototype);
            }

            return prototype;
        }

        var created = new JsObject();
        if (realm.ObjectPrototype is not null)
        {
            created.SetPrototype(realm.ObjectPrototype);
        }

        constructor.SetProperty("prototype", created);
        return created;
    }

    private static PrivateNameScope? CreatePrivateNameScope(ClassDefinition definition)
    {
        var hasPrivateFields = definition.Fields.Any(f => f.IsPrivate);
        var hasPrivateMembers = definition.Members.Any(m => m.Name.Length > 0 && m.Name[0] == '#');
        return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope() : null;
    }

    private static void AssignClassMembers(ImmutableArray<ClassMember> members, IJsPropertyAccessor constructorAccessor,
        JsObject prototype, IJsEnvironmentAwareCallable? superConstructor, JsObject? superPrototype,
        JsEnvironment environment, EvaluationContext context, PrivateNameScope? privateNameScope)
    {
        foreach (var member in members)
        {
            if (!member.TryResolveMemberName(expr => EvaluateExpression(expr, environment, context),
                    context,
                    privateNameScope,
                    out var propertyName))
            {
                return;
            }

            var value = EvaluateExpression(member.Function, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (value is not IJsCallable callable)
            {
                throw new InvalidOperationException("Class member must be callable.");
            }

            var homeObject = member.IsStatic
                ? constructorAccessor as IJsObjectLike
                : prototype;
            var superTarget = member.IsStatic
                ? superConstructor as IJsPropertyAccessor
                : superPrototype;
            if (value is TypedFunction typedFunction)
            {
                typedFunction.SetPrivateNameScope(privateNameScope);
                typedFunction.SetSuperBinding(superConstructor, superTarget);
                if (homeObject is not null)
                {
                    typedFunction.SetHomeObject(homeObject);
                }
                typedFunction.EnsureHasName(propertyName);
            }

            member.DefineMember(propertyName, callable, constructorAccessor, prototype);
        }
    }

    private static void InitializeStaticFields(ImmutableArray<ClassField> fields,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment, EvaluationContext context, PrivateNameScope? privateNameScope)
    {
        using var staticFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, skipAnnexBInstantiation: true);
        Func<IDisposable?>? privateScopeFactory = privateNameScope is not null
            ? () => context.EnterPrivateNameScope(privateNameScope)
            : null;

        if (!fields.TryInitializeStaticFields(constructorAccessor,
            expr => EvaluateExpression(expr, environment, context),
            context,
            privateNameScope,
            privateScopeFactory))
        {
            return;
        }
    }

    private static object? EvaluateVariableDeclaration(VariableDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        foreach (var declarator in declaration.Declarators)
        {
            EvaluateVariableDeclarator(declaration.Kind, declarator, environment, context);
            if (context.ShouldStopEvaluation)
            {
                break;
            }
        }

        return EmptyCompletion;
    }

    private static void EvaluateVariableDeclarator(VariableKind kind, VariableDeclarator declarator,
        JsEnvironment environment, EvaluationContext context)
    {
        var value = declarator.Initializer is null
            ? Symbol.Undefined
            : EvaluateExpression(declarator.Initializer, environment, context);

        if (context.ShouldStopEvaluation)
        {
            return;
        }

        var mode = kind switch
        {
            VariableKind.Var => BindingMode.DefineVar,
            VariableKind.Let => BindingMode.DefineLet,
            VariableKind.Const => BindingMode.DefineConst,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        ApplyBindingTarget(declarator.Target, value, environment, context, mode, declarator.Initializer is not null);
    }

    private static object? EvaluateFunctionDeclaration(FunctionDeclaration declaration, JsEnvironment environment,
        EvaluationContext context)
    {
        var currentScope = context.CurrentScope;
        var annexBEnabled = currentScope.AllowAnnexB;
        var isStrictScope = currentScope.IsStrict;
        object? function = null;
        if (!isStrictScope &&
            environment.TryFindBinding(declaration.Name, out var bindingEnvironment, out var existingValue) &&
            bindingEnvironment.HasOwnLexicalBinding(declaration.Name))
        {
            function = existingValue;
        }

        function ??= CreateFunctionValue(declaration.Function, environment, context);
        var isBlockEnvironment = !environment.IsFunctionScope;
        var shouldCreateLexicalBinding = isStrictScope ||
                                         (!annexBEnabled && isBlockEnvironment);
        if (shouldCreateLexicalBinding)
        {
            environment.Define(declaration.Name, function);
        }
        var skipVarBinding = context.BlockedFunctionVarNames is { } blocked &&
            blocked.Contains(declaration.Name) || environment.HasBodyLexicalName(declaration.Name);

        var hasBlockingLexicalBeforeFunctionScope =
            !isStrictScope && HasBlockingLexicalBeforeFunctionScope(environment, declaration.Name);

        var shouldCreateVarBinding = annexBEnabled || !isBlockEnvironment;
        if (shouldCreateVarBinding && !skipVarBinding && !hasBlockingLexicalBeforeFunctionScope)
        {
            if (!isStrictScope)
            {
                var assigned = environment.TryAssignBlockedBinding(declaration.Name, function);
            }

            var configurable = context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false };
            environment.DefineFunctionScoped(
                declaration.Name,
                function,
                true,
                true,
                configurable,
                context);
        }

        return EmptyCompletion;
    }

    private static IJsCallable CreateFunctionValue(FunctionExpression functionExpression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (functionExpression.IsGenerator)
        {
            if (functionExpression.IsAsync)
            {
                return new AsyncGeneratorFactory(functionExpression, environment, context.RealmState);
            }

            return new TypedGeneratorFactory(functionExpression, environment, context.RealmState);
        }

        return new TypedFunction(functionExpression, environment, context.RealmState);
    }

    private static object? EvaluateLabeled(LabeledStatement statement, JsEnvironment environment,
        EvaluationContext context)
    {
        context.PushLabel(statement.Label);
        try
        {
            var result = EvaluateStatement(statement.Statement, environment, context, statement.Label);

            if (context.TryClearBreak(statement.Label))
            {
                return EmptyCompletion;
            }

            return result;
        }
        finally
        {
            context.PopLabel();
        }
    }

    private static object? EvaluateExpression(ExpressionNode expression, JsEnvironment environment,
        EvaluationContext context)
    {
        context.SourceReference = expression.Source;

        return expression switch
        {
            LiteralExpression literal => EvaluateLiteral(literal, context),
            IdentifierExpression identifier => EvaluateIdentifier(identifier, environment, context),
            BinaryExpression binary => EvaluateBinary(binary, environment, context),
            UnaryExpression unary => EvaluateUnary(unary, environment, context),
            ConditionalExpression conditional => EvaluateConditional(conditional, environment, context),
            CallExpression call => EvaluateCall(call, environment, context),
            FunctionExpression functionExpression => CreateFunctionValue(functionExpression, environment, context),
            AssignmentExpression assignment => EvaluateAssignment(assignment, environment, context),
            DestructuringAssignmentExpression destructuringAssignment =>
                EvaluateDestructuringAssignment(destructuringAssignment, environment, context),
            PropertyAssignmentExpression propertyAssignment =>
                EvaluatePropertyAssignment(propertyAssignment, environment, context),
            IndexAssignmentExpression indexAssignment =>
                EvaluateIndexAssignment(indexAssignment, environment, context),
            SequenceExpression sequence => EvaluateSequence(sequence, environment, context),
            MemberExpression member => EvaluateMember(member, environment, context),
            NewExpression newExpression => EvaluateNew(newExpression, environment, context),
            NewTargetExpression => environment.TryGet(Symbol.NewTarget, out var newTarget)
                ? newTarget
                : Symbol.Undefined,
            ArrayExpression array => EvaluateArray(array, environment, context),
            ObjectExpression obj => EvaluateObject(obj, environment, context),
            ClassExpression classExpression => EvaluateClassExpression(classExpression, environment, context),
            DecoratorExpression => throw new NotSupportedException("Decorators are not supported."),
            TemplateLiteralExpression template => EvaluateTemplateLiteral(template, environment, context),
            TaggedTemplateExpression taggedTemplate =>
                EvaluateTaggedTemplate(taggedTemplate, environment, context),
            AwaitExpression awaitExpression => EvaluateAwait(awaitExpression, environment, context),
            YieldExpression yieldExpression => EvaluateYield(yieldExpression, environment, context),
            ThisExpression => environment.Get(Symbol.This),
            SuperExpression => throw new InvalidOperationException(
                $"Super is not available in this context.{GetSourceInfo(context, expression.Source)}"),
            _ => throw new NotSupportedException(
                $"Typed evaluator does not yet support '{expression.GetType().Name}'.")
        };
    }

    private static object? EvaluateAssignment(AssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var reference = AssignmentReferenceResolver.Resolve(
            new IdentifierExpression(expression.Source, expression.Target), environment, context, EvaluateExpression);

        if (TryEvaluateCompoundAssignmentValue(expression, reference, environment, context, out var compoundValue))
        {
            if (context.ShouldStopEvaluation)
            {
                return compoundValue;
            }

            reference.SetValue(compoundValue);
            return compoundValue;
        }

        var targetValue = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return targetValue;
        }

        try
        {
            reference.SetValue(targetValue);
            return targetValue;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            object? errorObject = ex.Message;

            // If a ReferenceError constructor is available, use it to
            // create a proper JS error instance so user code can catch
            // and inspect it.
            if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctor) &&
                ctor is IJsCallable callable)
            {
                errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
            }

            context.SetThrow(errorObject);
            return errorObject;
        }
    }

    private static bool TryEvaluateCompoundAssignmentValue(
        AssignmentExpression assignment,
        AssignmentReference reference,
        JsEnvironment environment,
        EvaluationContext context,
        out object? value)
    {
        if (assignment.Value is not BinaryExpression binary ||
            binary.Left is not IdentifierExpression identifier ||
            !ReferenceEquals(identifier.Name, assignment.Target))
        {
            value = null;
            return false;
        }

        var leftValue = reference.GetValue();
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
            return true;
        }

        switch (binary.Operator)
        {
            case "&&":
                if (!IsTruthy(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
            case "||":
                if (IsTruthy(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
            case "??":
                if (!IsNullish(leftValue))
                {
                    value = leftValue;
                    return true;
                }

                value = EvaluateExpression(binary.Right, environment, context);
                return true;
        }

        var rightValue = EvaluateExpression(binary.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            value = Symbol.Undefined;
            return true;
        }

        value = binary.Operator switch
        {
            "+" => Add(leftValue, rightValue, context),
            "-" => Subtract(leftValue, rightValue, context),
            "*" => Multiply(leftValue, rightValue, context),
            "/" => Divide(leftValue, rightValue, context),
            "%" => Modulo(leftValue, rightValue, context),
            "**" => Power(leftValue, rightValue, context),
            "==" => LooseEquals(leftValue, rightValue, context),
            "!=" => !LooseEquals(leftValue, rightValue, context),
            "===" => StrictEquals(leftValue, rightValue),
            "!==" => !StrictEquals(leftValue, rightValue),
            "<" => JsOps.LessThan(leftValue, rightValue, context),
            "<=" => JsOps.LessThanOrEqual(leftValue, rightValue, context),
            ">" => JsOps.GreaterThan(leftValue, rightValue, context),
            ">=" => JsOps.GreaterThanOrEqual(leftValue, rightValue, context),
            "&" => BitwiseAnd(leftValue, rightValue, context),
            "|" => BitwiseOr(leftValue, rightValue, context),
            "^" => BitwiseXor(leftValue, rightValue, context),
            "<<" => LeftShift(leftValue, rightValue, context),
            ">>" => RightShift(leftValue, rightValue, context),
            ">>>" => UnsignedRightShift(leftValue, rightValue, context),
            "in" => InOperator(leftValue, rightValue, context),
            "instanceof" => InstanceofOperator(leftValue, rightValue, context),
            _ => throw new NotSupportedException(
                $"Compound assignment operator '{binary.Operator}' is not supported yet.")
        };

        return true;
    }

    private static object? EvaluateIdentifier(IdentifierExpression identifier, JsEnvironment environment,
        EvaluationContext context)
    {
        var reference = AssignmentReferenceResolver.Resolve(identifier, environment, context, EvaluateExpression);
        try
        {
            return reference.GetValue();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("ReferenceError:", StringComparison.Ordinal))
        {
            object? errorObject = ex.Message;

            if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctor) &&
                ctor is IJsCallable callable)
            {
                try
                {
                    errorObject = callable.Invoke([ex.Message], Symbol.Undefined);
                }
                catch (ThrowSignal signal)
                {
                    errorObject = signal.ThrownValue;
                }
            }

            context.SetThrow(errorObject);
            return errorObject;
        }
    }

    private static object? EvaluateAwait(AwaitExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Async generators execute on the generator IR path via TypedGeneratorInstance.
        // When an await expression runs under that executor, the execution environment
        // carries a back-reference to the active generator instance so we can surface
        // pending promises instead of blocking. In that case the generator instance
        // is responsible for evaluating the awaited expression and managing resume.
        if (environment.TryGet(GeneratorInstanceSymbol, out var instanceObj) &&
            instanceObj is TypedGeneratorInstance generator)
        {
            return generator.EvaluateAwaitInGenerator(expression, environment, context);
        }

        var awaited = EvaluateExpression(expression.Expression, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return awaited;
        }

        // Plain async functions now honor pending promises via the shared scheduler.
        object? pendingPromise = null;
        if (!AwaitScheduler.TryAwaitPromiseOrSchedule(awaited, true, ref pendingPromise, context,
                out var resolved))
        {
            if (context.IsThrow || context.IsReturn)
            {
                return resolved;
            }

            // if (pendingPromise is JsObject promise && AwaitScheduler.IsPromiseLike(promise))
            // {
            //     return new PendingAwaitResult(promise);
            // }
        }

        return resolved;
    }

    private static object? EvaluateYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        return expression.IsDelegated
            ? EvaluateDelegatedYield(expression, environment, context)
            : EvaluateSimpleYield(expression, environment, context);
    }

    private static object? EvaluateSimpleYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var yieldedValue = expression.Expression is null
            ? Symbol.Undefined
            : EvaluateExpression(expression.Expression, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return yieldedValue;
        }

        var yieldTracker = GetYieldTracker(environment);
        if (!yieldTracker.ShouldYield(out var yieldIndex))
        {
            var payload = GetResumePayload(environment, yieldIndex);
            if (!payload.HasValue)
            {
                return Symbol.Undefined;
            }

            if (payload.IsThrow)
            {
                context.SetThrow(payload.Value);
                return payload.Value;
            }

            if (payload.IsReturn)
            {
                context.SetReturn(payload.Value);
                return payload.Value;
            }

            return payload.Value;
        }

        context.SetYield(yieldedValue);
        return yieldedValue;
    }

    private static object? EvaluateDelegatedYield(YieldExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Expression is null)
        {
            throw new InvalidOperationException("yield* requires an expression.");
        }

        var stateKey = GetDelegatedStateKey(expression);
        var state = GetDelegatedState(stateKey, environment);

        if (state is null)
        {
            var iterable = EvaluateExpression(expression.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return iterable;
            }

            state = CreateDelegatedState(iterable);
            StoreDelegatedState(stateKey, environment, state);
        }

        var tracker = GetYieldTracker(environment);
        object? pendingSend = null;
        var hasPendingSend = false;
        var pendingThrow = false;
        var pendingReturn = false;

        while (true)
        {
            var iteratorResult = state.MoveNext(pendingSend,
                hasPendingSend && !pendingThrow && !pendingReturn,
                pendingThrow,
                pendingReturn,
                context,
                out var awaitedPromise);

            if (awaitedPromise && context.IsThrow)
            {
                return Symbol.Undefined;
            }

            pendingSend = null;
            hasPendingSend = false;
            pendingThrow = false;
            pendingReturn = false;

            if (iteratorResult.IsDelegatedCompletion)
            {
                if (iteratorResult.PropagateThrow)
                {
                    context.SetThrow(iteratorResult.Value);
                    ClearDelegatedState(stateKey, environment);
                    return iteratorResult.Value;
                }

                ClearDelegatedState(stateKey, environment);
                return iteratorResult.Value;
            }

            var (value, done) = (iteratorResult.Value, iteratorResult.Done);
            if (done)
            {
                ClearDelegatedState(stateKey, environment);
                return value;
            }

            if (!tracker.ShouldYield(out var yieldIndex))
            {
                var payload = GetResumePayload(environment, yieldIndex);
                if (!payload.HasValue)
                {
                    continue;
                }

                if (payload.IsThrow)
                {
                    pendingSend = payload.Value;
                    hasPendingSend = true;
                    pendingThrow = true;
                    continue;
                }

                if (payload.IsReturn)
                {
                    pendingSend = payload.Value;
                    hasPendingSend = true;
                    pendingReturn = true;
                    continue;
                }

                pendingSend = payload.Value;
                hasPendingSend = true;
                continue;
            }

            context.SetYield(value);
            return value;
        }
    }

    private static YieldTracker GetYieldTracker(JsEnvironment environment)
    {
        if (!environment.TryGet(YieldTrackerSymbol, out var tracker) || tracker is not YieldTracker yieldTracker)
        {
            throw new InvalidOperationException("'yield' can only be used inside a generator function.");
        }

        return yieldTracker;
    }

    private static DelegatedYieldState CreateDelegatedState(object? iterable)
    {
        if (TryGetIteratorFromProtocols(iterable, out var iterator) && iterator is not null)
        {
            return DelegatedYieldState.FromIterator(iterator);
        }

        var values = EnumerateValues(iterable);
        return DelegatedYieldState.FromEnumerable(values);
    }

    private static Symbol? GetDelegatedStateKey(YieldExpression expression)
    {
        if (expression.Source is null)
        {
            return null;
        }

        var key = $"__yield_delegate_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
        return Symbol.Intern(key);
    }

    private static Symbol? GetAwaitStateKey(AwaitExpression expression)
    {
        if (expression.Source is null)
        {
            return null;
        }

        var key = $"__await_state_{expression.Source.StartPosition}_{expression.Source.EndPosition}";
        return Symbol.Intern(key);
    }

    private static DelegatedYieldState? GetDelegatedState(Symbol? key, JsEnvironment environment)
    {
        if (key is null)
        {
            return null;
        }

        if (environment.TryGet(key, out var existing) && existing is DelegatedYieldState state)
        {
            return state;
        }

        return null;
    }

    private static void StoreDelegatedState(Symbol? key, JsEnvironment environment, DelegatedYieldState state)
    {
        if (key is null)
        {
            return;
        }

        if (environment.TryGet(key, out _))
        {
            environment.Assign(key, state);
        }
        else
        {
            environment.Define(key, state);
        }
    }

    private static void ClearDelegatedState(Symbol? key, JsEnvironment environment)
    {
        if (key is null)
        {
            return;
        }

        if (environment.TryGet(key, out _))
        {
            environment.Assign(key, null);
        }
    }

    private static ResumePayload GetResumePayload(JsEnvironment environment, int yieldIndex)
    {
        if (!environment.TryGet(YieldResumeContextSymbol, out var contextValue) ||
            contextValue is not YieldResumeContext resumeContext)
        {
            return ResumePayload.Empty;
        }

        return resumeContext.TakePayload(yieldIndex);
    }

    private static bool IsGeneratorContext(JsEnvironment environment)
    {
        return environment.TryGet(YieldResumeContextSymbol, out var contextValue) &&
               contextValue is YieldResumeContext;
    }

    private static GeneratorPendingCompletion GetGeneratorPendingCompletion(JsEnvironment environment)
    {
        if (environment.TryGet(GeneratorPendingCompletionSymbol, out var existing) &&
            existing is GeneratorPendingCompletion pending)
        {
            return pending;
        }

        var created = new GeneratorPendingCompletion();
        environment.DefineFunctionScoped(GeneratorPendingCompletionSymbol, created, true);
        return created;
    }

    private static object? EvaluateDestructuringAssignment(DestructuringAssignmentExpression expression,
        JsEnvironment environment, EvaluationContext context)
    {
        var assignedValue = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return assignedValue;
        }

        // Reuse the same binding machinery as variable declarations so nested
        // destructuring assignments behave consistently.
        AssignBindingTarget(expression.Target, assignedValue, environment, context);
        return assignedValue;
    }

    private static object? EvaluatePropertyAssignment(PropertyAssignmentExpression expression,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is MemberExpression { Target: SuperExpression } superMember)
        {
            if (!context.IsThisInitialized)
            {
                throw CreateSuperReferenceError(environment, context, null);
            }

            var propertyKey = EvaluateExpression(superMember.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var propertyName = JsOps.GetRequiredPropertyName(propertyKey, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var assignedValue = EvaluateExpression(expression.Value, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var binding = ExpectSuperBinding(environment, context);
            binding.SetProperty(propertyName, assignedValue);
            return assignedValue;
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (expression.IsComputed && IsNullish(target))
        {
            throw new InvalidOperationException("Cannot set property on null or undefined.");
        }

        var property = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        AssignPropertyValue(target, property, value, context);
        return value;
    }

    private static object? EvaluateIndexAssignment(IndexAssignmentExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is SuperExpression)
        {
            throw new InvalidOperationException(
                $"Assigning through super is not supported.{GetSourceInfo(context, expression.Source)}");
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var index = EvaluateExpression(expression.Index, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var value = EvaluateExpression(expression.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        AssignPropertyValue(target, index, value, context);
        return value;
    }

    private static object? EvaluateSequence(SequenceExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        _ = EvaluateExpression(expression.Left, environment, context);
        return context.ShouldStopEvaluation
            ? Symbol.Undefined
            : EvaluateExpression(expression.Right, environment, context);
    }

    private static object? EvaluateLiteral(LiteralExpression literal, EvaluationContext context)
    {
        return literal.Value switch
        {
            RegexLiteralValue regex => StandardLibrary.CreateRegExpLiteral(regex.Pattern, regex.Flags,
                context.RealmState),
            _ => literal.Value
        };
    }

    private static object? EvaluateMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        // Fast-path well-known symbol properties so expressions like
        // Symbol.iterator and Symbol.asyncIterator produce real JS symbol
        // values that can be used as keys (e.g. o[Symbol.iterator]).
        if (expression is { IsComputed: false, Target: IdentifierExpression symbolIdentifier } &&
            string.Equals(symbolIdentifier.Name.Name, "Symbol", StringComparison.Ordinal) &&
            expression.Property is LiteralExpression { Value: string symbolProp })
        {
            return symbolProp switch
            {
                "iterator" => TypedAstSymbol.For("Symbol.iterator"),
                "asyncIterator" => TypedAstSymbol.For("Symbol.asyncIterator"),
                "toStringTag" => TypedAstSymbol.For("Symbol.toStringTag"),
                _ => EvaluateDefaultMember(expression, environment, context)
            };
        }

        return EvaluateDefaultMember(expression, environment, context);
    }

    private static object? EvaluateDefaultMember(MemberExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Target is SuperExpression)
        {
            var (memberValue, _) = ResolveSuperMember(expression, environment, context);
            return context.ShouldStopEvaluation ? Symbol.Undefined : memberValue;
        }

        var target = EvaluateExpression(expression.Target, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (expression.IsOptional && IsNullish(target))
        {
            return Symbol.Undefined;
        }

        if (IsNullish(target))
        {
            var error = StandardLibrary.CreateTypeError(
                "Cannot read properties of null or undefined",
                context,
                context.RealmState);
            context.SetThrow(error);
            return Symbol.Undefined;
        }

        var propertyValue = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var propertyName = JsOps.GetRequiredPropertyName(propertyValue, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var isPrivateName = propertyName.Length > 0 && propertyName[0] == '#';
        PrivateNameScope? privateScopeForAccess = null;
        if (isPrivateName)
        {
            privateScopeForAccess = context.CurrentPrivateNameScope;
            if (privateScopeForAccess is null)
            {
                PrivateNameScope.TryResolveScope(propertyName, out privateScopeForAccess);
            }

            if (privateScopeForAccess is null)
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
            }

            if (!propertyName.Contains("@", StringComparison.Ordinal))
            {
                propertyName = privateScopeForAccess.GetKey(propertyName);
            }

            var brandToken = privateScopeForAccess.BrandToken;
            if (target is not IPrivateBrandHolder brandHolder || !brandHolder.HasPrivateBrand(brandToken))
            {
                throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
            }
        }

        if (TryGetPropertyValue(target, propertyName, out var value, context))
        {
            return context.ShouldStopEvaluation ? Symbol.Undefined : value;
        }

        if (privateScopeForAccess is not null)
        {
            throw StandardLibrary.ThrowTypeError("Invalid access of private member", context, context.RealmState);
        }

        return Symbol.Undefined;
    }

    private static object? EvaluateConditional(ConditionalExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var test = EvaluateExpression(expression.Test, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        return IsTruthy(test)
            ? EvaluateExpression(expression.Consequent, environment, context)
            : EvaluateExpression(expression.Alternate, environment, context);
    }

    private static object? EvaluateUnary(UnaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        if (expression.Operator is "++" or "--")
        {
            var reference = AssignmentReferenceResolver.Resolve(
                expression.Operand,
                environment,
                context,
                EvaluateExpression);
            var currentValue = reference.GetValue();
            var updatedValue = expression.Operator == "++"
                ? IncrementValue(currentValue, context)
                : DecrementValue(currentValue, context);
            reference.SetValue(updatedValue);
            return expression.IsPrefix ? updatedValue : currentValue;
        }

        if (expression.Operator == "delete")
        {
            return EvaluateDelete(expression.Operand, environment, context);
        }

        if (expression.Operator == "typeof")
        {
            if (expression.Operand is IdentifierExpression identifier &&
                !environment.TryGet(identifier.Name, out var value))
            {
                return "undefined";
            }

            var operandValue = EvaluateExpression(expression.Operand, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return GetTypeofString(operandValue);
        }

        var operand = EvaluateExpression(expression.Operand, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        return expression.Operator switch
        {
            "!" => !IsTruthy(operand),
            "+" => operand is JsBigInt
                ? throw StandardLibrary.ThrowTypeError("Cannot convert a BigInt value to a number", context)
                : JsOps.ToNumber(operand, context),
            "-" => operand is JsBigInt bigInt ? -bigInt : -JsOps.ToNumber(operand, context),
            "~" => BitwiseNot(operand, context),
            "void" => Symbol.Undefined,
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateBinary(BinaryExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var left = EvaluateExpression(expression.Left, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        switch (expression.Operator)
        {
            case "&&":
                return IsTruthy(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
            case "||":
                return IsTruthy(left)
                    ? left
                    : EvaluateExpression(expression.Right, environment, context);
            case "??":
                return IsNullish(left)
                    ? EvaluateExpression(expression.Right, environment, context)
                    : left;
        }

        var right = EvaluateExpression(expression.Right, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        return expression.Operator switch
        {
            "+" => Add(left, right, context),
            "-" => Subtract(left, right, context),
            "*" => Multiply(left, right, context),
            "/" => Divide(left, right, context),
            "%" => Modulo(left, right, context),
            "**" => Power(left, right, context),
            "==" => LooseEquals(left, right, context),
            "!=" => !LooseEquals(left, right, context),
            "===" => StrictEquals(left, right),
            "!==" => !StrictEquals(left, right),
            "<" => JsOps.LessThan(left, right, context),
            "<=" => JsOps.LessThanOrEqual(left, right, context),
            ">" => JsOps.GreaterThan(left, right, context),
            ">=" => JsOps.GreaterThanOrEqual(left, right, context),
            "&" => BitwiseAnd(left, right, context),
            "|" => BitwiseOr(left, right, context),
            "^" => BitwiseXor(left, right, context),
            "<<" => LeftShift(left, right, context),
            ">>" => RightShift(left, right, context),
            ">>>" => UnsignedRightShift(left, right, context),
            "in" => InOperator(left, right, context),
            "instanceof" => InstanceofOperator(left, right, context),
            _ => throw new NotSupportedException($"Operator '{expression.Operator}' is not supported yet.")
        };
    }

    private static object? EvaluateCall(CallExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var (callee, thisValue, skippedOptional) = EvaluateCallTarget(expression.Callee, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return Symbol.Undefined;
        }

        if (++context.CallDepth > context.MaxCallDepth)
        {
            throw new InvalidOperationException($"Exceeded maximum call depth of {context.MaxCallDepth}.");
        }

        if (expression.IsOptional && IsNullish(callee))
        {
            context.CallDepth--;
            return Symbol.Undefined;
        }

        if (callee is not IJsCallable callable)
        {
            // Special-case Function.prototype.apply / call patterns such as
            // Object.prototype.hasOwnProperty.apply(target, args).
            if (expression.Callee is MemberExpression member)
            {
                if (thisValue is IJsCallable targetFunction &&
                    member.Property is LiteralExpression { Value: string propertyName })
                {
                    if (string.Equals(propertyName, "apply", StringComparison.Ordinal))
                    {
                        return InvokeWithApply(targetFunction, expression.Arguments, environment, context);
                    }

                    if (string.Equals(propertyName, "call", StringComparison.Ordinal))
                    {
                        return InvokeWithCall(targetFunction, expression.Arguments, environment, context);
                    }
                }

                // Fallback for patterns like `obj.formatArgs.call(this, ...)`
                // where `formatArgs` is a callable copied onto `obj` but the
                // `.call` helper is missing or not modeled. In that case we
                // invoke the underlying function directly with the provided
                // `this` value and arguments instead of throwing.
                if (member is
                    {
                        Property: LiteralExpression { Value: "call" }, Target: MemberExpression
                        {
                            Property: LiteralExpression { Value: "formatArgs" }
                        } inner
                    })
                {
                    var target = EvaluateExpression(inner.Target, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    if (TryGetPropertyValue(target, "formatArgs", out var innerValue) &&
                        innerValue is IJsCallable innerFunction)
                    {
                        return InvokeWithCall(innerFunction, expression.Arguments, environment, context);
                    }
                }
            }

            var typeName = callee?.GetType().Name ?? "null";
            var sourceInfo = GetSourceInfo(context, expression.Source);
            var symbolName = callee is Symbol sym ? sym.Name : null;
            var symbolSuffix = symbolName is null ? string.Empty : $" (symbol '{symbolName}')";
            var calleeDescription = DescribeCallee(expression.Callee);
            Console.Error.WriteLine(
                $"[EvaluateCall] Non-callable callee={calleeDescription}, type={typeName}, thisValueType={thisValue?.GetType().Name ?? "null"}{symbolSuffix}{sourceInfo}");
            var error = StandardLibrary.CreateTypeError(
                $"Attempted to call a non-callable value '{calleeDescription}' of type '{typeName}'{symbolSuffix}.",
                context,
                context.RealmState);
            context.SetThrow(error);
            context.CallDepth--;
            return Symbol.Undefined;
        }

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            if (argument.IsSpread)
            {
                var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue, context))
                {
                    arguments.Add(item);
                }

                continue;
            }

            arguments.Add(EvaluateExpression(argument.Expression, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }
        }

        var isAsyncCallable = callable is TypedFunction { IsAsyncLike: true };

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

        var frozenArguments = FreezeArguments(arguments);

        object? callResult = Symbol.Undefined;
        object? newTargetForCall = null;
        if (expression.Callee is SuperExpression &&
            environment.TryGet(Symbol.NewTarget, out var inheritedNewTarget))
        {
            newTargetForCall = inheritedNewTarget;
        }

        SuperBinding? superBindingForCall = null;
        if (expression.Callee is SuperExpression)
        {
            superBindingForCall = ExpectSuperBinding(environment, context);
        }

        JsEnvironment? thisInitializationEnvironment = null;
        object? thisInitializationValue = null;
        if (expression.Callee is SuperExpression &&
            environment.TryFindBinding(Symbol.ThisInitialized, out var foundEnv, out var foundValue))
        {
            thisInitializationEnvironment = foundEnv;
            thisInitializationValue = foundValue;
        }

        try
        {
            if (callable is TypedFunction typedFunction)
            {
                callResult = typedFunction.InvokeWithContext(frozenArguments, thisValue, context,
                    newTarget: newTargetForCall);
            }
            else
            {
                callResult = callable.Invoke(frozenArguments, thisValue);
            }

            if (expression.Callee is SuperExpression)
            {
                var thisAfterSuper = callResult;
                if (callResult is not JsObject && callResult is not IJsObjectLike)
                {
                    thisAfterSuper = thisValue;
                }

                if (thisInitializationEnvironment is not null &&
                    thisInitializationEnvironment.TryGet(Symbol.ThisInitialized, out var alreadyInitialized) &&
                    JsOps.ToBoolean(alreadyInitialized))
                {
                    throw StandardLibrary.ThrowReferenceError(
                        "Super constructor may only be called once.", context, context.RealmState);
                }

                environment.Assign(Symbol.This, thisAfterSuper);

                if (environment.TryGet(Symbol.Super, out var superBinding) && superBinding is SuperBinding binding)
                {
                    var constructorForSuper = superBindingForCall?.Constructor ?? binding.Constructor;
                    var prototypeForSuper = superBindingForCall?.Prototype ?? binding.Prototype;
                    environment.Assign(Symbol.Super,
                        new SuperBinding(constructorForSuper, prototypeForSuper, thisAfterSuper, true));
                }

                context.MarkThisInitialized();
                SetThisInitializationStatus(thisInitializationEnvironment ?? environment, context.IsThisInitialized);
            }
        }
        catch (ThrowSignal signal)
        {
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
            callResult = CreateRejectedPromise(ex, environment);
        }
        finally
        {
            context.CallDepth--;
            if (debugFunction is not null)
            {
                debugFunction.CurrentJsEnvironment = null;
                debugFunction.CurrentContext = null;
            }

            if (envAwareHandle is not null)
            {
                envAwareHandle.CallingJsEnvironment = null;
            }

            if (contextAwareHandle is not null)
            {
                contextAwareHandle.CallingContext = null;
            }
        }

        // If an async callable left a pending throw signal (e.g., default parameter TDZ),
        // translate it into a rejected promise and clear the signal so it does not
        // escape to the caller's context.
        if (isAsyncCallable && context.IsThrow)
        {
            var reason = context.FlowValue;
            context.Clear();
            return CreateRejectedPromise(reason, environment);
        }

        if (isAsyncCallable)
        {
            // Async functions should never propagate a throw signal; ensure the
            // calling context stays clear.
            context.Clear();
        }

        return callResult;
    }

    private static ImmutableArray<object?> FreezeArguments(ImmutableArray<object?>.Builder builder)
    {
        return builder.Count == builder.Capacity
            ? builder.MoveToImmutable()
            : builder.ToImmutable();
    }

    private static object? CreateRejectedPromise(object? reason, JsEnvironment environment)
    {
        if (environment.TryGet(Symbol.Intern("Promise"), out var promiseCtor) &&
            promiseCtor is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("reject", out var rejectValue) &&
            rejectValue is IJsCallable rejectCallable)
        {
            try
            {
                return rejectCallable.Invoke([reason], promiseCtor);
            }
            catch (ThrowSignal signal)
            {
                return signal.ThrownValue;
            }
        }

        return reason;
    }

    private static object? CreateResolvedPromise(object? value, JsEnvironment environment)
    {
        if (environment.TryGet(Symbol.Intern("Promise"), out var promiseCtor) &&
            promiseCtor is IJsPropertyAccessor accessor &&
            accessor.TryGetProperty("resolve", out var resolveValue) &&
            resolveValue is IJsCallable resolveCallable)
        {
            try
            {
                return resolveCallable.Invoke([value], promiseCtor);
            }
            catch (ThrowSignal signal)
            {
                return signal.ThrownValue;
            }
        }

        return value;
    }

    private static object? InvokeWithApply(IJsCallable targetFunction,
        ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        object? thisArg = Symbol.Undefined;
        if (callArguments.Length > 0)
        {
            thisArg = EvaluateExpression(callArguments[0].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }
        }

        var argsBuilder = ImmutableArray.CreateBuilder<object?>();
        if (callArguments.Length > 1)
        {
            var argsArray = EvaluateExpression(callArguments[1].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            foreach (var item in EnumerateSpread(argsArray, context))
            {
                argsBuilder.Add(item);
            }
        }

        if (targetFunction is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
        }

        var frozenArguments = FreezeArguments(argsBuilder);
        if (targetFunction is TypedFunction typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context, newTarget: null);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static object? InvokeWithCall(IJsCallable targetFunction,
        ImmutableArray<CallArgument> callArguments,
        JsEnvironment environment,
        EvaluationContext context)
    {
        object? thisArg = Symbol.Undefined;
        var argsBuilder = ImmutableArray.CreateBuilder<object?>();

        for (var i = 0; i < callArguments.Length; i++)
        {
            var argValue = EvaluateExpression(callArguments[i].Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
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
        if (targetFunction is TypedFunction typedFunction)
        {
            return typedFunction.InvokeWithContext(frozenArguments, thisArg, context, newTarget: null);
        }

        return targetFunction.Invoke(frozenArguments, thisArg);
    }

    private static (object? Callee, object? ThisValue, bool SkippedOptional) EvaluateCallTarget(ExpressionNode callee,
        JsEnvironment environment, EvaluationContext context)
    {
        if (callee is SuperExpression superExpression)
        {
            var binding = ExpectSuperBinding(environment, context);
            if (binding.Constructor is null)
            {
                throw new InvalidOperationException(
                    $"Super constructor is not available in this context.{GetSourceInfo(context, superExpression.Source)}");
            }

            return (binding.Constructor, binding.ThisValue, false);
        }

        if (callee is MemberExpression member)
        {
            if (member.Target is SuperExpression)
            {
                var (memberValue, binding) = ResolveSuperMember(member, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return (Symbol.Undefined, binding.ThisValue, true);
                }

                return (memberValue, binding.ThisValue, false);
            }

            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, null, true);
            }

            if (member.IsOptional && IsNullish(target))
            {
                return (null, null, true);
            }

            var property = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, null, true);
            }

            var propertyName = JsOps.GetRequiredPropertyName(property, context);
            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, null, true);
            }

            if (!TryGetPropertyValue(target, propertyName, out var value, context))
            {
                return context.ShouldStopEvaluation
                    ? (Symbol.Undefined, null, true)
                    : (Symbol.Undefined, target, false);
            }

            if (context.ShouldStopEvaluation)
            {
                return (Symbol.Undefined, null, true);
            }

            return (value, target, false);
        }

        var directCallee = EvaluateExpression(callee, environment, context);
        return (directCallee, null, false);
    }

    // SpreadElement runtime semantics (ECMA-262 §12.2.5.2) use GetIterator on the operand.
    private static IEnumerable<object?> EnumerateSpread(object? value, EvaluationContext context)
    {
        if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
        {
            throw StandardLibrary.ThrowTypeError("Value is not iterable.", context, context.RealmState);
        }

        var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);
        var iteratorThrew = false;

        try
        {
            while (true)
            {
                var (item, done) = iteratorRecord.Next(context);
                if (context.ShouldStopEvaluation)
                {
                    if (iterator is not null)
                    {
                        IteratorClose(iterator, context);
                    }

                    yield break;
                }

                if (done)
                {
                    yield break;
                }

                yield return item;
            }
        }
        finally
        {
            if (iterator is not null && context.IsThrow)
            {
                IteratorClose(iterator, context);
            }

            enumerator?.Dispose();
        }
    }

    private static bool EvaluateDelete(ExpressionNode operand, JsEnvironment environment, EvaluationContext context)
    {
        if (operand is MemberExpression member)
        {
            var target = EvaluateExpression(member.Target, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            var propertyValue = EvaluateExpression(member.Property, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return false;
            }

            var deleted = DeletePropertyValue(target, propertyValue, context);
            if (!deleted && context.CurrentScope.IsStrict)
            {
                throw StandardLibrary.ThrowTypeError("Cannot delete property", context, context.RealmState);
            }

            return deleted;
        }

        if (operand is IdentifierExpression identifier)
        {
            if (context.CurrentScope.IsStrict)
            {
                throw StandardLibrary.ThrowSyntaxError(
                    "Delete of an unqualified identifier is not allowed in strict mode.",
                    context,
                    context.RealmState);
            }

            var outcome = environment.DeleteBinding(identifier.Name);
            return outcome is DeleteBindingResult.Deleted or DeleteBindingResult.NotFound;
        }

        _ = EvaluateExpression(operand, environment, context);
        return true;
    }

    private static object? EvaluateNew(NewExpression expression, JsEnvironment environment, EvaluationContext context)
    {
        var realm = context.RealmState;
        var constructor = EvaluateExpression(expression.Constructor, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (constructor is not IJsCallable callable)
        {
            throw new InvalidOperationException("Attempted to construct a non-callable value.");
        }

        if (constructor is HostFunction hostFunction &&
            (!hostFunction.IsConstructor || hostFunction.DisallowConstruct))
        {
            var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke([hostFunction.ConstructErrorMessage ?? "is not a constructor"], null)
                : new InvalidOperationException(hostFunction.ConstructErrorMessage ?? "Target is not a constructor.");
            throw new ThrowSignal(error);
        }

        if (constructor is TypedFunction { IsArrowFunction: true })
        {
            var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                ? typeErrorCtor.Invoke(["Target is not a constructor"], null)
                : new InvalidOperationException("Target is not a constructor.");
            throw new ThrowSignal(error);
        }

        var instance = new JsObject();
        if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject proto)
        {
            instance.SetPrototype(proto);
        }

        InitializeClassInstance(constructor, instance, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        var args = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
        foreach (var argument in expression.Arguments)
        {
            args.Add(EvaluateExpression(argument, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }
        }

        object? result;
        try
        {
            if (callable is TypedFunction typedFunction)
            {
                result = typedFunction.InvokeWithContext(args.MoveToImmutable(), instance, context,
                    newTarget: constructor);
            }
            else
            {
                result = callable.Invoke(args.MoveToImmutable(), instance);
            }
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
            return signal.ThrownValue;
        }

        // In JavaScript, constructors can explicitly return an object to override the
        // default instance that `new` creates. Our host objects (Map, Set, custom
        // host functions, etc.) don't necessarily derive from JsObject, but they do
        // expose their members through IJsPropertyAccessor/IJsCallable. Treat any
        // such object-like result as the constructed value; otherwise fall back to
        // the auto-created instance.
        return result switch
        {
            IJsPropertyAccessor => result,
            IJsCallable => result,
            _ => instance
        };
    }

    private static void InitializeClassInstance(object? constructor, JsObject instance, JsEnvironment environment,
        EvaluationContext context)
    {
        if (constructor is TypedFunction typedFunction)
        {
            typedFunction.InitializeInstance(instance, environment, context);
        }
    }

    private static object? EvaluateArray(ArrayExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var array = new JsArray(context.RealmState);
        foreach (var element in expression.Elements)
        {
            if (element.IsSpread)
            {
                var spreadValue = EvaluateExpression(element.Expression!, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }

                foreach (var item in EnumerateSpread(spreadValue, context))
                {
                    array.Push(item);
                }

                continue;
            }

            if (element.Expression is null)
            {
                array.PushHole();
            }
            else
            {
                array.Push(EvaluateExpression(element.Expression, environment, context));
            }
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }
        }

        StandardLibrary.AddArrayMethods(array, context.RealmState);
        return array;
    }

    private static string DescribeCallee(ExpressionNode expression)
    {
        return expression switch
        {
            IdentifierExpression id => id.Name.Name,
            MemberExpression member => $"{DescribeCallee(member.Target)}.{DescribeMemberName(member.Property)}",
            CallExpression call => $"{DescribeCallee(call.Callee)}(...)",
            _ => expression.GetType().Name
        };
    }

    private static string DescribeMemberName(ExpressionNode property)
    {
        return property switch
        {
            LiteralExpression { Value: string s } => s,
            IdentifierExpression id => id.Name.Name,
            _ => property.GetType().Name
        };
    }

    private static object? EvaluateObject(ObjectExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
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
                        return Symbol.Undefined;
                    }

                    var value = member.Value is null
                        ? Symbol.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Method:
                {
                    var callable = CreateFunctionValue(member.Function!, environment, context);
                    if (callable is TypedFunction typed)
                    {
                        typed.SetHomeObject(obj);
                    }
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    obj.SetProperty(name, callable);
                    break;
                }
                case ObjectMemberKind.Getter:
                {
                    var getter = new TypedFunction(member.Function!, environment, context.RealmState);
                    getter.SetHomeObject(obj);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    DefineAccessorProperty(obj, name, getter, null);
                    break;
                }
                case ObjectMemberKind.Setter:
                {
                    var setter = new TypedFunction(member.Function!, environment, context.RealmState);
                    setter.SetHomeObject(obj);
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    DefineAccessorProperty(obj, name, null, setter);
                    break;
                }
                case ObjectMemberKind.Field:
                {
                    var name = ResolveObjectMemberName(member, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    var value = member.Value is null
                        ? Symbol.Undefined
                        : EvaluateExpression(member.Value, environment, context);
                    obj.SetProperty(name, value);
                    break;
                }
                case ObjectMemberKind.Spread:
                {
                    var spreadValue = EvaluateExpression(member.Value!, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    if (IsNullish(spreadValue) || spreadValue is IIsHtmlDda)
                    {
                        break;
                    }

                    // Object spread uses CopyDataProperties (ECMA-262 PropertyDefinitionEvaluation),
                    // which skips null/undefined and copies enumerable own keys in [[OwnPropertyKeys]] order.
                    if (spreadValue is IDictionary<string, object?> dictionary and not JsObject)
                    {
                        foreach (var kvp in dictionary)
                        {
                            obj.SetProperty(kvp.Key, kvp.Value);
                        }

                        break;
                    }

                    var accessor = spreadValue is IJsPropertyAccessor propertyAccessor
                        ? propertyAccessor
                        : ToObjectForDestructuring(spreadValue, context);

                    foreach (var key in GetEnumerableOwnPropertyKeysInOrder(accessor))
                    {
                        var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                            ? val
                            : Symbol.Undefined;
                        obj.SetProperty(key, spreadPropertyValue);
                    }

                    break;
                }
            }
        }

        return obj;
    }

    private static void DefineAccessorProperty(JsObject obj, string name, IJsCallable? getter, IJsCallable? setter)
    {
        var descriptor = obj.GetOwnPropertyDescriptor(name) ??
                         new PropertyDescriptor { Enumerable = true, Configurable = true };

        descriptor.Get = getter ?? descriptor.Get;
        descriptor.Set = setter ?? descriptor.Set;
        obj.DefineProperty(name, descriptor);
    }

    private static string ResolveObjectMemberName(ObjectMember member, JsEnvironment environment,
        EvaluationContext context)
    {
        object? keyValue;

        if (member.IsComputed)
        {
            if (member.Key is not ExpressionNode keyExpression)
            {
                throw new InvalidOperationException("Computed property name must be an expression.");
            }

            keyValue = EvaluateExpression(keyExpression, environment, context);
        }
        else
        {
            keyValue = member.Key;
        }

        if (context.ShouldStopEvaluation)
        {
            return string.Empty;
        }

        var propertyName = JsOps.GetRequiredPropertyName(keyValue, context);
        return context.ShouldStopEvaluation ? string.Empty : propertyName;
    }

    private static object? EvaluateTemplateLiteral(TemplateLiteralExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var builder = new StringBuilder();
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

            var value = EvaluateExpression(part.Expression, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            builder.Append(value.ToJsString());
        }

        return builder.ToString();
    }

    private static object? EvaluateTaggedTemplate(TaggedTemplateExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var (tagValue, thisValue, skippedOptional) = EvaluateCallTarget(expression.Tag, environment, context);
        if (context.ShouldStopEvaluation || skippedOptional)
        {
            return Symbol.Undefined;
        }

        if (tagValue is not IJsCallable callable)
        {
            throw new InvalidOperationException("Tag in tagged template must be a function.");
        }

        var stringsArrayValue = EvaluateExpression(expression.StringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (stringsArrayValue is not JsArray stringsArray)
        {
            throw new InvalidOperationException("Tagged template strings array is invalid.");
        }

        var rawStringsArrayValue = EvaluateExpression(expression.RawStringsArray, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return Symbol.Undefined;
        }

        if (rawStringsArrayValue is not JsArray rawStringsArray)
        {
            throw new InvalidOperationException("Tagged template raw strings array is invalid.");
        }

        var templateObject = CreateTemplateObject(stringsArray, rawStringsArray);

        var arguments = ImmutableArray.CreateBuilder<object?>(expression.Expressions.Length + 1);
        arguments.Add(templateObject);

        foreach (var expr in expression.Expressions)
        {
            arguments.Add(EvaluateExpression(expr, environment, context));
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }
        }

        if (callable is IJsEnvironmentAwareCallable envAware)
        {
            envAware.CallingJsEnvironment = environment;
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
            return callable.Invoke(frozenArguments, thisValue);
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

    private static JsObject CreateTemplateObject(JsArray stringsArray, JsArray rawStringsArray)
    {
        var templateObject = new JsObject();
        for (var i = 0; i < stringsArray.Items.Count; i++)
        {
            templateObject[i.ToString(CultureInfo.InvariantCulture)] = stringsArray.Items[i];
        }

        templateObject["length"] = (double)stringsArray.Items.Count;
        templateObject["raw"] = rawStringsArray;
        return templateObject;
    }

    private static bool IsNullish(object? value)
    {
        return value.IsNullish();
    }

    private static bool IsTruthy(object? value)
    {
        return JsOps.IsTruthy(value);
    }

    private static object? Add(object? left, object? right, EvaluationContext context)
    {
        var leftPrimitive = JsOps.ToPrimitive(left, "default", context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightPrimitive = JsOps.ToPrimitive(right, "default", context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftPrimitive is string || rightPrimitive is string)
        {
            bool IsRealSymbol(object? v) =>
                v switch
                {
                    TypedAstSymbol => true,
                    Symbol sym when !ReferenceEquals(sym, Symbol.Undefined) => true,
                    _ => false
                };

            if (IsRealSymbol(leftPrimitive) || IsRealSymbol(rightPrimitive))
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", context);
            }

            return JsOps.ToJsString(leftPrimitive, context) + JsOps.ToJsString(rightPrimitive, context);
        }

        var leftNumeric = JsOps.ToNumeric(leftPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        var rightNumeric = JsOps.ToNumeric(rightPrimitive, context);
        if (context.ShouldStopEvaluation)
        {
            return context.FlowValue;
        }

        if (leftNumeric is JsBigInt leftBigInt && rightNumeric is JsBigInt rightBigInt)
        {
            return leftBigInt + rightBigInt;
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return JsOps.ToNumber(leftNumeric, context) + JsOps.ToNumber(rightNumeric, context);
    }

    private static object Subtract(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l - r,
            (l, r) => l - r,
            context);
    }

    private static object Multiply(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l * r,
            (l, r) => l * r,
            context);
    }

    private static object Divide(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l / r,
            (l, r) => l / r,
            context);
    }

    private static object Modulo(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            (l, r) => l % r,
            (l, r) => l % r,
            context);
    }

    private static object Power(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrNumericOperation(left, right,
            JsBigInt.Pow,
            (l, r) => Math.Pow(l, r),
            context);
    }

    private static object PerformBigIntOrNumericOperation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<double, double, object> numericOp,
        EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        return numericOp(JsOps.ToNumber(left, context), JsOps.ToNumber(right, context));
    }

    private static bool LooseEquals(object? left, object? right, EvaluationContext context)
    {
        return JsOps.LooseEquals(left, right, context);
    }

    private static bool StrictEquals(object? left, object? right)
    {
        return JsOps.StrictEquals(left, right);
    }

    private static object BitwiseAnd(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l & r,
            (l, r) => l & r,
            context);
    }

    private static object BitwiseOr(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l | r,
            (l, r) => l | r,
            context);
    }

    private static object BitwiseXor(object? left, object? right, EvaluationContext context)
    {
        return PerformBigIntOrInt32Operation(left, right,
            (l, r) => l ^ r,
            (l, r) => l ^ r,
            context);
    }

    private static object BitwiseNot(object? operand, EvaluationContext context)
    {
        var numeric = JsOps.ToNumeric(operand, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (numeric is JsBigInt bigInt)
        {
            return ~bigInt;
        }

        var int32 = JsNumericConversions.ToInt32(JsOps.ToNumber(numeric, context));
        return (double)~int32;
    }

    private static object LeftShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt << (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt << rightInt);
    }

    private static object RightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt leftBigInt && right is JsBigInt rightBigInt)
        {
            if (rightBigInt.Value > int.MaxValue || rightBigInt.Value < int.MinValue)
            {
                throw StandardLibrary.ThrowRangeError("BigInt shift amount is too large", context);
            }

            return leftBigInt >> (int)rightBigInt.Value;
        }

        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = ToInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftInt >> rightInt);
    }

    private static object UnsignedRightShift(object? left, object? right, EvaluationContext context)
    {
        if (left is JsBigInt || right is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("BigInts have no unsigned right shift, use >> instead", context);
        }

        var leftUInt = ToUInt32(left, context);
        var rightInt = ToInt32(right, context) & 0x1F;
        return (double)(leftUInt >> rightInt);
    }

    private static object PerformBigIntOrInt32Operation(
        object? left,
        object? right,
        Func<JsBigInt, JsBigInt, object> bigIntOp,
        Func<int, int, int> int32Op,
        EvaluationContext context)
    {
        var leftNumeric = JsOps.ToNumeric(left, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        var rightNumeric = JsOps.ToNumeric(right, context);
        if (context.IsThrow)
        {
            return context.FlowValue ?? Symbol.Undefined;
        }

        if (leftNumeric is JsBigInt leftBigInt && rightNumeric is JsBigInt rightBigInt)
        {
            return bigIntOp(leftBigInt, rightBigInt);
        }

        if (leftNumeric is JsBigInt || rightNumeric is JsBigInt)
        {
            throw StandardLibrary.ThrowTypeError("Cannot mix BigInt and other types, use explicit conversions",
                context);
        }

        var leftInt = JsNumericConversions.ToInt32(JsOps.ToNumber(leftNumeric, context));
        var rightInt = JsNumericConversions.ToInt32(JsOps.ToNumber(rightNumeric, context));
        return (double)int32Op(leftInt, rightInt);
    }

    private static int ToInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToInt32(JsOps.ToNumber(value, context));
    }

    private static uint ToUInt32(object? value, EvaluationContext context)
    {
        return JsNumericConversions.ToUInt32(JsOps.ToNumber(value, context));
    }

    private static object IncrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value + BigInteger.One),
            _ => JsOps.ToNumber(value, context) + 1
        };
    }

    private static object DecrementValue(object? value, EvaluationContext context)
    {
        return value switch
        {
            JsBigInt bigInt => new JsBigInt(bigInt.Value - BigInteger.One),
            _ => JsOps.ToNumber(value, context) - 1
        };
    }

    private static string? ToPropertyName(object? value, EvaluationContext? context = null)
    {
        return JsOps.ToPropertyName(value, context);
    }

    private static bool TryResolveArrayIndex(object? candidate, out int index, EvaluationContext? context = null)
    {
        return JsOps.TryResolveArrayIndex(candidate, out index, context);
    }

    private static bool TryGetPropertyValue(object? target, string propertyName, out object? value)
    {
        return JsOps.TryGetPropertyValue(target, propertyName, out value);
    }

    private static bool TryGetPropertyValue(object? target, object? propertyKey, out object? value,
        EvaluationContext? context = null)
    {
        return JsOps.TryGetPropertyValue(target, propertyKey, out value, context);
    }

    private static void AssignPropertyValue(object? target, object? propertyKey, object? value,
        EvaluationContext? context = null)
    {
        JsOps.AssignPropertyValue(target, propertyKey, value, context);
    }

    private static void AssignPropertyValueByName(object? target, string propertyName, object? value)
    {
        JsOps.AssignPropertyValueByName(target, propertyName, value);
    }

    private static bool DeletePropertyValue(object? target, object? propertyKey, EvaluationContext? context = null)
    {
        return JsOps.DeletePropertyValue(target, propertyKey, context);
    }

    private static void HoistVarDeclarations(
        BlockStatement block,
        JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues = true,
        HashSet<Symbol>? lexicalNames = null,
        HashSet<Symbol>? catchParameterNames = null,
        HashSet<Symbol>? simpleCatchParameterNames = null,
        bool inBlockScope = false)
    {
        var effectiveLexicalNames = lexicalNames is null
            ? CollectLexicalNames(block)
            : [..lexicalNames];
        if (lexicalNames is not null)
        {
            effectiveLexicalNames.UnionWith(CollectLexicalNames(block));
        }

        var effectiveCatchNames = catchParameterNames is null
            ? CollectCatchParameterNames(block)
            : [..catchParameterNames];
        if (catchParameterNames is not null)
        {
            effectiveCatchNames.UnionWith(CollectCatchParameterNames(block));
        }

        var effectiveSimpleCatchNames = simpleCatchParameterNames is null
            ? CollectSimpleCatchParameterNames(block)
            : [..simpleCatchParameterNames];
        if (simpleCatchParameterNames is not null)
        {
            effectiveSimpleCatchNames.UnionWith(CollectSimpleCatchParameterNames(block));
        }

        HoistVarDeclarationsPass(
            block,
            environment,
            context,
            hoistFunctionValues,
            effectiveLexicalNames,
            effectiveCatchNames,
            effectiveSimpleCatchNames,
            HoistPass.Functions,
            inBlockScope);
        HoistVarDeclarationsPass(
            block,
            environment,
            context,
            hoistFunctionValues: false,
            effectiveLexicalNames,
            effectiveCatchNames,
            effectiveSimpleCatchNames,
            HoistPass.Vars,
            inBlockScope);
    }

    private static void HoistVarDeclarationsPass(
        BlockStatement block,
        JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues,
        HashSet<Symbol> lexicalNames,
        HashSet<Symbol> catchParameterNames,
        HashSet<Symbol> simpleCatchParameterNames,
        HoistPass pass,
        bool inBlockScope)
    {
        foreach (var statement in block.Statements)
        {
            HoistFromStatement(statement, environment, context, hoistFunctionValues, lexicalNames, catchParameterNames,
                simpleCatchParameterNames,
                pass,
                inBlockScope);
        }
    }

    private static HashSet<Symbol> MergeLexicalNames(BlockStatement block, HashSet<Symbol> lexicalNames)
    {
        var merged = new HashSet<Symbol>(lexicalNames);
        merged.UnionWith(CollectLexicalNames(block));
        return merged;
    }

    private static HashSet<Symbol> MergeCatchNames(BlockStatement block, HashSet<Symbol> catchParameterNames)
    {
        var merged = new HashSet<Symbol>(catchParameterNames);
        merged.UnionWith(CollectCatchParameterNames(block));
        return merged;
    }

    private static HashSet<Symbol> MergeSimpleCatchNames(BlockStatement block, HashSet<Symbol> simpleCatchParameterNames)
    {
        var merged = new HashSet<Symbol>(simpleCatchParameterNames);
        merged.UnionWith(CollectSimpleCatchParameterNames(block));
        return merged;
    }

    private static void HoistFromStatement(
        StatementNode statement,
        JsEnvironment environment,
        EvaluationContext context,
        bool hoistFunctionValues,
        HashSet<Symbol> lexicalNames,
        HashSet<Symbol> catchParameterNames,
        HashSet<Symbol> simpleCatchParameterNames,
        HoistPass pass,
        bool inBlockScope)
    {
        while (true)
        {
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var } varDeclaration when pass == HoistPass.Vars:
                    foreach (var declarator in varDeclaration.Declarators)
                    {
                        HoistFromBindingTarget(declarator.Target, environment, context, lexicalNames);
                    }

                    break;
                case BlockStatement block:
                    HoistVarDeclarationsPass(
                        block,
                        environment,
                        context,
                        hoistFunctionValues,
                        MergeLexicalNames(block, lexicalNames),
                        MergeCatchNames(block, catchParameterNames),
                        MergeSimpleCatchNames(block, simpleCatchParameterNames),
                        pass,
                        true);
                    break;
                case IfStatement ifStatement:
                    HoistFromStatement(ifStatement.Then, environment, context, false,
                        lexicalNames, catchParameterNames, simpleCatchParameterNames, pass, true);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        hoistFunctionValues = false;
                        inBlockScope = true;
                        continue;
                    }

                    break;
                case WhileStatement whileStatement:
                    statement = whileStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    continue;
                case DoWhileStatement doWhileStatement:
                    statement = doWhileStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    continue;
                case WithStatement withStatement:
                    statement = withStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    continue;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var } initVar &&
                        pass == HoistPass.Vars)
                    {
                        HoistFromStatement(initVar, environment, context, hoistFunctionValues, lexicalNames,
                            catchParameterNames, simpleCatchParameterNames, pass,
                            inBlockScope);
                    }

                    statement = forStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    continue;
                case ForEachStatement forEachStatement:
                    if (pass == HoistPass.Vars && forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        HoistFromBindingTarget(forEachStatement.Target, environment, context, lexicalNames);
                    }

                    statement = forEachStatement.Body;
                    hoistFunctionValues = false;
                    inBlockScope = true;
                    continue;
                case LabeledStatement labeled:
                    statement = labeled.Statement;
                    continue;
                case TryStatement tryStatement:
                    HoistVarDeclarationsPass(tryStatement.TryBlock, environment, context, false,
                        MergeLexicalNames(tryStatement.TryBlock, lexicalNames),
                        MergeCatchNames(tryStatement.TryBlock, catchParameterNames),
                        MergeSimpleCatchNames(tryStatement.TryBlock, simpleCatchParameterNames),
                        pass,
                        true);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        HoistVarDeclarationsPass(catchClause.Body, environment, context, false,
                            MergeLexicalNames(catchClause.Body, lexicalNames),
                            MergeCatchNames(catchClause.Body, catchParameterNames),
                            MergeSimpleCatchNames(catchClause.Body, simpleCatchParameterNames),
                            pass,
                            true);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        HoistVarDeclarationsPass(finallyBlock, environment, context, false,
                            MergeLexicalNames(finallyBlock, lexicalNames),
                            MergeCatchNames(finallyBlock, catchParameterNames),
                            MergeSimpleCatchNames(finallyBlock, simpleCatchParameterNames),
                            pass,
                            true);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        HoistVarDeclarationsPass(switchCase.Body, environment, context, false,
                            MergeLexicalNames(switchCase.Body, lexicalNames),
                            MergeCatchNames(switchCase.Body, catchParameterNames),
                            MergeSimpleCatchNames(switchCase.Body, simpleCatchParameterNames),
                            pass,
                            true);
                    }

                    break;
                case FunctionDeclaration functionDeclaration:
                {
                    if (pass != HoistPass.Functions)
                    {
                        break;
                    }

                    if (context.BlockedFunctionVarNames is { } blockedHoists &&
                        blockedHoists.Contains(functionDeclaration.Name))
                    {
                        break;
                    }

                    if (context.CurrentScope.IsStrict && lexicalNames.Contains(functionDeclaration.Name))
                    {
                        break;
                    }

                    var hasNonCatchLexical = lexicalNames.Contains(functionDeclaration.Name) &&
                                             !simpleCatchParameterNames.Contains(functionDeclaration.Name);
                    var functionScope = environment.GetFunctionScope();
                    var isAnnexBBlockFunction =
                        inBlockScope &&
                        context.CurrentScope is { IsStrict: false, AllowAnnexB: true };

                    if (isAnnexBBlockFunction)
                    {
                        if (hasNonCatchLexical ||
                            functionScope.HasBodyLexicalName(functionDeclaration.Name))
                        {
                            break;
                        }

                        functionScope.DefineFunctionScoped(
                            functionDeclaration.Name,
                            Symbol.Undefined,
                            hasInitializer: false,
                            isFunctionDeclaration: true,
                            globalFunctionConfigurable: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                            context,
                            blocksFunctionScopeOverride: true);

                        break;
                    }

                    if (inBlockScope)
                    {
                        break;
                    }

                    if (hoistFunctionValues)
                    {
                        var functionValue = CreateFunctionValue(functionDeclaration.Function, environment, context);
                        environment.DefineFunctionScoped(
                            functionDeclaration.Name,
                            functionValue,
                            hasInitializer: true,
                            isFunctionDeclaration: true,
                            globalFunctionConfigurable: context is { ExecutionKind: ExecutionKind.Eval, IsStrictSource: false },
                            context);
                    }
                    break;
                }
                case ClassDeclaration:
                case ModuleStatement:
                    break;
            }

            break;
        }
    }

    private static HashSet<Symbol> CollectLexicalNames(BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        CollectLexicalNamesFromStatement(block, names);
        return names;
    }

    private static HashSet<Symbol> CollectCatchParameterNames(BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        CollectCatchNamesFromStatement(block, names);
        return names;
    }

    private static HashSet<Symbol> CollectSimpleCatchParameterNames(BlockStatement block)
    {
        var names = new HashSet<Symbol>();
        CollectSimpleCatchNamesFromStatement(block, names);
        return names;
    }

    private static bool HasHoistableDeclarations(BlockStatement block)
    {
        var stack = new Stack<StatementNode>();
        stack.Push(block);

        while (stack.Count > 0)
        {
            var statement = stack.Pop();
            switch (statement)
            {
                case VariableDeclaration { Kind: VariableKind.Var }:
                case FunctionDeclaration:
                    return true;
                case BlockStatement b:
                    foreach (var inner in b.Statements)
                    {
                        stack.Push(inner);
                    }

                    break;
                case IfStatement ifStatement:
                    stack.Push(ifStatement.Then);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        stack.Push(elseBranch);
                    }

                    break;
                case WhileStatement whileStatement:
                    stack.Push(whileStatement.Body);
                    break;
                case DoWhileStatement doWhileStatement:
                    stack.Push(doWhileStatement.Body);
                    break;
                case WithStatement withStatement:
                    stack.Push(withStatement.Body);
                    break;
                case ForStatement forStatement:
                    if (forStatement.Initializer is VariableDeclaration { Kind: VariableKind.Var })
                    {
                        return true;
                    }

                    if (forStatement.Body is not null)
                    {
                        stack.Push(forStatement.Body);
                    }

                    break;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind == VariableKind.Var)
                    {
                        return true;
                    }

                    stack.Push(forEachStatement.Body);
                    break;
                case LabeledStatement labeled:
                    stack.Push(labeled.Statement);
                    break;
                case TryStatement tryStatement:
                    stack.Push(tryStatement.TryBlock);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        stack.Push(catchClause.Body);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        stack.Push(finallyBlock);
                    }

                    break;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        stack.Push(switchCase.Body);
                    }

                    break;
            }
        }

        return false;
    }

    private static void CollectLexicalNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectLexicalNamesFromStatement(inner, names);
                    }

                    break;
                case VariableDeclaration { Kind: VariableKind.Let or VariableKind.Const } letDecl:
                    foreach (var declarator in letDecl.Declarators)
                    {
                        CollectSymbolsFromBinding(declarator.Target, names);
                    }

                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    break;
                case FunctionDeclaration:
                    // Function declarations are handled separately; they should not block themselves.
                    break;
                case IfStatement ifStatement:
                    CollectLexicalNamesFromStatement(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    break;
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
                    if (forStatement.Initializer is VariableDeclaration
                        {
                            Kind: VariableKind.Let or VariableKind.Const
                        } decl)
                    {
                        foreach (var declarator in decl.Declarators)
                        {
                            CollectSymbolsFromBinding(declarator.Target, names);
                        }
                    }

                    statement = forStatement.Body;
                    continue;
                case ForEachStatement forEachStatement:
                    if (forEachStatement.DeclarationKind is VariableKind.Let or VariableKind.Const)
                    {
                        CollectSymbolsFromBinding(forEachStatement.Target, names);
                    }

                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectLexicalNamesFromStatement(switchCase.Body, names);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectLexicalNamesFromStatement(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        CollectSymbolsFromBinding(catchClause.Binding, names);
                        CollectLexicalNamesFromStatement(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectCatchNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectCatchNamesFromStatement(inner, names);
                    }

                    break;
                case IfStatement ifStatement:
                    CollectCatchNamesFromStatement(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    break;
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
                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    break;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectCatchNamesFromStatement(switchCase.Body, names);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectCatchNamesFromStatement(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        CollectSymbolsFromBinding(catchClause.Binding, names);
                        CollectCatchNamesFromStatement(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectSimpleCatchNamesFromStatement(StatementNode statement, HashSet<Symbol> names)
    {
        while (true)
        {
            switch (statement)
            {
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        CollectSimpleCatchNamesFromStatement(inner, names);
                    }

                    break;
                case IfStatement ifStatement:
                    CollectSimpleCatchNamesFromStatement(ifStatement.Then, names);
                    if (ifStatement.Else is { } elseBranch)
                    {
                        statement = elseBranch;
                        continue;
                    }

                    break;
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
                    if (forStatement.Body is not null)
                    {
                        statement = forStatement.Body;
                        continue;
                    }

                    break;
                case ForEachStatement forEachStatement:
                    statement = forEachStatement.Body;
                    continue;
                case SwitchStatement switchStatement:
                    foreach (var switchCase in switchStatement.Cases)
                    {
                        CollectSimpleCatchNamesFromStatement(switchCase.Body, names);
                    }

                    break;
                case TryStatement tryStatement:
                    CollectSimpleCatchNamesFromStatement(tryStatement.TryBlock, names);
                    if (tryStatement.Catch is { } catchClause)
                    {
                        if (catchClause.Binding is IdentifierBinding identifierBinding)
                        {
                            names.Add(identifierBinding.Name);
                        }

                        CollectSimpleCatchNamesFromStatement(catchClause.Body, names);
                    }

                    if (tryStatement.Finally is { } finallyBlock)
                    {
                        statement = finallyBlock;
                        continue;
                    }

                    break;
            }

            break;
        }
    }

    private static void CollectSymbolsFromBinding(BindingTarget target, HashSet<Symbol> names)
    {
        WalkBindingTargets(target, id => names.Add(id.Name));
    }

    private static void CollectParameterNamesFromFunction(FunctionExpression function, List<Symbol> names)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name is not null)
            {
                names.Add(parameter.Name);
            }

            if (parameter.Pattern is not null)
            {
                WalkBindingTargets(parameter.Pattern, id => names.Add(id.Name));
            }
        }
    }

    private static void HoistFromBindingTarget(
        BindingTarget target,
        JsEnvironment environment,
        EvaluationContext context,
        HashSet<Symbol>? lexicalNames = null)
    {
        WalkBindingTargets(target,
            identifier =>
            {
                if (!context.CurrentScope.IsStrict && lexicalNames is not null && lexicalNames.Contains(identifier.Name))
                {
                    return;
                }

                environment.DefineFunctionScoped(identifier.Name, Symbol.Undefined, false, context: context);
            });
    }

    private static void WalkBindingTargets(BindingTarget target, Action<IdentifierBinding> onIdentifier)
    {
        while (true)
        {
            switch (target)
            {
                case IdentifierBinding id:
                    onIdentifier(id);
                    return;
                case ArrayBinding array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Target is null)
                        {
                            continue;
                        }

                        WalkBindingTargets(element.Target, onIdentifier);
                    }

                    if (array.RestElement is null)
                    {
                        return;
                    }

                    target = array.RestElement;
                    continue;

                case ObjectBinding obj:
                    foreach (var property in obj.Properties)
                    {
                        WalkBindingTargets(property.Target, onIdentifier);
                    }

                    if (obj.RestElement is null)
                    {
                        return;
                    }

                    target = obj.RestElement;
                    continue;

                default:
                    return;
            }
        }
    }

    private static bool InOperator(object? property, object? target, EvaluationContext context)
    {
        var propertyName = JsOps.GetRequiredPropertyName(property, context);
        if (context.ShouldStopEvaluation)
        {
            return false;
        }

        if (target is ModuleNamespace moduleNamespace)
        {
            return moduleNamespace.HasExport(propertyName);
        }

        return TryGetPropertyValue(target, propertyName, out _, context);
    }

    private static bool InstanceofOperator(object? left, object? right, EvaluationContext context)
    {
        if (right is not IJsPropertyAccessor)
        {
            context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not an object",
                context));
            return false;
        }

        var hasInstanceSymbol = TypedAstSymbol.For("Symbol.hasInstance");
        if (TryGetPropertyValue(right, hasInstanceSymbol, out var hasInstance, context))
        {
            if (!IsNullish(hasInstance))
            {
                if (hasInstance is not IJsCallable callable)
                {
                    context.SetThrow(StandardLibrary.CreateTypeError("@@hasInstance is not callable", context));
                    return false;
                }

                try
                {
                    var result = callable.Invoke([left], right);
                    return JsOps.ToBoolean(result);
                }
                catch (ThrowSignal signal)
                {
                    context.SetThrow(signal.ThrownValue);
                    return false;
                }
            }
        }
        else if (context.ShouldStopEvaluation)
        {
            return false;
        }

        if (right is IJsCallable)
        {
            return OrdinaryHasInstance(left, right, context);
        }

        context.SetThrow(StandardLibrary.CreateTypeError("Right-hand side of 'instanceof' is not callable",
            context));
        return false;

    }

    private static bool OrdinaryHasInstance(object? candidate, object? constructor, EvaluationContext context)
    {
        if (constructor is not IJsCallable)
        {
            return false;
        }

        if (candidate is not JsObject && candidate is not IJsObjectLike)
        {
            return false;
        }

        if (!TryGetPropertyValue(constructor, "prototype", out var prototype, context) ||
            prototype is not JsObject prototypeObject)
        {
            context.SetThrow(
                StandardLibrary.CreateTypeError("Function has non-object prototype in instanceof check", context));
            return false;
        }

        var current = candidate switch
        {
            JsObject obj => obj.Prototype,
            IJsObjectLike objectLike => objectLike.Prototype,
            _ => null
        };

        while (current is not null)
        {
            if (ReferenceEquals(current, prototypeObject))
            {
                return true;
            }

            current = current.Prototype;
        }

        return false;
    }

    private static string GetTypeofString(object? value)
    {
        return JsOps.GetTypeofString(value);
    }

    private static void AssignBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.Assign);
    }

    private static void DefineBindingTarget(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context, bool isConst)
    {
        ApplyBindingTarget(target, value, environment, context,
            isConst ? BindingMode.DefineConst : BindingMode.DefineLet);
    }

    private static void DefineOrAssignVar(BindingTarget target, object? value, JsEnvironment environment,
        EvaluationContext context)
    {
        ApplyBindingTarget(target, value, environment, context, BindingMode.DefineVar);
    }

    private static void ApplyBindingTarget(
        BindingTarget target,
        object? value,
        JsEnvironment environment,
        EvaluationContext context,
        BindingMode mode,
        bool hasInitializer = true,
        bool allowNameInference = true)
    {
        switch (target)
        {
            case IdentifierBinding identifier:
                ApplyIdentifierBinding(identifier, value, environment, context, mode, hasInitializer, allowNameInference);
                break;
            case ArrayBinding arrayBinding:
                BindArrayPattern(arrayBinding, value, environment, context, mode);
                break;
            case ObjectBinding objectBinding:
                BindObjectPattern(objectBinding, value, environment, context, mode);
                break;
            case AssignmentTargetBinding assignmentTarget:
            {
                var reference = AssignmentReferenceResolver.Resolve(
                    assignmentTarget.Expression,
                    environment,
                    context,
                    EvaluateExpression);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                reference.SetValue(value);
                break;
            }
            default:
                throw new NotSupportedException($"Binding target '{target.GetType().Name}' is not supported.");
        }
    }

    private static void ApplyIdentifierBinding(
        IdentifierBinding identifier,
        object? value,
        JsEnvironment environment,
        EvaluationContext context,
        BindingMode mode,
        bool hasInitializer,
        bool allowNameInference)
    {
        if (allowNameInference && value is IFunctionNameTarget nameTarget)
        {
            nameTarget.EnsureHasName(identifier.Name.Name);
        }

        if (mode == BindingMode.Assign && environment.IsConstBinding(identifier.Name))
        {
            throw new ThrowSignal(StandardLibrary.CreateTypeError(
                $"Cannot reassign constant '{identifier.Name.Name}'.", context, context.RealmState));
        }

        switch (mode)
        {
            case BindingMode.Assign:
                environment.Assign(identifier.Name, value);
                break;
            case BindingMode.DefineLet:
                environment.Define(identifier.Name, value, isLexical: true, blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineConst:
                environment.Define(identifier.Name, value, true, blocksFunctionScopeOverride: true);
                break;
            case BindingMode.DefineVar:
            {
                var assignedBlockedBinding = environment.TryAssignBlockedBinding(identifier.Name, value);

                EnsureFunctionScopedVarBinding(environment, identifier.Name, context);

                if (hasInitializer && !assignedBlockedBinding)
                {
                    environment.Assign(identifier.Name, value);
                }

                break;
            }
            case BindingMode.DefineParameter:
                // Parameters are created before defaults run (see the pre-pass in BindFunctionParameters),
                // so by the time we bind the value the slot should already exist and still be
                // uninitialized. Assign into it to preserve the TDZ throw on reads during
                // initializer evaluation, and fall back to Define only if the slot was not
                // created (defensive).
                if (environment.HasBinding(identifier.Name))
                {
                    environment.Assign(identifier.Name, value);
                }
                else
                {
                    environment.Define(identifier.Name, value, isLexical: false);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static void EnsureFunctionScopedVarBinding(JsEnvironment environment, Symbol name,
        EvaluationContext context)
    {
        if (environment.HasFunctionScopedBinding(name))
        {
            return;
        }

        environment.DefineFunctionScoped(name, Symbol.Undefined, hasInitializer: false, context: context);
    }

    private static bool IsAnonymousFunctionDefinition(ExpressionNode expression)
    {
        return expression switch
        {
            FunctionExpression func => func.Name is null,
            ClassExpression classExpression => classExpression.Name is null,
            _ => false
        };
    }

    private static void BindArrayPattern(ArrayBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        if (!TryGetIteratorForDestructuring(value, context, out var iterator, out var enumerator))
        {
            throw StandardLibrary.ThrowTypeError(
                $"Cannot destructure non-iterable value.{GetSourceInfo(context)}", context);
        }

        if (iterator is not null && binding.Elements.Length == 0 && binding.RestElement is null)
        {
            IteratorClose(iterator, context);
            return;
        }

        var iteratorRecord = new ArrayPatternIterator(iterator, enumerator);
        var iteratorThrew = false;
        var iteratorDone = false;

        try
        {
            foreach (var element in binding.Elements)
            {
                AssignmentReference? preResolvedReference = null;
                if (mode == BindingMode.Assign && element.Target is AssignmentTargetBinding assignmentTarget)
                {
                    preResolvedReference = AssignmentReferenceResolver.Resolve(
                        assignmentTarget.Expression,
                        environment,
                        context,
                        EvaluateExpression);
                    if (context.ShouldStopEvaluation)
                    {
                        if (iterator is not null)
                        {
                            IteratorClose(iterator, context);
                        }

                        return;
                    }
                }

                (object? nextValue, bool done) next;
                try
                {
                    next = iteratorRecord.Next(context);
                }
                catch (ThrowSignal)
                {
                    iteratorThrew = true;
                    throw;
                }

                var (nextValue, done) = next;
                iteratorDone = done;
                if (context.ShouldStopEvaluation)
                {
                    if (iterator is not null)
                    {
                        IteratorClose(iterator, context);
                    }

                    return;
                }

                var elementValue = done ? Symbol.Undefined : nextValue;

                if (element.Target is null)
                {
                    continue;
                }

                var usedDefault = false;
                if (element.DefaultValue is not null &&
                    ReferenceEquals(elementValue, Symbol.Undefined))
                {
                    usedDefault = true;
                    elementValue = EvaluateExpression(element.DefaultValue, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        if (iterator is not null)
                        {
                            IteratorClose(iterator, context);
                        }

                        return;
                    }
                }

                if (usedDefault &&
                    element is { Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression } &&
                    IsAnonymousFunctionDefinition(defaultExpression) &&
                    elementValue is IFunctionNameTarget nameTarget)
                {
                    nameTarget.EnsureHasName(identifierTarget.Name.Name);
                }

                if (preResolvedReference is { } resolvedReference)
                {
                    resolvedReference.SetValue(elementValue);
                }
                else
                {
                    ApplyBindingTarget(element.Target, elementValue, environment, context, mode, allowNameInference: false);
                }

                if (!context.ShouldStopEvaluation)
                {
                    continue;
                }

                if (iterator is not null)
                {
                    IteratorClose(iterator, context);
                }

                return;
            }

            if (binding.RestElement is not null)
            {
                AssignmentReference? preResolvedRest = null;
                if (mode == BindingMode.Assign && binding.RestElement is AssignmentTargetBinding restTarget)
                {
                    preResolvedRest = AssignmentReferenceResolver.Resolve(
                        restTarget.Expression,
                        environment,
                        context,
                        EvaluateExpression);
                    if (context.ShouldStopEvaluation)
                    {
                        if (iterator is not null)
                        {
                            IteratorClose(iterator, context);
                        }

                        return;
                    }
                }

                var restArray = new JsArray(context.RealmState);
                while (true)
                {
                    (object? restValue, bool done) restNext;
                    try
                    {
                        restNext = iteratorRecord.Next(context);
                    }
                    catch (ThrowSignal)
                    {
                        iteratorThrew = true;
                        throw;
                    }

                    var (restValue, done) = restNext;
                    iteratorDone = done;
                    if (context.ShouldStopEvaluation)
                    {
                        if (iterator is not null)
                        {
                            IteratorClose(iterator, context);
                        }

                        return;
                    }

                    if (done)
                    {
                        break;
                    }

                    restArray.Push(restValue);
                }

                if (preResolvedRest is { } resolvedRestReference)
                {
                    resolvedRestReference.SetValue(restArray);
                }
                else
                {
                    ApplyBindingTarget(binding.RestElement, restArray, environment, context, mode, allowNameInference: false);
                }
            }
        }
        catch (ThrowSignal)
        {
            if (iterator is not null && !iteratorThrew)
            {
                IteratorClose(iterator, context, preserveExistingThrow: true);
            }

            throw;
        }
        catch
        {
            if (iterator is not null)
            {
                IteratorClose(iterator, context);
            }

            throw;
        }

        if (iterator is not null && !iteratorDone)
        {
            IteratorClose(iterator, context);
        }
    }

    private static void BindObjectPattern(ObjectBinding binding, object? value, JsEnvironment environment,
        EvaluationContext context, BindingMode mode)
    {
        var obj = ToObjectForDestructuring(value, context);

        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in binding.Properties)
        {
            var propertyName = property.Name;
            if (property.NameExpression is not null)
            {
                var propertyKeyValue = EvaluateExpression(property.NameExpression, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                propertyName = JsOps.GetRequiredPropertyName(propertyKeyValue, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            usedKeys.Add(propertyName);
            var hasProperty = obj.TryGetProperty(propertyName, out var val);
            var propertyValue = hasProperty ? val : Symbol.Undefined;

            var usedDefault = false;
            if (ReferenceEquals(propertyValue, Symbol.Undefined) && property.DefaultValue is not null)
            {
                usedDefault = true;
                propertyValue = EvaluateExpression(property.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (usedDefault &&
                property is { Target: IdentifierBinding identifierTarget, DefaultValue: { } defaultExpression } &&
                IsAnonymousFunctionDefinition(defaultExpression) &&
                propertyValue is IFunctionNameTarget nameTarget)
            {
                nameTarget.EnsureHasName(identifierTarget.Name.Name);
            }

            ApplyBindingTarget(property.Target, propertyValue, environment, context, mode, allowNameInference: false);
        }

        if (binding.RestElement is not null)
        {
            var restObject = new JsObject();
            if (context.RealmState?.ObjectPrototype is not null)
            {
                restObject.SetPrototype(context.RealmState.ObjectPrototype);
            }
            foreach (var key in GetEnumerableOwnPropertyKeysInOrder(obj))
            {
                if (!usedKeys.Contains(key))
                {
                    if (obj.TryGetProperty(key, out var restValue))
                    {
                        restObject.SetProperty(key, restValue);
                    }
                }
            }

            ApplyBindingTarget(binding.RestElement, restObject, environment, context, mode, allowNameInference: false);
        }
    }

    private static IEnumerable<string> GetEnumerableOwnPropertyKeysInOrder(IJsPropertyAccessor accessor)
    {
        if (accessor is JsObject jsObject)
        {
            foreach (var key in jsObject.GetOwnEnumerablePropertyKeysInOrder())
            {
                yield return key;
            }

            yield break;
        }

        foreach (var key in accessor.GetEnumerablePropertyNames())
        {
            yield return key;
        }
    }

    // Array/object destructuring uses iterator protocol (ECMA-262 §14.1.5).
    private static bool TryGetIteratorForDestructuring(object? value, EvaluationContext context,
        out JsObject? iterator, [MustDisposeResource] out IEnumerator<object?>? enumerator)
    {
        iterator = null;
        enumerator = null;

        var iteratorTarget = value as IJsPropertyAccessor;
        var thisArg = value;
        if (iteratorTarget is null && value is not null && !ReferenceEquals(value, Symbol.Undefined))
        {
            iteratorTarget = ToObjectForDestructuring(value, context);
            thisArg = iteratorTarget;
        }

        if (iteratorTarget is not null)
        {
            if (TryGetIteratorFromProtocols(iteratorTarget, out var iteratorCandidate) &&
                iteratorCandidate is not null)
            {
                iterator = iteratorCandidate;
                return true;
            }

            // Fallback: treat objects with a callable `next` as iterators even if
            // @@iterator is missing so generator objects still participate in
            // destructuring when their symbol lookup fails.
            if (iteratorTarget.TryGetProperty("next", out var nextVal) && nextVal is IJsCallable)
            {
                iterator = thisArg as JsObject;
                if (iterator is null && thisArg is IJsObjectLike objectLike)
                {
                    var wrapper = new JsObject();
                    foreach (var key in objectLike.Keys)
                    {
                        if (objectLike.TryGetProperty(key, out var val))
                        {
                            wrapper.SetProperty(key, val);
                        }
                    }

                    iterator = wrapper;
                }

                return true;
            }

            return false;
        }

        switch (value)
        {
            case string s:
                enumerator = EnumerateStringCharacters(s);
                return true;
            case IEnumerable<object?> enumerable:
                enumerator = enumerable.GetEnumerator();
                return true;
        }

        return false;
    }

    [MustDisposeResource]
    private static IEnumerator<object?> EnumerateArrayElements(JsArray array)
    {
        IEnumerable<object?> Enumerate()
        {
            var length = array.Length;
            var truncated = Math.Truncate(length);
            var clamped = truncated > int.MaxValue ? int.MaxValue : truncated;
            var count = clamped < 0 ? 0 : (int)clamped;
            for (var i = 0; i < count; i++)
            {
                yield return array.GetElement(i);
            }
        }

        return Enumerate().GetEnumerator();
    }

    [MustDisposeResource]
    private static IEnumerator<object?> EnumerateStringCharacters(string value)
    {
        IEnumerable<object?> Enumerate()
        {
            foreach (var ch in value)
            {
                yield return ch.ToString();
            }
        }

        return Enumerate().GetEnumerator();
    }

    private readonly struct ArrayPatternIterator(JsObject? iterator, IEnumerator<object?>? enumerator)
    {
        public (object? Value, bool Done) Next(EvaluationContext context)
        {
            if (iterator is not null)
            {
                var candidate = InvokeIteratorNext(iterator);
                if (candidate is not JsObject result)
                {
                    throw StandardLibrary.ThrowTypeError("Iterator result is not an object.", context);
                }

                var done = result.TryGetProperty("done", out var doneValue) &&
                           doneValue is bool and true;

                var value = result.TryGetProperty("value", out var yielded)
                    ? yielded
                    : Symbol.Undefined;

                return (done ? Symbol.Undefined : value, done);
            }

            if (enumerator?.MoveNext() != true)
            {
                return (Symbol.Undefined, true);
            }

            return (enumerator.Current, false);
        }
    }

    private static JsObject ToObjectForDestructuring(object? value, EvaluationContext context)
    {
        var realm = context.RealmState;
        switch (value)
        {
            case JsObject jsObj:
                return jsObj;
            case JsArray jsArray:
            {
                var obj = new JsObject();
                if (realm?.ArrayPrototype is not null)
                {
                    obj.SetPrototype(realm.ArrayPrototype);
                }

                var length = jsArray.Length;
                var count = length > int.MaxValue ? int.MaxValue : (int)length;
                for (var i = 0; i < count; i++)
                {
                    obj.SetProperty(i.ToString(CultureInfo.InvariantCulture), jsArray.GetElement(i));
                }

                obj.SetProperty("length", length);
                return obj;
            }
            case null:
            case Symbol sym when ReferenceEquals(sym, Symbol.Undefined):
            case IIsHtmlDda:
                throw StandardLibrary.ThrowTypeError("Cannot destructure undefined or null", context, realm);
            case string s:
                return StandardLibrary.CreateStringWrapper(s, context, realm);
            case JsBigInt bi:
                return StandardLibrary.CreateBigIntWrapper(bi, context, realm);
            case TypedAstSymbol symbolValue:
            {
                var obj = new JsObject();
                if (realm?.ObjectPrototype is not null)
                {
                    obj.SetPrototype(realm.ObjectPrototype);
                }

                obj.SetProperty("__value__", symbolValue);
                return obj;
            }
            case double:
            case float:
            case decimal:
            case int:
            case uint:
            case long:
            case ulong:
            case short:
            case ushort:
            case byte:
            case sbyte:
            {
                var num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return StandardLibrary.CreateNumberWrapper(num, context, realm);
            }
            case bool b:
            {
                var obj = new JsObject();
                if (realm?.BooleanPrototype is not null)
                {
                    obj.SetPrototype(realm.BooleanPrototype);
                }

                obj.SetProperty("__value__", b);
                return obj;
            }
            default:
            {
                var obj = new JsObject();
                if (realm?.ObjectPrototype is not null)
                {
                    obj.SetPrototype(realm.ObjectPrototype);
                }

                return obj;
            }
        }
    }

    private static (object? Value, SuperBinding Binding) ResolveSuperMember(MemberExpression expression,
        JsEnvironment environment, EvaluationContext context)
    {
        if (!context.IsThisInitialized)
        {
            throw CreateSuperReferenceError(environment, context, null);
        }

        var binding = ExpectSuperBinding(environment, context);
        var propertyValue = EvaluateExpression(expression.Property, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return (Symbol.Undefined, binding);
        }

        var propertyName = ToPropertyName(propertyValue, context)
                           ?? throw new InvalidOperationException(
                               $"Property name cannot be null.{GetSourceInfo(context, expression.Source)}");

        if (context.ShouldStopEvaluation)
        {
            return (Symbol.Undefined, binding);
        }

        if (!binding.TryGetProperty(propertyName, out var value))
        {
            throw new InvalidOperationException(
                $"Cannot read property '{propertyName}' from super prototype.{GetSourceInfo(context, expression.Source)}");
        }

        return (value, binding);
    }

    private static SuperBinding ExpectSuperBinding(JsEnvironment environment, EvaluationContext context)
    {
        try
        {
            if (environment.Get(Symbol.Super) is SuperBinding binding)
            {
                return binding;
            }
        }
        catch (InvalidOperationException ex)
        {
            var wrapped = CreateSuperReferenceError(environment, context, ex);
            if (wrapped is ThrowSignal throwSignal)
            {
                throw throwSignal;
            }

            throw wrapped;
        }

        var fallback = CreateSuperReferenceError(environment, context, null);
        if (fallback is ThrowSignal signal)
        {
            throw signal;
        }

        throw fallback;
    }

    private static Exception CreateSuperReferenceError(JsEnvironment environment, EvaluationContext context,
        Exception? inner)
    {
        var message = $"Super is not available in this context.{GetSourceInfo(context)}";
        if (environment.TryGet(Symbol.Intern("ReferenceError"), out var ctorVal) &&
            ctorVal is IJsCallable ctor)
        {
            var error = ctor.Invoke([message], Symbol.Undefined);
            return new ThrowSignal(error);
        }

        return new InvalidOperationException(message, inner);
    }

    private static string GetSourceInfo(EvaluationContext context, SourceReference? fallback = null)
    {
        var source = fallback ?? context.SourceReference;
        if (source is null)
        {
            return " (no source reference)";
        }

        var snippet = source.GetText();
        if (snippet.Length > 50)
        {
            snippet = snippet[..47] + "...";
        }

        return
            $" at {source} (snippet: '{snippet}') Source: '{source.Source}' Start: {source.StartPosition} End: {source.EndPosition}";
    }

    private static bool IsNullOrUndefined(object? value)
    {
        return value.IsNullish();
    }

    extension(FunctionExpression function)
    {
        private JsArgumentsObject CreateArgumentsObject(IReadOnlyList<object?> arguments, JsEnvironment environment, RealmState realmState, IJsCallable? callee)
        {
            var values = new object?[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                values[i] = arguments[i];
            }

            var mapped = !function.Body.IsStrict && IsSimpleParameterList(function);
            var mappedParameters = new Symbol?[arguments.Count];
            if (mapped)
            {
                var parameterSymbols = function.Parameters
                    .Where(p => p is { IsRest: false, Pattern: null, DefaultValue: null, Name: not null })
                    .Select(p => p.Name!)
                    .ToArray();

                for (var i = 0; i < mappedParameters.Length && i < parameterSymbols.Length; i++)
                {
                    mappedParameters[i] = parameterSymbols[i];
                }
            }

            return new JsArgumentsObject(values, mappedParameters, environment, mapped, realmState, callee,
                function.Body.IsStrict);
        }
    }

    extension(FunctionExpression function)
    {
        private bool IsSimpleParameterList()
        {
            foreach (var parameter in function.Parameters)
            {
                if (parameter.IsRest || parameter.Pattern is not null || parameter.DefaultValue is not null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static void RestoreSignal(this EvaluationContext context, ISignal? signal)
    {
        switch (signal)
        {
            case null:
                return;
            case ReturnSignal returnSignal:
                context.SetReturn(returnSignal.Value);
                break;
            case BreakSignal breakSignal:
                context.SetBreak(breakSignal.Label);
                break;
            case ContinueSignal continueSignal:
                context.SetContinue(continueSignal.Label);
                break;
            case ThrowFlowSignal throwSignal:
                context.SetThrow(throwSignal.Value);
                break;
        }
    }

    private static void BindFunctionParameters(this FunctionExpression function, IReadOnlyList<object?> arguments,
        JsEnvironment environment, EvaluationContext context)
    {
        var parameterNames = new List<Symbol>();
        foreach (var parameter in function.Parameters)
        {
            CollectParameterNames(parameter, parameterNames);
        }

        foreach (var name in parameterNames)
        {
            environment.Define(name, JsEnvironment.Uninitialized, isLexical: false, blocksFunctionScopeOverride: true);
        }

        var argumentIndex = 0;

        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsRest)
            {
                var restArray = new JsArray(context.RealmState);
                for (; argumentIndex < arguments.Count; argumentIndex++)
                {
                    restArray.Push(arguments[argumentIndex]);
                }

                if (parameter.Pattern is not null)
                {
                    ApplyBindingTarget(parameter.Pattern, restArray, environment, context,
                        BindingMode.DefineParameter);
                    if (context.ShouldStopEvaluation)
                    {
                        return;
                    }
                }
                else
                {
                    if (parameter.Name is null)
                    {
                        throw new InvalidOperationException("Rest parameter must have an identifier.");
                    }

                    environment.Define(parameter.Name, restArray, isLexical: false);
                }

                continue;
            }

            var value = argumentIndex < arguments.Count ? arguments[argumentIndex] : Symbol.Undefined;
            argumentIndex++;

            if (ReferenceEquals(value, Symbol.Undefined) && parameter.DefaultValue is not null)
            {
                if (parameter.Name is not null && DefaultReferencesParameter(parameter.DefaultValue, parameter.Name))
                {
                    var error = StandardLibrary.ThrowReferenceError(
                        $"{parameter.Name.Name} is not initialized", context, context.RealmState);
                    context.SetThrow(error.ThrownValue);
                    return;
                }

                value = EvaluateExpression(parameter.DefaultValue, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }
            }

            if (parameter.Pattern is not null)
            {
                ApplyBindingTarget(parameter.Pattern, value, environment, context, BindingMode.DefineParameter);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                continue;
            }

            if (parameter.Name is null)
            {
                throw new InvalidOperationException("Parameter must have an identifier when no pattern is provided.");
            }

            environment.Define(parameter.Name, value, isLexical: false);
        }

        return;

        static bool DefaultReferencesParameter( ExpressionNode expression, Symbol parameterName)
        {
            switch (expression)
            {
                case IdentifierExpression ident:
                    return ReferenceEquals(ident.Name, parameterName);
                case AssignmentExpression assign:
                    return ReferenceEquals(assign.Target, parameterName) ||
                           DefaultReferencesParameter(assign.Value, parameterName);
                case BinaryExpression binary:
                    return DefaultReferencesParameter(binary.Left, parameterName) ||
                           DefaultReferencesParameter(binary.Right, parameterName);
                case ConditionalExpression cond:
                    return DefaultReferencesParameter(cond.Test, parameterName) ||
                           DefaultReferencesParameter(cond.Consequent, parameterName) ||
                           DefaultReferencesParameter(cond.Alternate, parameterName);
                case CallExpression call:
                    return DefaultReferencesParameter(call.Callee, parameterName) || call.Arguments.Any(arg => DefaultReferencesParameter(arg.Expression, parameterName));

                case MemberExpression member:
                    return DefaultReferencesParameter(member.Target, parameterName) ||
                           DefaultReferencesParameter(member.Property, parameterName);
                case UnaryExpression unary:
                    return DefaultReferencesParameter(unary.Operand, parameterName);
                case SequenceExpression seq:
                    return DefaultReferencesParameter(seq.Left, parameterName) ||
                           DefaultReferencesParameter(seq.Right, parameterName);
                case ArrayExpression arr:
                    foreach (var element in arr.Elements)
                    {
                        if (element.Expression is not null &&
                            DefaultReferencesParameter(element.Expression, parameterName))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null &&
                            DefaultReferencesParameter(member.Value, parameterName))
                        {
                            return true;
                        }

                        if (member.Function is not null &&
                            DefaultReferencesParameter(member.Function, parameterName))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null &&
                            DefaultReferencesParameter(part.Expression, parameterName))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression tagged:
                    return DefaultReferencesParameter(tagged.Tag, parameterName) ||
                           DefaultReferencesParameter(tagged.StringsArray, parameterName) ||
                           DefaultReferencesParameter(tagged.RawStringsArray, parameterName) ||
                           tagged.Expressions.Any(expr => DefaultReferencesParameter(expr, parameterName));
                case YieldExpression { Expression: not null } yieldExpression:
                    return DefaultReferencesParameter(yieldExpression.Expression, parameterName);
                case AwaitExpression awaitExpression:
                    return DefaultReferencesParameter(awaitExpression.Expression, parameterName);
                case FunctionExpression:
                    // Nested functions have their own scope; references to the parameter name
                    // do not count towards self-referential defaults here.
                    return false;
                default:
                    return false;
            }
        }

        static void CollectParameterNames(FunctionParameter parameter, List<Symbol> names)
        {
            if (parameter.Name is not null)
            {
                names.Add(parameter.Name);
            }

            if (parameter.Pattern is not null)
            {
                CollectBindingNames(parameter.Pattern, names);
            }
        }

        static void CollectBindingNames(BindingTarget target, List<Symbol> names)
        {
            while (true)
            {
                switch (target)
                {
                    case IdentifierBinding identifier:
                        names.Add(identifier.Name);
                        break;
                    case ArrayBinding arrayBinding:
                        foreach (var element in arrayBinding.Elements)
                        {
                            if (element.Target is not null)
                            {
                                CollectBindingNames(element.Target, names);
                            }
                        }

                        if (arrayBinding.RestElement is not null)
                        {
                            target = arrayBinding.RestElement;
                            continue;
                        }

                        break;
                    case ObjectBinding objectBinding:
                        foreach (var property in objectBinding.Properties)
                        {
                            CollectBindingNames(property.Target, names);
                        }

                        if (objectBinding.RestElement is not null)
                        {
                            target = objectBinding.RestElement;
                            continue;
                        }

                        break;
                    case AssignmentTargetBinding:
                        // Assignment targets do not declare new bindings in parameter lists.
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported binding target '{target.GetType().Name}'.");
                }

                break;
            }
        }
    }

    private static bool ContainsDirectEvalCall(this ExpressionNode expression)
    {
        while (true)
        {
            switch (expression)
            {
                case CallExpression { IsOptional: false, Callee: IdentifierExpression { Name.Name: "eval" } }:
                    return true;
                case CallExpression call:
                    if (ContainsDirectEvalCall(call.Callee))
                    {
                        return true;
                    }

                    foreach (var arg in call.Arguments)
                    {
                        if (ContainsDirectEvalCall(arg.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case BinaryExpression binary:
                    return ContainsDirectEvalCall(binary.Left) || ContainsDirectEvalCall(binary.Right);
                case ConditionalExpression cond:
                    return ContainsDirectEvalCall(cond.Test) || ContainsDirectEvalCall(cond.Consequent) || ContainsDirectEvalCall(cond.Alternate);
                case MemberExpression member:
                    return ContainsDirectEvalCall(member.Target) || ContainsDirectEvalCall(member.Property);
                case UnaryExpression unary:
                    expression = unary.Operand;
                    continue;
                case SequenceExpression seq:
                    return ContainsDirectEvalCall(seq.Left) || ContainsDirectEvalCall(seq.Right);
                case ArrayExpression array:
                    foreach (var element in array.Elements)
                    {
                        if (element.Expression is not null && ContainsDirectEvalCall(element.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case ObjectExpression obj:
                    foreach (var member in obj.Members)
                    {
                        if (member.Value is not null && ContainsDirectEvalCall(member.Value))
                        {
                            return true;
                        }

                        if (member.Function is not null && ContainsDirectEvalCall(member.Function))
                        {
                            return true;
                        }
                    }

                    return false;
                case TemplateLiteralExpression template:
                    foreach (var part in template.Parts)
                    {
                        if (part.Expression is not null && ContainsDirectEvalCall(part.Expression))
                        {
                            return true;
                        }
                    }

                    return false;
                case TaggedTemplateExpression tagged:
                    if (ContainsDirectEvalCall(tagged.Tag) || ContainsDirectEvalCall(tagged.StringsArray) || ContainsDirectEvalCall(tagged.RawStringsArray))
                    {
                        return true;
                    }

                    foreach (var expr in tagged.Expressions)
                    {
                        if (ContainsDirectEvalCall(expr))
                        {
                            return true;
                        }
                    }

                    return false;
                case FunctionExpression:
                    // Direct eval inside nested functions does not affect the parameter scope we are validating here.
                    return false;
                default:
                    return false;
            }
        }
    }

    private static JsObject CreateGeneratorIteratorObject(
        Func<IReadOnlyList<object?>, object?> next,
        Func<IReadOnlyList<object?>, object?> @return,
        Func<IReadOnlyList<object?>, object?> @throw)
    {
        var iterator = new JsObject();
        iterator.SetProperty("next", new HostFunction(next));
        iterator.SetProperty("return", new HostFunction(@return));
        iterator.SetProperty("throw", new HostFunction(@throw));
        return iterator;
    }

    private enum BindingMode
    {
        Assign,
        DefineLet,
        DefineConst,
        DefineVar,
        DefineParameter
    }


    private static int GetExpectedParameterCount(this ImmutableArray<FunctionParameter> parameters)
    {
        var count = 0;
        foreach (var parameter in parameters)
        {
            if (parameter.IsRest || parameter.DefaultValue is not null)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool HasParameterExpressions(this FunctionExpression function)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.DefaultValue is not null)
            {
                return true;
            }

            if (parameter.Pattern is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetThisInitializationStatus(this JsEnvironment environment, bool initialized)
    {
        if (environment.HasBinding(Symbol.ThisInitialized))
        {
            environment.Assign(Symbol.ThisInitialized, initialized);
            if (initialized &&
                environment.TryGet(Symbol.Super, out var superBinding) &&
                superBinding is SuperBinding { IsThisInitialized: false } binding)
            {
                environment.Assign(Symbol.Super,
                    new SuperBinding(binding.Constructor, binding.Prototype, binding.ThisValue, true));
            }
            return;
        }

        environment.Define(Symbol.ThisInitialized, initialized, isConst: false, isLexical: true,
            blocksFunctionScopeOverride: true);
    }
}
