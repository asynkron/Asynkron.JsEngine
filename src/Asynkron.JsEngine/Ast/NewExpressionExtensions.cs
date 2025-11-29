using System.Collections.Immutable;
using Asynkron.JsEngine.JsTypes;

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
                    : new InvalidOperationException(hostFunction.ConstructErrorMessage ?? "Target is not a constructor.");
                throw new ThrowSignal(error);
            }

            if (constructor is TypedFunction { IsArrowFunction: true })
            {
                var error = realm.TypeErrorConstructor is IJsCallable typeErrorCtor
                    ? typeErrorCtor.Invoke(["Target is not a constructor"], null)
                    : new InvalidOperationException("Target is not a constructor.");
                throw new ThrowSignal(error);
            }

            var instance = new JsObject();
            if (TryGetPropertyValue(constructor, "prototype", out var prototype) && prototype is JsObject proto)
            {
                instance.SetPrototype(proto);
            }

            InitializeClassInstance(constructor, instance, environment, context);
            if (context.ShouldStopEvaluation)
            {
                return Symbol.Undefined;
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
            try
            {
                if (callable is TypedFunction typedFunction)
                {
                    result = typedFunction.InvokeWithContext(args.MoveToImmutable(), instance, context,
                        newTarget: constructor);
                }
                else
                {
                    result = callable.Invoke(args.MoveToImmutable(), instance);
                }
            }
            catch (ThrowSignal signal)
            {
                context.SetThrow(signal.ThrownValue);
                return signal.ThrownValue;
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
                _ => instance
            };
        }
    }

}
