using Asynkron.JsEngine;

var engine = new JsEngine(new JsEngineOptions { DebugMode = true });

var script = @"
function SwitchTest1(value){
  var result = 0;
  try{  
    switch(value) {
      case 1:
        result += 4;
        throw result;
        break;
      case 4:
        result += 64;
        throw 'ex';
    }
    return result;
  }
  catch(e){
    // caught exception
  }
  finally{
    return result;
  }
}
SwitchTest1(1);
";

try {
    var result = engine.Evaluate(script);
    Console.WriteLine($"Result: {result} (expected: 4)");
} catch (Exception ex) {
    Console.WriteLine($"Exception: {ex.Message}");
}
