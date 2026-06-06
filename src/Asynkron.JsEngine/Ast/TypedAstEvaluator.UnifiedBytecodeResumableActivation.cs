using System.Collections.Immutable;
using System.Collections.Generic;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private readonly record struct ResumableHoistedFunctionDeclaration(
        Symbol Name,
        FunctionDeclarationDescriptor Descriptor,
        bool CapturesActivationSlot);

    private static bool TryCollectResumableRootHoistedFunctionDeclarations(
        FunctionExpression function,
        ExecutionPlan plan,
        bool allowCapturedActivationSlots,
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
                UnifiedBytecodeProductionEligibility.FunctionLiteralNeedsLexicalThisOrPrivateNameContext(
                    functionDeclaration.Function,
                    out _) ||
                UnifiedBytecodeProductionEligibility.FunctionCapturesActivationSlot(
                    functionDeclaration.Function,
                    activationSlots,
                    out var capturedName) &&
                (!allowCapturedActivationSlots ||
                 capturedName.Length == 0 ||
                 capturedName[0] == '<'))
            {
                declarations = ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
                return false;
            }

            builder ??= ImmutableArray.CreateBuilder<ResumableHoistedFunctionDeclaration>();
            builder.Add(new ResumableHoistedFunctionDeclaration(
                functionDeclaration.Name,
                FunctionDeclarationDescriptor.Create(functionDeclaration),
                CapturesActivationSlot:
                UnifiedBytecodeProductionEligibility.FunctionCapturesActivationSlot(
                    functionDeclaration.Function,
                    activationSlots,
                    out _)));
        }

        declarations = builder?.ToImmutable() ?? ImmutableArray<ResumableHoistedFunctionDeclaration>.Empty;
        return true;
    }

    private static bool HoistedFunctionDeclarationsNeedMaterializedBodyEnvironment(
        ImmutableArray<ResumableHoistedFunctionDeclaration> declarations)
    {
        for (var i = 0; i < declarations.Length; i++)
        {
            if (declarations[i].CapturesActivationSlot)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResumableCapturedOrDynamicActivationDecline(
        FunctionExpression function,
        JsEnvironment closure) =>
        closure.HasWithObjectInChain() ||
        DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters) ||
        ResumableDirectEvalActivationDetector.ContainsDynamicActivationDependency(function.Body);

    private static bool HasResumableArgumentsObjectDependency(FunctionExpression function) =>
        !function.IsArrow &&
        (ArgumentsReferenceDetector.ContainsArgumentsReference(function.Body) ||
         ArgumentsReferenceDetector.ContainsArgumentsReferenceInParameters(function.Parameters) ||
         DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters) ||
         ResumableDirectEvalActivationDetector.ContainsArgumentsObjectDependency(function.Body));

    private sealed class ResumableDirectEvalActivationDetector : AstVisitor
    {
        [ThreadStatic] private static ResumableDirectEvalActivationDetector? _instance;

        private bool _foundArgumentsObjectDependency;
        private bool _foundDynamicActivationDependency;

        public static bool ContainsDynamicActivationDependency(BlockStatement block)
        {
            var detector = _instance ??= new ResumableDirectEvalActivationDetector();
            detector.Reset();
            detector.Visit(block);
            return detector._foundDynamicActivationDependency;
        }

        public static bool ContainsArgumentsObjectDependency(BlockStatement block)
        {
            var detector = _instance ??= new ResumableDirectEvalActivationDetector();
            detector.Reset();
            detector.Visit(block);
            return detector._foundArgumentsObjectDependency;
        }

        protected override void VisitCallExpression(CallExpression node)
        {
            if (!node.IsOptional && node.Callee is IdentifierExpression { Name.Name: "eval" })
            {
                ClassifyDirectEval(node);
                return;
            }

            base.VisitCallExpression(node);
        }

        protected override void VisitFunctionExpression(FunctionExpression node)
        {
            if (!node.IsArrow)
            {
                return;
            }

            base.VisitFunctionExpression(node);
        }

        protected override void VisitFunctionDeclaration(FunctionDeclaration node) { }

        private void Reset()
        {
            _foundArgumentsObjectDependency = false;
            _foundDynamicActivationDependency = false;
            ShouldStop = false;
        }

        private void ClassifyDirectEval(CallExpression node)
        {
            if (!TryGetSingleLiteralDirectEvalSource(node, out var source))
            {
                _foundArgumentsObjectDependency = true;
                _foundDynamicActivationDependency = true;
                ShouldStop = true;
                return;
            }

            if (ContainsEvalDeclarationKeyword(source))
            {
                _foundDynamicActivationDependency = true;
            }

            if (ContainsKeyword(source, "arguments"))
            {
                _foundArgumentsObjectDependency = true;
            }

            ShouldStop = _foundArgumentsObjectDependency || _foundDynamicActivationDependency;
        }

        private static bool TryGetSingleLiteralDirectEvalSource(CallExpression node, out string source)
        {
            source = string.Empty;
            if (node.Arguments.Length != 1 ||
                node.Arguments[0].IsSpread ||
                node.Arguments[0].Expression is not LiteralExpression literal ||
                !literal.Value.IsString)
            {
                return false;
            }

            source = literal.Value.AsString();
            return true;
        }

        private static bool ContainsEvalDeclarationKeyword(string source) =>
            ContainsKeyword(source, "var") ||
            ContainsKeyword(source, "let") ||
            ContainsKeyword(source, "const") ||
            ContainsKeyword(source, "function") ||
            ContainsKeyword(source, "class");

        private static bool ContainsKeyword(string source, string keyword)
        {
            var index = 0;
            while (index < source.Length)
            {
                index = source.IndexOf(keyword, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }

                var before = index == 0 ? '\0' : source[index - 1];
                var afterIndex = index + keyword.Length;
                var after = afterIndex >= source.Length ? '\0' : source[afterIndex];
                if (!IsIdentifierPart(before) && !IsIdentifierPart(after))
                {
                    return true;
                }

                index += keyword.Length;
            }

            return false;
        }

        private static bool IsIdentifierPart(char ch) =>
            ch is '_' or '$' ||
            ch >= '0' && ch <= '9' ||
            ch >= 'A' && ch <= 'Z' ||
            ch >= 'a' && ch <= 'z';
    }

    private static bool TryInitializeResumableSlots(
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        IReadOnlyList<JsValue> arguments,
        out JsValue[] slots)
    {
        slots = [];
        slots = new JsValue[program.SlotCount];
        Array.Fill(slots, JsValue.Undefined);
        InitializeResumableLexicalSlots(slots, program);
        PopulateResumableParameterSlots(arguments, slots, program);
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
                UnifiedBytecodeOpCode.UpdateComputedSuperProperty or
                UnifiedBytecodeOpCode.PrepareNamedSuperCallTarget or
                UnifiedBytecodeOpCode.PrepareComputedSuperCallTarget)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateResumableSuperBinding(
        JsEnvironment closure,
        JsValue boundThis,
        IJsObjectLike? homeObject,
        out SuperBinding binding)
    {
        binding = null!;
        if (homeObject is not null)
        {
            var superPrototype = (homeObject as IPrototypeAccessorProvider)?.PrototypeAccessor ??
                                 homeObject.Prototype;
            if (superPrototype is null && boundThis.TryGetObject<JsObject>(out var thisObject))
            {
                superPrototype = thisObject.PrototypeAccessor ?? thisObject.Prototype;
            }

            var superConstructor = superPrototype as IJsEnvironmentAwareCallable;
            binding = new SuperBinding(superConstructor, superPrototype, boundThis, true);
            return true;
        }

        if (!closure.TryGetObject<SuperBinding>(Symbol.Super, out var inheritedBinding))
        {
            return false;
        }

        binding = new SuperBinding(
            inheritedBinding.Constructor,
            inheritedBinding.Prototype,
            boundThis,
            inheritedBinding.IsThisInitialized);
        return true;
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
            var functionJsValue = JsValue.FromObjectUnsafe(functionValue);
            slots[slotIndex] = functionJsValue;
            if (closure.TryGetSlotIndex(declaration.Name, out var environmentSlotIndex))
            {
                closure.SetSlotDirect(environmentSlotIndex, functionJsValue);
            }
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
