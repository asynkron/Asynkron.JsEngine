using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

public static class SuperTest
{
    public static async Task Run()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate(@"var hit = false; class C { func = () => { super.prop = 'value'; hit = true; }; } var c = new C(); c.func(); ({ hit, value: c.prop })");
            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }
    }
}
