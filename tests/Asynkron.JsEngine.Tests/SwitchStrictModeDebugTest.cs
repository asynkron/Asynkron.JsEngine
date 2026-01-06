using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SwitchStrictModeDebugTest
{
    private readonly ITestOutputHelper _output;

    public SwitchStrictModeDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DebugSwitchCaseFunctionInStrictMode()
    {
        var code = @"
""use strict"";

var err1, err2;

(function() {
  try {
    f;
  } catch (exception) {
    err1 = exception;
  }

  switch (1) {
    case 1:
      function f() {  }
  }

  try {
    f;
  } catch (exception) {
    err2 = exception;
  }
}());

[err1, err2];
";

        var engine = new JsEngine();
        var result = engine.Evaluate(code);
        
        _output.WriteLine($"Result: {result}");
        _output.WriteLine($"Result type: {result?.GetType().FullName}");
        
        // Check if the result is an array with two elements
        if (result is System.Collections.IList list && list.Count == 2)
        {
            _output.WriteLine($"err1: {list[0]}");
            _output.WriteLine($"err1 type: {list[0]?.GetType().Name}");
            _output.WriteLine($"err2: {list[1]}");
            _output.WriteLine($"err2 type: {list[1]?.GetType().Name}");
        }
    }
}
