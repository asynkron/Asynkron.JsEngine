using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Test262Harness;

namespace Asynkron.JsEngine.Tests.Test262;

[Test262ActivityTrace]
public abstract partial class Test262Test
{
    private static readonly ConcurrentDictionary<string, ParsedProgram> HarnessProgramCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, string> SharedModuleSourceCache =
        new(StringComparer.OrdinalIgnoreCase);

    private const string CompareArrayPatchScript = @"// Patched compareArray harness to align with modern Test262 semantics
function compareArray(a, b) {
  compareArray.__callCount = (compareArray.__callCount || 0) + 1;
  compareArray.__lastFailure = null;
  if (b.length !== a.length) {
    compareArray.__lastFailure = {
      kind: ""length"",
      expectedLength: b.length,
      actualLength: a.length,
      expectedType: typeof b.length,
      actualType: typeof a.length
    };
    compareArray.__lastResult = false;
    return false;
  }

  for (var i = 0; i < a.length; i++) {
    if (!compareArray.isSameValue(b[i], a[i])) {
      compareArray.__lastFailure = {
        kind: ""value"",
        index: i,
        expected: b[i],
        actual: a[i],
        expectedType: typeof b[i],
        actualType: typeof a[i]
      };
      compareArray.__lastResult = false;
      return false;
    }
  }
  compareArray.__lastResult = true;
  return true;
}

compareArray.isSameValue = function(a, b) {
  if (a === 0 && b === 0) return 1 / a === 1 / b;
  if (a !== a && b !== b) return true;

  return a === b;
};

compareArray.format = function(arrayLike) {
  return `[${Array.prototype.map.call(arrayLike, String).join(', ')}]`;
};
compareArray.__patchedByAsynkron = true;

assert.compareArray = function(actual, expected, message) {
  message = message === undefined ? '' : message;

  if (typeof message === 'symbol') {
    message = message.toString();
  }

  assert(actual != null, `Actual argument shouldn't be nullish. ${message}`);
  assert(expected != null, `Expected argument shouldn't be nullish. ${message}`);
  var actualLengthType = typeof actual.length;
  var expectedLengthType = typeof expected.length;
  var comparisonDebug = { lengthMismatch: false, mismatchIndex: -1 };
  var mismatch = false;
  if (actual.length !== expected.length) {
    comparisonDebug.lengthMismatch = true;
    mismatch = true;
  } else {
    for (var i = 0; i < actual.length; i++) {
      if (!compareArray.isSameValue(actual[i], expected[i])) {
        comparisonDebug.mismatchIndex = i;
        mismatch = true;
        break;
      }
    }
  }
  if (mismatch) {
    var format = compareArray.format;
    var actualTypes = Array.prototype.map.call(actual, function (value) { return typeof value; }).join(',');
    var perIndex = [];
    var length = Math.min(actual.length, expected.length);
    for (var i = 0; i < length; i++) {
      perIndex.push(compareArray.isSameValue(actual[i], expected[i]));
    }
    var actualInfo = actual && typeof actual === 'object'
      ? {
          type: typeof actual,
          array: Array.isArray(actual),
          protoArray: Object.getPrototypeOf(actual) === Array.prototype,
          ctor: actual.constructor && actual.constructor.name
        }
      : { type: typeof actual };
    var expectedInfo = expected && typeof expected === 'object'
      ? {
          type: typeof expected,
          array: Array.isArray(expected),
          protoArray: Object.getPrototypeOf(expected) === Array.prototype,
          ctor: expected.constructor && expected.constructor.name
        }
      : { type: typeof expected };
    var stack = new Error().stack;
    throw new Error(`Actual ${format(actual)} and expected ${format(expected)} should have the same contents. ${message} (mismatch=${mismatch}, actualTypes=${actualTypes}, lengths=${actual.length}/${expected.length}, lengthTypes=${actualLengthType}/${expectedLengthType}, perIndex=${perIndex.join(',')}, comparisonDebug=${JSON.stringify(comparisonDebug)}, actualInfo=${JSON.stringify(actualInfo)}, expectedInfo=${JSON.stringify(expectedInfo)}, compareArraySource=${compareArray.toString()}, patched=${compareArray.__patchedByAsynkron}, failure=${JSON.stringify(compareArray.__lastFailure)}, lastResult=${compareArray.__lastResult}, callCount=${compareArray.__callCount}, stack=${stack})`);
  }
};

assert.compareArray.isSameValue = compareArray.isSameValue;
assert.compareArray.format = compareArray.format;
if (typeof compareArray([], []) !== ""boolean"") {
  throw new Error(""compareArray patch failed"");
}
try {
  var probeKeys = Reflect.ownKeys(new Intl.Locale('en').getWeekInfo());
  if (!compareArray(probeKeys, ['firstDay','weekend','minimalDays'])) {
    throw new Error('compareArray mismatch: ' + JSON.stringify(probeKeys));
  }
} catch (err) {
  throw err;
}
";


