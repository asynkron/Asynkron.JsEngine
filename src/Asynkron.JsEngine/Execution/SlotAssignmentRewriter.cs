#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

#endregion

namespace Asynkron.JsEngine.Execution;

/// <summary>
/// Rewriter that stamps IdentifierExpression nodes with ScopeId/SlotIndex using
/// scope-aware slot analysis. Also updates IR instructions to carry finalized
/// slot maps and counts for each scope.
/// </summary>
internal sealed class SlotAssignmentRewriter : AstRewriter
{
    private readonly Dictionary<BlockStatement, int> _blockScopeIds;
    private readonly Stack<int> _tryFrameCatchScopes = new();
    private readonly Dictionary<int, ImmutableDictionary<Symbol, int>> _immutableSlotMaps;
    private readonly Dictionary<int, ImmutableHashSet<Symbol>> _lexicalBindings;
    private readonly Dictionary<int, int> _reverseScopeIdRemap = new();
    private readonly Dictionary<int, int> _scopeIdRemap = new();

    private readonly Dictionary<int, ScopeSlotInfo> _scopes;
    private readonly Stack<int> _scopeStack = new();
    private readonly int _analysisRootScopeId;
    private readonly int _targetRootScopeId;
    private readonly int _mappedRootScopeId;

    /// <summary>
    /// When true, we're re-stamping nested function instructions from an outer context.
    /// In this mode, we skip overwriting identifiers that are already resolved to scopes
    /// not on the current stack (i.e., inner function scopes).
    /// </summary>
    private bool _isRestampingNestedFunction;

    /// <summary>
    /// Maps (scopeId, slotIndex) pairs to flat slot IDs for O(1) variable access.
    /// </summary>
    private readonly Dictionary<(int scopeId, int slotIndex), int> _flatSlotMap = new();

    /// <summary>
    /// Total number of flat slots allocated during rewriting.
    /// </summary>
    public int FlatSlotCount => _flatSlotMap.Count;

    /// <summary>
    /// Builds the flat slot mappings grouped by scope ID for eager initialization.
    /// Returns a dictionary mapping scopeId to array of (slotIndex, flatSlotId) pairs.
    /// </summary>
    public ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>> BuildFlatSlotMappings()
    {
        if (_flatSlotMap.Count == 0)
        {
            return ImmutableDictionary<int, ImmutableArray<(int SlotIndex, int FlatSlotId)>>.Empty;
        }

        // Group by scopeId
        var grouped = _flatSlotMap
            .GroupBy(kv => kv.Key.scopeId)
            .ToImmutableDictionary(
                g => g.Key,
                g => g.Select(kv => (kv.Key.slotIndex, kv.Value)).ToImmutableArray());

        return grouped;
    }

    public SlotAssignmentRewriter(ScopeSlotAnalysis analysis, int targetRootScopeId, int analysisRootScopeId = 0)
    {
        _analysisRootScopeId = analysisRootScopeId;
        _targetRootScopeId = targetRootScopeId;
        _scopes = analysis.Scopes;
        _immutableSlotMaps = analysis.ImmutableSlotMaps;
        _lexicalBindings = analysis.LexicalBindings;
        _blockScopeIds = analysis.BlockScopeIds;
        _mappedRootScopeId = MapScopeId(_analysisRootScopeId);
        _scopeStack.Push(_mappedRootScopeId);
    }

    public void RewriteInstructions(IList<ExecutionInstruction> instructions, int entryIndex)
    {
        _scopeStack.Clear();
        _tryFrameCatchScopes.Clear();
        _scopeStack.Push(_mappedRootScopeId);

        var visited = new bool[instructions.Count];
        RewriteFrom(entryIndex, instructions, visited);
    }

    private void RewriteFrom(int index, IList<ExecutionInstruction> instructions, bool[] visited)
    {
        if (index < 0 || index >= instructions.Count || visited[index])
        {
            return;
        }

        instructions[index] = RewriteInstruction(instructions[index]);
        visited[index] = true;

        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();

        foreach (var successor in instructions[index].GetSuccessors())
        {
            RestoreStack(scopeSnapshot, tryFrameSnapshot);
            RewriteFrom(successor, instructions, visited);
        }
    }

    private void RestoreStack(int[] scopeSnapshot, int[] tryFrameSnapshot)
    {
        _scopeStack.Clear();
        for (var i = scopeSnapshot.Length - 1; i >= 0; i--)
        {
            _scopeStack.Push(scopeSnapshot[i]);
        }

        _tryFrameCatchScopes.Clear();
        for (var i = tryFrameSnapshot.Length - 1; i >= 0; i--)
        {
            _tryFrameCatchScopes.Push(tryFrameSnapshot[i]);
        }
    }

