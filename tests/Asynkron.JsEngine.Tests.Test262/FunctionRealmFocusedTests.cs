namespace Asynkron.JsEngine.Tests.Test262;

public sealed class FunctionRealmFocusedTests : Test262Test
{
    [TestCase("built-ins/Function/internals/Call/class-ctor-realm.js", false)]
    [TestCase("built-ins/Function/internals/Call/class-ctor-realm.js", true)]
    public void Function_internals_Call_class_ctor_realm(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [TestCase("built-ins/Function/internals/Construct/derived-return-val-realm.js", false)]
    [TestCase("built-ins/Function/internals/Construct/derived-return-val-realm.js", true)]
    public void Function_internals_Construct_derived_return_val_realm(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [TestCase("built-ins/Function/internals/Construct/derived-this-uninitialized-realm.js", false)]
    [TestCase("built-ins/Function/internals/Construct/derived-this-uninitialized-realm.js", true)]
    public void Function_internals_Construct_derived_this_uninitialized_realm(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