    private static ParsedProgram GetHarnessProgram(string source)
    {
        return HarnessProgramCache.GetOrAdd(source, static s =>
        {
            var parserEngine = new JsEngine();
            return parserEngine.ParseForExecution(s);
        });
    }

    private static void ExecuteHarnessProgram(JsEngine engine, string source)
    {
        var program = GetHarnessProgram(source);
        engine.ExecuteProgram(program, engine.GlobalEnvironment);
        engine.DrainMicrotasks();
    }

    private static object? EvalScriptSync(JsEngine engine, string source)
    {
        var result = engine.EvaluateSync(source);
        engine.DrainMicrotasks();
        return result;
    }

    private static JsEngine BuildTestExecutor(Test262File file)
    {
        var engine = BaseRealmSnapshot.UseSnapshot
            ? BaseRealmSnapshot.Instance.Value.CreateEngine()
            : new JsEngine { ExecutionTimeout = null };

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JSENGINE_TRACE_REALM")))
        {
            engine.RealmState.Logger = new ConsoleLogger();
        }

        if (file.Flags.Contains("raw"))
        {
            // nothing should be loaded
            return engine;
        }

        // Execute test harness files
        ExecuteHarnessProgram(engine, State.Sources["assert.js"]);
        ExecuteHarnessProgram(engine, State.Sources["sta.js"]);

        // Add print function
        engine.SetGlobalFunction("print", args =>
        {
            if (args.Count > 0)
            {
                var value = args[0];
                // Convert to string representation
                return value?.ToString() ?? "";
            }

            return "";
        });

        // Create $262 object for Test262 compatibility
        var obj262 = new JsObject
        {
            // evalScript function
            ["evalScript"] = new HostFunction(args => args.Count switch
            {
                > 1 => throw new Exception("only script parsing supported"),
                > 0 when args[0] is string script => EvalScriptSync(engine, script),
                _ => null
            }),

            // createRealm function - not fully implemented but needed for compatibility
            ["createRealm"] = new HostFunction(_ =>
            {
                // Create a fresh engine with its own intrinsics; expose its global
                // object so tests can access constructors like Array/Function.
                var realmEngine = new JsEngine();
                var realmGlobal = realmEngine.GlobalObject;
                realmGlobal["global"] = realmGlobal;

                return realmGlobal;
            }),

            // detachArrayBuffer function - placeholder implementation
            ["detachArrayBuffer"] = new HostFunction(args =>
            {
                if (args.Count == 0)
                {
                    return Symbol.Undefined;
                }

                switch (args[0])
                {
                    case TypedArrayBase view:
                        view.Buffer.Detach();
                        break;
                    case JsArrayBuffer buffer:
                        buffer.Detach();
                        break;
                    case IJsPropertyAccessor accessor when accessor.TryGetProperty("buffer", out var inner) &&
                                                           inner is JsArrayBuffer innerBuffer:
                        innerBuffer.Detach();
                        break;
                }

                return Symbol.Undefined;
            }),

            // Host hook for resizable ArrayBuffers
            ["createResizableArrayBuffer"] = new HostFunction(args =>
            {
                var length = args.Count > 0 && args[0] is double d ? (int)d : 0;
                var max = args.Count > 1 && args[1] is double d2 ? (int)d2 : length;
                return new JsArrayBuffer(length, max);
            }),

            // gc function - triggers garbage collection
            ["gc"] = new HostFunction(_ =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return null;
            }),

            // HTMLDDA-like object used by Test262 harness
            ["IsHTMLDDA"] = new HtmlDdaValue(),

            // %AbstractModuleSource% intrinsic (minimal host stub for Test262)
            ["AbstractModuleSource"] = CreateAbstractModuleSource(engine)
        };

        engine.SetGlobalValue("$262", obj262);

        // Helper used by some modern reduce tests
        ExecuteHarnessProgram(engine,
            "function ReduceCollecting(list){ return function(acc, v){ list.push(v); return acc; }; }");

