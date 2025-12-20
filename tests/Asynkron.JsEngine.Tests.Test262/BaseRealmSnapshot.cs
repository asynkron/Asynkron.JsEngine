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
            Func<string, bool>? shouldSkipKey = skipExcludedGlobals
                ? key => _excludedGlobals.Contains(key)
                : null;
            clone.CloneFromSnapshot(original, CloneValue, CloneDescriptor, shouldSkipKey, _newRealm);

            return clone;
        }

        private HostFunction CloneHostFunction(HostFunction original)
        {
            if (_map.TryGetValue(original, out var mapped))
            {
                return (HostFunction)mapped;
            }

            var clone = new HostFunction(
                (_, _) => JsValue.Undefined,
                new JsObject(),
                realmState: null,
                isConstructor: original.IsConstructor,
                initializePrototype: false);
            _map[original] = clone;

            _map[original.Properties] = clone.Properties;

            var handler = (Func<JsValue, IReadOnlyList<JsValue>, JsValue>)CloneValue(original.HandlerForSnapshot)!;
            clone.SetHandlerForSnapshot(handler);

            if (original.InvokeWithContextForSnapshot is { } invokeWithContext)
            {
                var clonedInvoke = (Func<IReadOnlyList<JsValue>, JsValue, EvaluationContext?, JsValue, JsValue>?)CloneValue(invokeWithContext);
                clone.SetInvokeWithContextForSnapshot(clonedInvoke);
            }

            clone.SetConstructorFlagForSnapshot(original.IsConstructor);

            // Clone properties object.
            clone.Properties.CloneFromSnapshot(original.Properties, CloneValue, CloneDescriptor, shouldSkipKey: null, _newRealm);

            clone.IsBoundFunction = original.IsBoundFunction;
            clone.DisallowConstruct = original.DisallowConstruct;
            clone.ConstructErrorMessage = original.ConstructErrorMessage;

            clone.Realm = CloneValue(original.Realm) as JsObject;
            clone.RealmState = _newRealm;
            clone.Properties.RealmState ??= _newRealm;

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
