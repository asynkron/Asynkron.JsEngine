using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.StdLib;
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

            // ArgumentListEvaluation must run before the IsConstructor check (ES2024 12.3.3.1.1 step 6-7).
            var argsBuilder = ImmutableArray.CreateBuilder<object?>(expression.Arguments.Length);
            foreach (var argument in expression.Arguments)
            {
                if (argument.IsSpread)
                {
                    var spreadValue = EvaluateExpression(argument.Expression, environment, context);
                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    foreach (var item in EnumerateSpread(spreadValue, context))
                    {
                        argsBuilder.Add(item);
                    }

                    if (context.ShouldStopEvaluation)
                    {
                        return Symbol.Undefined;
                    }

                    continue;
                }

                argsBuilder.Add(EvaluateExpression(argument.Expression, environment, context));
                if (context.ShouldStopEvaluation)
                {
                    return Symbol.Undefined;
                }
            }

            var args = argsBuilder.ToImmutable();

            if (constructor is not IJsCallable callable)
            {
                var notCtor = StandardLibrary.CreateTypeError("Target is not a constructor", context, realm);
                throw new ThrowSignal(notCtor);
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
                if (typedConstructor is not null)
                {
                    var protoObject = typedConstructor.GetOrCreatePrototypeObject();
                    instance.SetPrototype(protoObject);
                    logger?.LogInformation("new: pre-call prototype set hash={Hash} derived={Derived}",
                        RuntimeHelpers.GetHashCode(protoObject),
                        isDerivedClassCtor);
                }
                else if (TryGetPropertyValue(constructor, "prototype", out var prototype) &&
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

            object? result;
            instance?.BeginConstruction();
            try
            {
                object? receiver = isDerivedClassCtor ? Symbol.Undefined : instance;
                if (typedConstructor is not null)
                {
                    result = typedConstructor.InvokeWithContext(args, receiver, context,
                        constructor);
                }
                else if (callable is HostFunction hostFn)
                {
                    result = hostFn.InvokeWithContext(args, receiver, context,
                        constructor);
                }
                else
                {
                    result = callable.Invoke(args, receiver);
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

            // Ensure the instance prototype matches the constructor's current prototype
            // (non-derived classes). This guards against earlier prototype lookup
            // failures that could leave the instance with a null/incorrect [[Prototype]].
            if (!isDerivedClassCtor && instance is not null)
            {
                if (typedConstructor is not null)
                {
                    var finalProto = typedConstructor.GetOrCreatePrototypeObject();
                    if (!ReferenceEquals(instance.PrototypeAccessor, finalProto))
                    {
                        instance.SetPrototype(finalProto);
                    }

                    logger?.LogInformation(
                        "new: final prototype set hash={Hash} derived={Derived}",
                        RuntimeHelpers.GetHashCode(finalProto),
                        isDerivedClassCtor);
                }
                else if (TryGetPropertyValue(constructor, "prototype", out var finalPrototype, context) &&
                         finalPrototype is IJsPropertyAccessor finalProtoAccessor)
                {
                    if (!ReferenceEquals(instance.PrototypeAccessor, finalProtoAccessor))
                    {
                        instance.SetPrototype(finalProtoAccessor);
                    }

                    logger?.LogInformation(
                        "new: final prototype set hash={Hash} derived={Derived}",
                        RuntimeHelpers.GetHashCode(finalProtoAccessor),
                        isDerivedClassCtor);
                }
                else
                {
                    logger?.LogInformation("new: final prototype missing derived={Derived}", isDerivedClassCtor);
                }
            }

            // In JavaScript, constructors can explicitly return an object to override the
            // default instance that `new` creates. Our host objects (Map, Set, custom
            // host functions, etc.) don't necessarily derive from JsObject, but they do
            // expose their members through IJsPropertyAccessor/IJsCallable. Treat any
            // such object-like result as the constructed value; otherwise fall back to
            // the auto-created instance.
            var constructedResult = result switch
            {
                IJsPropertyAccessor => result,
                IJsCallable => result,
                _ => instance ?? result
            };

            // If the constructor did not supply its own object, ensure the returned
            // instance carries the constructor's current prototype object.
            if (!isDerivedClassCtor &&
                typedConstructor is not null &&
                constructedResult is JsObject constructedJsObj &&
                ReferenceEquals(constructedJsObj, instance))
            {
                var ctorProto = typedConstructor.GetOrCreatePrototypeObject();
                if (!ReferenceEquals(constructedJsObj.PrototypeAccessor, ctorProto))
                {
                    constructedJsObj.SetPrototype(ctorProto);
                }
            }

            return constructedResult;
        }
    }
}
