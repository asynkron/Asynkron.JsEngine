using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class NBodyPrototypeTest
{
    [Fact]
    public async Task PrototypeMethod_CallOnArrayElement_Works()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function Body(x) {
                this.x = x;
            }
            
            Body.prototype.double = function() {
                return this.x * 2;
            };
            
            var arr = [new Body(5), new Body(10)];
            arr[0].double();
        ");
        
        Assert.Equal(10.0, result);
    }
    
    [Fact]
    public async Task PrototypeMethod_CallOnArrayElementFromConstructorCreatedArray_Works()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function Body(x) {
                this.x = x;
            }
            
            Body.prototype.double = function() {
                return this.x * 2;
            };
            
            function makeBody(x) {
                return new Body(x);
            }
            
            var arr = Array(makeBody(5), makeBody(10));
            arr[0].double();
        ");
        
        Assert.Equal(10.0, result);
    }
    
    [Fact]
    public async Task OffsetMomentum_Simplified()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            var SOLAR_MASS = 4 * 3.14 * 3.14;
            
            function Body(vx) {
                this.vx = vx;
            }
            
            Body.prototype.offsetMomentum = function(px) {
                this.vx = -px / SOLAR_MASS;
                return this;
            };
            
            function Sun() {
                return new Body(0.0);
            }
            
            var bodies = Array(Sun());
            bodies[0].offsetMomentum(10.0);
            bodies[0].vx;
        ");
        
        // Should be -10.0 / SOLAR_MASS
        Assert.NotEqual(0.0, result);
    }
}
