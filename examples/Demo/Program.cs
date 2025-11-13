using Asynkron.JsEngine;

var engine = new JsEngine();
engine.SetGlobalFunction("__debug", args => null);

var script = @"
var SOLAR_MASS = 4 * 3.14 * 3.14;

function Body(vx){
   this.vx = vx;
}

Body.prototype.offsetMomentum = function(px) {
   this.vx = -px / SOLAR_MASS;
   return this;
}

function Sun(){
   return new Body(0.0);
}

function NBodySystem(bodies){
   this.bodies = bodies;
   var px = 10.0;
   __debug();
   this.bodies[0].offsetMomentum(px);
}

var bodies = new NBodySystem( Array(Sun()) );
bodies.bodies[0].vx;
";

try 
{
    var result = await engine.Evaluate(script);
    Console.WriteLine($"Success! Result: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}
