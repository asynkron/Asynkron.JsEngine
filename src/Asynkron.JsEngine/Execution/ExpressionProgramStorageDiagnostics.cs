using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

internal static class ExpressionProgramStorageDiagnostics
{
    public static ExpressionProgramStorageSnapshot Collect(ProgramNode program)
    {
        var collector = new Collector();
        collector.VisitProgram(program);
        return collector.Build();
    }

    public static ExpressionProgramStorageSnapshot Collect(ExpressionProgram program)
    {
        var collector = new Collector();
        collector.AddProgram(program);
        return collector.Build();
    }

    private sealed class Collector : AstVisitor
    {
        private readonly HashSet<FunctionExpression> _visitedFunctions = new(ReferenceEqualityComparer<FunctionExpression>.Instance);
        private readonly HashSet<ClassDefinition> _visitedClassDefinitions = new(ReferenceEqualityComparer<ClassDefinition>.Instance);
        private readonly Dictionary<int, int> _maxStackDepthHistogram = [];
        private readonly Dictionary<ExpressionOpKind, long> _operationKindHistogram = [];
        private long _programCount;
        private long _operationCount;
        private long _estimatedEncodedOperationBytes;
        private long _literalConstantCount;
        private long _stringConstantCount;
        private long _objectConstantCount;
        private long _identifierConstantCount;
        private long _spreadMaskConstantCount;
        private long _opsWithFlagsCount;
        private long _opsWithImmediate0Count;
        private long _opsWithImmediate1Count;
        private long _opsWithBothImmediatesCount;
        private long _optionalOperationCount;
        private long _shortCircuitOperationCount;
        private long _estimatedMaxStackSlotCount;
        private long _estimatedMaxStackFlagWordCount;

        public void VisitProgram(ProgramNode program)
        {
            var scriptCache = ((IAstCacheable<ScriptPlanCache>)program).GetOrCreateCache();
            if (scriptCache.Succeeded && scriptCache.Plan is { } scriptPlan)
            {
                VisitExecutionPlan(scriptPlan);
            }

            foreach (var statement in program.Body)
            {
                Visit(statement);
            }
        }

        public void AddProgram(ExpressionProgram program)
        {
            _programCount++;
            _operationCount += program.OperationCount;
            _estimatedEncodedOperationBytes += program.EstimatedEncodedOperationBytes;
            _literalConstantCount += GetLength(program.LiteralConstants);
            _stringConstantCount += GetLength(program.StringConstants);
            _objectConstantCount += GetLength(program.ObjectConstants);
            _identifierConstantCount += GetLength(program.IdentifierConstants);
            _spreadMaskConstantCount += GetLength(program.SpreadMaskConstants);

            var stackDepth = program.MaxStackDepth;
            _estimatedMaxStackSlotCount += stackDepth;
            _estimatedMaxStackFlagWordCount += GetFlagWordCount(stackDepth);
            if (_maxStackDepthHistogram.TryGetValue(stackDepth, out var count))
            {
                _maxStackDepthHistogram[stackDepth] = count + 1;
            }
            else
            {
                _maxStackDepthHistogram[stackDepth] = 1;
            }

            if (program.IsEmpty)
            {
                return;
            }

            var objectConstants = program.ObjectConstants.AsSpan();
            foreach (var op in program.EnumerateOperations())
            {
                if (_operationKindHistogram.TryGetValue(op.Kind, out var opKindCount))
                {
                    _operationKindHistogram[op.Kind] = opKindCount + 1;
                }
                else
                {
                    _operationKindHistogram[op.Kind] = 1;
                }

                var hasImmediate0 = op.HasImmediate0;
                var hasImmediate1 = op.HasImmediate1;
                if (hasImmediate0)
                {
                    _opsWithImmediate0Count++;
                }

                if (hasImmediate1)
                {
                    _opsWithImmediate1Count++;
                }

                if (hasImmediate0 && hasImmediate1)
                {
                    _opsWithBothImmediatesCount++;
                }

                if (op.EncodedFlags != 0)
                {
                    _opsWithFlagsCount++;
                }

                if (IsOptionalOperation(op))
                {
                    _optionalOperationCount++;
                }

                if (IsShortCircuitOperation(op))
                {
                    _shortCircuitOperationCount++;
                }

                switch (op.Kind)
                {
                    case ExpressionOpKind.ApplyBindingTarget:
                        VisitBindingTargetProgram(op.GetObject<BindingTargetProgram>(objectConstants));
                        break;

                    case ExpressionOpKind.LoadFunctionLiteral:
                        VisitFunction(op.GetObject<FunctionLiteralDescriptor>(objectConstants).Function);
                        break;

                    case ExpressionOpKind.LoadClassLiteral:
                        VisitClassDefinition(op.GetObject<ClassExpression>(objectConstants).Definition);
                        break;
                }
            }
        }

