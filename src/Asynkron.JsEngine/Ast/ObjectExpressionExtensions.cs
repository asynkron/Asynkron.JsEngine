using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    extension(ObjectExpression expression)
    {
        private JsValue EvaluateObject(JsEnvironment environment,
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
                            return JsValue.Undefined;
                        }

                        var value = member.Value is null
                            ? Symbol.Undefined
                            : EvaluateExpression(member.Value, environment, context).ToObject();

                        if (!member.IsComputed &&
                            string.Equals(name, "__proto__", StringComparison.Ordinal) &&
                            member.Parameter is null)
                        {
                            if (value is null)
                            {
                                obj.SetPrototype(value);
                            }
                            else if (value is IJsObjectLike || value is IPrototypeAccessorProvider)
                            {
                                obj.SetPrototype(value);
                            }

                            // Per ES spec, __proto__: value with colon syntax sets the prototype,
                            // it does not create an own property. Shorthand { __proto__ } DOES
                            // create an own property (handled by DefineProperty below).
                            break;
                        }

                        if (value is IFunctionNameTarget nameTarget &&
                            member.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                        {
                            var displayName = BuildFunctionNameDisplay(name);
                            nameTarget.EnsureHasName(displayName);
                        }

                        obj.DefineProperty(name, new PropertyDescriptor
                        {
                            Value = value,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                        break;
                }
                    case ObjectMemberKind.Method:
                    {
                        var callable = CreateFunctionValue(member.Function!, environment, context,
                            isConstructorFunction: false);
                        if (callable is TypedFunction typed)
                        {
                            typed.SetHomeObject(obj);
                            typed.DisableConstruction();
                        }
                        else if (callable is TypedGeneratorFactory generatorFactory)
                        {
                            generatorFactory.SetHomeObject(obj);
                            generatorFactory.DisableConstruction();
                        }
                        else if (callable is AsyncGeneratorFactory asyncGeneratorFactory)
                        {
                            asyncGeneratorFactory.SetHomeObject(obj);
                            asyncGeneratorFactory.DisableConstruction();
                        }

                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (callable is IFunctionNameTarget nameTarget)
                        {
                            var displayName = BuildFunctionNameDisplay(name);
                            nameTarget.EnsureHasName(displayName);
                        }

                        obj.DefineProperty(name, new PropertyDescriptor
                        {
                            Value = callable,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                        break;
                    }
                    case ObjectMemberKind.Getter:
                    {
                        var getter = new TypedFunction(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        getter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        getter.EnsureHasName($"get {BuildFunctionNameDisplay(name)}");

                        DefineAccessorProperty(obj, name, getter, null);
                        break;
                    }
                    case ObjectMemberKind.Setter:
                    {
                        var setter = new TypedFunction(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        setter.SetHomeObject(obj);
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        setter.EnsureHasName($"set {BuildFunctionNameDisplay(name)}");

                        DefineAccessorProperty(obj, name, null, setter);
                        break;
                    }
                    case ObjectMemberKind.Field:
                    {
                        var name = ResolveObjectMemberName(member, environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var value = member.Value is null
                            ? Symbol.Undefined
                            : EvaluateExpression(member.Value, environment, context).ToObject();
                        obj.DefineProperty(name, new PropertyDescriptor
                        {
                            Value = value,
                            Writable = true,
                            Enumerable = true,
                            Configurable = true
                        });
                        break;
                    }
                    case ObjectMemberKind.Spread:
                    {
                        var spreadValue = EvaluateExpression(member.Value!, environment, context).ToObject();
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
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
                                obj.DefineProperty(kvp.Key, new PropertyDescriptor
                                {
                                    Value = kvp.Value,
                                    Writable = true,
                                    Enumerable = true,
                                    Configurable = true
                                });
                            }

                            break;
                        }

                        var accessor = spreadValue is IJsPropertyAccessor propertyAccessor
                            ? propertyAccessor
                            : ToObjectForDestructuring(spreadValue, context);

                        foreach (var key in GetEnumerableOwnPropertyKeysInOrder(accessor))
                        {
                            var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                                ? val.ToObject()
                                : Symbol.Undefined;
                            obj.DefineProperty(key, new PropertyDescriptor
                            {
                                Value = spreadPropertyValue,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        }

                        break;
                    }
                }
            }

            return new JsValue(obj);
        }

        private static string BuildFunctionNameDisplay(string propertyName)
        {
            if (TypedAstSymbol.TryGetByInternalKey(propertyName, out var symbol))
            {
                return symbol.Description is null ? string.Empty : $"[{symbol.Description}]";
            }

            return propertyName;
        }
    }
}
