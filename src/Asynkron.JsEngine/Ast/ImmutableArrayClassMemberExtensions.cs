#region

using System.Collections.Immutable;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.Execution.Instructions;
using Asynkron.JsEngine.StdLib;
using Microsoft.Extensions.Logging;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static void AssignClassMembers(this ImmutableArray<LoweredClassMember> members,
        ImmutableArray<ExpressionProgram?> memberNamePrograms,
        IJsPropertyAccessor constructorAccessor,
        JsObject prototype, IJsEnvironmentAwareCallable? superConstructor, IJsPropertyAccessor? superPrototype,
        JsEnvironment environment, EvaluationContext context, PrivateNameScope? privateNameScope)
    {
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            if (!member.TryResolveMemberName(
                    memberNamePrograms.IsDefaultOrEmpty ? null : memberNamePrograms[index],
                    environment,
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
                member.Callable.Function.IsAsync,
                member.Callable.Function.WasAsync,
                member.Kind);

            var baseDisplayName = member.IsPrivate ? member.Name : propertyName;
            var displayName = member.Kind switch
            {
                ClassMemberKind.Getter => $"get {baseDisplayName.BuildFunctionNameDisplay()}",
                ClassMemberKind.Setter => $"set {baseDisplayName.BuildFunctionNameDisplay()}",
                _ => baseDisplayName.BuildFunctionNameDisplay()
            };

            if (member.IsStatic &&
                string.Equals(propertyName, "prototype", StringComparison.Ordinal))
            {
                context.SetThrow(StandardLibrary.CreateTypeError(
                    "Cannot redefine constructor prototype via static member",
                    context));
                return;
            }

            // Get value as JsValue and extract callable
            // Class methods are non-constructors, so pass isConstructorFunction: false
            var valueJs = JsValue.FromObjectUnsafe(member.Callable.Function.CreateFunctionValue(
                environment,
                context,
                false,
                false,
                ResolveClassCallablePlanSeed(member.Callable, environment)));
            if (context.ShouldStopEvaluation)
            {
                return;
            }

            if (!valueJs.TryGetObject<IJsCallable>(out var callable))
            {
                throw new InvalidOperationException("Class member must be callable.");
            }

            var homeObject = member.IsStatic
                ? constructorAccessor as IJsObjectLike
                : prototype;
            var superTarget = member.IsStatic
                ? superConstructor as IJsPropertyAccessor
                : superPrototype;

            // Pattern match on callable to configure the function
            switch (callable)
            {
                case SyncFunctionInvoker typedFunction:
                    typedFunction.SetPrivateNameScope(privateNameScope);
                    typedFunction.SetSuperBinding(superConstructor, superTarget);
                    if (homeObject is not null)
                    {
                        typedFunction.SetHomeObject(homeObject);
                    }

                    typedFunction.DisableConstruction();
                    typedFunction.EnsureHasName(displayName, true);
                    break;
                case SyncGeneratorInvoker generatorFactory:
                    generatorFactory.SetPrivateNameScope(privateNameScope);
                    if (homeObject is not null)
                    {
                        generatorFactory.SetHomeObject(homeObject);
                    }

                    // Class methods are non-constructors, even for generator forms.
                    generatorFactory.DisableConstruction();
                    generatorFactory.EnsureHasName(displayName, true);
                    break;
                case AsyncGeneratorFunctionInvoker asyncGeneratorFactory:
                    asyncGeneratorFactory.SetPrivateNameScope(privateNameScope);
                    if (homeObject is not null)
                    {
                        asyncGeneratorFactory.SetHomeObject(homeObject);
                    }

                    asyncGeneratorFactory.DisableConstruction();
                    asyncGeneratorFactory.EnsureHasName(displayName, true);
                    break;
                case IFunctionNameTarget nameTarget:
                    nameTarget.EnsureHasName(displayName);
                    break;
            }

            member.DefineMember(propertyName, callable, constructorAccessor, prototype);
        }
    }
}
