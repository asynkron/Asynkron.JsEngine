// using Asynkron.JsEngine.JsTypes;
// using Xunit.Abstractions;
//
// namespace Asynkron.JsEngine.Tests;
//
// public abstract class NBodyFiveBodyTestBase(ITestOutputHelper output) : FastPathTestBase(output)
// {
//     [Fact(Timeout = 10000)]
//     public async Task FiveBodies_Energy_Works()
//     {
//         await using var engine = CreateEngine();
//         engine.SetGlobalFunction("__debug", _ =>  JsValue.Null);
//
//         var content = SunSpiderTestsBase.GetEmbeddedFile("access-nbody.js");
//
//         await engine.Evaluate(content);
//     }
//
//     [Fact]
//     public async Task FiveBodies_FullTest_Works()
//     {
//         await using var engine = CreateEngine();
//         engine.SetGlobalFunction("__debug", _ =>  JsValue.Null);
//
//         var content = SunSpiderTestsBase.GetEmbeddedFile("access-nbody.js");
//
//         // Run the script - should throw a ThrowSignal with the expected error
//         try
//         {
//             await engine.Evaluate(content);
//             // If we get here, the test passed
//             Assert.True(true);
//         }
//         catch (ThrowSignal ex)
//         {
//             // JavaScript threw an error - this is a failure
//             var message = !ex.ThrownValue.IsUndefined ? ex.ThrownValue.ToString() : "undefined";
//             throw new InvalidOperationException($"JavaScript error: {message}", ex);
//         }
//     }
// }
//
// public class FastPath_NBodyFiveBodyTest(ITestOutputHelper output) : NBodyFiveBodyTestBase(output)
// {
//     protected override bool EnableFastPaths => true;
// }
//
// public class Reference_NBodyFiveBodyTest(ITestOutputHelper output) : NBodyFiveBodyTestBase(output)
// {
//     protected override bool EnableFastPaths => false;
// }
