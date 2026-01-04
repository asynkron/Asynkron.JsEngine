#region

using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

#endregion

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
            new JsEnvironment(environment, isStrict: true, creatingSource: source, description: "class scope");
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
                            expr => EvaluateStaticFieldExpression(expr, constructorAccessor, environment, context),
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

    private static void ExecuteStaticBlock(
        ClassStaticBlock block,
        IJsPropertyAccessor constructorAccessor,
        JsEnvironment environment,
        EvaluationContext context,
        Func<IDisposable?>? privateScopeFactory)
    {
        using var privateScope = privateScopeFactory?.Invoke();
        var blockEnvironment = CreateStaticInitializationEnvironment(constructorAccessor, environment, out _);
        _ = block.Body.EvaluateStatementJsValue(blockEnvironment, context);
    }

    extension(ClassDefinition definition)
    {
    private JsValue CreateClassValue(JsEnvironment environment,
        EvaluationContext context,
        Symbol? className,
        bool createNameScope = true)
    {
        using var classScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict);
        var (evaluationEnvironment, classScopeEnvironment) = CreateClassScopeIfNeeded(
            environment,
            className,
            definition.Source,
            createNameScope);

            var (superConstructor, superPrototype) =
                definition.Extends.ResolveSuperclass(evaluationEnvironment, context);
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
                ClassDefinition.ResolveFieldNames(definition.Fields, evaluationEnvironment, context, privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            var constructorDefinition = definition.Constructor;
            if (definition.Extends is not null &&
                !constructorDefinition.IsDefaultDerivedConstructor &&
                IsImplicitDefaultDerivedConstructor(constructorDefinition))
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
                var resolvedInstanceFields = ClassDefinition.ResolveInstanceFieldNames(instanceFields,
                    evaluationEnvironment, context, privateNameScope);
                if (context.ShouldStopEvaluation)
                {
                    return JsValue.Undefined;
                }

                typedFunction.SetInstanceFields(resolvedInstanceFields);
                typedFunction.SetIsClassConstructor(definition.Extends is not null);
                typedFunction.SetPrivateNameScope(privateNameScope);
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

            definition.Members.AssignClassMembers(constructorAccessor, prototype, superConstructor, superPrototype,
                evaluationEnvironment, context, privateNameScope);
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

            InitializeStaticElements(definition, resolvedFields, constructorAccessor, evaluationEnvironment, context,
                privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return JsValue.Undefined;
            }

            return constructorJsValue;
        }

        private PrivateNameScope? CreatePrivateNameScope(RealmState realm)
        {
            var hasPrivateFields = definition.Fields.Any(static f => f.IsPrivate);
            var hasPrivateMembers = definition.Members.Any(static m => m.IsPrivate);
            return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope(realm) : null;
        }

        // ClassFieldDefinitionEvaluation evaluates computed field names during class evaluation,
        // so resolve all field keys eagerly in declaration order (static + instance).
        private static ImmutableArray<ClassField> ResolveFieldNames(
            ImmutableArray<ClassField> fields,
            JsEnvironment environment,
            EvaluationContext context,
            PrivateNameScope? privateNameScope)
        {
            if (fields.IsDefaultOrEmpty)
            {
                return fields;
            }

            var builder = ImmutableArray.CreateBuilder<ClassField>(fields.Length);
            foreach (var field in fields)
            {
                var propertyName = field.Name;
                if (!field.TryResolveFieldName(expr => expr.EvaluateExpression(environment, context),
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

        // Instance field keys must already be resolved; this simply returns
        // the provided collection (kept for clarity / future adjustments).
        private static ImmutableArray<ClassField> ResolveInstanceFieldNames(
            ImmutableArray<ClassField> fields,
            JsEnvironment environment,
            EvaluationContext context,
            PrivateNameScope? privateNameScope)
        {
            _ = environment;
            _ = context;
            _ = privateNameScope;
            return fields;
        }

        private static bool IsImplicitDefaultDerivedConstructor(FunctionExpression constructor)
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
}
