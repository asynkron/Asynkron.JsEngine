using Asynkron.JsEngine;

var engine = new JsEngine();
try
{
    await engine.Evaluate("10n + 5");
}
catch (Exception ex)
{
    Console.WriteLine("Exception message:");
    Console.WriteLine(ex.Message);
    Console.WriteLine("Length: " + ex.Message.Length);
}