    /// <summary>
    /// Maps a (possibly negative) scope id to the rewritten id used in instructions.
    /// Exposed so we can stamp iterator plan bodies with consistent scope ids.
    /// </summary>
    public int MapScopeId(int scopeId)
    {
        return RemapScopeId(scopeId);
    }

    /// <summary>
    /// Stamps an arbitrary AST node with slot metadata using the current slot analysis.
    /// The node is visited in the context of the provided scope (pushed on top of the stack)
    /// and the rewritten, stamped node is returned.
    /// </summary>
    public T StampNodeInScope<T>(T node, int scopeId) where T : AstNode
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (scopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(scopeId);
        }

        var rewritten = node switch
        {
            StatementNode stmt => (T)(AstNode)Rewrite(stmt),
            ExpressionNode expr => (T)(AstNode)Rewrite(expr),
            _ => node
        };
        RestoreStack(scopeSnapshot, tryFrameSnapshot);
        return rewritten;
    }

    public bool TryResolveSlot(Symbol symbol, int mappedScopeId, out int slotIndex)
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (mappedScopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(mappedScopeId);
        }

        var found = TryResolve(symbol, out var resolution) && resolution.slotIndex >= 0;
        slotIndex = found ? resolution.slotIndex : -1;
        RestoreStack(scopeSnapshot, tryFrameSnapshot);
        return found;
    }

    /// <summary>
    /// Stamps an instruction with slot metadata from the enclosing scope.
    /// Used to stamp nested function execution plan instructions with outer scope slot info.
    /// </summary>
    public ExecutionInstruction StampInstructionInScope(ExecutionInstruction instruction, int enclosingScopeId)
    {
        var scopeSnapshot = _scopeStack.ToArray();
        var tryFrameSnapshot = _tryFrameCatchScopes.ToArray();
        _scopeStack.Clear();
        _scopeStack.Push(_mappedRootScopeId);
        if (enclosingScopeId != _mappedRootScopeId)
        {
            _scopeStack.Push(enclosingScopeId);
        }

        // Set the flag to indicate we're re-stamping nested function instructions.
        // This prevents overwriting identifiers already resolved to inner scopes.
        _isRestampingNestedFunction = true;
        try
        {
            var result = RewriteInstruction(instruction);
            return result;
        }
        finally
        {
            _isRestampingNestedFunction = false;
            RestoreStack(scopeSnapshot, tryFrameSnapshot);
        }
    }

    public int GetSlotCountForScope(int mappedScopeId)
    {
        return GetSlotCount(mappedScopeId);
    }

    public ExecutionInstruction RewriteInstruction(ExecutionInstruction instruction)
    {
        if (_isRestampingNestedFunction)
        {
            switch (instruction)
            {
                case EnterTryInstruction:
                case LeaveTryInstruction:
                case PushEnvironmentInstruction:
                case PopEnvironmentInstruction:
                case EnterCatchInstruction:
                case BreakInstruction:
                case ContinueInstruction:
                    return instruction;
            }
        }

        switch (instruction)
        {
            case EnterTryInstruction:
                _tryFrameCatchScopes.Push(-1);
                return instruction;

            case PushEnvironmentInstruction push:
                var mappedPushScope = RemapScopeId(push.ScopeId);
                var lexical = GetLexicalBindings(mappedPushScope);
                var updatedPush = push with
                {
                    ScopeId = mappedPushScope,
                    SlotCount = GetSlotCount(mappedPushScope),
                    SlotMap = GetSlotMap(mappedPushScope),
                    LexicalBindings = lexical
                };
                _scopeStack.Push(mappedPushScope);
                return updatedPush;

            case PopEnvironmentInstruction pop:
                var mappedPopScope = RemapScopeId(pop.ScopeId);
                LeaveScope(mappedPopScope);
                if (_tryFrameCatchScopes.Count > 0 && _tryFrameCatchScopes.Peek() == mappedPopScope)
                {
                    _tryFrameCatchScopes.Pop();
                    _tryFrameCatchScopes.Push(-1);
                }
                return pop with { ScopeId = mappedPopScope };

            case EnterCatchInstruction enterCatch:
                var mappedCatchScope = RemapScopeId(enterCatch.ScopeId);
                var updatedCatch = enterCatch with
                {
                    ScopeId = mappedCatchScope,
                    SlotCount = GetSlotCount(mappedCatchScope),
                    SlotMap = GetSlotMap(mappedCatchScope)
                };
                _scopeStack.Push(mappedCatchScope);
                if (_tryFrameCatchScopes.Count > 0)
                {
                    _tryFrameCatchScopes.Pop();
                    _tryFrameCatchScopes.Push(mappedCatchScope);
                }
                return updatedCatch;

            case LeaveTryInstruction:
                if (_tryFrameCatchScopes.Count > 0)
                {
                    var catchScopeId = _tryFrameCatchScopes.Pop();
                    var isOnStack = false;
                    foreach (var scopeId in _scopeStack)
                    {
                        if (scopeId == catchScopeId)
                        {
                            isOnStack = true;
                            break;
                        }
                    }

                    if (isOnStack)
                    {
                        LeaveScope(catchScopeId);
                    }
                }

                return instruction;

            case BreakInstruction breakInstruction:
                if (breakInstruction.TargetScopeId < 0)
                {
                    return breakInstruction;
                }

                var mappedBreakScope = RemapScopeId(breakInstruction.TargetScopeId);
                PopToScope(mappedBreakScope);
                return breakInstruction with { TargetScopeId = mappedBreakScope };

            case ContinueInstruction continueInstruction:
                if (continueInstruction.TargetScopeId < 0)
                {
                    return continueInstruction;
                }

                var mappedContinueScope = RemapScopeId(continueInstruction.TargetScopeId);
                PopToScope(mappedContinueScope);
                return continueInstruction with { TargetScopeId = mappedContinueScope };

            case EvaluateAndDiscardInstruction eval:
                return eval with
                {
                    ExpressionProgram = RewriteExpressionProgram(eval.ExpressionProgram)
                };

            case AwaitAndDiscardInstruction awaitDiscard:
                return awaitDiscard with { AwaitedProgram = RewriteExpressionProgram(awaitDiscard.AwaitedProgram) };

            case AssignmentSlotInstruction assign:
                {
                    var rewrittenProgram = assign.ValueProgram is { } assignValueProgram
                        ? RewriteExpressionProgram(assignValueProgram)
                        : (ExpressionProgram?)null;
                    var rewrittenAwaitedProgram = assign.AwaitedProgram is { } assignAwaitedProgram
                        ? RewriteExpressionProgram(assignAwaitedProgram)
                        : (ExpressionProgram?)null;
                    if (TryResolve(assign.TargetSymbol, out var assignmentResolution))
                    {
                        var assignmentFlatSlotId = GetOrCreateFlatSlotId(
                            assignmentResolution.scopeId,
                            assignmentResolution.slotIndex);
                        return assign with
                        {
                            ValueProgram = rewrittenProgram,
                            AwaitedProgram = rewrittenAwaitedProgram,
                            ScopeId = assignmentResolution.scopeId,
                            SlotIndex = assignmentResolution.slotIndex,
                            FlatSlotId = assignmentFlatSlotId
                        };
                    }

                    return assign with
                    {
                        ValueProgram = rewrittenProgram,
                        AwaitedProgram = rewrittenAwaitedProgram
                    };
                }

            case LogicalCompoundAssignmentSlotInstruction logicalCompound:
                {
                    var rewrittenProgram = logicalCompound.RhsProgram is { } logicalRhsProgram
                        ? RewriteExpressionProgram(logicalRhsProgram)
                        : (ExpressionProgram?)null;
                    var rewrittenAwaitedProgram = logicalCompound.AwaitedProgram is { } logicalAwaitedProgram
                        ? RewriteExpressionProgram(logicalAwaitedProgram)
                        : (ExpressionProgram?)null;
                    if (TryResolve(logicalCompound.TargetSymbol, out var logicalResolution))
                    {
                        var logicalFlatSlotId = GetOrCreateFlatSlotId(
                            logicalResolution.scopeId,
                            logicalResolution.slotIndex);
                        return logicalCompound with
                        {
                            RhsProgram = rewrittenProgram,
                            AwaitedProgram = rewrittenAwaitedProgram,
                            ScopeId = logicalResolution.scopeId,
                            SlotIndex = logicalResolution.slotIndex,
                            FlatSlotId = logicalFlatSlotId
                        };
                    }

                    return logicalCompound with
                    {
                        RhsProgram = rewrittenProgram,
                        AwaitedProgram = rewrittenAwaitedProgram
                    };
                }

            case CompoundAssignmentSlotInstruction compoundAssign:
                {
                    var rewrittenProgram = compoundAssign.RhsProgram is { } compoundRhsProgram
                        ? RewriteExpressionProgram(compoundRhsProgram)
                        : (ExpressionProgram?)null;
                    var rewrittenAwaitedProgram = compoundAssign.AwaitedProgram is { } compoundAwaitedProgram
                        ? RewriteExpressionProgram(compoundAwaitedProgram)
                        : (ExpressionProgram?)null;
                    var rhsFlatSlotId = rewrittenProgram is { } rewrittenRhsProgram
                        ? TryGetFlatSlotId(rewrittenRhsProgram)
                        : -1;

                    if (TryResolve(compoundAssign.TargetSymbol, out var compoundResolution))
                    {
                        var compoundFlatSlotId = GetOrCreateFlatSlotId(
                            compoundResolution.scopeId,
                            compoundResolution.slotIndex);
                        return compoundAssign with
                        {
                            RhsProgram = rewrittenProgram,
                            AwaitedProgram = rewrittenAwaitedProgram,
                            ScopeId = compoundResolution.scopeId,
                            SlotIndex = compoundResolution.slotIndex,
                            FlatSlotId = compoundFlatSlotId,
                            RhsFlatSlotId = rhsFlatSlotId
                        };
                    }

                    return compoundAssign with
                    {
                        RhsProgram = rewrittenProgram,
                        AwaitedProgram = rewrittenAwaitedProgram,
                        RhsFlatSlotId = rhsFlatSlotId
                    };
                }

            case YieldInstruction { AwaitedProgram: not null } yieldInstruction:
                return yieldInstruction with
                {
                    AwaitedProgram = RewriteExpressionProgram(yieldInstruction.AwaitedProgram!.Value)
                };

            case YieldInstruction { YieldProgram: not null } yieldInstruction:
                return yieldInstruction with { YieldProgram = RewriteExpressionProgram(yieldInstruction.YieldProgram!.Value) };

            case ReturnInstruction { AwaitedProgram: not null } returnInstruction:
                return returnInstruction with
                {
                    AwaitedProgram = RewriteExpressionProgram(returnInstruction.AwaitedProgram!.Value)
                };

            case ReturnInstruction { ReturnProgram: not null } returnInstruction:
                return returnInstruction with { ReturnProgram = RewriteExpressionProgram(returnInstruction.ReturnProgram!.Value) };

            case ThrowInstruction thr:
                return thr with
                {
                    ThrowProgram = thr.ThrowProgram is { } throwProgram
                        ? RewriteExpressionProgram(throwProgram)
                        : null,
                    AwaitedProgram = thr.AwaitedProgram is { } awaitedThrowProgram
                        ? RewriteExpressionProgram(awaitedThrowProgram)
                        : null
                };

            case BranchInstruction branch:
                return branch with { ConditionProgram = RewriteExpressionProgram(branch.ConditionProgram) };

            case SimpleVariableDeclarationInstruction varDecl:
                return varDecl with
                {
                    InitializerProgram = varDecl.InitializerProgram is { } simpleInitializerProgram
                        ? RewriteExpressionProgram(simpleInitializerProgram)
                        : null,
                    AwaitedProgram = varDecl.AwaitedProgram is { } simpleAwaitedProgram
                        ? RewriteExpressionProgram(simpleAwaitedProgram)
                        : null
                };

            case BindingVariableDeclarationInstruction bindingDecl:
                return bindingDecl with
                {
                    TargetProgram = RewriteBindingTargetProgram(bindingDecl.TargetProgram),
                    InitializerProgram = bindingDecl.InitializerProgram is { } bindingProgramOnlyInitializer
                        ? RewriteExpressionProgram(bindingProgramOnlyInitializer)
                        : null,
                    AwaitedProgram = bindingDecl.AwaitedProgram is { } bindingAwaitedProgram
                        ? RewriteExpressionProgram(bindingAwaitedProgram)
                        : null
                };

            case IteratorInitInstruction iterInit:
                return iterInit with
                {
                    IterableProgram = iterInit.IterableProgram is { } iterableProgram
                        ? RewriteExpressionProgram(iterableProgram)
                        : null,
                    AwaitedProgram = iterInit.AwaitedProgram is { } iterableAwaitedProgram
                        ? RewriteExpressionProgram(iterableAwaitedProgram)
                        : null
                };

            case EnterWithInstruction enterWith:
                return enterWith with
                {
                    ObjectProgram = enterWith.ObjectProgram is { } objectProgram
                        ? RewriteExpressionProgram(objectProgram)
                        : null,
                    AwaitedProgram = enterWith.AwaitedProgram is { } withAwaitedProgram
                        ? RewriteExpressionProgram(withAwaitedProgram)
                        : null
                };

            case YieldStarInstruction { AwaitedProgram: not null } yieldStar:
                return yieldStar with
                {
                    AwaitedProgram = RewriteExpressionProgram(yieldStar.AwaitedProgram!.Value)
                };

            case YieldStarInstruction yieldStar:
                return yieldStar with
                {
                    IterableProgram = yieldStar.IterableProgram is { } yieldStarIterableProgram
                        ? RewriteExpressionProgram(yieldStarIterableProgram)
                        : null
                };

            case ForInInitInstruction forInInit:
                return forInInit with
                {
                    ObjectProgram = forInInit.ObjectProgram is { } forInObjectProgram
                        ? RewriteExpressionProgram(forInObjectProgram)
                        : null,
                    AwaitedProgram = forInInit.AwaitedProgram is { } objectAwaitedProgram
                        ? RewriteExpressionProgram(objectAwaitedProgram)
                        : null
                };

            case ArrayDestructuringInitInstruction destructuringInit:
                return destructuringInit with
                {
                    SourceProgram = RewriteExpressionProgram(destructuringInit.SourceProgram)
                };

            case IncrementSlotInstruction increment:
                {
                    // Resolve the target symbol to get scope/slot/flatSlot metadata
                    if (TryResolve(increment.TargetSymbol, out var incrementResolution))
                    {
                        var incrementFlatSlotId = GetOrCreateFlatSlotId(incrementResolution.scopeId, incrementResolution.slotIndex);
                        return increment with
                        {
                            ScopeId = incrementResolution.scopeId,
                            SlotIndex = incrementResolution.slotIndex,
                            FlatSlotId = incrementFlatSlotId
                        };
                    }
                    return increment;
                }

            default:
                return instruction;
        }
    }

    private ExpressionProgram RewriteExpressionProgram(ExpressionProgram program)
    {
        if (program.IsEmpty)
        {
            return program;
        }

        var objectConstants = program.ObjectConstants.AsSpan();
        var identifierConstants = program.IdentifierConstants.AsSpan();
        var objectPool = new ExpressionProgramObjectPoolBuilder(program.ObjectConstants);
        var identifierPool = new ExpressionProgramIdentifierPoolBuilder(program.IdentifierConstants);
        var changed = false;
        var builder = ImmutableArray.CreateBuilder<PackedExpressionOp>(program.Operations.Length);
        foreach (var operation in program.Operations)
        {
            var rewritten = RewriteExpressionOp(operation, objectConstants, identifierConstants, objectPool, identifierPool);
            changed |= !operation.Equals(rewritten);
            builder.Add(rewritten);
        }

        return changed
            ? new ExpressionProgram(
                builder.MoveToImmutable(),
                program.StringConstants,
                objectPool.Build(),
                identifierPool.Build(),
                program.SpreadMaskConstants)
            : program;
    }

    private PackedExpressionOp RewriteExpressionOp(
        PackedExpressionOp operation,
        ReadOnlySpan<object> objectConstants,
        ReadOnlySpan<IdentifierOperand> identifierConstants,
        ExpressionProgramObjectPoolBuilder objectPool,
        ExpressionProgramIdentifierPoolBuilder identifierPool)
    {
        switch (operation.Kind)
        {
            case ExpressionOpKind.LoadIdentifier:
            case ExpressionOpKind.StoreIdentifier:
            case ExpressionOpKind.UpdateIdentifier:
            case ExpressionOpKind.TypeOfIdentifier:
                {
                    var identifier = operation.GetIdentifier(identifierConstants);
                    if (!TryResolve(identifier.Name, out var resolution))
                    {
                        return operation;
                    }

                    var rewrittenIdentifier = identifier with
                    {
                        ScopeId = resolution.scopeId,
                        SlotIndex = resolution.slotIndex,
                        FlatSlotId = GetOrCreateFlatSlotId(resolution.scopeId, resolution.slotIndex)
                    };

                    return rewrittenIdentifier.Equals(identifier)
                        ? operation
                        : operation.WithIdentifierConstant(identifierPool.InternIdentifier(rewrittenIdentifier));
                }

            case ExpressionOpKind.ApplyBindingTarget:
                {
                    var targetProgram = operation.GetObject<BindingTargetProgram>(objectConstants);
                    var rewrittenTargetProgram = RewriteBindingTargetProgram(targetProgram);
                    return ReferenceEquals(targetProgram, rewrittenTargetProgram)
                        ? operation
                        : operation.WithObjectConstant(objectPool.InternObject(rewrittenTargetProgram));
                }

            case ExpressionOpKind.LoadFunctionLiteral:
                {
                    var function = operation.GetObject<FunctionExpression>(objectConstants);
                    var rewrittenFunction = (FunctionExpression)Rewrite(function);
                    return ReferenceEquals(function, rewrittenFunction)
                        ? operation
                        : operation.WithObjectConstant(objectPool.InternObject(rewrittenFunction));
                }

            case ExpressionOpKind.LoadClassLiteral:
                {
                    var classExpression = operation.GetObject<ClassExpression>(objectConstants);
                    var rewrittenClassExpression = (ClassExpression)Rewrite(classExpression);
                    return ReferenceEquals(classExpression, rewrittenClassExpression)
                        ? operation
                        : operation.WithObjectConstant(objectPool.InternObject(rewrittenClassExpression));
                }

            default:
                return operation;
        }
    }

    private BindingTargetProgram RewriteBindingTargetProgram(BindingTargetProgram program)
    {
        switch (program)
        {
            case IdentifierBindingTargetProgram:
            case NamedSuperPropertyAssignmentBindingTargetProgram:
                return program;

            case ArrayBindingTargetProgram arrayBinding:
                {
                    var changed = false;
                    var elements = ImmutableArray.CreateBuilder<ArrayBindingElementProgram>(arrayBinding.Elements.Length);
                    foreach (var element in arrayBinding.Elements)
                    {
                        var rewrittenTarget = element.Target is null
                            ? null
                            : RewriteBindingTargetProgram(element.Target);
                        ExpressionProgram? rewrittenDefault = element.DefaultProgram is { } elementDefaultProgram
                            ? RewriteExpressionProgram(elementDefaultProgram)
                            : null;
                        changed |= !ReferenceEquals(element.Target, rewrittenTarget) || element.DefaultProgram != rewrittenDefault;
                        elements.Add(element with { Target = rewrittenTarget, DefaultProgram = rewrittenDefault });
                    }

                    var rewrittenRest = arrayBinding.RestElement is null
                        ? null
                        : RewriteBindingTargetProgram(arrayBinding.RestElement);
                    changed |= !ReferenceEquals(arrayBinding.RestElement, rewrittenRest);
                    return changed
                        ? arrayBinding with { Elements = elements.MoveToImmutable(), RestElement = rewrittenRest }
                        : program;
                }

            case ObjectBindingTargetProgram objectBinding:
                {
                    var changed = false;
                    var properties = ImmutableArray.CreateBuilder<ObjectBindingPropertyProgram>(objectBinding.Properties.Length);
                    foreach (var property in objectBinding.Properties)
                    {
                        var rewrittenTarget = RewriteBindingTargetProgram(property.Target);
                        ExpressionProgram? rewrittenDefault = property.DefaultProgram is { } propertyDefaultProgram
                            ? RewriteExpressionProgram(propertyDefaultProgram)
                            : null;
                        ExpressionProgram? rewrittenName = property.NameProgram is { } propertyNameProgram
                            ? RewriteExpressionProgram(propertyNameProgram)
                            : null;
                        changed |= !ReferenceEquals(property.Target, rewrittenTarget) ||
                                   property.DefaultProgram != rewrittenDefault ||
                                   property.NameProgram != rewrittenName;
                        properties.Add(property with
                        {
                            Target = rewrittenTarget,
                            DefaultProgram = rewrittenDefault,
                            NameProgram = rewrittenName
                        });
                    }

                    var rewrittenRest = objectBinding.RestElement is null
                        ? null
                        : RewriteBindingTargetProgram(objectBinding.RestElement);
                    changed |= !ReferenceEquals(objectBinding.RestElement, rewrittenRest);
                    return changed
                        ? objectBinding with { Properties = properties.MoveToImmutable(), RestElement = rewrittenRest }
                        : program;
                }

            case NamedPropertyAssignmentBindingTargetProgram namedPropertyAssignment:
                {
                    var rewrittenTarget = RewriteExpressionProgram(namedPropertyAssignment.TargetProgram);
                    return namedPropertyAssignment.TargetProgram == rewrittenTarget
                        ? program
                        : namedPropertyAssignment with { TargetProgram = rewrittenTarget };
                }

            case ComputedPropertyAssignmentBindingTargetProgram computedPropertyAssignment:
                {
                    var rewrittenTarget = RewriteExpressionProgram(computedPropertyAssignment.TargetProgram);
                    var rewrittenProperty = RewriteExpressionProgram(computedPropertyAssignment.PropertyProgram);
                    return computedPropertyAssignment.TargetProgram == rewrittenTarget &&
                           computedPropertyAssignment.PropertyProgram == rewrittenProperty
                        ? program
                        : computedPropertyAssignment with
                        {
                            TargetProgram = rewrittenTarget,
                            PropertyProgram = rewrittenProperty
                        };
                }

            case ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperPropertyAssignment:
                {
                    var rewrittenProperty = RewriteExpressionProgram(computedSuperPropertyAssignment.PropertyProgram);
                    return computedSuperPropertyAssignment.PropertyProgram == rewrittenProperty
                        ? program
                        : computedSuperPropertyAssignment with { PropertyProgram = rewrittenProperty };
                }

            default:
                return program;
        }
    }

    protected override StatementNode RewriteStatement(StatementNode statement)
    {
        if (statement is BlockStatement block)
        {
            var hoistPlan = ((IAstCacheable<HoistPlan>)block).GetOrCreateCache();

            // Fallback: if scope analysis missed this block, synthesize a scope so shadowed lets
            // get their own slots instead of reusing the parent scope.
            if (!_blockScopeIds.ContainsKey(block) && hoistPlan.NeedsEnvironment)
            {
                var syntheticScopeId = SyntheticScopeIdAllocator.Next();
                var scopeInfo = new ScopeSlotInfo(syntheticScopeId);
                var slotIndex = 0;
                foreach (var lexName in hoistPlan.TopLevelLexicalNames)
                {
                    scopeInfo.IncludeSlot(lexName, slotIndex++);
                    scopeInfo.LexicalBindings.Add(lexName);
                }

                scopeInfo.SlotCountHint = Math.Max(scopeInfo.SlotCountHint, slotIndex);
                _blockScopeIds[block] = syntheticScopeId;
                _scopes[syntheticScopeId] = scopeInfo;
                _immutableSlotMaps[syntheticScopeId] = scopeInfo.ToImmutableSlotMap();
                _lexicalBindings[syntheticScopeId] = scopeInfo.LexicalBindings
                    .ToImmutableHashSet(ReferenceEqualityComparer<Symbol>.Instance);
            }

            if (_blockScopeIds.TryGetValue(block, out var scopeId))
            {
                var mappedScopeId = RemapScopeId(scopeId);
                // Push the block's scope onto the stack for rewriting children
                _scopeStack.Push(mappedScopeId);
                try
                {
                    // Rewrite children in this scope
                    var rewrittenStatements = RewriteStatementList(block.Statements);

                    // Stamp the block with its scope metadata
                    return block with
                    {
                        Statements = rewrittenStatements,
                        ScopeId = mappedScopeId,
                        SlotCount = GetSlotCount(mappedScopeId),
                        SlotMap = GetSlotMap(mappedScopeId)
                    };
                }
                finally
                {
                    _scopeStack.Pop();
                }
            }
        }

        // Handle BlockStatement specially to stamp with scope metadata
        return base.RewriteStatement(statement);
    }

    protected override IdentifierExpression RewriteIdentifier(IdentifierExpression node)
    {
        // When re-stamping nested function instructions, don't touch identifiers already resolved.
        if (_isRestampingNestedFunction && node.ScopeId >= 0 && node.SlotIndex >= 0)
        {
            return node;
        }

        if (TryResolve(node.Name, out var resolution))
        {
            var flatSlotId = GetOrCreateFlatSlotId(resolution.scopeId, resolution.slotIndex);
            return node with
            {
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex,
                FlatSlotId = flatSlotId
            };
        }

        return node;
    }

    private static int TryGetFlatSlotId(ExpressionProgram program)
    {
        if (program.Operations.Length == 1 &&
            program.Operations[0].Kind == ExpressionOpKind.LoadIdentifier)
        {
            var identifier = program.Operations[0].GetIdentifier(program.IdentifierConstants.AsSpan());
            return identifier.FlatSlotId;
        }

        return -1;
    }

    /// <summary>
    /// Gets or creates a flat slot ID for the given (scopeId, slotIndex) pair.
    /// Flat slot IDs are assigned in order of first encounter during rewriting.
    /// </summary>
    private int GetOrCreateFlatSlotId(int scopeId, int slotIndex)
    {
        // When stamping nested function instructions from an outer scope, do not allocate
        // new flat-slot IDs. Flat slots are local to the execution plan being rewritten.
        // Assigning flat slots from an outer rewriter would produce IDs that are out of
        // range for the nested plan's FlatSlotCount and crash at runtime.
        if (_isRestampingNestedFunction)
        {
            return -1;
        }

        var key = (scopeId, slotIndex);
        if (_flatSlotMap.TryGetValue(key, out var flatSlotId))
        {
            return flatSlotId;
        }

        flatSlotId = _flatSlotMap.Count;
        _flatSlotMap[key] = flatSlotId;
        return flatSlotId;
    }

    protected override AssignmentExpression RewriteAssignment(AssignmentExpression node)
    {
        if (TryResolve(node.Target, out var resolution))
        {
            var flatSlotId = GetOrCreateFlatSlotId(resolution.scopeId, resolution.slotIndex);
            return node with
            {
                ScopeDepth = 0,
                ScopeId = resolution.scopeId,
                SlotIndex = resolution.slotIndex,
                FlatSlotId = flatSlotId,
                Value = RewriteExpression(node.Value)
            };
        }

        return base.RewriteAssignment(node);
    }

    private (int scopeId, int slotIndex) ResolveInScope(Symbol symbol, int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        if (_scopes.TryGetValue(lookupScopeId, out var info) &&
            info.Slots.TryGetValue(symbol, out var index))
        {
            return (scopeId, index);
        }

        return (-1, -1);
    }

    private bool TryResolve(Symbol symbol, out (int scopeId, int slotIndex) resolution)
    {
        foreach (var scopeId in _scopeStack)
        {
            var candidate = ResolveInScope(symbol, scopeId);
            if (candidate.scopeId >= 0 && candidate.slotIndex >= 0)
            {
                resolution = candidate;
                return true;
            }
        }

        foreach (var scope in _scopes)
        {
            if (scope.Value.Slots.TryGetValue(symbol, out var slotIndex))
            {
                var mappedScope = RemapScopeId(scope.Key);
                resolution = (mappedScope, slotIndex);
                return true;
            }
        }

        resolution = default;
        return false;
    }

    private ImmutableDictionary<Symbol, int> GetSlotMap(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _immutableSlotMaps.TryGetValue(lookupScopeId, out var map)
            ? map
            : ImmutableDictionary<Symbol, int>.Empty.WithComparers(ReferenceEqualityComparer<Symbol>.Instance);
    }

    private int GetSlotCount(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _scopes.TryGetValue(lookupScopeId, out var info) ? info.SlotCount : 0;
    }

    private ImmutableHashSet<Symbol> GetLexicalBindings(int scopeId)
    {
        var lookupScopeId = _reverseScopeIdRemap.TryGetValue(scopeId, out var original)
            ? original
            : scopeId;

        return _lexicalBindings.TryGetValue(lookupScopeId, out var set)
            ? set
            : ImmutableHashSet<Symbol>.Empty.WithComparer(ReferenceEqualityComparer<Symbol>.Instance);
    }

    private void LeaveScope(int scopeId)
    {
        if (_scopeStack.Count <= 1)
        {
            return;
        }

        if (_scopeStack.Peek() == scopeId)
        {
            _scopeStack.Pop();
            return;
        }

        while (_scopeStack.Count > 1)
        {
            var popped = _scopeStack.Pop();
            if (popped == scopeId)
            {
                break;
            }
        }
    }

    private void PopToScope(int targetScopeId)
    {
        if (targetScopeId < 0 || _scopeStack.Count <= 1)
        {
            return;
        }

        while (_scopeStack.Count > 1 && _scopeStack.Peek() != targetScopeId)
        {
            _scopeStack.Pop();
        }
    }

    private int RemapScopeId(int scopeId)
    {
        if (_isRestampingNestedFunction)
        {
            return scopeId;
        }

        if (_scopeIdRemap.TryGetValue(scopeId, out var mapped))
        {
            return mapped;
        }

        var mappedId = scopeId == _analysisRootScopeId
            ? _targetRootScopeId
            : SyntheticScopeIdAllocator.Next();

        _scopeIdRemap[scopeId] = mappedId;
        _reverseScopeIdRemap[mappedId] = scopeId;
        return mappedId;
    }

    private sealed class ExpressionProgramObjectPoolBuilder
    {
        private readonly List<object> _objects;
        private readonly Dictionary<object, int> _indices;

        public ExpressionProgramObjectPoolBuilder(ImmutableArray<object> existingObjects)
        {
            _objects = existingObjects.IsDefaultOrEmpty ? [] : [.. existingObjects];
            _indices = new Dictionary<object, int>(ReferenceEqualityComparer<object>.Instance);
            for (var i = 0; i < _objects.Count; i++)
            {
                _indices[_objects[i]] = i;
            }
        }

        public int InternObject<T>(T value)
            where T : class
        {
            if (_indices.TryGetValue(value, out var existingIndex))
            {
                return existingIndex;
            }

            var index = _objects.Count;
            _objects.Add(value);
            _indices[value] = index;
            return index;
        }

        public ImmutableArray<object> Build()
        {
            return _objects.Count == 0 ? ImmutableArray<object>.Empty : [.. _objects];
        }
    }

    private sealed class ExpressionProgramIdentifierPoolBuilder
    {
        private readonly List<IdentifierOperand> _identifiers;
        private readonly Dictionary<IdentifierOperand, int> _indices;

        public ExpressionProgramIdentifierPoolBuilder(ImmutableArray<IdentifierOperand> existingIdentifiers)
        {
            _identifiers = existingIdentifiers.IsDefaultOrEmpty ? [] : [.. existingIdentifiers];
            _indices = new Dictionary<IdentifierOperand, int>(_identifiers.Count);
            for (var i = 0; i < _identifiers.Count; i++)
            {
                _indices[_identifiers[i]] = i;
            }
        }

        public int InternIdentifier(IdentifierOperand value)
        {
            if (_indices.TryGetValue(value, out var existingIndex))
            {
                return existingIndex;
            }

            var index = _identifiers.Count;
            _identifiers.Add(value);
            _indices[value] = index;
            return index;
        }

        public ImmutableArray<IdentifierOperand> Build()
        {
            return _identifiers.Count == 0 ? ImmutableArray<IdentifierOperand>.Empty : [.. _identifiers];
        }
    }
}
