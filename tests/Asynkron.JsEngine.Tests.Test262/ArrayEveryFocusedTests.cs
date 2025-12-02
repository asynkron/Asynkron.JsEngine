using NUnit.Framework;

namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Focused copy of a single Array.prototype.every test so we can filter by method name.
/// </summary>
public class ArrayEveryFocusedTests : Test262Test
{
    [TestCase("built-ins/Array/prototype/every/15.4.4.16-8-10.js", false)]
    public void Array_prototype_every_focused(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
