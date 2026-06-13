using System.Collections.Immutable;
using System.Collections.Generic;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Execution.UnifiedBytecode;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;

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
        JsEnvironment closure,
        bool allowDeclarationFreeDirectEval) =>
        closure.HasWithObjectInChain() ||
        DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters) ||
        HasResumableDirectEvalExplicitArgumentsBindingDependency(function) ||
        (allowDeclarationFreeDirectEval
            ? ResumableDirectEvalActivationDetector.ContainsDynamicActivationDependency(function.Body)
            : DynamicScopeDetector.ContainsDirectEval(function.Body));

    private static bool HasResumableArgumentsObjectDependency(
        FunctionExpression function,
        bool allowDeclarationFreeDirectEval) =>
        !function.IsArrow &&
        (ArgumentsReferenceDetector.ContainsArgumentsReference(function.Body) ||
         ArgumentsReferenceDetector.ContainsArgumentsReferenceInParameters(function.Parameters) ||
         DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters) ||
         (allowDeclarationFreeDirectEval
             ? ResumableDirectEvalActivationDetector.ContainsArgumentsObjectDependency(function.Body)
             : DynamicScopeDetector.ContainsDirectEval(function.Body)));

    private static bool CanUseNoParameterResumableImplicitArgumentsObjectPath(
        FunctionExpression function) =>
        !function.IsArrow &&
        function.Parameters.Length == 0 &&
        !HasArgumentsBindingDeclaration(function) &&
        ArgumentsReferenceDetector.ContainsArgumentsReference(function.Body);

    private static bool HasResumableDirectEvalImplicitArgumentsAccess(FunctionExpression function) =>
        !function.IsArrow &&
        !HasArgumentsBindingDeclaration(function) &&
        ResumableDirectEvalActivationDetector.ContainsImplicitArgumentsAccess(function.Body);

    private static bool HasResumableDirectEvalExplicitArgumentsBindingDependency(FunctionExpression function) =>
        !function.IsArrow &&
        HasArgumentsBindingDeclaration(function) &&
        ResumableDirectEvalActivationDetector.ContainsImplicitArgumentsAccess(function.Body);

    private static bool HasArgumentsBindingDeclaration(FunctionExpression function) =>
        HasArgumentsParameter(function) ||
        HasArgumentsBodyLexicalDeclaration(function);

    private static bool HasArgumentsParameter(FunctionExpression function)
    {
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name is { Name: "arguments" })
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasArgumentsBodyLexicalDeclaration(FunctionExpression function)
    {
        var hoistPlan = ((IAstCacheable<HoistPlan>)function.Body).GetOrCreateCache();
        foreach (var name in hoistPlan.BodyLexicalTemplate)
        {
            if (name.Name == "arguments")
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ResumableDirectEvalActivationDetector : AstVisitor
    {
        [ThreadStatic] private static ResumableDirectEvalActivationDetector? _instance;

        private bool _foundArgumentsObjectDependency;
        private bool _foundDynamicActivationDependency;
        private bool _foundImplicitArgumentsAccess;

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

        public static bool ContainsImplicitArgumentsAccess(BlockStatement block)
        {
            var detector = _instance ??= new ResumableDirectEvalActivationDetector();
            detector.Reset();
            detector.Visit(block);
            return detector._foundImplicitArgumentsAccess;
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
            _foundImplicitArgumentsAccess = false;
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

            var containsDeclaration = ContainsEvalDeclarationKeyword(source);
            if (containsDeclaration)
            {
                _foundDynamicActivationDependency = true;
            }

            if (ContainsKeyword(source, "arguments"))
            {
                if (containsDeclaration)
                {
                    _foundArgumentsObjectDependency = true;
                }
                else
                {
                    _foundImplicitArgumentsAccess = true;
                }
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

    private static void DefineResumableArgumentsObject(
        FunctionExpression function,
        IReadOnlyList<JsValue> arguments,
        JsEnvironment environment,
        RealmState realmState,
        IJsCallable callee,
        bool isStrict)
    {
        function.BindFunctionParameters(arguments, environment, realmState.CreateContext());
        var argumentsObject = function.CreateArgumentsObject(arguments, environment, realmState, callee, isStrict);
        environment.DefineJsValue(Symbol.Arguments, JsValue.FromObjectUnsafe(argumentsObject), isLexicalBinding: false);
    }

    private static bool ResumableParameterShapeAllowsEagerBinding(FunctionExpression function)
    {
        // Closures created inside parameter expressions capture the transient parameter
        // binding environment, which is discarded after the bound values seed the flat
        // slots. That is only hazardous when such a closure references one of this
        // function's own parameter bindings — the resumable body then mutates flat slots
        // the retained closure cannot observe. Closures over outer/global state (the
        // common fn-name-inference and IIFE-counter shapes) resolve through the parent
        // environment chain and stay coherent. Direct eval in parameters stays declined
        // (dynamic scope).
        var parameterNames =
            ((IAstCacheable<FunctionParameterNamesPlan>)function).GetOrCreateCache().ParameterNames;
        var parameterNameSet = new HashSet<Symbol>(parameterNames, ReferenceEqualityComparer<Symbol>.Instance);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.DefaultValue is { } defaultValue &&
                ParameterFunctionLiteralDetector.ContainsParameterCapturingFunctionLiteral(
                    defaultValue, parameterNameSet))
            {
                return false;
            }

            if (parameter.Pattern is { } pattern &&
                BindingTargetContainsFunctionLiteralInDefaultValue(pattern, parameterNameSet))
            {
                return false;
            }
        }

        return !DynamicScopeDetector.ContainsDirectEvalInParameters(function.Parameters);
    }

    private static bool BindingTargetContainsFunctionLiteralInDefaultValue(
        BindingTarget target,
        HashSet<Symbol> parameterNames)
    {
        switch (target)
        {
            case ArrayBinding arrayBinding:
                foreach (var element in arrayBinding.Elements)
                {
                    if (element.DefaultValue is not null &&
                        ParameterFunctionLiteralDetector.ContainsParameterCapturingFunctionLiteral(
                            element.DefaultValue, parameterNames))
                    {
                        return true;
                    }

                    if (element.Target is not null &&
                        BindingTargetContainsFunctionLiteralInDefaultValue(element.Target, parameterNames))
                    {
                        return true;
                    }
                }

                return arrayBinding.RestElement is not null &&
                       BindingTargetContainsFunctionLiteralInDefaultValue(arrayBinding.RestElement, parameterNames);

            case ObjectBinding objectBinding:
                foreach (var property in objectBinding.Properties)
                {
                    if (property.DefaultValue is not null &&
                        ParameterFunctionLiteralDetector.ContainsParameterCapturingFunctionLiteral(
                            property.DefaultValue, parameterNames))
                    {
                        return true;
                    }

                    if (BindingTargetContainsFunctionLiteralInDefaultValue(property.Target, parameterNames))
                    {
                        return true;
                    }
                }

                return objectBinding.RestElement is not null &&
                       BindingTargetContainsFunctionLiteralInDefaultValue(objectBinding.RestElement, parameterNames);

            case AssignmentTargetBinding assignmentTarget:
                return ParameterFunctionLiteralDetector.ContainsParameterCapturingFunctionLiteral(
                    assignmentTarget.Expression, parameterNames);

            default:
                return false;
        }
    }

    private sealed class ParameterFunctionLiteralDetector : AstVisitor
    {
        [ThreadStatic] private static ParameterFunctionLiteralDetector? _instance;

        private HashSet<Symbol>? _parameterNames;
        private int _functionLiteralDepth;
        private bool _found;

        public static bool ContainsParameterCapturingFunctionLiteral(
            ExpressionNode expression,
            HashSet<Symbol> parameterNames)
        {
            var detector = _instance ??= new ParameterFunctionLiteralDetector();
            detector._parameterNames = parameterNames;
            detector._functionLiteralDepth = 0;
            detector._found = false;
            detector.ShouldStop = false;
            detector.Visit(expression);
            detector._parameterNames = null;
            return detector._found;
        }

        protected override void VisitFunctionExpression(FunctionExpression node)
        {
            _functionLiteralDepth++;
            base.VisitFunctionExpression(node);
            _functionLiteralDepth--;
        }

        protected override void VisitFunctionDeclaration(FunctionDeclaration node)
        {
            _functionLiteralDepth++;
            base.VisitFunctionDeclaration(node);
            _functionLiteralDepth--;
        }

        protected override void VisitClassExpression(ClassExpression node)
        {
            _functionLiteralDepth++;
            base.VisitClassExpression(node);
            _functionLiteralDepth--;
        }

        protected override void VisitClassDeclaration(ClassDeclaration node)
        {
            _functionLiteralDepth++;
            base.VisitClassDeclaration(node);
            _functionLiteralDepth--;
        }

        protected override void VisitIdentifierExpression(IdentifierExpression node)
        {
            // Only identifiers INSIDE a retained function/class literal are hazardous; a
            // direct parameter reference in the default expression itself (`b = a * 2`)
            // evaluates during binding and never outlives the transient environment.
            // Shadowing inside the literal produces a conservative false positive (decline).
            if (_functionLiteralDepth > 0 &&
                _parameterNames is { } names &&
                names.Contains(node.Name))
            {
                _found = true;
                ShouldStop = true;
                return;
            }

            base.VisitIdentifierExpression(node);
        }
    }

    private static bool TryBindNonSimpleResumableParameters(
        FunctionExpression function,
        IReadOnlyList<JsValue> arguments,
        ExecutionPlan plan,
        UnifiedBytecodeProgram program,
        JsValue[] slots,
        JsEnvironment invocationEnvironment,
        bool isStrict,
        EvaluationContext context)
    {
        // Eager IteratorBindingInitialization: destructuring patterns, defaults, and rest
        // parameters bind in a transient environment at invocation time (per spec,
        // FunctionDeclarationInstantiation runs when the generator/async generator is
        // CALLED, so binding errors throw here rather than at the first resume). The bound
        // values then seed the flat parameter slots the resumable program reads; the
        // transient environment is not retained, which is why parameter expressions that
        // create closures stay declined in ResumableParameterShapeAllowsEagerBinding.
        var parameterEnvironment = JsEnvironment.CreateInstance(
            invocationEnvironment,
            isFunctionScope: true,
            isStrict,
            creatingSource: function.Source,
            description: "resumable parameter binding");

        function.BindFunctionParameters(arguments, parameterEnvironment, context);
        if (context.IsThrow)
        {
            var thrown = context.FlowValue;
            context.Clear();
            throw new ThrowSignal(thrown);
        }

        var parameterNames =
            ((IAstCacheable<FunctionParameterNamesPlan>)function).GetOrCreateCache().ParameterNames;
        foreach (var name in parameterNames)
        {
            if (!TryResolveResumableRootFlatSlot(plan, program, name, out var slotIndex) ||
                (uint)slotIndex >= (uint)slots.Length ||
                !parameterEnvironment.TryFindBindingJsValue(name, true, out _, out var value))
            {
                return false;
            }

            slots[slotIndex] = value;
        }

        return true;
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
