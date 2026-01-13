namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Focused copies of the TypedArray callback-this tests so we can filter by method name.
/// </summary>
public sealed class TypedArrayCallbackThisFocusedTests : Test262Test
{
    [TestCase("built-ins/TypedArray/prototype/every/BigInt/callbackfn-this.js", true)]
    [TestCase("built-ins/TypedArray/prototype/every/callbackfn-this.js", true)]
    public void TypedArray_callback_this_focused(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [TestCase("built-ins/TypedArray/prototype/reduce/callbackfn-this.js", false)]
    [TestCase("built-ins/TypedArray/prototype/reduce/callbackfn-this.js", true)]
    [TestCase("built-ins/TypedArray/prototype/reduce/BigInt/callbackfn-this.js", false)]
    [TestCase("built-ins/TypedArray/prototype/reduce/BigInt/callbackfn-this.js", true)]
    [TestCase("built-ins/TypedArray/prototype/reduceRight/callbackfn-this.js", false)]
    [TestCase("built-ins/TypedArray/prototype/reduceRight/callbackfn-this.js", true)]
    [TestCase("built-ins/TypedArray/prototype/reduceRight/BigInt/callbackfn-this.js", false)]
    [TestCase("built-ins/TypedArray/prototype/reduceRight/BigInt/callbackfn-this.js", true)]
    public void TypedArray_reduce_callback_this_focused(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
