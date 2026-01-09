
namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    private static JsValue EvaluateObject(this ObjectExpression expression, JsEnvironment environment,
        EvaluationContext context)
    {
        var obj = new JsObject();
        // Object literals should carry the current realm so errors/prototypes
        // resolve correctly when the object is used as an environment record.
        obj.RealmState = context.RealmState;
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
                        var name = member.ResolveObjectMemberName(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : member.Value.EvaluateExpression(environment, context);

                        if (!member.IsComputed &&
                            string.Equals(name, "__proto__", StringComparison.Ordinal) &&
                            member.Parameter is null)
                        {
                            if (valueJs.IsNull)
                            {
                                obj.SetPrototype(null);
                            }
                            else if (valueJs.TryGetObject<IJsPropertyAccessor>(out var protoAccessor))
                            {
                                obj.SetPrototype(protoAccessor);
                            }

                            // Per ES spec, __proto__: value with colon syntax sets the prototype,
                            // it does not create an own property. Shorthand { __proto__ } DOES
                            // create an own property (handled by DefineProperty below).
                            break;
                        }

                        if (valueJs.ObjectValue is IFunctionNameTarget nameTarget &&
                            member.Value is FunctionExpression { Name: null } or ClassExpression { Name: null })
                        {
                            var displayName = name.BuildFunctionNameDisplay();
                            nameTarget.EnsureHasName(displayName);
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Method:
                    {
                        var callable = member.Function!.CreateFunctionValue(environment, context,
                            false);
                        if (callable is SyncFunctionInvoker typed)
                        {
                            typed.SetHomeObject(obj);
                            typed.DisableConstruction();
                        }
                        else if (callable is SyncGeneratorInvoker generatorFactory)
                        {
                            generatorFactory.SetHomeObject(obj);
                            generatorFactory.DisableConstruction();
                        }
                        else if (callable is AsyncGeneratorFunctionInvoker asyncGeneratorFactory)
                        {
                            asyncGeneratorFactory.SetHomeObject(obj);
                            asyncGeneratorFactory.DisableConstruction();
                        }

                        var name = member.ResolveObjectMemberName(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        if (callable is IFunctionNameTarget nameTarget)
                        {
                            var displayName = name.BuildFunctionNameDisplay();
                            nameTarget.EnsureHasName(displayName);
                        }

                        obj.DefineProperty(name,
                            new PropertyDescriptor
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
                        // Mark environment as captured since the getter holds a reference to it
                        environment.Capture();
                        var getter = new SyncFunctionInvoker(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        getter.SetHomeObject(obj);
                        var name = member.ResolveObjectMemberName(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        getter.EnsureHasName($"get {name.BuildFunctionNameDisplay()}");

                        obj.DefineAccessorProperty(name, getter, null);
                        break;
                    }
                case ObjectMemberKind.Setter:
                    {
                        // Mark environment as captured since the setter holds a reference to it
                        environment.Capture();
                        var setter = new SyncFunctionInvoker(
                            member.Function!,
                            environment,
                            context.RealmState,
                            context.CurrentScope.IsStrict,
                            isConstructorFunction: false);
                        setter.SetHomeObject(obj);
                        var name = member.ResolveObjectMemberName(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        setter.EnsureHasName($"set {name.BuildFunctionNameDisplay()}");

                        obj.DefineAccessorProperty(name, null, setter);
                        break;
                    }
                case ObjectMemberKind.Field:
                    {
                        var name = member.ResolveObjectMemberName(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        var valueJs = member.Value is null
                            ? JsValue.Undefined
                            : member.Value.EvaluateExpression(environment, context);
                        obj.DefineProperty(name,
                            new PropertyDescriptor
                            {
                                JsValue = valueJs,
                                Writable = true,
                                Enumerable = true,
                                Configurable = true
                            });
                        break;
                    }
                case ObjectMemberKind.Spread:
                    {
                        var spreadValueJs = member.Value!.EvaluateExpression(environment, context);
                        if (context.ShouldStopEvaluation)
                        {
                            return JsValue.Undefined;
                        }

                        // Skip null/undefined per ES spec
                        if (spreadValueJs.IsNullOrUndefined)
                        {
                            break;
                        }

                        // Check for document.all-like objects
                        if (spreadValueJs.ObjectValue is IIsHtmlDda)
                        {
                            break;
                        }

                        // Object spread uses CopyDataProperties (ECMA-262 PropertyDefinitionEvaluation),
                        // which skips null/undefined and copies enumerable own keys in [[OwnPropertyKeys]] order.
                        if (spreadValueJs.ObjectValue is IDictionary<string, object?> dictionary and not JsObject)
                        {
                            foreach (var kvp in dictionary)
                            {
                                obj.DefineProperty(kvp.Key,
                                    new PropertyDescriptor
                                    {
                                        Value = kvp.Value,
                                        Writable = true,
                                        Enumerable = true,
                                        Configurable = true
                                    });
                            }

                            break;
                        }

                        var accessor = spreadValueJs.ObjectValue is IJsPropertyAccessor propertyAccessor
                            ? propertyAccessor
                            : ToObjectForDestructuringJsValue(spreadValueJs, context);

                        foreach (var key in accessor.GetEnumerableOwnPropertyKeysInOrder())
                        {
                            var spreadPropertyValue = accessor.TryGetProperty(key, out var val)
                                ? val
                                : JsValue.Undefined;
                            obj.DefineProperty(key,
                                new PropertyDescriptor
                                {
                                    JsValue = spreadPropertyValue,
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

    private static string BuildFunctionNameDisplay(this string propertyName)
    {
        if (JsSymbol.TryGetByInternalKey(propertyName, out var symbol))
        {
            return symbol!.Description is null ? string.Empty : $"[{symbol.Description}]";
        }

        return propertyName;
    }
}
