using Asynkron.JsEngine;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class PrototypeChainBugTest
{
    [Fact]
    public async Task PrototypeMethod_CanAccessObjectPropertyOnThis()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function Container(obj){
               this.obj = obj;
            }

            Container.prototype.test = function(){
                var o = this.obj;
                return o.x;
            }

            var c = new Container( {x: 42} );
            c.test();
        ");
        
        Assert.Equal(42.0, result);
    }
    
    [Fact]
    public async Task PrototypeMethod_CanAccessArrayPropertyOnThis()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            function Container(arr){
               this.arr = arr;
            }

            Container.prototype.test = function(){
                return this.arr[0];
            }

            var c = new Container( [1, 2, 3] );
            c.test();
        ");
        
        Assert.Equal(1.0, result);
    }
}
