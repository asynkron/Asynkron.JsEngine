using System.Net;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Test262Harness;

namespace Asynkron.JsEngine.Tests.Test262;

public abstract partial class Test262Test
{


    private static JsEngine BuildTestExecutor(Test262File file)
    {
        var engine = new JsEngine
        {
            ExecutionTimeout = null
        };

        if (file.Flags.Contains("raw"))
        {
            // nothing should be loaded
            return engine;
        }

        // Execute test harness files
        ExecuteSource(engine, State.Sources["assert.js"]).Wait();
        ExecuteSource(engine, State.Sources["sta.js"]).Wait();

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
            ["evalScript"] = new HostFunction(args =>
            {
                if (args.Count > 1)
                {
                    throw new Exception("only script parsing supported");
                }

                if (args.Count > 0 && args[0] is string script)
                {
                    return ExecuteSource(engine, script).GetAwaiter().GetResult();
                }

                return null;
            }),

            // createRealm function - not fully implemented but needed for compatibility
            ["createRealm"] = new HostFunction(args =>
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
            ["gc"] = new HostFunction(args =>
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

        var moduleSourceCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        engine.SetModuleLoader((specifier, referrer) =>
        {
            var normalized = specifier.Replace('\\', '/');
            if (moduleSourceCache.TryGetValue(normalized, out var cached))
            {
                return cached;
            }

            if (State.Sources.TryGetValue(Path.GetFileName(normalized), out var harnessSource))
            {
                moduleSourceCache[normalized] = harnessSource;
                return harnessSource;
            }

            try
            {
                var moduleFile = State.Test262Stream.GetTestFile(normalized);
                moduleSourceCache[normalized] = moduleFile.Program;
                return moduleFile.Program;
            }
            catch (Exception ex)
            {
                try
                {
                    var url = $"https://raw.githubusercontent.com/tc39/test262/{State.GitHubSha}/test/{normalized}";
                    using var client = new WebClient();
                    var source = client.DownloadString(url);
                    moduleSourceCache[normalized] = source;
                    return source;
                }
                catch (Exception downloadEx)
                {
                    throw new FileNotFoundException($"Module not found: {normalized}", ex.GetBaseException() ?? ex);
                }
            }
        });

        // Load includes
        var includes = file.Includes.ToArray();
        foreach (var include in includes)
        {
            ExecuteSource(engine, State.Sources[include]).Wait();
        }

        if (file.Flags.Contains("async"))
        {
            ExecuteSource(engine, State.Sources["doneprintHandle.js"]).Wait();
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

        var constructor = new HostFunction((thisValue, _) =>
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

    private partial bool ShouldThrow(Test262File testCase, bool strict)
    {
        return testCase.Negative;
    }
}
