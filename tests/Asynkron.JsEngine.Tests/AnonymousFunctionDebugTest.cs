using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public sealed class AnonymousFunctionDebugTest(ITestOutputHelper testOutputHelper) : InternalTestBase(testOutputHelper)
{
    [Fact]
    public async Task Debug_AnonymousRecursiveFunction_ShowEnvironments()
    {
        await using var engine = CreateDebugEngine();

        var source = """

                     var __func = function (arg){
                         __debug();
                         if (arg === 1) {
                             return arg;
                         } else {
                             return __func(arg-1)*arg;
                         }
                     };
                     __func(3);

                     """;

        await engine.Evaluate(source);

        // Read all debug messages (one per recursive call)
        var messages = new List<DebugMessage>();
        while (engine.DebugMessages().TryRead(out var msg))
        {
            messages.Add(msg);
        }

        // Print what we captured - focus on first call
        var firstMsg = messages[0];
        Output.WriteLine("=== Debug call 1 (arg=3) ===");
        Output.WriteLine($"Environment chain ({firstMsg.EnvironmentChain.Count} environments):");
        foreach (var (env, i) in firstMsg.EnvironmentChain.Select((e, i) => (e, i)))
        {
            Output.WriteLine($"  [{i}] ScopeId={env.ScopeId}, HasSlots={env.HasSlots}, SlotCount={env.SlotCount}, Desc={env.Description ?? "(none)"}");
            if (env.SlotVariables.Count > 0)
            {
                Output.WriteLine("      Slots:");
                foreach (var kvp in env.SlotVariables)
                {
                    Output.WriteLine($"        [{kvp.Key}] = {kvp.Value}");
                }
            }
            if (env.DictionaryVariables.Count > 0)
            {
                Output.WriteLine("      Dictionary:");
                foreach (var kvp in env.DictionaryVariables)
                {
                    Output.WriteLine($"        {kvp.Key} = {kvp.Value}");
                }
            }
        }

        // We expect 3 debug calls (for arg=3, arg=2, arg=1)
        Assert.Equal(3, messages.Count);

        // Find the environment with arg - it may be in slots (fast path mode) or dictionary (reference mode)
        var funcEnv = firstMsg.EnvironmentChain.FirstOrDefault(e => e.HasSlots && e.SlotCount > 0);
        if (funcEnv is not null)
        {
            // Fast path mode - arg should be in slot 0
            Output.WriteLine($"\nFunction env (slots): ScopeId={funcEnv.ScopeId}, arg in slot 0 = {funcEnv.SlotVariables.GetValueOrDefault(0)}");
        }
        else
        {
            // Reference mode - arg should be in dictionary
            funcEnv = firstMsg.EnvironmentChain.FirstOrDefault(e => e.DictionaryVariables.ContainsKey("arg"));
            Assert.NotNull(funcEnv);
            Output.WriteLine($"\nFunction env (dict): ScopeId={funcEnv.ScopeId}, arg = {funcEnv.DictionaryVariables["arg"]}");
        }

        // Find the script env with __func
        var scriptEnv = firstMsg.EnvironmentChain.FirstOrDefault(e => e.DictionaryVariables.ContainsKey("__func"));
        Assert.NotNull(scriptEnv);
        Output.WriteLine($"Script env: ScopeId={scriptEnv.ScopeId}, has __func in dictionary");
    }

    private JsEngine CreateDebugEngine()
    {
        return TestEngineFactory.CreateDebugEngine(nameof(AnonymousFunctionDebugTest));
    }
}
