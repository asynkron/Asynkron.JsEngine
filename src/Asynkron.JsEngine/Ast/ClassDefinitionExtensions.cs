using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDefinition definition)
    {
        private object? CreateClassValue(JsEnvironment environment,
            EvaluationContext context,
            Symbol? className)
        {
            using var classScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, true);
            var (evaluationEnvironment, classScopeEnvironment) = CreateClassScopeIfNeeded(
                environment,
                className,
                definition.Source);

            var (superConstructor, superPrototype) = ResolveSuperclass(definition.Extends, evaluationEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var privateNameScope = CreatePrivateNameScope(definition);
            context.RealmState.Logger?.LogInformation(
                "Class evaluation start: name='{Name}', fields={FieldCount}, staticElements={StaticCount}, envStrict={EnvStrict}",
                className?.Name ?? "<anonymous>",
                definition.Fields.Length,
                definition.StaticElements.Length,
                evaluationEnvironment.IsStrict);
            var resolvedFields =
                ResolveFieldNames(definition, definition.Fields, evaluationEnvironment, context, privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var constructorValue = EvaluateExpression(definition.Constructor, evaluationEnvironment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (constructorValue is not IJsEnvironmentAwareCallable constructor ||
                constructorValue is not IJsPropertyAccessor constructorAccessor)
            {
                throw new InvalidOperationException("Class constructor must be callable.");
            }

            var realm = context.RealmState;
            var prototype = EnsurePrototype(constructorAccessor, realm);
            if (definition.Extends is not null)
            {
                prototype.SetPrototype(superPrototype);
            }
            if (constructorValue is TypedFunction typedCtorForOrdering)
            {
                typedCtorForOrdering.SeedIntrinsicConstructorKeys();
                typedCtorForOrdering.SetPrototypeObject(prototype);
            }
            else if (constructorAccessor is JsObject ctorForOrdering)
            {
                ctorForOrdering.SeedIntrinsicConstructorKeys();
            }

            if (constructorValue is TypedFunction typedFunction)
            {
                typedFunction.SetSuperBinding(superConstructor, superPrototype);
                var instanceFields = resolvedFields.Where(field => !field.IsStatic).ToImmutableArray();
                var resolvedInstanceFields =
                    ResolveInstanceFieldNames(instanceFields, evaluationEnvironment, context, privateNameScope);
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
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
                if (constructorAccessor is IJsObjectLike ctorObject)
                {
                    ctorObject.SetPrototype(superConstructor);
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

            prototype.SetProperty("constructor", constructorValue);

            if (constructorAccessor is IPropertyDefinitionHost definitionHost &&
                constructorValue is TypedFunction { IsClassConstructor: true })
            {
                definitionHost.TryDefineProperty("prototype", new PropertyDescriptor
                {
                    Value = prototype,
                    Writable = false,
                    Enumerable = false,
                    Configurable = false,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
            }

            AssignClassMembers(definition.Members, constructorAccessor, prototype, superConstructor, superPrototype,
                evaluationEnvironment, context, privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (constructorValue is IFunctionNameTarget nameTarget && className is not null)
            {
                nameTarget.EnsureHasName(className.Name);
            }

            // Per ES spec 15.7.14 ClassDefinitionEvaluation step 26:
            // Initialize the class name binding BEFORE evaluating static elements
            // so that static blocks can reference the class name
            if (classScopeEnvironment is not null && className is not null)
            {
                classScopeEnvironment.TryAssignBlockedBinding(className, constructorValue);
            }

            InitializeStaticElements(definition, resolvedFields, constructorAccessor, evaluationEnvironment, context,
                privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            return constructorValue;
        }

        private PrivateNameScope? CreatePrivateNameScope()
        {
            var hasPrivateFields = definition.Fields.Any(f => f.IsPrivate);
            var hasPrivateMembers = definition.Members.Any(m => m.IsPrivate);
            return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope() : null;
        }

        // ClassFieldDefinitionEvaluation evaluates computed field names during class evaluation,
        // so resolve all field keys eagerly in declaration order (static + instance).
        private ImmutableArray<ClassField> ResolveFieldNames(
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
                if (!field.TryResolveFieldName(expr => EvaluateExpression(expr, environment, context),
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

                builder.Add(field with
                {
                    Name = propertyName,
                    IsComputed = false,
                    ComputedName = null
                });
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
    }

    private static (JsEnvironment EvaluationEnvironment, JsEnvironment? ClassScope) CreateClassScopeIfNeeded(
        JsEnvironment environment,
        Symbol? className,
        SourceReference? source)
    {
        if (className is null)
        {
            return (environment, null);
        }

        var classScope = new JsEnvironment(environment, isStrict: true, creatingSource: source, description: "class scope");
        classScope.Define(className, JsEnvironment.Uninitialized, isConst: true, blocksFunctionScopeOverride: true);
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

        using var staticFieldScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, true);
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
        EvaluateStatement(block.Body, blockEnvironment, context);
    }
}
