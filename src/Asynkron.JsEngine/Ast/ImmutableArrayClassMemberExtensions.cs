using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ImmutableArray<ClassMember> members)
    {
        private void AssignClassMembers(IJsPropertyAccessor constructorAccessor,
            JsObject prototype, IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype,
            JsEnvironment environment, EvaluationContext context, PrivateNameScope? privateNameScope)
        {
            foreach (var member in members)
            {
                if (!member.TryResolveMemberName(expr => EvaluateExpression(expr, environment, context).ToObject(),
                        context,
                        privateNameScope,
                        out var propertyName))
                {
                    return;
                }

                context.RealmState.Logger?.LogInformation(
                    "Defining class member name='{Name}' isStatic={IsStatic} isPrivate={IsPrivate} isAsync={IsAsync} wasAsync={WasAsync} kind={Kind}",
                    propertyName,
                    member.IsStatic,
                    member.IsPrivate,
                    member.Function.IsAsync,
                    member.Function.WasAsync,
                    member.Kind);

                var baseDisplayName = member.IsPrivate ? member.Name : propertyName;
                var displayName = member.Kind switch
                {
                    ClassMemberKind.Getter => $"get {BuildFunctionNameDisplay(baseDisplayName)}",
                    ClassMemberKind.Setter => $"set {BuildFunctionNameDisplay(baseDisplayName)}",
                    _ => BuildFunctionNameDisplay(baseDisplayName)
                };

                if (member.IsStatic &&
                    string.Equals(propertyName, "prototype", StringComparison.Ordinal))
                {
                    context.SetThrow(JsValue.FromObjectUnsafe(StandardLibrary.CreateTypeError(
                        "Cannot redefine constructor prototype via static member",
                        context)));
                    return;
                }

                var value = member.Function is { } functionExpression
                    ? CreateFunctionValue(functionExpression, environment, context,
                        createFunctionNameEnvironment: true,
                        isConstructorFunction: false)
                    : EvaluateExpression(member.Function, environment, context).ToObject();
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

                    typedFunction.DisableConstruction();
                    typedFunction.EnsureHasName(displayName, overwriteExisting: true);
                }
                else if (value is TypedGeneratorFactory generatorFactory)
                {
                    generatorFactory.SetPrivateNameScope(privateNameScope);
                    if (homeObject is not null)
                    {
                        generatorFactory.SetHomeObject(homeObject);
                    }

                    // Class methods are non-constructors, even for generator forms.
                    generatorFactory.DisableConstruction();
                    generatorFactory.EnsureHasName(displayName, overwriteExisting: true);
                }
                else if (value is AsyncGeneratorFactory asyncGeneratorFactory)
                {
                    asyncGeneratorFactory.SetPrivateNameScope(privateNameScope);
                    if (homeObject is not null)
                    {
                        asyncGeneratorFactory.SetHomeObject(homeObject);
                    }

                    asyncGeneratorFactory.DisableConstruction();
                    asyncGeneratorFactory.EnsureHasName(displayName, overwriteExisting: true);
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
