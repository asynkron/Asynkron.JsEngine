using Asynkron.JsEngine;

var engine = new JsEngine();

var script = @"
function Container(obj){
   this.obj = obj;
}

var proto = {};
proto.test = function(){
    var o = this.obj;
    return o.x;
};
Container.prototype = proto;

var c = new Container( {x: 42} );
c.test();
";

try 
{
    var result = await engine.Evaluate(script);
    Console.WriteLine($"Result: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