        public ExpressionProgramStorageSnapshot Build()
        {
            var estimatedStackValueBytes = _estimatedMaxStackSlotCount * Unsafe.SizeOf<JsValue>();
            var estimatedStackFlagBytes = _estimatedMaxStackFlagWordCount * sizeof(ulong);
            var maxStackDepthHistogram = _maxStackDepthHistogram
                .OrderBy(static pair => pair.Key)
                .ToImmutableArray();
            var operationKindHistogram = _operationKindHistogram
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .ToImmutableArray();

            return new ExpressionProgramStorageSnapshot(
                ProgramCount: _programCount,
                OperationCount: _operationCount,
                EstimatedEncodedOperationBytes: _estimatedEncodedOperationBytes,
                LiteralConstantCount: _literalConstantCount,
                StringConstantCount: _stringConstantCount,
                ObjectConstantCount: _objectConstantCount,
                IdentifierConstantCount: _identifierConstantCount,
                SpreadMaskConstantCount: _spreadMaskConstantCount,
                MaxStackDepthHistogram: maxStackDepthHistogram,
                OperationKindHistogram: operationKindHistogram,
                OperationsWithFlagsCount: _opsWithFlagsCount,
                OperationsWithImmediate0Count: _opsWithImmediate0Count,
                OperationsWithImmediate1Count: _opsWithImmediate1Count,
                OperationsWithBothImmediatesCount: _opsWithBothImmediatesCount,
                OptionalOperationCount: _optionalOperationCount,
                ShortCircuitOperationCount: _shortCircuitOperationCount,
                EstimatedMaxStackSlotCount: _estimatedMaxStackSlotCount,
                EstimatedMaxStackValueBytes: estimatedStackValueBytes,
                EstimatedMaxStackFlagWordCount: _estimatedMaxStackFlagWordCount,
                EstimatedMaxStackFlagBytes: estimatedStackFlagBytes);
        }

        private void VisitFunction(FunctionExpression function)
        {
            if (!_visitedFunctions.Add(function))
            {
                return;
            }

            var cache = ((IAstCacheable<ExecutionPlanCache>)function).GetOrCreateCache();
            if (cache.Succeeded && cache.Plan is { } plan)
            {
                VisitExecutionPlan(plan);
            }

            Visit(function);
        }

        private void VisitClassDefinition(ClassDefinition definition)
        {
            if (!_visitedClassDefinitions.Add(definition))
            {
                return;
            }

            var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
            AddOptionalProgram(cache.ExtendsProgram);
            foreach (var memberProgram in cache.MemberNamePrograms)
            {
                AddOptionalProgram(memberProgram);
            }

            foreach (var fieldProgram in cache.FieldNamePrograms)
            {
                AddOptionalProgram(fieldProgram);
            }

            foreach (var fieldInitializerProgram in cache.FieldInitializerPrograms)
            {
                AddOptionalProgram(fieldInitializerProgram);
            }

            foreach (var staticBlockPlan in cache.Definition.StaticBlockPlans)
            {
                VisitExecutionPlan(staticBlockPlan);
            }
        }

        protected override void VisitFunctionDeclaration(FunctionDeclaration funcDecl)
        {
            VisitFunction(funcDecl.Function);
            base.VisitFunctionDeclaration(funcDecl);
        }

        protected override void VisitFunctionExpression(FunctionExpression function)
        {
            VisitFunction(function);
            base.VisitFunctionExpression(function);
        }

