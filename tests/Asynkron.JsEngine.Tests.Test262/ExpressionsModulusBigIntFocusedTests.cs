namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Focused BigInt modulus rows called out by gh1833 so build/review can run
/// a bounded AC-1 classification command without executing the full method pack.
/// </summary>
public sealed class ExpressionsModulusBigIntFocusedTests : Test262Test
{
    [TestCase("language/expressions/modulus/bigint-and-number.js", false)]
    [TestCase("language/expressions/modulus/bigint-and-number.js", true)]
    [TestCase("language/expressions/modulus/bigint-arithmetic.js", false)]
    [TestCase("language/expressions/modulus/bigint-arithmetic.js", true)]
    [TestCase("language/expressions/modulus/bigint-errors.js", false)]
    [TestCase("language/expressions/modulus/bigint-errors.js", true)]
    [TestCase("language/expressions/modulus/bigint-modulo-zero.js", false)]
    [TestCase("language/expressions/modulus/bigint-modulo-zero.js", true)]
    public void Expressions_modulus_bigint_focused(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
