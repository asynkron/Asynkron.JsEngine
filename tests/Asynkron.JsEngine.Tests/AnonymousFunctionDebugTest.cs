namespace Asynkron.JsEngine.Tests;

public class AnonymousFunctionDebugTest
{
    [Fact]
    public async Task Debug_AnonymousRecursiveFunction_ShowEnvironments()
    {
        await using var engine = CreateDebugEngine();

        var source = @"
var __func = function (arg){
    __debug();
    if (arg === 1) {
        return arg;
    } else {
        return __func(arg-1)*arg;
    }
};
__func(3);
";

        await engine.Evaluate(source);

        // Read all debug messages (one per recursive call)
        var messages = new List<DebugMessage>();
        while (engine.DebugMessages().TryRead(out var msg))
        {
            messages.Add(msg);
        }

        // Print what we captured - focus on first call
        var firstMsg = messages[0];
        Console.WriteLine($"=== Debug call 1 (arg=3) ===");
        Console.WriteLine($"Environment chain ({firstMsg.EnvironmentChain.Count} environments):");
        foreach (var (env, i) in firstMsg.EnvironmentChain.Select((e, i) => (e, i)))
        {
            Console.WriteLine($"  [{i}] ScopeId={env.ScopeId}, HasSlots={env.HasSlots}, SlotCount={env.SlotCount}, Desc={env.Description ?? "(none)"}");
            if (env.SlotVariables.Count > 0)
            {
                Console.WriteLine($"      Slots:");
                foreach (var kvp in env.SlotVariables)
                {
                    Console.WriteLine($"        [{kvp.Key}] = {kvp.Value}");
                }
            }
            if (env.DictionaryVariables.Count > 0)
            {
                Console.WriteLine($"      Dictionary:");
                foreach (var kvp in env.DictionaryVariables)
                {
                    Console.WriteLine($"        {kvp.Key} = {kvp.Value}");
                }
            }
        }

        // We expect 3 debug calls (for arg=3, arg=2, arg=1)
        Assert.Equal(3, messages.Count);

        // Find the environment with arg in slots
        var funcEnv = firstMsg.EnvironmentChain.FirstOrDefault(e => e.HasSlots && e.SlotCount > 0);
        Assert.NotNull(funcEnv);
        Console.WriteLine($"\nFunction env: ScopeId={funcEnv.ScopeId}, arg in slot 0 = {funcEnv.SlotVariables.GetValueOrDefault(0)}");

        // Find the script env with __func
        var scriptEnv = firstMsg.EnvironmentChain.FirstOrDefault(e => e.DictionaryVariables.ContainsKey("__func"));
        Assert.NotNull(scriptEnv);
        Console.WriteLine($"Script env: ScopeId={scriptEnv.ScopeId}, has __func in dictionary");
    }

    private static JsEngine CreateDebugEngine()
    {
        return new JsEngine(new JsEngineOptions { DebugMode = true });
    }
}
