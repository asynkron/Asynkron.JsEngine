using Xunit;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests to verify that control flow (break, continue, return) works correctly
/// with the new signal-based implementation.
/// </summary>
public class SignalPatternTests
{
    [Fact(Timeout = 2000)]
    public async Task WhileLoop_WithBreak_WorksCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let count = 0;
                                                       while (true) {
                                                           count++;
                                                           if (count >= 5) {
                                                               break;
                                                           }
                                                       }
                                                       count;
                                                   
                                           """);
        
        Assert.Equal(5.0, result);
    }
    
    [Fact(Timeout = 2000)]
    public async Task WhileLoop_WithContinue_WorksCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let sum = 0;
                                                       let i = 0;
                                                       while (i < 10) {
                                                           i++;
                                                           if (i % 2 === 0) {
                                                               continue;
                                                           }
                                                           sum += i;
                                                       }
                                                       sum;
                                                   
                                           """);
        
        // Sum of odd numbers from 1 to 9: 1+3+5+7+9 = 25
        Assert.Equal(25.0, result);
    }
    
    [Fact(Timeout = 2000)]
    public async Task Function_WithReturn_WorksCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test() {
                                                           let x = 10;
                                                           return x * 2;
                                                       }
                                                       test();
                                                   
                                           """);
        
        Assert.Equal(20.0, result);
    }
    
    [Fact(Timeout = 2000)]
    public async Task NestedLoops_WithBreakAndContinue_WorkCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       let result = 0;
                                                       for (let i = 0; i < 5; i++) {
                                                           for (let j = 0; j < 5; j++) {
                                                               if (j === 2) continue;
                                                               if (i === 3 && j === 4) break;
                                                               result++;
                                                           }
                                                       }
                                                       result;
                                                   
                                           """);
        
        // i=0: j runs 0,1,3,4 (skip 2) = 4
        // i=1: j runs 0,1,3,4 (skip 2) = 4
        // i=2: j runs 0,1,3,4 (skip 2) = 4
        // i=3: j runs 0,1,3 (skip 2, break at 4) = 3
        // i=4: j runs 0,1,3,4 (skip 2) = 4
        // Total: 4+4+4+3+4 = 19
        Assert.Equal(19.0, result);
    }
    
    [Fact(Timeout = 2000)]
    public async Task TryCatchFinally_WithReturn_WorksCorrectly()
    {
        var engine = new JsEngine();
        var result = await engine.Evaluate("""

                                                       function test() {
                                                           try {
                                                               return 'from try';
                                                           } finally {
                                                               // finally executes even with return
                                                           }
                                                       }
                                                       test();
                                                   
                                           """);
        
        Assert.Equal("from try", result);
    }
}
