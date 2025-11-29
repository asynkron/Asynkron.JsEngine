using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{

extension(ObjectExpression expression)
    {
        private object? EvaluateObject(JsEnvironment environment,
            EvaluationContext context)
        {
            var obj = new JsObject();
            if (context.RealmState.ObjectPrototype is { } objectProto)
            {
                obj.SetPrototype(objectProto);
            }

            foreach (var member in expression.Members)
            {
                switch (member.Kind)
                {
                    case ObjectMemberKind.Property:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        var value = member.Value is null
                            ? Symbol.Undefined
                            : EvaluateExpression(member.Value, environment, context);
                        obj.SetProperty(name, value);
                        break;
                    }
                    case ObjectMemberKind.Method:
                    {
                        var callable = CreateFunctionValue(member.Function!, environment, context);
                        if (callable is TypedFunction typed)
                        {
                            typed.SetHomeObject(obj);
                        }
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        obj.SetProperty(name, callable);
                        break;
                    }
                    case ObjectMemberKind.Getter:
                    {
                        var getter = new TypedFunction(member.Function!, environment, context.RealmState);
                        getter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        DefineAccessorProperty(obj, name, getter, null);
                        break;
                    }
                    case ObjectMemberKind.Setter:
                    {
                        var setter = new TypedFunction(member.Function!, environment, context.RealmState);
                        setter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        DefineAccessorProperty(obj, name, null, setter);
                        break;
                    }
                    case ObjectMemberKind.Field:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        var value = member.Value is null
                            ? Symbol.Undefined
                            : EvaluateExpression(member.Value, environment, context);
                        obj.SetProperty(name, value);
                        break;
                    }
                    case ObjectMemberKind.Spread:
                    {
                        var spreadValue = EvaluateExpression(member.Value!, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return Symbol.Undefined;
                        }

                        if (IsNullish(spreadValue) || spreadValue is IIsHtmlDda)
                        {
                            break;
                        }

                        // Object spread uses CopyDataProperties (ECMA-262 PropertyDefinitionEvaluation),
                        // which skips null/undefined and copies enumerable own keys in [[OwnPropertyKeys]] order.
                        if (spreadValue is IDictionary<string, object?> dictionary and not JsObject)
                        {
                            foreach (var kvp in dictionary)
                            {
                                obj.SetProperty(kvp.Key, kvp.Value);
                            }

                            break;
                        }

                        var accessor = spreadValue is IJsPropertyAccessor propertyAccessor
                            ? propertyAccessor
                            : ToObjectForDestructuring(spreadValue, context);

                        foreach (var key in GetEnumerableOwnPropertyKeysInOrder(accessor))
                        {
                            var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                                ? val
                                : Symbol.Undefined;
                            obj.SetProperty(key, spreadPropertyValue);
                        }

                        break;
                    }
                }
            }

            return obj;
        }
    }

}
