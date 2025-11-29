using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using Asynkron.JsEngine.Converters;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Parser;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;
using JetBrains.Annotations;

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
                    typedFunction.EnsureHasName(propertyName);
                }

                member.DefineMember(propertyName, callable, constructorAccessor, prototype);
            }
        }
    }

}
