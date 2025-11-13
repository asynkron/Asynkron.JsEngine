using Asynkron.JsEngine;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class NBodyEnergyTest(ITestOutputHelper output)
{
    [Fact]
    public async Task Energy_MethodCall_Works()
    {
        var engine = new JsEngine();
        
        engine.SetGlobalFunction("__log", args =>
        {
            output.WriteLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
            return null;
        });
        
        var result = await engine.Evaluate(@"
            var PI = 3.141592653589793;
            var SOLAR_MASS = 4 * PI * PI;
            var DAYS_PER_YEAR = 365.24;

            function Body(x,y,z,vx,vy,vz,mass){
               this.x = x;
               this.y = y;
               this.z = z;
               this.vx = vx;
               this.vy = vy;
               this.vz = vz;
               this.mass = mass;
            }

            Body.prototype.offsetMomentum = function(px,py,pz) {
               this.vx = -px / SOLAR_MASS;
               this.vy = -py / SOLAR_MASS;
               this.vz = -pz / SOLAR_MASS;
               return this;
            }

            function Sun(){
               return new Body(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, SOLAR_MASS);
            }

            function Jupiter(){
               return new Body(
                  4.84143144246472090e+00,
                  -1.16032004402742839e+00,
                  -1.03622044471123109e-01,
                  1.66007664274403694e-03 * DAYS_PER_YEAR,
                  7.69901118419740425e-03 * DAYS_PER_YEAR,
                  -6.90460016972063023e-05 * DAYS_PER_YEAR,
                  9.54791938424326609e-04 * SOLAR_MASS
               );
            }

            function NBodySystem(bodies){
               this.bodies = bodies;
               var px = 0.0;
               var py = 0.0;
               var pz = 0.0;
               var size = this.bodies.length;
               for (var i=0; i<size; i++){
                  var b = this.bodies[i];
                  var m = b.mass;
                  px += b.vx * m;
                  py += b.vy * m;
                  pz += b.vz * m;
               }
               this.bodies[0].offsetMomentum(px,py,pz);
            }

            NBodySystem.prototype.energy = function(){
               var dx, dy, dz, distance;
               var e = 0.0;
               var size = this.bodies.length;

               for (var i=0; i<size; i++) {
                  var bodyi = this.bodies[i];

                  e += 0.5 * bodyi.mass *
                     ( bodyi.vx * bodyi.vx
                     + bodyi.vy * bodyi.vy
                     + bodyi.vz * bodyi.vz );

                  for (var j=i+1; j<size; j++) {
                     var bodyj = this.bodies[j];
                     dx = bodyi.x - bodyj.x;
                     dy = bodyi.y - bodyj.y;
                     dz = bodyi.z - bodyj.z;

                     distance = Math.sqrt(dx*dx + dy*dy + dz*dz);
                     e -= (bodyi.mass * bodyj.mass) / distance;
                  }
               }
               return e;
            }

            __log('Creating NBodySystem...');
            var bodies = new NBodySystem( Array(Sun(), Jupiter()) );
            __log('Calling energy()...');
            bodies.energy();
        ");
        
        Assert.IsType<double>(result);
        output.WriteLine($"Energy result: {result}");
    }
}
