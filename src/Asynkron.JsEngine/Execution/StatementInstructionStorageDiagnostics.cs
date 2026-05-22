using System.Collections.Immutable;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.Execution.Instructions;

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
        private readonly HashSet<ExecutionPlan> _visitedPlans = new(ReferenceEqualityComparer<ExecutionPlan>.Instance);
        private readonly HashSet<FunctionExpression> _visitedFunctions = new(ReferenceEqualityComparer<FunctionExpression>.Instance);
        private readonly HashSet<ClassDefinition> _visitedClassDefinitions = new(ReferenceEqualityComparer<ClassDefinition>.Instance);
        private readonly Dictionary<InstructionKind, long> _instructionKindHistogram = [];
        private readonly Dictionary<InstructionKind, long> _supportedKindHistogram = [];
        private readonly Dictionary<InstructionKind, long> _unsupportedKindHistogram = [];
        private long _planCount;
        private long _instructionCount;
        private long _supportedInstructionCount;
        private long _unsupportedInstructionCount;
        private long _estimatedCompactEncodedBytes;

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
            if (!_visitedPlans.Add(plan))
            {
                return;
            }

            _planCount++;
            foreach (var instruction in plan.Instructions)
            {
                Observe(instruction);
            }
        }

        public StatementInstructionStorageSnapshot Build()
        {
            return new StatementInstructionStorageSnapshot(
                PlanCount: _planCount,
                InstructionCount: _instructionCount,
                SupportedInstructionCount: _supportedInstructionCount,
                UnsupportedInstructionCount: _unsupportedInstructionCount,
                EstimatedCompactEncodedBytes: _estimatedCompactEncodedBytes,
                InstructionKindHistogram: Sort(_instructionKindHistogram),
                SupportedInstructionKindHistogram: Sort(_supportedKindHistogram),
                UnsupportedInstructionKindHistogram: Sort(_unsupportedKindHistogram));
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

        private static ImmutableArray<KeyValuePair<InstructionKind, long>> Sort(Dictionary<InstructionKind, long> values)
        {
            return values
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key)
                .ToImmutableArray();
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

        private static long GetCompactEncodedByteEstimate(ExecutionInstruction instruction) =>
            StatementInstructionDiagnosticsCodec.TryEncode(instruction, out var encoded)
                ? encoded.EstimatedCompactByteSize
                : -1L;

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
            if (!cache.Succeeded)
            {
                return;
            }

            foreach (var staticBlockPlan in cache.Definition.StaticBlockPlans)
            {
                VisitExecutionPlan(staticBlockPlan);
            }
        }

        private void Observe(ExecutionInstruction instruction)
        {
            _instructionCount++;
            Increment(_instructionKindHistogram, instruction.Kind);
            VisitNestedPlans(instruction);

            var estimate = GetCompactEncodedByteEstimate(instruction);
            if (estimate >= 0)
            {
                _supportedInstructionCount++;
                _estimatedCompactEncodedBytes += estimate;
                Increment(_supportedKindHistogram, instruction.Kind);
                return;
            }

            _unsupportedInstructionCount++;
            Increment(_unsupportedKindHistogram, instruction.Kind);
        }

        private void VisitNestedPlans(ExecutionInstruction instruction)
        {
            switch (instruction)
            {
                case FunctionDeclarationInstruction { Descriptor: { } descriptor }:
                    if (descriptor.PlanSeed.Succeeded && descriptor.PlanSeed.Plan is { } functionPlan)
                    {
                        VisitExecutionPlan(functionPlan);
                    }

                    break;
                case ClassDeclarationInstruction classDeclaration:
                    VisitClassDeclarationDescriptor(classDeclaration.Descriptor);
                    break;
            }
        }

        private void VisitClassDeclarationDescriptor(ClassDeclarationDescriptor descriptor)
        {
            if (!descriptor.ProgramCache.Succeeded)
            {
                return;
            }

            var definition = descriptor.ProgramCache.Definition;
            if (definition.Constructor.PlanSeed.Succeeded && definition.Constructor.PlanSeed.Plan is { } constructorPlan)
            {
                VisitExecutionPlan(constructorPlan);
            }

            foreach (var member in definition.Members)
            {
                if (member.Callable.PlanSeed.Succeeded && member.Callable.PlanSeed.Plan is { } memberPlan)
                {
                    VisitExecutionPlan(memberPlan);
                }
            }

            foreach (var staticBlockPlan in definition.StaticBlockPlans)
            {
                VisitExecutionPlan(staticBlockPlan);
            }
        }
    }
}

internal sealed record StatementInstructionStorageSnapshot(
    long PlanCount,
    long InstructionCount,
    long SupportedInstructionCount,
    long UnsupportedInstructionCount,
    long EstimatedCompactEncodedBytes,
    ImmutableArray<KeyValuePair<InstructionKind, long>> InstructionKindHistogram,
    ImmutableArray<KeyValuePair<InstructionKind, long>> SupportedInstructionKindHistogram,
    ImmutableArray<KeyValuePair<InstructionKind, long>> UnsupportedInstructionKindHistogram);
