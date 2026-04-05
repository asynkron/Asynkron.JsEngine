#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

#endregion

#pragma warning disable CS0618 // Obsolete AST evaluation methods are used intentionally here

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static (JsEnvironment EvaluationEnvironment, JsEnvironment? ClassScope) CreateClassScopeIfNeeded(
        JsEnvironment environment,
        Symbol? className,
        SourceReference? source,
        bool createNameScope)
    {
        if (className is null || !createNameScope)
        {
            return (environment, null);
        }

        var classScope =
            JsEnvironment.CreateInstance(environment, isStrict: true, creatingSource: source, description: "class scope");
        classScope.DefineJsValue(className, JsValue.Uninitialized, true, blocksFunctionScopeOverride: true,
            isImmutableBinding: true);
        return (classScope, classScope);
    }

    private static void InitializeStaticElements(
        ClassDefinition definition,
        ImmutableArray<ClassField> resolvedFields,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope)
    {
        if (definition.StaticElements.IsDefaultOrEmpty)
        {
            return;
        }

        using var staticFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict);
        Func<IDisposable?>? privateScopeFactory = privateNameScope is not null
            ? () => context.EnterPrivateNameScope(privateNameScope)
            : null;

        foreach (var element in definition.StaticElements)
        {
            if (context.ShouldStopEvaluation)
            {
                break;
            }

            switch (element.Kind)
            {
                case ClassStaticElementKind.Field:
                    var field = resolvedFields[element.Index];
                    context.RealmState.Logger?.LogInformation(
                        "Initializing static field '{Name}' (index {Index})",
                        field.Name,
                        element.Index);
                    if (!field.TryInitializeStaticField(
                            constructorAccessor,
                            environment,
                            context,
                            privateNameScope,
                            privateScopeFactory))
                    {
                        return;
                    }

                    break;
                case ClassStaticElementKind.Block:
                    var block = definition.StaticBlocks[element.Index];
                    ExecuteStaticBlock(block, constructorAccessor, environment, context, privateScopeFactory);
                    break;
            }
        }
    }

    private static (IJsEnvironmentAwareCallable? Constructor, IJsPropertyAccessor? Prototype) ResolveSuperclass(
        ExpressionProgram? extendsProgram,
        JsEnvironment environment,
        EvaluationContext context)
    {
        if (extendsProgram is null)
        {
            return (null, null);
        }

        var baseJsValue = EvaluateLoweredExpressionProgram(extendsProgram.Value, environment, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (baseJsValue.IsNullOrUndefined)
        {
            return (null, null);
        }

        var baseValue = baseJsValue.Kind == JsValueKind.Object ? baseJsValue.ObjectValue : null;

        if (!JsOps.IsConstructor(JsValue.FromObjectUnsafe(baseValue)))
        {
            throw StandardLibrary.ThrowTypeError(
                "Class extends value is not a constructor or null",
                context,
                context.RealmState);
        }

        if (baseValue is IJsPropertyAccessor accessorWithMarker &&
            JsOps.TryGetPropertyValue(
                JsValue.FromObjectUnsafe(accessorWithMarker),
                "__proxyHasNoPrototype__",
                out var marker,
                context) &&
            JsOps.ToBoolean(marker))
        {
            throw StandardLibrary.ThrowTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState);
        }

        if (baseValue is not (IJsEnvironmentAwareCallable callable and IJsPropertyAccessor))
        {
            throw StandardLibrary.ThrowTypeError(
                "Class extends value is not a constructor or null",
                context,
                context.RealmState);
        }

        var hasPrototype = JsOps.TryGetPropertyValue(baseJsValue, "prototype", out var prototypeValue, context);
        if (context.ShouldStopEvaluation)
        {
            return (null, null);
        }

        if (!hasPrototype)
        {
            throw StandardLibrary.ThrowTypeError(
                "Class extends value does not have a valid prototype",
                context,
                context.RealmState);
        }

        if (prototypeValue.IsNull)
        {
            return (callable, null);
        }

        if (prototypeValue.TryGetObject<IJsPropertyAccessor>(out var prototype))
        {
            return (callable, prototype);
        }

        throw StandardLibrary.ThrowTypeError(
            "Class extends value does not have a valid prototype",
            context,
            context.RealmState);
    }

    private static void ExecuteStaticBlock(
        ClassStaticBlock block,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context,
        Func<IDisposable?>? privateScopeFactory)
    {
        using var privateScope = privateScopeFactory?.Invoke();
        var planCache = ((IAstCacheable<StaticBlockPlanCache>)block).GetOrCreateCache();
        if (!planCache.Succeeded || planCache.Plan is null)
        {
            var reason = planCache.FailureReason ?? "IR static block plan is unavailable";
            throw new NotSupportedException($"IR plan generation failed for class static block: {reason}");
        }

        var blockEnvironment = CreateStaticInitializationEnvironment(constructorAccessor, environment, out _);
        try
        {
            _ = ExecutionPlanRunner.RunScript(planCache.Plan, blockEnvironment, context);
        }
        catch (ThrowSignal signal)
        {
            context.SetThrow(signal.ThrownValue);
        }
    }

    private static JsValue CreateClassValue(this ClassDefinition definition, JsEnvironment environment,
        EvaluationContext context,
        Symbol? className,
        bool createNameScope = true)
    {
        using var classScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict);
        var programCache = ((IAstCacheable<ClassDefinitionProgramCache>)definition).GetOrCreateCache();
        if (!programCache.Succeeded)
        {
            var reason = programCache.FailureReason ?? "unknown failure";
            throw new NotSupportedException($"IR class definition lowering failed: {reason}");
        }

        var (evaluationEnvironment, classScopeEnvironment) = CreateClassScopeIfNeeded(
            environment,
            className,
            definition.Source,
            createNameScope);

        var (superConstructor, superPrototype) =
            ResolveSuperclass(programCache.ExtendsProgram, evaluationEnvironment, context);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var privateNameScope = definition.CreatePrivateNameScope(context.RealmState);
        context.RealmState.Logger?.LogInformation(
            "Class evaluation start: name='{Name}', fields={FieldCount}, staticElements={StaticCount}, envStrict={EnvStrict}",
            className?.Name ?? "<anonymous>",
            definition.Fields.Length,
            definition.StaticElements.Length,
            evaluationEnvironment.IsStrict);
        var resolvedFields =
            definition.ResolveFieldNames(
                definition.Fields,
                programCache.FieldNamePrograms,
                evaluationEnvironment,
                context,
                privateNameScope);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        var constructorDefinition = definition.Constructor;
        if (definition.Extends is not null &&
            !constructorDefinition.IsDefaultDerivedConstructor &&
            constructorDefinition.IsImplicitDefaultDerivedConstructor())
        {
            constructorDefinition = constructorDefinition with { IsDefaultDerivedConstructor = true };
        }

        var constructorCallable = constructorDefinition.CreateFunctionValue(
            evaluationEnvironment,
            context,
            isConstructorFunction: true,
            skipInternalNameBinding: true);
        var constructorJsValue = JsValue.FromObjectUnsafe(constructorCallable);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (constructorJsValue.ObjectValue is not (IJsEnvironmentAwareCallable
            and IJsPropertyAccessor constructorAccessor))
        {
            throw new InvalidOperationException("Class constructor must be callable.");
        }

        var realm = context.RealmState;
        var prototype = constructorAccessor.EnsurePrototype(realm);
        if (definition.Extends is not null)
        {
            prototype.SetPrototype(superPrototype);
        }

        if (constructorAccessor is SyncFunctionInvoker typedCtorForOrdering)
        {
            typedCtorForOrdering.SeedIntrinsicConstructorKeys();
            typedCtorForOrdering.SetPrototypeObject(prototype);
        }
        else if (constructorAccessor is JsObject ctorForOrdering)
        {
            ctorForOrdering.SeedIntrinsicConstructorKeys();
        }

        if (constructorAccessor is SyncFunctionInvoker typedFunction)
        {
            typedFunction.SetSuperBinding(superConstructor, superPrototype);
            var instanceFields = resolvedFields.Where(static field => !field.IsStatic).ToImmutableArray();
            _ = evaluationEnvironment;
            _ = context;
            _ = privateNameScope;
            var resolvedInstanceFields = instanceFields;
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            typedFunction.SetInstanceFields(resolvedInstanceFields);
            typedFunction.SetIsClassConstructor(definition.Extends is not null);
            typedFunction.SetPrivateNameScope(privateNameScope);
            typedFunction.SetSourceReference(definition.Source);
            if (privateNameScope is not null)
            {
                typedFunction.AddPrivateBrand(privateNameScope.BrandToken);
            }
        }

        if (superConstructor is not null)
        {
            if (constructorAccessor is IJsObjectLike ctorObject &&
                superConstructor is IJsPropertyAccessor superProto)
            {
                ctorObject.SetPrototype(superProto);
            }
            else
            {
                throw new InvalidOperationException(
                    "Class constructor must implement IJsObjectLike to set prototype chain.");
            }
        }
        else if (constructorAccessor is IJsObjectLike { Prototype: null } baseCtor &&
                 realm.FunctionPrototype is not null)
        {
            baseCtor.SetPrototype(realm.FunctionPrototype);
        }

        prototype.SetProperty("constructor", constructorJsValue);

        if (constructorAccessor is IPropertyDefinitionHost definitionHost and SyncFunctionInvoker
            {
                IsClassConstructor: true
            })
        {
            definitionHost.TryDefineProperty("prototype",
                new PropertyDescriptor
                {
                    Value = prototype,
                    Writable = false,
                    Enumerable = false,
                    Configurable = false
                });
        }

        definition.Members.AssignClassMembers(
            programCache.MemberNamePrograms,
            constructorAccessor,
            prototype,
            superConstructor,
            superPrototype,
            evaluationEnvironment,
            context,
            privateNameScope);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        if (constructorAccessor is IFunctionNameTarget nameTarget && className is not null)
        {
            nameTarget.EnsureHasName(className.Name);
        }

        // Per ES spec 15.7.14 ClassDefinitionEvaluation step 26:
        // Initialize the class name binding BEFORE evaluating static elements
        // so that static blocks can reference the class name
        if (classScopeEnvironment is not null && className is not null)
        {
            classScopeEnvironment.TryAssignBlockedBinding(className, JsValue.FromObjectUnsafe(constructorAccessor));
        }

        InitializeStaticElements(
            definition,
            resolvedFields,
            constructorAccessor,
            evaluationEnvironment,
            context,
            privateNameScope);
        if (context.ShouldStopEvaluation)
        {
            return JsValue.Undefined;
        }

        return constructorJsValue;
    }

    private static PrivateNameScope? CreatePrivateNameScope(this ClassDefinition definition, RealmState realm)
    {
        var hasPrivateFields = definition.Fields.Any(static f => f.IsPrivate);
        var hasPrivateMembers = definition.Members.Any(static m => m.IsPrivate);
        return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope(realm) : null;
    }

    // ClassFieldDefinitionEvaluation evaluates computed field names during class evaluation,
    // so resolve all field keys eagerly in declaration order (static + instance).
    private static ImmutableArray<ClassField> ResolveFieldNames(this ClassDefinition _,
        ImmutableArray<ClassField> fields,
        ImmutableArray<ExpressionProgram?> fieldNamePrograms,
        JsEnvironment environment,
        EvaluationContext context,
        PrivateNameScope? privateNameScope)
    {
        if (fields.IsDefaultOrEmpty)
        {
            return fields;
        }

        var builder = ImmutableArray.CreateBuilder<ClassField>(fields.Length);
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var propertyName = field.Name;
            if (!field.TryResolveFieldName(
                    fieldNamePrograms.IsDefaultOrEmpty ? null : fieldNamePrograms[index],
                    environment,
                    context,
                    privateNameScope,
                    out propertyName))
            {
                context.RealmState.Logger?.LogInformation(
                    "Class field name resolution aborted (computed={IsComputed}, static={IsStatic}, private={IsPrivate})",
                    field.IsComputed,
                    field.IsStatic,
                    field.IsPrivate);
                return fields;
            }

            context.RealmState.Logger?.LogInformation(
                "Class field resolved name: original='{Original}' resolved='{Resolved}' (computed={IsComputed}, static={IsStatic}, private={IsPrivate})",
                field.Name,
                propertyName,
                field.IsComputed,
                field.IsStatic,
                field.IsPrivate);

            builder.Add(field with { Name = propertyName, IsComputed = false, ComputedName = null });
        }

        return builder.ToImmutable();
    }

    [UsedImplicitly]
    public static bool IsImplicitDefaultDerivedConstructor(this FunctionExpression constructor)
    {
        if (constructor.Parameters.Length != 0)
        {
            return false;
        }

        if (constructor.Body.Statements.Length != 1)
        {
            return false;
        }

        if (constructor.Body.Statements[0] is not ExpressionStatement
            {
                Expression: CallExpression
                {
                    Callee: SuperExpression,
                    Arguments.Length: 1
                } superCall
            })
        {
            return false;
        }

        var arg = superCall.Arguments[0];
        return arg.IsSpread && arg.Expression is IdentifierExpression
        {
            Name.Name: "arguments"
        };
    }
}
