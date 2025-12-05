using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(NewExpression expression)
    {
        private object? EvaluateNew(JsEnvironment environment, EvaluationContext context)
        {
            var realm = context.RealmState;
            var constructor = EvaluateExpression(expression.Constructor, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
            }

            if (constructor is not IJsCallable callable)
            {
                throw new InvalidOperationException("Attempted to construct a non-callable value.");
            }

            if (constructor is HostFunction hostFunction &&
                (!hostFunction.IsConstructor || hostFunction.DisallowConstruct))
            {
                var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke([hostFunction.ConstructErrorMessage ?? "is not a constructor"], null)
                    : new InvalidOperationException(
                        hostFunction.ConstructErrorMessage ?? "Target is not a constructor.");
                throw new ThrowSignal(error);
            }

            if (constructor is TypedFunction { IsArrowFunction: true })
            {
                var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke(["Target is not a constructor"], null)
                    : new InvalidOperationException("Target is not a constructor.");
                throw new ThrowSignal(error);
            }

            var typedConstructor = constructor as TypedFunction;
            var isDerivedClassCtor = typedConstructor?.IsDerivedClassConstructor == true;
            var logger = realm?.Logger;

            JsObject? instance = null;
            if (!isDerivedClassCtor)
            {
                instance = new JsObject();
                if (TryGetPropertyValue(constructor, "prototype", out var prototype) &&
                    prototype is IJsPropertyAccessor protoAccessor)
                {
                    instance.SetPrototype(protoAccessor);
                    logger?.LogInformation("new: pre-call prototype set hash={Hash} derived={Derived}",
                        RuntimeHelpers.GetHashCode(protoAccessor),
                        isDerivedClassCtor);
                }
                else
                {
                    logger?.LogInformation("new: pre-call prototype missing derived={Derived}", isDerivedClassCtor);
                }
            }

            var args = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
            foreach (var argument in expression.Arguments)
            {
                args.Add(EvaluateExpression(argument, environment, context));
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
            }

            object? result;
            instance?.BeginConstruction();
            try
            {
                object? receiver = isDerivedClassCtor ? Symbol.Undefined : instance;
                if (typedConstructor is not null)
                {
                    result = typedConstructor.InvokeWithContext(args.MoveToImmutable(), receiver, context,
                        constructor);
                }
                else if (callable is HostFunction hostFn)
                {
                    result = hostFn.InvokeWithContext(args.MoveToImmutable(), receiver, context,
                        constructor);
                }
                else
                {
                    result = callable.Invoke(args.MoveToImmutable(), receiver);
                }
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                return signal.ThrownValue;
            }
            finally
            {
                instance?.EndConstruction();
            }

            if (!isDerivedClassCtor &&
                instance is not null &&
                TryGetPropertyValue(constructor, "prototype", out var finalPrototype, context) &&
                finalPrototype is IJsPropertyAccessor finalProtoAccessor)
            {
                instance.SetPrototype(finalProtoAccessor);
                logger?.LogInformation(
                    "new: final prototype set hash={Hash} derived={Derived}",
                    RuntimeHelpers.GetHashCode(finalProtoAccessor),
                    isDerivedClassCtor);
            }
            else if (!isDerivedClassCtor && instance is not null)
            {
                logger?.LogInformation("new: final prototype missing derived={Derived}", isDerivedClassCtor);
            }

            // In JavaScript, constructors can explicitly return an object to override the
            // default instance that `new` creates. Our host objects (Map, Set, custom
            // host functions, etc.) don't necessarily derive from JsObject, but they do
            // expose their members through IJsPropertyAccessor/IJsCallable. Treat any
            // such object-like result as the constructed value; otherwise fall back to
            // the auto-created instance.
            return result switch
            {
                IJsPropertyAccessor => result,
                IJsCallable => result,
                _ => instance ?? result
            };
        }
    }
}
