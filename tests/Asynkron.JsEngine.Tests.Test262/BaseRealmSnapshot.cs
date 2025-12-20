using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Microsoft.Extensions.Logging;

namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Creates and caches a pre-initialized realm (stdlib only) that can be cloned per test.
/// Enabled by default; disable with JSENGINE_TEST262_BASE_REALM=0|false|off.
/// </summary>
internal sealed class BaseRealmSnapshot
{
#pragma warning disable SYSLIB0050 // FormatterServices is used in tests to clone realm snapshots.
    internal static readonly Lazy<BaseRealmSnapshot> Instance = new(CreateSnapshot);

    internal static bool UseSnapshot
    {
        get
        {
            var setting = Environment.GetEnvironmentVariable("JSENGINE_TEST262_BASE_REALM");
            if (string.IsNullOrWhiteSpace(setting))
            {
                return true;
            }

            return !string.Equals(setting, "0", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "false", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase);
        }
    }

    private readonly JsEngine _templateEngine;
    private readonly JsObject _templateGlobal;
    private readonly RealmState _templateRealm;
    private readonly HashSet<string> _excludedGlobals;

    private BaseRealmSnapshot(JsEngine templateEngine, HashSet<string> excludedGlobals)
    {
        _templateEngine = templateEngine;
        _templateGlobal = templateEngine.GlobalObject;
        _templateRealm = templateEngine.RealmState;
        _excludedGlobals = excludedGlobals;
    }

    private static BaseRealmSnapshot CreateSnapshot()
    {
        var engine = new JsEngine { ExecutionTimeout = null };

        // All globals are cloned; keep a list here if some future engine-bound
        // helper should be re-registered per test instead.
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        return new BaseRealmSnapshot(engine, excluded);
    }

    internal JsEngine CreateEngine(IJsEngineOptions? options = null)
    {
        var engine = new JsEngine(options, skipStdLibInitialization: true)
        {
            ExecutionTimeout = null
        };

        var cloner = new RealmCloner(_templateEngine, _templateGlobal, _templateRealm, _excludedGlobals);
        cloner.CloneInto(engine);
        return engine;
    }

