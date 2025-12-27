#region

using System.Collections.Immutable;
using System.Globalization;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Asynkron.JsEngine.StdLib;

#endregion

namespace Asynkron.JsEngine.Ast;

public static partial class TypedAstEvaluator
{
    /// <summary>
    /// Base class for generator-like function callables (function* and async function*).
    /// Contains all shared infrastructure: property management, call/apply/bind, private slots, etc.
    /// Subclasses only need to implement Invoke() and intrinsic setup.
    /// </summary>
    private abstract class GeneratorFunctionBase : IJsCallable, IJsObjectLike, IPropertyDefinitionHost,
        IExtensibilityControl,
        IFunctionNameTarget, ICallableMetadata
    {
        private protected readonly JsEnvironment _closure;
        private protected readonly FunctionExpression _function;
        private protected readonly bool _hasFunctionNameEnvironment;
        private protected readonly bool _isLexicallyStrict;
        private readonly Dictionary<string, JsValue> _privateSlots = new(StringComparer.Ordinal);
        private protected readonly JsObject _properties = new();
        private protected ImmutableArray<PrivateNameScope> _capturedPrivateNameScopes = ImmutableArray<PrivateNameScope>.Empty;
        private protected IJsObjectLike? _homeObject;
        private protected bool _isConstructorEnabled;

        protected GeneratorFunctionBase(
            FunctionExpression function,
            JsEnvironment closure,
            RealmState realmState,
            bool isLexicallyStrict,
            bool hasFunctionNameEnvironment,
            bool isConstructorFunction)
        {
            _function = function;
            _closure = closure;
            RealmState = realmState;
            _isLexicallyStrict = isLexicallyStrict;
            _hasFunctionNameEnvironment = hasFunctionNameEnvironment;
            _isConstructorEnabled = isConstructorFunction;
        }

        public PrivateNameScope? PrivateNameScope { get; private set; }

        public bool IsArrowFunction => false;
        public bool DisallowConstruct => true;
        public RealmState RealmState { get; }

        public bool IsExtensible => _properties.IsExtensible;

        public void PreventExtensions()
        {
            _properties.PreventExtensions();
        }

        public void EnsureHasName(string name, bool overwriteExisting = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            if (!overwriteExisting && _function.Name is not null)
            {
                return;
            }

            var descriptor = _properties.GetOwnPropertyDescriptor("name");
            if (descriptor is { Configurable: false })
            {
                return;
            }

            if (!overwriteExisting && descriptor is not null)
            {
                if (descriptor.IsAccessorDescriptor || descriptor.JsValue.TryGetObject<IJsCallable>(out _))
                {
                    return;
                }

                if (descriptor.JsValue.TryGetString(out var existingName) && existingName.Length > 0)
                {
                    return;
                }
            }

            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    JsValue = new JsValue(name),
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }

        /// <summary>
        /// Invokes this generator function with the given arguments.
        /// Subclasses implement this to return the appropriate iterator/promise.
        /// </summary>
        public abstract JsValue Invoke(IReadOnlyList<JsValue> arguments, JsValue thisValue);

        public JsObject? Prototype => _properties.Prototype;

        public bool IsSealed => _properties.IsSealed;
        public bool IsFrozen => _properties.IsFrozen;

        public IEnumerable<string> Keys => _properties.Keys;

        public void DefineProperty(string name, PropertyDescriptor descriptor)
        {
            _properties.DefineProperty(name, descriptor);
        }

        public void SetPrototype(IJsPropertyAccessor? candidate)
        {
            _properties.SetPrototype(candidate);
        }

        public void Seal()
        {
            _properties.Seal();
        }

        public bool Delete(string name)
        {
            return _properties.DeleteOwnProperty(name);
        }

        public bool TryGetProperty(string name, JsValue receiver, out JsValue value)
        {
            if (name.IsPrivateSlotName())
            {
                if (_privateSlots.TryGetValue(name, out value))
                {
                    return true;
                }
            }

            // Handle call/apply/bind specially BEFORE looking them up in prototype chain
            // This ensures generator functions get proper constructor semantics for bound functions
            var callable = (IJsCallable)this;
            switch (name)
            {
                case "call":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        var callArgs = args.SliceFrom(1);
                        return callable.Invoke(callArgs, thisArg);
                    });
                    return true;

                case "apply":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var thisArg = args.GetArgument(0);
                        IReadOnlyList<JsValue> argList = ArgumentSlice.Empty;
                        if (args.Count > 1 && args[1].TryUnwrap(out JsArray? jsArray))
                        {
                            // items[i] is already JsValue from JsArray.Items
                            var items = jsArray.Items;
                            var converted = new JsValue[items.Count];
                            for (var i = 0; i < items.Count; i++)
                            {
                                converted[i] = items[i];
                            }

                            argList = converted;
                        }

                        return callable.Invoke(argList, thisArg);
                    });
                    return true;

                case "bind":
                    value = (JsValue)new HostFunction((_, args) =>
                    {
                        var boundThis = args.GetArgument(0);
                        var boundArgs = args.SliceFrom(1);

                        // Generator functions are never constructors, so bound generator functions
                        // must also have DisallowConstruct = true per ES spec.
                        return (JsValue)new HostFunction((_, innerArgs) =>
                        {
                            if (boundArgs.Count == 0)
                            {
                                return callable.Invoke(innerArgs, boundThis);
                            }

                            if (innerArgs.Count == 0)
                            {
                                return callable.Invoke(boundArgs, boundThis);
                            }

                            var finalArgs = new JsValue[boundArgs.Count + innerArgs.Count];
                            for (var i = 0; i < boundArgs.Count; i++)
                            {
                                finalArgs[i] = boundArgs[i];
                            }

                            for (var i = 0; i < innerArgs.Count; i++)
                            {
                                finalArgs[boundArgs.Count + i] = innerArgs[i];
                            }

                            return callable.Invoke(finalArgs, boundThis);
                        }, RealmState, false) { DisallowConstruct = true };
                    });
                    return true;
            }

            // Fall back to properties lookup for all other properties
            var receiverValue = receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver;
            if (_properties.TryGetProperty(name, receiverValue, out var objValue))
            {
                value = objValue;
                return true;
            }

            value = JsValue.Undefined;
            return false;
        }

        public bool TryGetProperty(string name, out JsValue value)
        {
            return TryGetProperty(name, JsValue.FromObjectUnsafe(this), out value);
        }


        public void SetProperty(string name, JsValue value)
        {
            SetProperty(name, value, JsValue.FromObjectUnsafe(this));
        }


        public void SetProperty(string name, JsValue value, JsValue receiver)
        {
            if (name.IsPrivateSlotName())
            {
                _privateSlots[name] = value;
                return;
            }

            var receiverValue = receiver.IsUndefined ? JsValue.FromObjectUnsafe(this) : receiver;
            _properties.SetProperty(name, value, receiverValue);
        }

        PropertyDescriptor? IJsPropertyAccessor.GetOwnPropertyDescriptor(string name)
        {
            var descriptor = _properties.GetOwnPropertyDescriptor(name);
            if (descriptor is not null && string.Equals(name, "name", StringComparison.Ordinal))
            {
                descriptor.Writable = false;
                descriptor.Enumerable = false;
                descriptor.Configurable = true;
            }

            return descriptor;
        }

        public IEnumerable<string> GetOwnPropertyNames()
        {
            return _properties.GetOwnPropertyNames();
        }

        public IEnumerable<string> GetEnumerablePropertyNames()
        {
            return _properties.GetEnumerablePropertyNames();
        }

        public bool TryDefineProperty(string name, PropertyDescriptor descriptor)
        {
            return _properties.TryDefineProperty(name, descriptor);
        }

        public void SetPrivateNameScope(PrivateNameScope? scope)
        {
            PrivateNameScope = scope;
        }

        public void SetCapturedPrivateNameScopes(ImmutableArray<PrivateNameScope> scopes)
        {
            _capturedPrivateNameScopes = scopes;
        }

        public void DisableConstruction()
        {
            if (!_isConstructorEnabled)
            {
                return;
            }

            _isConstructorEnabled = false;
            _properties.DeleteOwnProperty("prototype");
        }

        public void SetHomeObject(IJsObjectLike homeObject)
        {
            _homeObject = homeObject;
        }

        /// <summary>
        /// Returns the function description string for ToString().
        /// </summary>
        protected abstract string FunctionTypeName { get; }

        public override string ToString()
        {
            return _function.Name is { } name
                ? $"[{FunctionTypeName}: {name.Name}]"
                : $"[{FunctionTypeName}]";
        }

        /// <summary>
        /// Ensures the required intrinsics (prototypes, constructors) are initialized.
        /// Called during InitializeProperties.
        /// </summary>
        protected abstract void EnsureIntrinsics();

        /// <summary>
        /// Gets the function prototype to use for this callable's [[Prototype]].
        /// </summary>
        protected abstract IJsPropertyAccessor? GetFunctionPrototype();

        /// <summary>
        /// Gets the generator prototype to use for the .prototype property's [[Prototype]].
        /// </summary>
        protected abstract JsObject? GetGeneratorPrototype();

        /// <summary>
        /// Called after the prototype object is created, allowing subclasses to add additional properties.
        /// Default implementation does nothing.
        /// </summary>
        protected virtual void CustomizePrototypeObject(JsObject prototypeObject)
        {
            // Default: no additional customization
        }

        /// <summary>
        /// Initializes the standard properties (prototype, length, name).
        /// </summary>
        protected void InitializeProperties()
        {
            EnsureIntrinsics();

            var funcProto = GetFunctionPrototype();
            if (funcProto is not null)
            {
                _properties.SetPrototype(funcProto);
            }
            else if (RealmState.FunctionPrototype is { } functionPrototype)
            {
                _properties.SetPrototype(functionPrototype);
            }

            // Per ES spec: Generator functions ALWAYS have a prototype property
            var genProto = GetGeneratorPrototype();
            if (genProto is not null)
            {
                var generatorPrototype = new JsObject();
                generatorPrototype.SetPrototype(genProto);
                CustomizePrototypeObject(generatorPrototype);
                _properties.DefineProperty("prototype",
                    new PropertyDescriptor
                    {
                        Value = generatorPrototype,
                        Writable = true,
                        Enumerable = false,
                        Configurable = false,
                        HasValue = true,
                        HasWritable = true,
                        HasEnumerable = true,
                        HasConfigurable = true
                    });
            }

            var paramCount = _function.Parameters.GetExpectedParameterCount();
            _properties.DefineProperty("length",
                new PropertyDescriptor
                {
                    Value = (double)paramCount,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });

            var functionNameValue = _function.Name?.Name ?? string.Empty;
            _properties.DefineProperty("name",
                new PropertyDescriptor
                {
                    Value = functionNameValue,
                    Writable = false,
                    Enumerable = false,
                    Configurable = true,
                    HasValue = true,
                    HasWritable = true,
                    HasEnumerable = true,
                    HasConfigurable = true
                });
        }

        /// <summary>
        /// Helper for converting a value to a function argument string (used by dynamic Function constructors).
        /// </summary>
        protected static string ToFunctionArgumentString(JsValue value, EvaluationContext evalContext, RealmState realm)
        {
            var primitiveObj = JsOps.ToPrimitive(value, ToPrimitiveHint.String, evalContext);
            if (evalContext.IsThrow)
            {
                throw new ThrowSignal(evalContext.FlowValue);
            }

            var primitive = primitiveObj;

            if (primitive.IsNull)
            {
                return "null";
            }

            if (primitive.IsUndefined)
            {
                return "undefined";
            }

            if (primitive.TryUnwrap(out Symbol? _) || primitive.TryUnwrap(out TypedAstSymbol? _))
            {
                throw StandardLibrary.ThrowTypeError("Cannot convert a Symbol value to a string", evalContext, realm);
            }

            if (primitive.TryGetBoolean(out var flag))
            {
                return flag ? "true" : "false";
            }

            if (primitive.TryGetString(out var s))
            {
                return s ?? string.Empty;
            }

            if (primitive.TryUnwrap(out JsBigInt? bigInt))
            {
                return bigInt.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (primitive.TryGetDouble(out var d))
            {
                if (double.IsNaN(d))
                {
                    return "NaN";
                }

                if (double.IsPositiveInfinity(d))
                {
                    return "Infinity";
                }

                if (double.IsNegativeInfinity(d))
                {
                    return "-Infinity";
                }

                return d.ToString(CultureInfo.InvariantCulture);
            }

            // At this point, all primitive types have been handled above;
            // remaining cases are objects, so use ObjectValue directly.
            return Convert.ToString(primitive.ObjectValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>
        /// Creates an ExecutionPlanRunner for this generator function.
        /// </summary>
        protected ExecutionPlanRunner CreateRunner(IReadOnlyList<JsValue> arguments, JsValue thisValue)
        {
            return new ExecutionPlanRunner(
                _function,
                _closure,
                arguments,
                thisValue,
                this,
                RealmState,
                _isLexicallyStrict,
                _hasFunctionNameEnvironment,
                _homeObject,
                PrivateNameScope,
                _capturedPrivateNameScopes);
        }
    }
}
