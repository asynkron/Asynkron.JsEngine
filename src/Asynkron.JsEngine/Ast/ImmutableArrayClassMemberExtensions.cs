using System.Collections.Immutable;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ImmutableArray<ClassMember> members)
    {
        private void AssignClassMembers(IJsPropertyAccessor constructorAccessor,
            JsObject prototype, IJsEnvironmentAwareCallable? superConstructor, JsObject? superPrototype,
            JsEnvironment environment, EvaluationContext context, PrivateNameScope? privateNameScope)
        {
            foreach (var member in members)
            {
                if (!member.TryResolveMemberName(expr => EvaluateExpression(expr, environment, context),
                        context,
                        privateNameScope,
                        out var propertyName))
                {
                    return;
                }

                var displayName = propertyName;
                if (member.Name.IsPrivateName())
                {
                    displayName = member.Name;
                }

                if (member.IsStatic &&
                    string.Equals(propertyName, "prototype", StringComparison.Ordinal))
                {
                    context.SetThrow(StandardLibrary.CreateTypeError(
                        "Cannot redefine constructor prototype via static member",
                        context));
                    return;
                }

                var value = EvaluateExpression(member.Function, environment, context);
                if (context.ShouldStopEvaluation)
                {
                    return;
                }

                if (value is not IJsCallable callable)
                {
                    throw new InvalidOperationException("Class member must be callable.");
                }

                var homeObject = member.IsStatic
                    ? constructorAccessor as IJsObjectLike
                    : prototype;
                var superTarget = member.IsStatic
                    ? superConstructor as IJsPropertyAccessor
                    : superPrototype;
                if (value is TypedFunction typedFunction)
                {
                    typedFunction.SetPrivateNameScope(privateNameScope);
                    typedFunction.SetSuperBinding(superConstructor, superTarget);
                    if (homeObject is not null)
                    {
                        typedFunction.SetHomeObject(homeObject);
                    }

                    typedFunction.EnsureHasName(displayName);
                }
                else if (value is IFunctionNameTarget nameTarget)
                {
                    nameTarget.EnsureHasName(displayName);
                }

                member.DefineMember(propertyName, callable, constructorAccessor, prototype);
            }
        }
    }
}