    private sealed class RealmCloner
    {
        private static readonly FieldInfo? JsObjectStateField =
            typeof(JsObject).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? JsObjectDescriptorsField =
            typeof(JsObject).GetField("_descriptors", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? JsObjectPrivateBrandsField =
            typeof(JsObject).GetField("_privateBrands", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? JsObjectPrivateFieldsField =
            typeof(JsObject).GetField("_privateFields", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? JsObjectInsertionOrderField =
            typeof(JsObject).GetField("_propertyInsertionOrder", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo? JsObjectInsertionNodesField =
            typeof(JsObject).GetField("_propertyInsertionNodes", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo JsObjectTrackArrayLengthField =
            typeof(JsObject).GetField("_trackArrayLength", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectTrackedArrayLengthField =
            typeof(JsObject).GetField("_trackedArrayLength", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo? JsObjectPrototypeAccessorField =
            typeof(JsObject).GetField("_prototypeAccessor", BindingFlags.Instance | BindingFlags.NonPublic) ??
            typeof(JsObject).GetField("<PrototypeAccessor>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo JsObjectVirtualProviderField =
            typeof(JsObject).GetField("_virtualPropertyProvider", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectPrototypeBackingField =
            typeof(JsObject).GetField("<Prototype>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectIsFrozenField =
            typeof(JsObject).GetField("<IsFrozen>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectIsSealedField =
            typeof(JsObject).GetField("<IsSealed>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectIsExtensibleField =
            typeof(JsObject).GetField("<IsExtensible>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo JsObjectIsConstructingField =
            typeof(JsObject).GetField("<IsConstructing>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static FieldInfo? JsObjectStateStorageField;
        private static FieldInfo? JsObjectStateDescriptorsField;
        private static FieldInfo? JsObjectStatePrivateBrandsField;
        private static FieldInfo? JsObjectStatePrivateFieldsField;
        private static FieldInfo? JsObjectStateInsertionOrderField;
        private static FieldInfo? JsObjectStateInsertionNodesField;

        private static readonly FieldInfo HostFunctionHandlerField =
            typeof(HostFunction).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo HostFunctionInvokeWithContextField =
            typeof(HostFunction).GetField("_invokeWithContext", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo HostFunctionIsConstructorField =
            typeof(HostFunction).GetField("_isConstructor", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo HostFunctionRealmStateField =
            typeof(HostFunction).GetField("_realmState", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo HostFunctionPropertiesField =
            typeof(HostFunction).GetField("<Properties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly FieldInfo RealmStateSymbolKeysField =
            typeof(RealmState).GetField("_symbolPropertyKeys", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private readonly JsEngine _templateEngine;
        private readonly JsObject _templateGlobal;
        private readonly RealmState _templateRealm;
        private readonly HashSet<string> _excludedGlobals;

        private readonly Dictionary<object, object> _map =
            new(ReferenceEqualityComparer.Instance);

        private JsEngine _engine = null!;
        private RealmState _newRealm = null!;
        private JsObject _newGlobal = null!;

        internal RealmCloner(
            JsEngine templateEngine,
            JsObject templateGlobal,
            RealmState templateRealm,
            HashSet<string> excludedGlobals)
        {
            _templateEngine = templateEngine;
            _templateGlobal = templateGlobal;
            _templateRealm = templateRealm;
            _excludedGlobals = excludedGlobals;
        }

        internal void CloneInto(JsEngine engine)
        {
            _engine = engine;
            _newRealm = engine.RealmState;
            _newGlobal = engine.GlobalObject;

            _map[_templateEngine] = engine;
            _map[_templateRealm] = _newRealm;
            _map[_templateGlobal] = _newGlobal;

            CloneRealmState();
            CloneJsObject(_templateGlobal, _newGlobal);
        }

        private void CloneRealmState()
        {
            // Copy cached well-known symbol property keys for perf.
            if (RealmStateSymbolKeysField.GetValue(_templateRealm) is Dictionary<string, string> baseKeys &&
                RealmStateSymbolKeysField.GetValue(_newRealm) is Dictionary<string, string> newKeys)
            {
                foreach (var kv in baseKeys)
                {
                    newKeys[kv.Key] = kv.Value;
                }
            }

            foreach (var prop in typeof(RealmState).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanRead || !prop.CanWrite)
                {
                    continue;
                }

                if (prop.Name is "Engine" or "Options" or "Logger")
                {
                    continue;
                }

                var value = prop.GetValue(_templateRealm);
                if (value is null)
                {
                    continue;
                }

                prop.SetValue(_newRealm, CloneValue(value));
            }
        }

        private object? CloneValue(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (_map.TryGetValue(value, out var existing))
            {
                return existing;
            }

            switch (value)
            {
                case string:
                case double:
                case bool:
                case int:
                case long:
                case float:
                case decimal:
                case DateTime:
                case TimeZoneInfo:
                case CultureInfo:
                case Regex:
                case TypedAstSymbol:
                case Symbol:
                case JsBigInt:
                    return value;

                case Delegate del:
                    return CloneDelegate(del);

                case JsObject jsObj:
                    return CloneJsObject(jsObj);

                case HostFunction host:
                    return CloneHostFunction(host);

                case Array arr:
                    return CloneArray(arr);

                default:
                    var type = value.GetType();
                    if (IsFrameworkImmutable(type))
                    {
                        return value;
                    }

                    return CloneByReflection(value, type);
            }
        }

        private static bool IsFrameworkImmutable(Type type)
        {
            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            if (type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(TimeSpan) ||
                type == typeof(Guid) ||
                type == typeof(Type) ||
                typeof(MemberInfo).IsAssignableFrom(type) ||
                typeof(ILogger).IsAssignableFrom(type) ||
                typeof(Regex).IsAssignableFrom(type))
            {
                return true;
            }

            var ns = type.Namespace;
            if (ns is not null &&
                (ns.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                 ns.StartsWith("System.Threading", StringComparison.Ordinal)))
            {
                return true;
            }

            return false;
        }

        private object CloneArray(Array original)
        {
            var elementType = original.GetType().GetElementType()!;
            var clone = Array.CreateInstance(elementType, original.Length);
            _map[original] = clone;
            for (var i = 0; i < original.Length; i++)
            {
                clone.SetValue(CloneValue(original.GetValue(i)), i);
            }

            return clone;
        }

        private object CloneByReflection(object original, Type type)
        {
            var clone = FormatterServices.GetUninitializedObject(type);
            _map[original] = clone;

            foreach (var field in GetAllInstanceFields(type))
            {
                var fieldValue = field.GetValue(original);
                var clonedFieldValue = CloneValue(fieldValue);
                field.SetValue(clone, clonedFieldValue);
            }

            return clone;
        }

        private static IEnumerable<FieldInfo> GetAllInstanceFields(Type type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsStatic)
                    {
                        yield return field;
                    }
                }
            }
        }

        private Delegate CloneDelegate(Delegate original)
        {
            if (original.Target is null)
            {
                return original;
            }

            var targetClone = CloneValue(original.Target);
            return Delegate.CreateDelegate(original.GetType(), targetClone, original.Method);
        }

        private JsObject CloneJsObject(JsObject original, JsObject? existing = null, bool skipExcludedGlobals = false)
        {
            if (_map.TryGetValue(original, out var mapped))
            {
                if (existing is not null && ReferenceEquals(mapped, existing))
                {
                    // Populate existing below.
                }
                else
                {
                    return (JsObject)mapped;
                }
            }

            var clone = existing ?? new JsObject();
            if (existing is null)
            {
                _map[original] = clone;
            }

            clone.Clear();
            foreach (var kv in original)
            {
                if (skipExcludedGlobals && _excludedGlobals.Contains(kv.Key))
                {
                    continue;
                }

                clone[kv.Key] = CloneValue(kv.Value);
            }

            // Copy core flags.
            JsObjectIsFrozenField.SetValue(clone, JsObjectIsFrozenField.GetValue(original));
            JsObjectIsSealedField.SetValue(clone, JsObjectIsSealedField.GetValue(original));
            JsObjectIsExtensibleField.SetValue(clone, JsObjectIsExtensibleField.GetValue(original));
            JsObjectIsConstructingField.SetValue(clone, JsObjectIsConstructingField.GetValue(original));

            if (JsObjectStateField is not null)
            {
                var baseState = JsObjectStateField.GetValue(original);
                if (baseState is not null)
                {
                    EnsureStateFields(baseState);
                    var cloneState = JsObjectStateField.GetValue(clone);
                    if (cloneState is null)
                    {
                        cloneState = Activator.CreateInstance(baseState.GetType(), nonPublic: true);
                        JsObjectStateField.SetValue(clone, cloneState);
                    }

                    // Copy descriptors (with deep cloning of values/getters/setters).
                    var baseDescriptors = (Dictionary<string, PropertyDescriptor>)JsObjectStateDescriptorsField!.GetValue(baseState)!;
                    var cloneDescriptors = (Dictionary<string, PropertyDescriptor>)JsObjectStateDescriptorsField.GetValue(cloneState)!;
                    cloneDescriptors.Clear();
                    foreach (var kv in baseDescriptors)
                    {
                        if (skipExcludedGlobals && _excludedGlobals.Contains(kv.Key))
                        {
                            continue;
                        }

                        cloneDescriptors[kv.Key] = CloneDescriptor(kv.Value);
                    }

                    // Copy private fields.
                    var basePrivateFields = (Dictionary<string, object?>)JsObjectStatePrivateFieldsField!.GetValue(baseState)!;
                    var clonePrivateFields = (Dictionary<string, object?>)JsObjectStatePrivateFieldsField.GetValue(cloneState)!;
                    clonePrivateFields.Clear();
                    foreach (var kv in basePrivateFields)
                    {
                        clonePrivateFields[kv.Key] = kv.Value switch
                        {
                            PropertyDescriptor desc => CloneDescriptor(desc),
                            _ => CloneValue(kv.Value)
                        };
                    }

                    // Copy private brands.
                    var baseBrands = (HashSet<object>)JsObjectStatePrivateBrandsField!.GetValue(baseState)!;
                    var cloneBrands = (HashSet<object>)JsObjectStatePrivateBrandsField.GetValue(cloneState)!;
                    cloneBrands.Clear();
                    foreach (var brand in baseBrands)
                    {
                        if (CloneValue(brand) is { } clonedBrand)
                        {
                            cloneBrands.Add(clonedBrand);
                        }
                    }

                    // Copy insertion order using LinkedList + Dictionary structure.
                    var baseOrder = (LinkedList<string>)JsObjectStateInsertionOrderField!.GetValue(baseState)!;
                    var cloneOrder = (LinkedList<string>)JsObjectStateInsertionOrderField.GetValue(cloneState)!;
                    var cloneNodes = (Dictionary<string, LinkedListNode<string>>)JsObjectStateInsertionNodesField!.GetValue(cloneState)!;
                    cloneOrder.Clear();
                    cloneNodes.Clear();
                    foreach (var key in baseOrder)
                    {
                        var node = cloneOrder.AddLast(key);
                        cloneNodes[key] = node;
                    }
                }
            }
            else if (JsObjectDescriptorsField is not null &&
                     JsObjectPrivateFieldsField is not null &&
                     JsObjectPrivateBrandsField is not null &&
                     JsObjectInsertionOrderField is not null &&
                     JsObjectInsertionNodesField is not null)
            {
                // Copy descriptors (with deep cloning of values/getters/setters).
                var baseDescriptors = (Dictionary<string, PropertyDescriptor>)JsObjectDescriptorsField.GetValue(original)!;
                var cloneDescriptors = (Dictionary<string, PropertyDescriptor>)JsObjectDescriptorsField.GetValue(clone)!;
                cloneDescriptors.Clear();
                foreach (var kv in baseDescriptors)
                {
                    if (skipExcludedGlobals && _excludedGlobals.Contains(kv.Key))
                    {
                        continue;
                    }

                    cloneDescriptors[kv.Key] = CloneDescriptor(kv.Value);
                }

                // Copy private fields.
                var basePrivateFields = (Dictionary<string, object?>)JsObjectPrivateFieldsField.GetValue(original)!;
                var clonePrivateFields = (Dictionary<string, object?>)JsObjectPrivateFieldsField.GetValue(clone)!;
                clonePrivateFields.Clear();
                foreach (var kv in basePrivateFields)
                {
                    clonePrivateFields[kv.Key] = kv.Value switch
                    {
                        PropertyDescriptor desc => CloneDescriptor(desc),
                        _ => CloneValue(kv.Value)
                    };
                }

                // Copy private brands.
                var baseBrands = (HashSet<object>)JsObjectPrivateBrandsField.GetValue(original)!;
                var cloneBrands = (HashSet<object>)JsObjectPrivateBrandsField.GetValue(clone)!;
                cloneBrands.Clear();
                foreach (var brand in baseBrands)
                {
                    if (CloneValue(brand) is { } clonedBrand)
                    {
                        cloneBrands.Add(clonedBrand);
                    }
                }

                // Copy insertion order using LinkedList + Dictionary structure.
                var baseOrder = (LinkedList<string>)JsObjectInsertionOrderField.GetValue(original)!;
                var cloneOrder = (LinkedList<string>)JsObjectInsertionOrderField.GetValue(clone)!;
                var cloneNodes = (Dictionary<string, LinkedListNode<string>>)JsObjectInsertionNodesField.GetValue(clone)!;
                cloneOrder.Clear();
                cloneNodes.Clear();
                foreach (var key in baseOrder)
                {
                    var node = cloneOrder.AddLast(key);
                    cloneNodes[key] = node;
                }
            }

            JsObjectTrackArrayLengthField.SetValue(clone, JsObjectTrackArrayLengthField.GetValue(original));
            JsObjectTrackedArrayLengthField.SetValue(clone, JsObjectTrackedArrayLengthField.GetValue(original));

            // Clone prototype links.
            if (JsObjectPrototypeAccessorField is not null)
            {
                var protoAccessor = (IJsPropertyAccessor?)JsObjectPrototypeAccessorField.GetValue(original);
                JsObjectPrototypeAccessorField.SetValue(clone, CloneValue(protoAccessor));
            }
            JsObjectPrototypeBackingField.SetValue(clone, CloneValue(original.Prototype));

            // Virtual providers are treated as immutable host helpers.
            JsObjectVirtualProviderField.SetValue(clone, JsObjectVirtualProviderField.GetValue(original));

            clone.Origin = original.Origin;
            clone.RealmState = original.RealmState is null ? null : _newRealm;

            return clone;
        }

        private static void EnsureStateFields(object state)
        {
            if (JsObjectStateStorageField is not null)
            {
                return;
            }

            var stateType = state.GetType();
            JsObjectStateStorageField = stateType.GetField("Storage", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            JsObjectStateDescriptorsField = stateType.GetField("Descriptors", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            JsObjectStatePrivateBrandsField = stateType.GetField("PrivateBrands", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            JsObjectStatePrivateFieldsField = stateType.GetField("PrivateFields", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            JsObjectStateInsertionOrderField = stateType.GetField("PropertyInsertionOrder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            JsObjectStateInsertionNodesField = stateType.GetField("PropertyInsertionNodes", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        private HostFunction CloneHostFunction(HostFunction original)
        {
            if (_map.TryGetValue(original, out var mapped))
            {
                return (HostFunction)mapped;
            }

            var clone = (HostFunction)FormatterServices.GetUninitializedObject(typeof(HostFunction));
            _map[original] = clone;

            var handler = (Delegate)HostFunctionHandlerField.GetValue(original)!;
            HostFunctionHandlerField.SetValue(clone, CloneDelegate(handler));

            var invokeWithContext = HostFunctionInvokeWithContextField.GetValue(original);
            if (invokeWithContext is Delegate invokeDel)
            {
                HostFunctionInvokeWithContextField.SetValue(clone, CloneDelegate(invokeDel));
            }
            else
            {
                HostFunctionInvokeWithContextField.SetValue(clone, null);
            }

            HostFunctionIsConstructorField.SetValue(clone, HostFunctionIsConstructorField.GetValue(original));

            // Clone properties object.
            var originalProps = (JsObject)HostFunctionPropertiesField.GetValue(original)!;
            var clonedProps = CloneJsObject(originalProps);
            HostFunctionPropertiesField.SetValue(clone, clonedProps);

            clone.IsBoundFunction = original.IsBoundFunction;
            clone.DisallowConstruct = original.DisallowConstruct;
            clone.ConstructErrorMessage = original.ConstructErrorMessage;

            clone.Realm = CloneValue(original.Realm) as JsObject;
            HostFunctionRealmStateField.SetValue(clone, _newRealm);
            clonedProps.RealmState ??= _newRealm;

            // Ensure function prototype lazily once used.
            return clone;
        }

        private PropertyDescriptor CloneDescriptor(PropertyDescriptor original)
        {
            var clone = original.Clone();

            if (original.HasValue)
            {
                clone.Value = CloneValue(original.Value);
            }

            if (original.HasGet)
            {
                clone.Get = CloneValue(original.Get) as IJsCallable;
            }

            if (original.HasSet)
            {
                clone.Set = CloneValue(original.Set) as IJsCallable;
            }

            return clone;
        }
    }
}
#pragma warning restore SYSLIB0050
