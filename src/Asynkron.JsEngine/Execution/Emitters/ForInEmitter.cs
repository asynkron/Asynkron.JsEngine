#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Ast.ShapeAnalyzer;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution.Emitters;

/// <summary>
/// Emits IR instructions for for-in loops.
/// For-in loops enumerate property keys from an object and its prototype chain.
/// Unlike for-of, there is no iterator protocol - property keys are collected upfront.
/// </summary>
internal static class ForInEmitter
{
    /// <summary>
    /// Emit IR for a for-in statement.
    /// </summary>
    public static bool TryEmit(
        EmitContext ctx,
        ForEachStatement statement,
        int nextIndex,
        Symbol? label,
        out int entryIndex)
    {
        // for-in should only be called for ForEachKind.In
        if (statement.Kind != ForEachKind.In)
        {
            entryIndex = -1;
            return false;
        }

        // Yield in the object expression is not supported in IR
        if (AstShapeAnalyzer.ContainsYield(statement.Iterable))
        {
            entryIndex = -1;
            return false;
        }

        var iteratorPlan = ((IAstCacheable<IteratorDriverPlan>)statement).GetOrCreateCache();

        var lexicalBindings = iteratorPlan.PerIterationBindings.IsDefaultOrEmpty
            ? null
            : iteratorPlan.PerIterationBindings.ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);

        // Pre-create symbols and allocate slots for O(1) access
        var stateSymbol = Symbol.Synthetic("__forIn_state");
        var valueSymbol = Symbol.Synthetic("__forIn_value");
        var stateSlotIndex = ctx.AllocateSlot(stateSymbol);
        var valueSlotIndex = ctx.AllocateSlot(valueSymbol);

        // For let/const declarations, the per-iteration bindings need TDZ during object evaluation.
        var tdzBindings =
            iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const or VariableKind.Using
                or VariableKind.AwaitUsing
                ? iteratorPlan.PerIterationBindings
                : default;
        var tdzIsConst = iteratorPlan.DeclarationKind is VariableKind.Const or VariableKind.Using
            or VariableKind.AwaitUsing;

        var bindingStatement = ctx.CreateIteratorBindingStatement(iteratorPlan, valueSymbol, valueSlotIndex);

        var needsPerIterEnv = iteratorPlan.DeclarationKind is VariableKind.Let or VariableKind.Const
            && !iteratorPlan.PerIterationBindings.IsDefaultOrEmpty;

        var config = new LoopSkeletonConfig
        {
            Body = iteratorPlan.Body,
            BindingStatement = bindingStatement,
            PerIterationBindings = needsPerIterEnv ? iteratorPlan.PerIterationBindings : default,
            IterationScopeId = needsPerIterEnv ? iteratorPlan.IterationScopeId : -1,
            IterationSlotCount = iteratorPlan.IterationSlotCount,
            PerIterationSlotIndices = iteratorPlan.PerIterationSlotIndices,
            CanReuseIterationEnvironment = iteratorPlan.CanReuseIterationEnvironment,
            LexicalBindings = lexicalBindings,
            LoopScopeId = -1,
            ConditionAfterBody = false,
            PerIterationEnvAfterBody = false,
            NeedsTryFinally = false,
        };

        var driver = new ForInLoopDriver(
            stateSymbol, valueSymbol,
            stateSlotIndex, valueSlotIndex,
            statement.Iterable,
            tdzBindings, tdzIsConst);

        return LoopEmitterHelpers.EmitLoopSkeleton(ctx, ref driver, in config, nextIndex, label, out entryIndex);
    }
}

/// <summary>
/// Driver for for-in loop emission.
/// Provides ForInInit and ForInMoveNext instructions.
/// </summary>
internal struct ForInLoopDriver : ILoopDriver
{
    private readonly Symbol _stateSymbol;
    private readonly Symbol _valueSymbol;
    private readonly int _stateSlotIndex;
    private readonly int _valueSlotIndex;
    private readonly ExpressionNode _objectExpression;
    private readonly ImmutableArray<Symbol> _tdzBindings;
    private readonly bool _tdzIsConst;

    private int _initIndex;
    private int _moveNextIndex;

