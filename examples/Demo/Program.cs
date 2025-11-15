using Asynkron.JsEngine;

var engine = new JsEngine();
var script = @"
function Body(x) {
    this.x = x;
}

Body.prototype.double = function() {
    return this.x * 2;
};

function Container(bodies){
   this.bodies = bodies;
}

Container.prototype.sum = function(){
    var total = 0;
    for (var i = 0; i < this.bodies.length; i++) {
       var b = this.bodies[i];
       total += b.double();
    }
    return total;
}

var c = new Container( Array(new Body(5), new Body(10), new Body(15)) );
c.sum();
";

try 
{
    var result = await engine.Evaluate(script).ConfigureAwait(false);
    Console.WriteLine($"Success! Result: {result} (expected: 60)");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