        protected override void VisitClassDeclaration(ClassDeclaration classDeclaration)
        {
            VisitClassDefinition(classDeclaration.Definition);
            base.VisitClassDeclaration(classDeclaration);
        }

        protected override void VisitClassExpression(ClassExpression classExpression)
        {
            VisitClassDefinition(classExpression.Definition);
            base.VisitClassExpression(classExpression);
        }

        private void AddOptionalProgram(ExpressionProgram? program)
        {
            if (program is { } value)
            {
                AddProgram(value);
            }
        }

        private void VisitExecutionPlan(ExecutionPlan plan)
        {
            foreach (var instruction in plan.Instructions)
            {
                switch (instruction)
                {
                    case EvaluateAndDiscardInstruction eval:
                        AddProgram(eval.ExpressionProgram);
                        break;

                    case AwaitAndDiscardInstruction awaitDiscard:
                        AddProgram(awaitDiscard.AwaitedProgram);
                        break;

                    case AssignmentSlotInstruction assign:
                        AddProgram(assign.AwaitedProgram ?? assign.ValueProgram!.Value);
                        break;

                    case LogicalCompoundAssignmentSlotInstruction logicalCompound:
                        AddProgram(logicalCompound.AwaitedProgram ?? logicalCompound.RhsProgram!.Value);
                        break;

                    case YieldInstruction { AwaitedProgram: not null } yieldAwaited:
                        AddProgram(yieldAwaited.AwaitedProgram.Value);
                        break;

                    case YieldInstruction { YieldProgram: not null } yieldProgram:
                        AddProgram(yieldProgram.YieldProgram.Value);
                        break;

                    case ReturnInstruction { AwaitedProgram: not null } returnAwaited:
                        AddProgram(returnAwaited.AwaitedProgram.Value);
                        break;

                    case ReturnInstruction { ReturnProgram: not null } returnProgram:
                        AddProgram(returnProgram.ReturnProgram.Value);
                        break;

                    case ThrowInstruction throwInstruction:
                        AddProgram(throwInstruction.AwaitedProgram ?? throwInstruction.ThrowProgram!.Value);
                        break;

                    case BranchInstruction branch:
                        AddProgram(branch.ConditionProgram);
                        break;

                    case SimpleVariableDeclarationInstruction varDecl:
                        if (varDecl.AwaitedProgram is { } awaitedDeclarationProgram)
                        {
                            AddProgram(awaitedDeclarationProgram);
                        }
                        else
                        {
                            AddOptionalProgram(varDecl.InitializerProgram);
                        }
                        break;

                    case BindingVariableDeclarationInstruction bindingDecl:
                        if (bindingDecl.AwaitedProgram is { } awaitedBindingProgram)
                        {
                            AddProgram(awaitedBindingProgram);
                        }
                        else
                        {
                            AddOptionalProgram(bindingDecl.InitializerProgram);
                        }
                        break;

                    case IteratorInitInstruction iterInit:
                        AddProgram(iterInit.AwaitedProgram ?? iterInit.IterableProgram!.Value);
                        break;

                    case ForInInitInstruction forInInit:
                        AddProgram(forInInit.AwaitedProgram ?? forInInit.ObjectProgram!.Value);
                        break;

                    case ArrayDestructuringInitInstruction arrayDestructuringInit:
                        AddProgram(arrayDestructuringInit.SourceProgram);
                        break;

                    case EnterCatchInstruction { CatchBindingProgram: not null } enterCatch:
                        VisitBindingTargetProgram(enterCatch.CatchBindingProgram);
                        break;

                    case CompoundAssignmentSlotInstruction compoundAssign:
                        AddProgram(compoundAssign.AwaitedProgram ?? compoundAssign.RhsProgram!.Value);
                        break;

                    case EnterWithInstruction enterWith:
                        AddProgram(enterWith.AwaitedProgram ?? enterWith.ObjectProgram!.Value);
                        break;

                    case YieldStarInstruction { AwaitedProgram: not null } yieldStarAwaited:
                        AddProgram(yieldStarAwaited.AwaitedProgram.Value);
                        break;

                    case YieldStarInstruction { IterableProgram: not null } yieldStar:
                        AddProgram(yieldStar.IterableProgram.Value);
                        break;
                }
            }
        }