    public ForInLoopDriver(
        Symbol stateSymbol, Symbol valueSymbol,
        int stateSlotIndex, int valueSlotIndex,
        ExpressionNode objectExpression,
        ImmutableArray<Symbol> tdzBindings, bool tdzIsConst)
    {
        _stateSymbol = stateSymbol;
        _valueSymbol = valueSymbol;
        _stateSlotIndex = stateSlotIndex;
        _valueSlotIndex = valueSlotIndex;
        _objectExpression = objectExpression;
        _tdzBindings = tdzBindings;
        _tdzIsConst = tdzIsConst;
        _initIndex = -1;
        _moveNextIndex = -1;
    }

    public bool EmitMoveNext(EmitContext ctx, out int moveNextEntry, out int moveNextBranch)
    {
        if (_objectExpression is AwaitExpression awaitObject)
        {
            if (!ExpressionProgramCompiler.TryCompile(awaitObject.Expression, out var awaitedProgram, out var awaitFailure))
            {
                ctx.SetExpressionProgramFailure("ForInInitInstruction", awaitObject.Expression, awaitFailure);
                moveNextEntry = -1;
                moveNextBranch = -1;
                return false;
            }

            _initIndex = ctx.Append(new ForInInitInstruction(
                _stateSymbol, _stateSlotIndex,
                _valueSymbol, _valueSlotIndex,
                Next: -1,
                AwaitStateKey: ((IAstCacheable<Symbol>)awaitObject).GetOrCreateCache(),
                AwaitedProgram: awaitedProgram,
                TdzBindings: _tdzBindings,
                TdzIsConst: _tdzIsConst,
                ObjectSource: _objectExpression.Source));

            _moveNextIndex = ctx.Append(new ForInMoveNextInstruction(
                _stateSymbol, _valueSymbol,
                _stateSlotIndex, _valueSlotIndex,
                -1, -1));

            moveNextEntry = _moveNextIndex;
            moveNextBranch = _moveNextIndex;
            return true;
        }

        if (AstShapeAnalyzer.ContainsAwait(_objectExpression))
        {
            _initIndex = ctx.Append(new SuspendingForInInitInstruction(
                _stateSymbol, _stateSlotIndex,
                _valueSymbol, _valueSlotIndex,
                Next: -1,
                ObjectExpression: _objectExpression,
                TdzBindings: _tdzBindings,
                TdzIsConst: _tdzIsConst,
                ObjectSource: _objectExpression.Source));

            _moveNextIndex = ctx.Append(new ForInMoveNextInstruction(
                _stateSymbol, _valueSymbol,
                _stateSlotIndex, _valueSlotIndex,
                -1, -1));

            moveNextEntry = _moveNextIndex;
            moveNextBranch = _moveNextIndex;
            return true;
        }

        if (!ExpressionProgramCompiler.TryCompile(_objectExpression, out var objectProgram, out var failureReason))
        {
            ctx.SetExpressionProgramFailure("ForInInitInstruction", _objectExpression, failureReason);
            moveNextEntry = -1;
            moveNextBranch = -1;
            return false;
        }

        _initIndex = ctx.Append(new ForInInitInstruction(
            _stateSymbol, _stateSlotIndex,
            _valueSymbol, _valueSlotIndex,
            Next: -1,
            ObjectProgram: objectProgram,
            TdzBindings: _tdzBindings,
            TdzIsConst: _tdzIsConst,
            ObjectSource: _objectExpression.Source));

        _moveNextIndex = ctx.Append(new ForInMoveNextInstruction(
            _stateSymbol, _valueSymbol,
            _stateSlotIndex, _valueSlotIndex,
            -1, -1));

        moveNextEntry = _moveNextIndex;
        moveNextBranch = _moveNextIndex;
        return true;
    }

    public void WireMoveNext(EmitContext ctx, int moveNextBranch, int bodyTarget, int exitTarget)
    {
        ctx.Patch(moveNextBranch,
            (ForInMoveNextInstruction)ctx.Instructions[moveNextBranch] with
            {
                Next = bodyTarget,
                BreakIndex = exitTarget
            });
    }

    public int EmitInitAndWire(EmitContext ctx, int loopEnterTarget)
    {
        ctx.Patch(
            _initIndex,
            ctx.Instructions[_initIndex] switch
            {
                ForInInitInstruction forInInit => forInInit with { Next = loopEnterTarget },
                SuspendingForInInitInstruction suspendingForInInit => suspendingForInInit with { Next = loopEnterTarget },
                _ => throw new InvalidOperationException("Unexpected for-in init instruction shape.")
            });
        return _initIndex;
    }

    public (int CleanupEntry, int EndFinallyIndex) EmitCleanup(EmitContext ctx, int nextIndex) => (-1, -1);
}
