using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

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

            var staticFields = definition.Fields.Where(field => field.IsStatic).ToImmutableArray();
            InitializeStaticFields(staticFields, constructorAccessor, environment, context, privateNameScope);
            return context.ShouldStopEvaluation ? Symbol.Undefined : constructorValue;
        }

        private PrivateNameScope? CreatePrivateNameScope()
        {
            var hasPrivateFields = definition.Fields.Any(f => f.IsPrivate);
            var hasPrivateMembers = definition.Members.Any(m => m.Name.Length > 0 && m.Name[0] == '#');
            return hasPrivateFields || hasPrivateMembers ? new PrivateNameScope() : null;
        }
    }
}