        var moduleSourceCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? TryReadRawModuleSource(string candidate)
        {
            try
            {
                var options = State.Test262Stream.Options;
                var fsProp = options.GetType().GetProperty("FileSystem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var fileSystem = fsProp?.GetValue(options);
                if (fileSystem is null)
                {
                    return null;
                }

                var fsType = fileSystem.GetType();
                Type? uPathType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    uPathType = asm.GetType("Zio.UPath", throwOnError: false);
                    if (uPathType is not null)
                    {
                        break;
                    }
                }

                if (uPathType is null)
                {
                    return null;
                }

                var openFile = fsType.GetMethod("OpenFile",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: [uPathType, typeof(FileMode), typeof(FileAccess), typeof(FileShare)],
                    modifiers: null);

                if (openFile is null)
                {
                    foreach (var method in fsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!string.Equals(method.Name, "OpenFile", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var parameters = method.GetParameters();
                        if (parameters.Length == 4 && parameters[0].ParameterType == uPathType)
                        {
                            openFile = method;
                            break;
                        }
                    }

                    if (openFile is null)
                    {
                        return null;
                    }
                }

                var candidatePaths = new[]
                {
                    candidate,
                    $"test/{candidate}",
                    candidate.StartsWith("/", StringComparison.Ordinal) ? candidate : $"/{candidate}",
                    candidate.StartsWith("/", StringComparison.Ordinal) ? $"test{candidate}" : $"/test/{candidate}"
                };

                foreach (var path in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var uPath = Activator.CreateInstance(uPathType, path);
                    if (uPath is null)
                    {
                        continue;
                    }

                    try
                    {
                        using var stream = (Stream)openFile.Invoke(fileSystem,
                            [uPath, FileMode.Open, FileAccess.Read, FileShare.Read])!;
                        using var reader = new StreamReader(stream);
                        return reader.ReadToEnd();
                    }
                    catch
                    {
                        // Try next path shape.
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        engine.SetModuleLoader((specifier, referrer) =>
        {
            var normalized = specifier.Replace('\\', '/');
            if (moduleSourceCache.TryGetValue(normalized, out var cached))
            {
                return cached;
            }

            if (SharedModuleSourceCache.TryGetValue(normalized, out var sharedCached))
            {
                moduleSourceCache[normalized] = sharedCached;
                return sharedCached;
            }

            if (State.Sources.TryGetValue(Path.GetFileName(normalized), out var harnessSource))
            {
                moduleSourceCache[normalized] = harnessSource;
                SharedModuleSourceCache.TryAdd(normalized, harnessSource);
                return harnessSource;
            }

            var referrerPath = referrer?.Replace('\\', '/');
            var candidates = new List<string>();

            void AddCandidate(string candidate)
            {
                if (candidate.StartsWith("./", StringComparison.Ordinal))
                {
                    candidate = candidate[2..];
                }

                if (candidate.StartsWith("test/", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = candidate[5..];
                }

                candidates.Add(candidate);
            }

            string NormalizeRelative(string baseDir, string relative)
            {
                var combined = $"{baseDir}/{relative}";
                var parts = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var stack = new List<string>(parts.Length);

                foreach (var part in parts)
                {
                    if (part == ".")
                    {
                        continue;
                    }

                    if (part == "..")
                    {
                        if (stack.Count > 0)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }
                        continue;
                    }

                    stack.Add(part);
                }

                return string.Join('/', stack);
            }

            AddCandidate(normalized);

            if (!string.IsNullOrEmpty(referrerPath))
            {
                var baseDir = referrerPath;
                var lastSlash = baseDir.LastIndexOf('/');
                if (lastSlash >= 0)
                {
                    baseDir = baseDir[..lastSlash];
                }

                if (normalized.StartsWith("./", StringComparison.Ordinal) ||
                    normalized.StartsWith("../", StringComparison.Ordinal))
                {
                    AddCandidate(NormalizeRelative(baseDir, normalized));
                }
                else if (!normalized.Contains('/'))
                {
                    AddCandidate($"{baseDir}/{normalized}");
                }
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (SharedModuleSourceCache.TryGetValue(candidate, out var sharedCandidate))
                {
                    moduleSourceCache[normalized] = sharedCandidate;
                    return sharedCandidate;
                }

                try
                {
                    var moduleFile = State.Test262Stream.GetTestFile(candidate);
                    moduleSourceCache[normalized] = moduleFile.Program;
                    SharedModuleSourceCache[candidate] = moduleFile.Program;
                    return moduleFile.Program;
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentException arg &&
                        arg.Message.Contains("YAML section start", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryReadRawModuleSource(candidate) is { } rawSource)
                        {
                            moduleSourceCache[normalized] = rawSource;
                            SharedModuleSourceCache[candidate] = rawSource;
                            return rawSource;
                        }
                    }

                    if (ReferenceEquals(candidate, candidates.Last()))
                    {
                        throw new FileNotFoundException($"Module not found: {normalized}",
                            ex.GetBaseException() ?? ex);
                    }
                }
            }

            throw new FileNotFoundException($"Module not found: {normalized}");
        });

        // Load includes
        var includes = file.Includes.ToArray();
        foreach (var include in includes)
        {
            ExecuteHarnessProgram(engine, State.Sources[include]);
        }

        ExecuteHarnessProgram(engine, CompareArrayPatchScript);

        if (file.Flags.Contains("async"))
        {
            ExecuteHarnessProgram(engine, State.Sources["doneprintHandle.js"]);
        }

        return engine;
    }

    private static async Task<object?> ExecuteSource(JsEngine engine, string source)
    {
        return await engine.Evaluate(source);
    }

    private static HostFunction CreateAbstractModuleSource(JsEngine engine)
    {
        // Prototype [[Prototype]] should be Object.prototype when available.
        var prototype = new JsObject();
        if (engine.GlobalObject.TryGetValue("Object", out var objectCtor) &&
            objectCtor is IJsPropertyAccessor objAccessor &&
            objAccessor.TryGetProperty("prototype", out var objectProto) &&
            objectProto is JsObject protoObj)
        {
            prototype.SetPrototype(protoObj);
        }

        var constructor = new HostFunction((_, _) =>
        {
            object? error = "%AbstractModuleSource% is not constructable";
            if (engine.GlobalObject.TryGetValue("TypeError", out var typeErrorValue) &&
                typeErrorValue is IJsCallable typeErrorCtor)
            {
                try
                {
                    error = typeErrorCtor.Invoke([error], null);
                }
                catch (ThrowSignal signal)
                {
                    error = signal.ThrownValue;
                }
            }

            throw new ThrowSignal(error);
        })
        {
            IsConstructor = true
        };

        constructor.DefineProperty("length", new PropertyDescriptor
        {
            Value = 0,
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        constructor.DefineProperty("name", new PropertyDescriptor
        {
            Value = "AbstractModuleSource",
            Writable = false,
            Enumerable = false,
            Configurable = true
        });

        constructor.DefineProperty("prototype", new PropertyDescriptor
        {
            Value = prototype,
            Writable = false,
            Enumerable = false,
            Configurable = false
        });

        prototype.DefineProperty("constructor", new PropertyDescriptor
        {
            Value = constructor,
            Writable = true,
            Enumerable = false,
            Configurable = true
        });
        var ctorDescriptor = prototype.GetOwnPropertyDescriptor("constructor");
        if (ctorDescriptor is not null)
        {
            ctorDescriptor.Configurable = true;
        }

        var toStringTagGetter = new HostFunction((thisValue, _) =>
        {
            if (thisValue is JsObject obj &&
                obj.TryGetProperty("__moduleSourceClassName__", out var name) &&
                name is string tag)
            {
                return tag;
            }

            return Symbol.Undefined;
        });

        var toStringTagKey = $"@@symbol:{TypedAstSymbol.For("Symbol.toStringTag").GetHashCode()}";
        prototype.DefineProperty(toStringTagKey, new PropertyDescriptor
        {
            Get = toStringTagGetter,
            Enumerable = false,
            Configurable = true
        });
        var tagDescriptor = prototype.GetOwnPropertyDescriptor(toStringTagKey);
        if (tagDescriptor is not null)
        {
            tagDescriptor.Configurable = true;
        }

        if (engine.GlobalObject.TryGetValue("Function", out var functionCtor) &&
            functionCtor is IJsPropertyAccessor fnAccessor &&
            fnAccessor.TryGetProperty("prototype", out var fnProto) &&
            fnProto is JsObject fnProtoObj)
        {
            constructor.SetProperty("__proto__", fnProtoObj);
        }

        return constructor;
    }

    private static void ExecuteTest(JsEngine engine, Test262File file)
    {
        ExecuteTestAsync(engine, file).GetAwaiter().GetResult();
    }

    private static async Task ExecuteTestAsync(JsEngine engine, Test262File file)
    {
        if (file.Type == ProgramType.Module)
        {
            await engine.EvaluateModule(file.Program, file.FileName);
        }
        else
        {
            await ExecuteSource(engine, file.Program);
        }
    }

#pragma warning disable CA1822
    // ReSharper disable once UnusedParameterInPartialMethod
    private partial bool ShouldThrow(Test262File testCase, bool strict)
#pragma warning restore CA1822
    {
        return testCase.Negative;
    }
}
