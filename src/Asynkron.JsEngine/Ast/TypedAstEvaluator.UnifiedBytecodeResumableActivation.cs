using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private readonly record struct ResumableHoistedFunctionDeclaration(
        Symbol Name,
        FunctionDeclarationDescriptor Descriptor);

    private static bool TryCollectResumableRootHoistedFunctionDeclarations(
        FunctionExpression function,
        ExecutionPlan plan,
        out ImmutableArray<ResumableHoistedFunctionDeclaration> declarations)
    {
        declarations = ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        ImmutableArray<ResumableHoistedFunctionDeclaration>.Builder? builder = null;
        foreach (var statement in function.Body.Statements)
        {
            if (statement is not FunctionDeclaration functionDeclaration)
            {
                continue;
            }

            if (!activationSlots.SlotMap.ContainsKey(functionDeclaration.Name))
            {
                declarations = ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
                return false;
            }

            if (!AllowsIdentifierCaching(functionDeclaration.Function) ||
                UnifiedBytecodeProductionEligibility.FunctionCapturesActivationSlot(
                    functionDeclaration.Function,
                    activationSlots,
                    out _))
            {
                declarations = ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
                return false;
            }

            builder ??= ImmutableArray.CreateBuilder<ResumableHoistedFunctionDeclaration>();
            builder.Add(new ResumableHoistedFunctionDeclaration(
                functionDeclaration.Name,
                FunctionDeclarationDescriptor.Create(functionDeclaration)));
        }

        declarations = builder?.ToImmutable() ?? ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
        return true;
    }

    private static bool TryInitializeResumableSlots(
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        IReadOnlyList<JsValue> arguments,
        ImmutableArray<ResumableHoistedFunctionDeclaration> hoistedFunctionDeclarations,
        JsEnvironment closure,
        EvaluationContext context,
        out JsValue[] slots)
    {
        slots = [];
        slots = new JsValue[program.SlotCount];
        Array.Fill(slots, JsValue.Undefined);
        InitializeResumableLexicalSlots(slots, program);
        PopulateResumableParameterSlots(arguments, slots, program);
        if (!TryPopulateResumableRootHoistedFunctionDeclarations(
            hoistedFunctionDeclarations,
            plan,
            program,
            slots,
            closure,
            context))
        {
            slots = [];
            return false;
        }

        return true;
    }

    private static bool TryCreateMaterializedResumableBodyEnvironment(
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        JsValue[] slots,
        JsEnvironment parent,
        bool isStrict,
        SourceReference? source,
        out JsEnvironment environment)
    {
        environment = null!;
        if (plan.ActivationSlots is not { } activationSlots)
        {
            return false;
        }

        environment = JsEnvironment.CreateInstance(
            parent,
            isFunctionScope: true,
            isStrict,
            creatingSource: source,
            description: "resumable body activation",
            isBodyEnvironment: true);
        environment.InitializeSlots(activationSlots.SlotCount, activationSlots.ScopeId);
        environment.SetSlotNames(activationSlots.SlotNames);
        environment.SetSlotsLexicalUninitialized(activationSlots.LexicalSlotIndices);
        environment.SetSlotsConst(activationSlots.ConstLexicalSlotIndices);

        var slotNames = activationSlots.SlotNames;
        for (var i = 0; i < slotNames.Length; i++)
        {
            var (name, activationSlotIndex) = slotNames[i];
            if ((uint)activationSlotIndex >= (uint)environment.SlotCount ||
                !TryResolveResumableRootFlatSlot(plan, program, name, out var flatSlotIndex) ||
                (uint)flatSlotIndex >= (uint)slots.Length)
            {
                continue;
            }

            var value = slots[flatSlotIndex];
            if (value.IsUninitialized)
            {
                ref var slot = ref environment.GetSlotByIndex(activationSlotIndex);
                slot.Value = value;
                slot.Flags |= SlotFlags.Uninitialized;
                continue;
            }

            environment.SetSlotDirect(activationSlotIndex, value);
        }

        return true;
    }

    private static bool RequiresResumableSuperEnvironment(UnifiedBytecodeProgram program)
    {
        foreach (var instruction in program.Instructions)
        {
            if (instruction.OpCode is
                UnifiedBytecodeOpCode.EnsureSuperReference or
                UnifiedBytecodeOpCode.GetNamedSuperProperty or
                UnifiedBytecodeOpCode.GetComputedSuperProperty or
                UnifiedBytecodeOpCode.SetNamedSuperProperty or
                UnifiedBytecodeOpCode.SetComputedSuperProperty or
                UnifiedBytecodeOpCode.UpdateNamedSuperProperty or
                UnifiedBytecodeOpCode.UpdateComputedSuperProperty)
            {
                return true;
            }
        }

        return false;
    }

    private static void InitializeResumableLexicalSlots(JsValue[] slots, UnifiedBytecodeProgram program)
    {
        var lexicalSlotIndices = program.LexicalSlotIndices;
        if (lexicalSlotIndices.IsDefaultOrEmpty)
        {
            return;
        }

        for (var i = 0; i < lexicalSlotIndices.Length; i++)
        {
            slots[lexicalSlotIndices[i]] = JsValue.Uninitialized;
        }
    }

    private static void PopulateResumableParameterSlots(
        IReadOnlyList<JsValue> arguments,
        JsValue[] slots,
        UnifiedBytecodeProgram program)
    {
        var parameterSlotIndices = program.ParameterSlotIndices;
        if (parameterSlotIndices.IsDefaultOrEmpty)
        {
            return;
        }

        for (var i = 0; i < parameterSlotIndices.Length; i++)
        {
            var parameterSlotIndex = parameterSlotIndices[i];
            if (parameterSlotIndex >= 0)
            {
                slots[parameterSlotIndex] = i < arguments.Count ? arguments[i] : JsValue.Undefined;
            }
        }
    }

    private static bool TryPopulateResumableRootHoistedFunctionDeclarations(
        ImmutableArray<ResumableHoistedFunctionDeclaration> declarations,
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        JsValue[] slots,
        JsEnvironment closure,
        EvaluationContext context)
    {
        if (declarations.IsEmpty)
        {
            return true;
        }

        for (var i = 0; i < declarations.Length; i++)
        {
            var declaration = declarations[i];
            if (!TryResolveResumableRootFlatSlot(plan, program, declaration.Name, out var slotIndex))
            {
                return false;
            }

            var descriptor = declaration.Descriptor;
            var functionValue = CreateFunctionValueFromDeclaration(
                new FunctionLiteralDescriptor(descriptor.Function, descriptor.PlanSeed),
                closure,
                context);
            slots[slotIndex] = JsValue.FromObjectUnsafe(functionValue);
        }

        return true;
    }

    private static bool TryResolveResumableRootFlatSlot(
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        Symbol symbol,
        out int flatSlot)
    {
        flatSlot = -1;
        if (plan.ActivationSlots is not { } activationSlots ||
            !activationSlots.SlotMap.TryGetValue(symbol, out var activationSlotIndex))
        {
            return false;
        }

        if (plan.FlatSlotMappings is not null &&
            plan.FlatSlotMappings.TryGetValue(activationSlots.ScopeId, out var mappings))
        {
            for (var i = 0; i < mappings.Length; i++)
            {
                if (mappings[i].SlotIndex == activationSlotIndex)
                {
                    flatSlot = mappings[i].FlatSlotId;
                    return flatSlot >= 0;
                }
            }
        }

        return TryResolveUniqueProgramSlotName(program, symbol, out flatSlot);
    }

    private static bool TryResolveUniqueProgramSlotName(
        UnifiedBytecodeProgram program,
        Symbol symbol,
        out int flatSlot)
    {
        flatSlot = -1;
        var slotNames = program.SlotNames;
        if (slotNames.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var i = 0; i < slotNames.Length; i++)
        {
            if (!string.Equals(slotNames[i], symbol.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (flatSlot >= 0)
            {
                flatSlot = -1;
                return false;
            }

            flatSlot = i;
        }

        return flatSlot >= 0;
    }
}
