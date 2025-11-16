using System.Text;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to compare behavior of for-await-of with iterables in different scopes.
/// This helps diagnose why global scope iterables fail while local scope iterables work.
///
/// DETAILED FINDINGS: See docs/investigations/ASYNC_ITERABLE_SCOPE_DEBUG_NOTES.md
///
/// Key Discovery: The for-await-of loop works correctly when the iterable is declared
/// in LOCAL scope (inside the async function), but FAILS when the iterable is declared
/// in GLOBAL scope (outside the async function). The S-expression transformation is
/// correct in both cases, but execution differs.
/// </summary>
public class AsyncIterableScopeComparisonTests(ITestOutputHelper output)
{
    [Fact(Timeout = 5000)]
    public Task CompareGlobalVsLocalScope_SExpression()
    {
        // Test 1: Iterable in LOCAL scope (works)
        var localScopeCode = @"
            async function test() {
                let localIterable = {
                    [Symbol.iterator]() {
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                if (index < values.length) {
                                    return { value: values[index++], done: false };
                                }
                                return { done: true };
                            }
                        };
                    }
                };

                let result = '';
                for await (let item of localIterable) {
                    result = result + item;
                }
                return result;
            }
        ";

        // Test 2: Iterable in GLOBAL scope (fails)
        var globalScopeCode = @"
            let globalIterable = {
                [Symbol.iterator]() {
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            if (index < values.length) {
                                return { value: values[index++], done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                let result = '';
                for await (let item of globalIterable) {
                    result = result + item;
                }
                return result;
            }
        ";

        var engine1 = new JsEngine();
        var engine2 = new JsEngine();

        // Parse both versions to get S-expressions
        var localParsed = engine1.Parse(localScopeCode);
        var globalParsed = engine2.Parse(globalScopeCode);

        output.WriteLine("=== LOCAL SCOPE S-EXPRESSION ===");
        output.WriteLine(localParsed.ToString());
        output.WriteLine("");
        output.WriteLine("=== GLOBAL SCOPE S-EXPRESSION ===");
        output.WriteLine(globalParsed.ToString());
        output.WriteLine("");

        // Now compare the for-await-of transformation
        output.WriteLine("=== COMPARISON NOTES ===");
        output.WriteLine("The S-expressions should show how the for-await-of loop is transformed.");
        output.WriteLine("Key things to look for:");
        output.WriteLine("1. How 'localIterable' vs 'globalIterable' appears in the transformed code");
        output.WriteLine("2. The __getAsyncIterator call and how it references the iterable");
        output.WriteLine("3. The __loopCheck function and variable capture");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Captures debug snapshots for both scope variants and asserts that the runtime maintains the same iterator scaffolding.
    /// </summary>
    /// <remarks>
    /// Detailed parity findings live in docs/investigations/ASYNC_ITERABLE_SCOPE_DEBUG_NOTES.md.
    /// </remarks>
    [Fact(Timeout = 5000)]
    public async Task CompareGlobalVsLocalScope_WithDebug()
    {
        var localResult = new StringBuilder();
        var globalResult = new StringBuilder();

        // Test with LOCAL scope iterable
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            localResult.Append("[LOCAL] ").Append(msg);
            localResult.AppendLine();
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE TEST ===');

                let localIterable = {
                    [Symbol.iterator]() {
                        log('LOCAL: Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('LOCAL: next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    log('LOCAL: returning value=' + val);
                                    return { value: val, done: false };
                                }
                                log('LOCAL: returning done=true');
                                return { done: true };
                            }
                        };
                    }
                };

                log('LOCAL: About to start for-await-of');
                __debug(); // Capture state before loop

                let result = '';
                for await (let item of localIterable) {
                    log('LOCAL: In loop, item=' + item);
                    __debug(); // Capture state during iteration
                    result = result + item;
                }

                log('LOCAL: After loop, result=' + result);
                __debug(); // Capture state after loop
                return result;
            }

            test();
        ");

        // Collect debug messages from local scope test
        var localDebugMessages = DrainDebugMessages(engine1);

        await Task.Delay(1000);
        var localFinalResult = await engine1.Evaluate("test()");
        output.WriteLine($"[LOCAL] Final result: '{localFinalResult}'");
        localResult.Append("[LOCAL] Final result: '").Append(localFinalResult).Append('\'').AppendLine();

        // Test with GLOBAL scope iterable
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            globalResult.Append("[GLOBAL] ").Append(msg).AppendLine();
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE TEST ===');

            let globalIterable = {
                [Symbol.iterator]() {
                    log('GLOBAL: Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('GLOBAL: next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('GLOBAL: returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('GLOBAL: returning done=true');
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('GLOBAL: About to start for-await-of');
                __debug(); // Capture state before loop

                let result = '';
                for await (let item of globalIterable) {
                    log('GLOBAL: In loop, item=' + item);
                    __debug(); // Capture state during iteration
                    result = result + item;
                }

                log('GLOBAL: After loop, result=' + result);
                __debug(); // Capture state after loop
                return result;
            }

            test();
        ");

        // Collect debug messages from global scope test
        var globalDebugMessages = DrainDebugMessages(engine2);

        await Task.Delay(1000);
        var globalFinalResult = await engine2.Evaluate("test()");
        output.WriteLine($"[GLOBAL] Final result: '{globalFinalResult}'");
        globalResult.Append("[GLOBAL] Final result: '").Append(globalFinalResult).Append('\'').AppendLine();

        // Compare debug messages
        output.WriteLine("");
        var localSnapshots = MaterializeSnapshots(localDebugMessages, "local");
        var globalSnapshots = MaterializeSnapshots(globalDebugMessages, "global");

        LogSnapshotSummary(output, "LOCAL", localSnapshots);
        LogSnapshotSummary(output, "GLOBAL", globalSnapshots);

        // We expect both executions to expose comparable iterator scaffolding.
        // The current bug manifests as missing iterator temporaries + loop state for the global run.
        var parityFailure = AnalyzeSnapshotParity(localSnapshots, globalSnapshots);
        Assert.True(parityFailure is null, parityFailure);
    }

    private static List<DebugMessage> DrainDebugMessages(JsEngine engine)
    {
        var messages = new List<DebugMessage>();
        while (engine.DebugMessages().TryRead(out var message))
        {
            messages.Add(message);
        }

        return messages;
    }

    private static IReadOnlyList<DebugSnapshot> MaterializeSnapshots(IReadOnlyList<DebugMessage> messages, string scenario)
    {
        var snapshots = new List<DebugSnapshot>(messages.Count);

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var materializedVariables = message.Variables.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString(),
                StringComparer.Ordinal);

            snapshots.Add(new DebugSnapshot(
                Scenario: scenario,
                Index: i,
                ControlFlowState: message.ControlFlowState,
                Variables: materializedVariables,
                CallStack: message.CallStack.Select(frame => frame.ToString()).ToArray(),
                HasLocalIterable: materializedVariables.ContainsKey("localIterable"),
                HasGlobalIterable: materializedVariables.ContainsKey("globalIterable"),
                IteratorIdentifiers: ExtractIdentifiers(materializedVariables.Keys, "__iterator"),
                LoopCheckIdentifiers: ExtractIdentifiers(materializedVariables.Keys, "__loopCheck"),
                LoopResolverIdentifiers: ExtractIdentifiers(materializedVariables.Keys, "__loopResolve"),
                ItemValue: materializedVariables.TryGetValue("item", out var item) ? item : null,
                ResultValue: materializedVariables.TryGetValue("result", out var result) ? result : null));
        }

        return snapshots;
    }

    private static IReadOnlyList<string> ExtractIdentifiers(IEnumerable<string> keys, string prefix)
    {
        return keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static void LogSnapshotSummary(ITestOutputHelper output, string scope, IReadOnlyList<DebugSnapshot> snapshots)
    {
        output.WriteLine(string.Empty);
        output.WriteLine($"=== {scope} SNAPSHOTS ===");
        output.WriteLine($"Total messages: {snapshots.Count}");

        foreach (var snapshot in snapshots)
        {
            output.WriteLine($"[{scope}] Snapshot #{snapshot.Index} :: ControlFlow={snapshot.ControlFlowState}");
            output.WriteLine($"[{scope}]   Iterator identifiers: {string.Join(", ", snapshot.IteratorIdentifiers.DefaultIfEmpty("<none>"))}");
            output.WriteLine($"[{scope}]   Loop checks: {string.Join(", ", snapshot.LoopCheckIdentifiers.DefaultIfEmpty("<none>"))}");
            output.WriteLine($"[{scope}]   Loop resolvers: {string.Join(", ", snapshot.LoopResolverIdentifiers.DefaultIfEmpty("<none>"))}");
            if (snapshot.ItemValue is not null)
            {
                output.WriteLine($"[{scope}]   item = {snapshot.ItemValue}");
            }

            if (snapshot.ResultValue is not null)
            {
                output.WriteLine($"[{scope}]   result = {snapshot.ResultValue}");
            }
        }
    }

    private static string? AnalyzeSnapshotParity(IReadOnlyList<DebugSnapshot> localSnapshots, IReadOnlyList<DebugSnapshot> globalSnapshots)
    {
        var sb = new StringBuilder();

        if (localSnapshots.Count == 0)
        {
            sb.Append("Local scenario failed to capture any debug snapshots.").AppendLine();
        }

        if (globalSnapshots.Count == 0)
        {
            sb.Append("Global scenario failed to capture any debug snapshots.").AppendLine();
        }

        if (localSnapshots.Count != globalSnapshots.Count)
        {
            sb.Append("Snapshot count mismatch (local=")
                .Append(localSnapshots.Count)
                .Append(", global=")
                .Append(globalSnapshots.Count)
                .Append(").");
            sb.AppendLine();
        }

        var localIteratorBindings = localSnapshots.Any(snapshot => snapshot.IteratorIdentifiers.Count > 0);
        var globalIteratorBindings = globalSnapshots.Any(snapshot => snapshot.IteratorIdentifiers.Count > 0);

        if (!globalIteratorBindings && localIteratorBindings)
        {
            sb.Append("Global execution never exposed iterator temporaries (prefix '__iterator').").AppendLine();
        }

        var localLoopState = localSnapshots.Count(snapshot => snapshot.ItemValue is not null);
        var globalLoopState = globalSnapshots.Count(snapshot => snapshot.ItemValue is not null);

        if (localLoopState > 0 && globalLoopState == 0)
        {
            sb.Append(
                    "Global execution never surfaced 'item' loop variables, indicating the for-await-of body did not run.")
                .AppendLine();
        }

        var missingGlobalBinding = globalSnapshots.Any(snapshot => !snapshot.HasGlobalIterable);
        if (missingGlobalBinding)
        {
            sb.Append("One or more global snapshots lost the 'globalIterable' binding.").AppendLine();
        }

        if (sb.Length > 0)
        {
            sb.Append("See docs/investigations/ASYNC_ITERABLE_SCOPE_DEBUG_NOTES.md for the captured environment diff.")
                .AppendLine();
            return sb.ToString();
        }

        return null;
    }

    private sealed record DebugSnapshot(
        string Scenario,
        int Index,
        string ControlFlowState,
        IReadOnlyDictionary<string, string?> Variables,
        IReadOnlyList<string> CallStack,
        bool HasLocalIterable,
        bool HasGlobalIterable,
        IReadOnlyList<string> IteratorIdentifiers,
        IReadOnlyList<string> LoopCheckIdentifiers,
        IReadOnlyList<string> LoopResolverIdentifiers,
        string? ItemValue,
        string? ResultValue);

    [Fact(Timeout = 5000)]
    public async Task InspectIteratorObject_GlobalVsLocal()
    {
        output.WriteLine("=== INSPECTING ITERATOR OBJECTS ===");

        // Test 1: Local scope - manually get iterator and inspect
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                let localIterable = {
                    [Symbol.iterator]() {
                        return {
                            next() {
                                return { value: 'x', done: false };
                            }
                        };
                    }
                };

                log('Getting iterator with __getAsyncIterator');
                let iter = __getAsyncIterator(localIterable);
                log('Iterator type: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));
                log('Iterator constructor: ' + iter.constructor.name);

                log('Calling __iteratorNext');
                let promise = __iteratorNext(iter);
                log('Promise type: ' + typeof promise);
                log('Promise has then: ' + (typeof promise.then));

                promise.then(result => {
                    log('Promise resolved, result: ' + JSON.stringify(result));
                });
            }

            test();
        ");

        await Task.Delay(1000);

        // Test 2: Global scope - manually get iterator and inspect
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            let globalIterable = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            return { value: 'x', done: false };
                        }
                    };
                }
            };

            async function test() {
                log('Getting iterator with __getAsyncIterator');
                let iter = __getAsyncIterator(globalIterable);
                log('Iterator type: ' + typeof iter);
                log('Iterator has next: ' + (typeof iter.next));
                log('Iterator constructor: ' + iter.constructor.name);

                log('Calling __iteratorNext');
                let promise = __iteratorNext(iter);
                log('Promise type: ' + typeof promise);
                log('Promise has then: ' + (typeof promise.then));

                promise.then(result => {
                    log('Promise resolved, result: ' + JSON.stringify(result));
                });
            }

            test();
        ");

        await Task.Delay(1000);
    }

    [Fact(Timeout = 5000)]
    public async Task DebugInsideLoopBody_GlobalVsLocal()
    {
        output.WriteLine("=== DEBUG INSIDE LOOP BODY COMPARISON ===");
        output.WriteLine("");

        // Test with LOCAL scope iterable - __debug() INSIDE loop body
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE - Debug Inside Loop ===');

                let localIterable = {
                    [Symbol.iterator]() {
                        log('LOCAL: Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('LOCAL: next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    log('LOCAL: returning value=' + val);
                                    return { value: val, done: false };
                                }
                                log('LOCAL: returning done=true');
                                return { done: true };
                            }
                        };
                    }
                };

                log('LOCAL: Before for-await-of');
                let result = '';
                for await (let item of localIterable) {
                    __debug(); // INSIDE loop body
                    log('LOCAL: Inside loop, item=' + item);
                    result = result + item;
                }
                log('LOCAL: After loop, result=' + result);
                return result;
            }

            test();
        ");

        // Collect debug messages from local scope test
        var localDebugMessages = new List<DebugMessage>();
        while (engine1.DebugMessages().TryRead(out var msg))
        {
            localDebugMessages.Add(msg);
        }

        await Task.Delay(1000);
        var localFinalResult = await engine1.Evaluate("test()");

        output.WriteLine("");
        output.WriteLine($"[LOCAL] Final result: '{localFinalResult}'");
        output.WriteLine($"[LOCAL] Debug messages captured: {localDebugMessages.Count}");

        if (localDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("LOCAL SCOPE DEBUG MESSAGES (from inside loop):");
            for (var i = 0; i < localDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1} - Variables count: {localDebugMessages[i].Variables.Count}");
                // Show key variables
                if (localDebugMessages[i].Variables.ContainsKey("item"))
                {
                    output.WriteLine($"    item = {localDebugMessages[i].Variables["item"]}");
                }

                if (localDebugMessages[i].Variables.ContainsKey("result"))
                {
                    output.WriteLine($"    result = {localDebugMessages[i].Variables["result"]}");
                }

                if (localDebugMessages[i].Variables.ContainsKey("localIterable"))
                {
                    output.WriteLine($"    localIterable = {localDebugMessages[i].Variables["localIterable"]}");
                }
            }
        }

        output.WriteLine("");
        output.WriteLine("===========================================");
        output.WriteLine("");

        // Test with GLOBAL scope iterable - __debug() INSIDE loop body
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE - Debug Inside Loop ===');

            let globalIterable = {
                [Symbol.iterator]() {
                    log('GLOBAL: Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('GLOBAL: next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                log('GLOBAL: returning value=' + val);
                                return { value: val, done: false };
                            }
                            log('GLOBAL: returning done=true');
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('GLOBAL: Before for-await-of');
                let result = '';
                for await (let item of globalIterable) {
                    __debug(); // INSIDE loop body
                    log('GLOBAL: Inside loop, item=' + item);
                    result = result + item;
                }
                log('GLOBAL: After loop, result=' + result);
                return result;
            }

            test();
        ");

        // Collect debug messages from global scope test
        var globalDebugMessages = new List<DebugMessage>();
        while (engine2.DebugMessages().TryRead(out var msg))
        {
            globalDebugMessages.Add(msg);
        }

        await Task.Delay(1000);
        var globalFinalResult = await engine2.Evaluate("test()");

        output.WriteLine("");
        output.WriteLine($"[GLOBAL] Final result: '{globalFinalResult}'");
        output.WriteLine($"[GLOBAL] Debug messages captured: {globalDebugMessages.Count}");

        if (globalDebugMessages.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("GLOBAL SCOPE DEBUG MESSAGES (from inside loop):");
            for (var i = 0; i < globalDebugMessages.Count; i++)
            {
                output.WriteLine($"  Message {i + 1} - Variables count: {globalDebugMessages[i].Variables.Count}");
                // Show key variables
                if (globalDebugMessages[i].Variables.ContainsKey("item"))
                {
                    output.WriteLine($"    item = {globalDebugMessages[i].Variables["item"]}");
                }

                if (globalDebugMessages[i].Variables.ContainsKey("result"))
                {
                    output.WriteLine($"    result = {globalDebugMessages[i].Variables["result"]}");
                }

                if (globalDebugMessages[i].Variables.ContainsKey("globalIterable"))
                {
                    output.WriteLine($"    globalIterable = {globalDebugMessages[i].Variables["globalIterable"]}");
                }
            }
        }
        else
        {
            output.WriteLine("");
            output.WriteLine("*** NO DEBUG MESSAGES CAPTURED - Loop body never executed! ***");
        }

        output.WriteLine("");
        output.WriteLine("=== CONCLUSION ===");
        output.WriteLine($"Local scope: {(localDebugMessages.Count > 0 ? "✅ Loop body executed" : "❌ Loop body NOT executed")}");
        output.WriteLine($"Global scope: {(globalDebugMessages.Count > 0 ? "✅ Loop body executed" : "❌ Loop body NOT executed")}");
    }

    [Fact(Timeout = 5000)]
    public async Task ManualIterationComparison_GlobalVsLocal()
    {
        output.WriteLine("=== MANUAL ITERATION COMPARISON ===");
        output.WriteLine("Testing if manually creating the loop structure makes a difference");
        output.WriteLine("");

        // Test with LOCAL scope - manual iteration
        var engine1 = new JsEngine();
        engine1.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[LOCAL-MANUAL] {msg}");
            return null;
        });

        await engine1.Run(@"
            async function test() {
                log('=== LOCAL SCOPE - Manual Iteration ===');

                let localIterable = {
                    [Symbol.iterator]() {
                        log('Symbol.iterator called');
                        let values = ['x', 'y', 'z'];
                        let index = 0;
                        return {
                            next() {
                                log('next() called, index=' + index);
                                if (index < values.length) {
                                    let val = values[index++];
                                    return { value: val, done: false };
                                }
                                return { done: true };
                            }
                        };
                    }
                };

                log('Getting iterator manually');
                let iterator = localIterable[Symbol.iterator]();
                log('Got iterator: ' + typeof iterator);

                log('Calling next() manually in loop');
                let result = '';
                let iterResult = iterator.next();
                while (!iterResult.done) {
                    log('Manual loop iteration, value=' + iterResult.value);
                    __debug();
                    result = result + iterResult.value;
                    iterResult = iterator.next();
                }

                log('Manual loop done, result=' + result);
                return result;
            }

            test();
        ");

        var localManualDebugMessages = new List<DebugMessage>();
        while (engine1.DebugMessages().TryRead(out var msg))
        {
            localManualDebugMessages.Add(msg);
        }

        await Task.Delay(1000);
        var localManualResult = await engine1.Evaluate("test()");

        output.WriteLine($"[LOCAL-MANUAL] Result: '{localManualResult}'");
        output.WriteLine($"[LOCAL-MANUAL] Debug messages: {localManualDebugMessages.Count}");
        output.WriteLine("");

        // Test with GLOBAL scope - manual iteration
        var engine2 = new JsEngine();
        engine2.SetGlobalFunction("log", args =>
        {
            var msg = args.Count > 0 ? args[0]?.ToString() ?? "null" : "null";
            output.WriteLine($"[GLOBAL-MANUAL] {msg}");
            return null;
        });

        await engine2.Run(@"
            log('=== GLOBAL SCOPE - Manual Iteration ===');

            let globalIterable = {
                [Symbol.iterator]() {
                    log('Symbol.iterator called');
                    let values = ['x', 'y', 'z'];
                    let index = 0;
                    return {
                        next() {
                            log('next() called, index=' + index);
                            if (index < values.length) {
                                let val = values[index++];
                                return { value: val, done: false };
                            }
                            return { done: true };
                        }
                    };
                }
            };

            async function test() {
                log('Getting iterator manually');
                let iterator = globalIterable[Symbol.iterator]();
                log('Got iterator: ' + typeof iterator);

                log('Calling next() manually in loop');
                let result = '';
                let iterResult = iterator.next();
                while (!iterResult.done) {
                    log('Manual loop iteration, value=' + iterResult.value);
                    __debug();
                    result = result + iterResult.value;
                    iterResult = iterator.next();
                }

                log('Manual loop done, result=' + result);
                return result;
            }

            test();
        ");

        var globalManualDebugMessages = new List<DebugMessage>();
        while (engine2.DebugMessages().TryRead(out var msg))
        {
            globalManualDebugMessages.Add(msg);
        }

        await Task.Delay(1000);
        var globalManualResult = await engine2.Evaluate("test()");

        output.WriteLine($"[GLOBAL-MANUAL] Result: '{globalManualResult}'");
        output.WriteLine($"[GLOBAL-MANUAL] Debug messages: {globalManualDebugMessages.Count}");

        output.WriteLine("");
        output.WriteLine("=== MANUAL ITERATION CONCLUSION ===");
        output.WriteLine($"Local scope manual: {(localManualResult?.ToString() == "xyz" ? "✅ Works" : "❌ Failed")}");
        output.WriteLine($"Global scope manual: {(globalManualResult?.ToString() == "xyz" ? "✅ Works" : "❌ Failed")}");
        output.WriteLine("");
        output.WriteLine("If manual iteration works for both, the issue is specific to for-await-of transformation.");
    }
}
