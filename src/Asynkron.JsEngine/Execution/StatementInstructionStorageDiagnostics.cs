using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Execution;

internal static class StatementInstructionStorageDiagnostics
{
    public static StatementInstructionStorageSnapshot Collect(ProgramNode program)
    {
        var collector = new Collector();
        collector.VisitProgram(program);
        return collector.Build();
    }

    public static StatementInstructionStorageSnapshot Collect(ExecutionPlan plan)
    {
        var collector = new Collector();
        collector.VisitExecutionPlan(plan);
        return collector.Build();
    }

    private sealed class Collector : AstVisitor
    {
        private readonly HashSet<FunctionExpression> _visitedFunctions = new(ReferenceEqualityComparer<FunctionExpression>.Instance);
        private readonly HashSet<ClassDefinition> _visitedClassDefinitions = new(ReferenceEqualityComparer<ClassDefinition>.Instance);
        private readonly Dictionary<InstructionKind, long> _instructionKindHistogram = [];
        private readonly Dictionary<InstructionKind, long> _unsupportedInstructionKindHistogram = [];
        private long _instructionCount;
        private long _encodedInstructionCount;
        private long _encodedInstructionBytes;

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

        public void VisitExecutionPlan(ExecutionPlan plan)
        {
            foreach (var instruction in plan.Instructions)
            {
                _instructionCount++;
                Increment(_instructionKindHistogram, instruction.Kind);

                if (StatementInstructionStorageCodec.TryEncode(instruction, out var encoded))
                {
                    _encodedInstructionCount++;
                    _encodedInstructionBytes += Unsafe.SizeOf<EncodedStatementInstruction>();
                    var decoded = StatementInstructionStorageCodec.Decode(encoded);

                    if (decoded.Kind != instruction.Kind || decoded.Next != instruction.Next)
                    {
                        throw new InvalidOperationException("Statement instruction diagnostic codec round-trip mismatch.");
                    }
                }
                else
                {
                    Increment(_unsupportedInstructionKindHistogram, instruction.Kind);
                }
            }
        }

        public StatementInstructionStorageSnapshot Build()
        {
            var instructionKindHistogram = _instructionKindHistogram
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .ToImmutableArray();
            var unsupportedInstructionKindHistogram = _unsupportedInstructionKindHistogram
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .ToImmutableArray();

            return new StatementInstructionStorageSnapshot(
                InstructionCount: _instructionCount,
                EncodedInstructionCount: _encodedInstructionCount,
                UnsupportedInstructionCount: _instructionCount - _encodedInstructionCount,
                EncodedInstructionBytes: _encodedInstructionBytes,
                InstructionKindHistogram: instructionKindHistogram,
                UnsupportedInstructionKindHistogram: unsupportedInstructionKindHistogram);
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
        }

        private void VisitClassDefinition(ClassDefinition definition)
        {
            if (!_visitedClassDefinitions.Add(definition))
            {
                return;
            }

            var cache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
            foreach (var staticBlockPlan in cache.Definition.StaticBlockPlans)
            {
                VisitExecutionPlan(staticBlockPlan);
            }
        }

        private static void Increment(Dictionary<InstructionKind, long> histogram, InstructionKind kind)
        {
            if (histogram.TryGetValue(kind, out var count))
            {
                histogram[kind] = count + 1;
            }
            else
            {
                histogram[kind] = 1;
            }
        }
    }
}

internal static class StatementInstructionStorageCodec
{
    public static bool TryEncode(ExecutionInstruction instruction, out EncodedStatementInstruction encoded)
    {
        encoded = instruction switch
        {
            EvaluateAndDiscardInstruction => new EncodedStatementInstruction(instruction.Kind, instruction.Next, 0),
            AwaitAndDiscardInstruction => new EncodedStatementInstruction(instruction.Kind, instruction.Next, 0),
            AssignmentSlotInstruction assign => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(assign.AwaitedProgram is not null, assign.SuppressCompletionValue)),
            LogicalCompoundAssignmentSlotInstruction logical => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(logical.AwaitedProgram is not null, logical.SuppressCompletionValue)),
            CompoundAssignmentSlotInstruction compound => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(compound.AwaitedProgram is not null, compound.SuppressCompletionValue)),
            SimpleVariableDeclarationInstruction simple => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags((simple.AwaitedProgram ?? simple.InitializerProgram) is not null, simple.IsScriptLevel)),
            BindingVariableDeclarationInstruction binding => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags((binding.AwaitedProgram ?? binding.InitializerProgram) is not null, false)),
            ReturnInstruction ret => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(ret.AwaitedProgram is not null, ret.ReturnProgram is not null)),
            ThrowInstruction thr => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(thr.AwaitedProgram is not null, thr.ThrowProgram is not null)),
            YieldInstruction yld => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(yld.AwaitedProgram is not null, yld.YieldProgram is not null)),
            YieldStarInstruction yldStar => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(yldStar.AwaitedProgram is not null, yldStar.IterableProgram is not null)),
            BranchInstruction => new EncodedStatementInstruction(instruction.Kind, instruction.Next, 0),
            IteratorInitInstruction iteratorInit => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(iteratorInit.AwaitedProgram is not null, iteratorInit.IterableProgram is not null)),
            ForInInitInstruction forInInit => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(forInInit.AwaitedProgram is not null, forInInit.ObjectProgram is not null)),
            EnterWithInstruction enterWith => new EncodedStatementInstruction(
                instruction.Kind,
                instruction.Next,
                ToFlags(enterWith.AwaitedProgram is not null, enterWith.ObjectProgram is not null)),
            ArrayDestructuringInitInstruction => new EncodedStatementInstruction(instruction.Kind, instruction.Next, 0),
            _ => default
        };

        return encoded != default;
    }

    public static DecodedStatementInstruction Decode(EncodedStatementInstruction encoded)
    {
        return new DecodedStatementInstruction(encoded.Kind, encoded.Next, encoded.Flags);
    }

    private static byte ToFlags(bool flag0, bool flag1)
    {
        return (byte)((flag0 ? 0b0000_0001 : 0) | (flag1 ? 0b0000_0010 : 0));
    }
}

internal readonly record struct EncodedStatementInstruction(InstructionKind Kind, int Next, byte Flags);

internal readonly record struct DecodedStatementInstruction(InstructionKind Kind, int Next, byte Flags);

internal sealed record StatementInstructionStorageSnapshot(
    long InstructionCount,
    long EncodedInstructionCount,
    long UnsupportedInstructionCount,
    long EncodedInstructionBytes,
    ImmutableArray<KeyValuePair<InstructionKind, long>> InstructionKindHistogram,
    ImmutableArray<KeyValuePair<InstructionKind, long>> UnsupportedInstructionKindHistogram);
