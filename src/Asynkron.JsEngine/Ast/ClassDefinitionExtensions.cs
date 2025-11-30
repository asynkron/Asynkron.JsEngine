using System;
using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ClassDefinition definition)
    {
        private object? CreateClassValue(JsEnvironment environment,
            EvaluationContext context)
        {
            using var classScope = context.PushScope(ScopeKind.Block, ScopeMode.Strict, true);
            var (superConstructor, superPrototype) = ResolveSuperclass(definition.Extends, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            var constructorValue = EvaluateExpression(definition.Constructor, environment, context);
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
            if (superPrototype is not null)
            {
                prototype.SetPrototype(superPrototype);
            }

            var privateNameScope = CreatePrivateNameScope(definition);
            if (constructorValue is TypedFunction typedFunction)
            {
                typedFunction.SetSuperBinding(superConstructor, superPrototype);
                var instanceFields = definition.Fields.Where(field => !field.IsStatic).ToImmutableArray();
                typedFunction.SetInstanceFields(instanceFields);
                typedFunction.SetIsClassConstructor(superConstructor is not null);
                typedFunction.SetPrivateNameScope(privateNameScope);
                if (privateNameScope is not null)
                {
                    typedFunction.AddPrivateBrand(privateNameScope.BrandToken);
                }
            }

            if (superConstructor is not null)
            {
                constructorAccessor.SetProperty("__proto__", superConstructor);
                if (constructorAccessor is IJsObjectLike ctorObject)
                {
                    ctorObject.SetPrototype(superConstructor);
                }
            }
            else if (constructorAccessor is IJsObjectLike { Prototype: null } baseCtor &&
                     realm.FunctionPrototype is not null)
            {
                baseCtor.SetPrototype(realm.FunctionPrototype);
            }

            prototype.SetProperty("constructor", constructorValue);

            AssignClassMembers(definition.Members, constructorAccessor, prototype, superConstructor, superPrototype,
                environment, context, privateNameScope);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            InitializeStaticElements(definition, constructorAccessor, environment, context, privateNameScope);
            return context.ShouldStopEvaluation ? Symbol.Undefined : constructorValue;
        }

        private PrivateNameScope? CreatePrivateNameScope()
        {
            var hasPrivateFields = definition.Fields.Any(f => f.IsPrivate);
            var hasPrivateMembers = definition.Members.Any(m => m.Name.Length > 0 && m.Name[0] == '#');
            return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope() : null;
        }
    }

    private static JsEnvironment CreateClassScopeEnvironment(
        JsEnvironment parentEnvironment,
        Symbol className,
        SourceReference? source)
    {
        var classEnvironment = new JsEnvironment(parentEnvironment, false, true, source, "class scope");
        classEnvironment.Define(
            className,
            JsEnvironment.Uninitialized,
            isConst: true,
            isLexical: true,
            blocksFunctionScopeOverride: true);
        return classEnvironment;
    }

    private static void InitializeStaticElements(
        ClassDefinition definition,
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
                    var field = definition.Fields[element.Index];
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