        private void VisitBindingTargetProgram(BindingTargetProgram program)
        {
            switch (program)
            {
                case ArrayBindingTargetProgram arrayBinding:
                    foreach (var element in arrayBinding.Elements)
                    {
                        if (element.Target is not null)
                        {
                            VisitBindingTargetProgram(element.Target);
                        }

                        AddOptionalProgram(element.DefaultProgram);
                    }

                    if (arrayBinding.RestElement is not null)
                    {
                        VisitBindingTargetProgram(arrayBinding.RestElement);
                    }
                    break;

                case ObjectBindingTargetProgram objectBinding:
                    foreach (var property in objectBinding.Properties)
                    {
                        VisitBindingTargetProgram(property.Target);
                        AddOptionalProgram(property.DefaultProgram);
                        AddOptionalProgram(property.NameProgram);
                    }

                    if (objectBinding.RestElement is not null)
                    {
                        VisitBindingTargetProgram(objectBinding.RestElement);
                    }
                    break;

                case NamedPropertyAssignmentBindingTargetProgram namedPropertyAssignment:
                    AddProgram(namedPropertyAssignment.TargetProgram);
                    break;

                case ComputedPropertyAssignmentBindingTargetProgram computedPropertyAssignment:
                    AddProgram(computedPropertyAssignment.TargetProgram);
                    AddProgram(computedPropertyAssignment.PropertyProgram);
                    break;

                case ComputedSuperPropertyAssignmentBindingTargetProgram computedSuperPropertyAssignment:
                    AddProgram(computedSuperPropertyAssignment.PropertyProgram);
                    break;
            }
        }

        private static int GetLength<T>(ImmutableArray<T> items)
        {
            return items.IsDefault ? 0 : items.Length;
        }

        private static long GetFlagWordCount(int maxStackDepth)
        {
            return (maxStackDepth + 63L) / 64L;
        }

        private static bool IsOptionalOperation(PackedExpressionOp op)
        {
            return op.Kind switch
            {
                ExpressionOpKind.GetNamedProperty => op.IsOptional,
                ExpressionOpKind.GetComputedProperty => op.IsOptional,
                ExpressionOpKind.GetNamedSuperProperty => op.IsOptional,
                ExpressionOpKind.GetComputedSuperProperty => op.IsOptional,
                _ => false
            };
        }

        private static bool IsShortCircuitOperation(PackedExpressionOp op)
        {
            return op.Kind switch
            {
                ExpressionOpKind.GetNamedProperty => op.ShortCircuitOnNullishTarget,
                ExpressionOpKind.GetComputedProperty => op.ShortCircuitOnNullishTarget,
                ExpressionOpKind.GetNamedSuperProperty => op.ShortCircuitOnNullishTarget,
                ExpressionOpKind.GetComputedSuperProperty => op.ShortCircuitOnNullishTarget,
                ExpressionOpKind.JumpIfShortCircuited => true,
                _ => false
            };
        }
    }
}

internal sealed record ExpressionProgramStorageSnapshot(
    long ProgramCount,
    long OperationCount,
    long EstimatedEncodedOperationBytes,
    long LiteralConstantCount,
    long StringConstantCount,
    long ObjectConstantCount,
    long IdentifierConstantCount,
    long SpreadMaskConstantCount,
    ImmutableArray<KeyValuePair<int, int>> MaxStackDepthHistogram,
    ImmutableArray<KeyValuePair<ExpressionOpKind, long>> OperationKindHistogram,
    long OperationsWithFlagsCount,
    long OperationsWithImmediate0Count,
    long OperationsWithImmediate1Count,
    long OperationsWithBothImmediatesCount,
    long OptionalOperationCount,
    long ShortCircuitOperationCount,
    long EstimatedMaxStackSlotCount,
    long EstimatedMaxStackValueBytes,
    long EstimatedMaxStackFlagWordCount,
    long EstimatedMaxStackFlagBytes);
