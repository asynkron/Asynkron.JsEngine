using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Test262Harness;

internal static class Test262ProfileRunner
{
    private const string Test262Sha = "a073f479f80b336256b7fc4e04700c827293e2fe";

    private static readonly ConcurrentDictionary<string, ProgramNode> HarnessProgramCache =
        new(StringComparer.Ordinal);

    internal static async Task RunAsync(
        string profileKey,
        ProfileDefinition profile,
        int warmup,
        int iterations)
    {
        var suite = await LoadSuiteAsync();
        var harnessSources = suite.GetHarnessFiles()
            .ToDictionary(file => Path.GetFileName(file.FileName), file => file.Program, StringComparer.OrdinalIgnoreCase);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Test262Profile {profileKey}: cases={profile.Test262Cases.Count}, warmup={warmup}, iterations={iterations}"));

        for (var i = 0; i < warmup; i++)
        {
            await RunCasesAsync(suite, harnessSources, profile.Test262Cases);
        }

        var sw = profile.ShowTiming ? Stopwatch.StartNew() : null;
        for (var iter = 0; iter < iterations; iter++)
        {
            await RunCasesAsync(suite, harnessSources, profile.Test262Cases);
            if (profile.ShowProgress)
            {
                Console.Write(".");
            }
        }

        sw?.Stop();
        if (profile.ShowProgress)
        {
            Console.WriteLine();
        }

        if (profile.ShowTiming)
        {
            var elapsedMs = sw?.ElapsedMilliseconds ?? 0;
            var caseRuns = iterations * profile.Test262Cases.Count;
            var avgMs = caseRuns > 0 ? elapsedMs / (double)caseRuns : 0d;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Done in {elapsedMs}ms (avg {avgMs:F2}ms per case)"));
        }
        else
        {
            Console.WriteLine("Done");
        }
    }

    private static async Task<Test262Stream> LoadSuiteAsync()
    {
        var cacheDirectory = GetDefaultCacheDirectory();
        if (Directory.Exists(cacheDirectory))
        {
            return Test262Stream.FromDirectory(cacheDirectory, _ => { });
        }

        return await Test262StreamExtensions.FromGitHub(Test262Sha);
    }

    private static string GetDefaultCacheDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".asynkron", "jsengine", "test262", Test262Sha);
    }

    private static async Task RunCasesAsync(
        Test262Stream suite,
        IReadOnlyDictionary<string, string> harnessSources,
        IReadOnlyList<Test262ProfileCase> cases)
    {
        foreach (var profileCase in cases)
        {
            var testCase = suite.GetTestFile(profileCase.File);
            if (profileCase.Strict)
            {
                testCase = testCase.AsStrict();
            }

            try
            {
                await RunCaseAsync(testCase, harnessSources);
                if (testCase.Negative)
                {
                    throw new InvalidOperationException(
                        $"Expected Test262 negative case to throw: {testCase.FileName}");
                }
            }
            catch when (testCase.Negative)
            {
                // Negative Test262 cases pass by throwing the expected class of error.
            }
        }
    }

    private static async Task RunCaseAsync(
        Test262File file,
        IReadOnlyDictionary<string, string> harnessSources)
    {
        await using var engine = CreateTest262Engine();

        if (!file.Flags.Contains("raw"))
        {
            ExecuteHarnessProgram(engine, harnessSources["assert.js"]);
            ExecuteHarnessProgram(engine, harnessSources["sta.js"]);
            InstallHostHooks(engine);

            foreach (var include in file.Includes)
            {
                ExecuteHarnessProgram(engine, harnessSources[include]);
            }

            if (file.Flags.Contains("async"))
            {
                ExecuteHarnessProgram(engine, harnessSources["doneprintHandle.js"]);
            }
        }

        if (string.Equals(file.Type.ToString(), "Module", StringComparison.Ordinal))
        {
            await engine.EvaluateModule(file.Program, file.FileName);
        }
        else
        {
            await engine.Evaluate(file.Program);
        }

        engine.DrainMicrotasks();
    }

    private static JsEngine CreateTest262Engine()
    {
        return new JsEngine
        {
            ExecutionTimeout = TimeSpan.FromMinutes(5),
        };
    }

    private static void ExecuteHarnessProgram(JsEngine engine, string source)
    {
        var program = HarnessProgramCache.GetOrAdd(source, static harnessSource =>
        {
            using var parserEngine = new JsEngine();
            return parserEngine.ParseProgram(harnessSource);
        });

        engine.ExecuteProgram(program, engine.GlobalEnvironment);
        engine.DrainMicrotasks();
    }

    private static void InstallHostHooks(JsEngine engine)
    {
        engine.SetGlobalFunction("print", args => args.Count > 0
            ? args[0].ToObject()?.ToString() ?? string.Empty
            : string.Empty);

        var obj262 = new JsObject
        {
            ["evalScript"] = new HostFunction(args =>
            {
                if (args.Count == 0)
                {
                    return JsValue.Undefined;
                }

                if (args[0].ToObject() is not string script)
                {
                    return JsValue.Undefined;
                }

                var result = engine.EvaluateSync(script);
                engine.DrainMicrotasks();
                return JsValue.FromObjectUnsafe(result);
            }),
            ["createRealm"] = new HostFunction(_ =>
            {
                var realmEngine = CreateTest262Engine();
                var realmGlobal = realmEngine.GlobalObject;
                realmGlobal["global"] = realmGlobal;
                return (JsValue)realmGlobal;
            }),
            ["detachArrayBuffer"] = new HostFunction(args =>
            {
                if (args.Count == 0)
                {
                    return JsValue.Undefined;
                }

                if (args[0].TryGetObject<TypedArrayBase>(out var view))
                {
                    view.Buffer.Detach();
                }
                else if (args[0].TryGetObject<JsArrayBuffer>(out var buffer))
                {
                    buffer.Detach();
                }

                return JsValue.Undefined;
            }),
            ["gc"] = new HostFunction(_ =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                return JsValue.Null;
            }),
        };

        engine.SetGlobalValue("$262", obj262);
    }
}
