using System;
using System.Threading.Tasks;
using Asynkron.JsEngine;

internal static class Program
{
    private static async Task Main()
    {
        var script = @"var f = async() => {
  try {
    await new Promise(function(resolve, reject) {
      reject('early-reject');
    });
  } finally {
    return 'override';
  }
};

f().then(
  function(v) { print('fulfill:' + v); },
  function(e) { print('reject:' + e); }
).then(
  function(_) { print('done fulfill'); },
  function(e) { print('done reject:' + e); }
);
";

        var engine = new JsEngine();
        engine.ExecutionTimeout = TimeSpan.FromSeconds(1);
        var t = engine.Evaluate(script);
        var completed = await Task.WhenAny(t, Task.Delay(5000));
        Console.WriteLine($"Evaluate completed: {ReferenceEquals(completed, t)} status={t.Status}");
        if (t.IsCompletedSuccessfully)
        {
            Console.WriteLine($"result: {t.Result}");
        }
        else if (t.IsFaulted)
        {
            Console.WriteLine($"faulted: {t.Exception}");
        }
    }
}
