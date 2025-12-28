namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Focused tests for fixing bugs from todo-builtins.md
/// </summary>
public class TodoBugFixTests : Test262Test
{
    // FIXED: Array.from iter-set-length-err
    [TestCase("built-ins/Array/from/iter-set-length-err.js", false)]
    [TestCase("built-ins/Array/from/iter-set-length-err.js", true)]
    public void Array_from_iter_set_length_err(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: indexOf should check length before ToIntegerOrInfinity(fromIndex)
    [TestCase("built-ins/Array/prototype/indexOf/length-zero-returns-minus-one.js", false)]
    [TestCase("built-ins/Array/prototype/indexOf/length-zero-returns-minus-one.js", true)]
    public void Array_indexOf_length_zero_returns_minus_one(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: includes should check length before ToIntegerOrInfinity(fromIndex)
    [TestCase("built-ins/Array/prototype/includes/length-zero-returns-false.js", false)]
    [TestCase("built-ins/Array/prototype/includes/length-zero-returns-false.js", true)]
    public void Array_includes_length_zero_returns_false(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: copyWithin should treat undefined end as length
    [TestCase("built-ins/Array/prototype/copyWithin/undefined-end.js", false)]
    [TestCase("built-ins/Array/prototype/copyWithin/undefined-end.js", true)]
    public void Array_copyWithin_undefined_end(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: TypedArray.copyWithin should treat undefined end as length
    [TestCase("built-ins/TypedArray/prototype/copyWithin/undefined-end.js", false)]
    [TestCase("built-ins/TypedArray/prototype/copyWithin/undefined-end.js", true)]
    public void TypedArray_copyWithin_undefined_end(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: join should properly coerce length with valueOf
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A2_T4.js", false)]
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A2_T4.js", true)]
    public void Array_join_length_valueOf(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: copyWithin should throw TypeError when start is a Symbol
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-start-as-symbol.js", false)]
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-start-as-symbol.js", true)]
    public void Array_copyWithin_symbol_start(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: copyWithin should throw TypeError when target is a Symbol
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-target-as-symbol.js", false)]
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-target-as-symbol.js", true)]
    public void Array_copyWithin_symbol_target(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: copyWithin should throw TypeError when end is a Symbol
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-end-as-symbol.js", false)]
    [TestCase("built-ins/Array/prototype/copyWithin/return-abrupt-from-end-as-symbol.js", true)]
    public void Array_copyWithin_symbol_end(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: fill should throw TypeError when start is a Symbol
    [TestCase("built-ins/Array/prototype/fill/return-abrupt-from-start-as-symbol.js", false)]
    [TestCase("built-ins/Array/prototype/fill/return-abrupt-from-start-as-symbol.js", true)]
    public void Array_fill_symbol_start(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: fill should throw TypeError when end is a Symbol
    [TestCase("built-ins/Array/prototype/fill/return-abrupt-from-end-as-symbol.js", false)]
    [TestCase("built-ins/Array/prototype/fill/return-abrupt-from-end-as-symbol.js", true)]
    public void Array_fill_symbol_end(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: flat should throw when depth is a Symbol wrapped in Object.create(null)
    [TestCase("built-ins/Array/prototype/flat/symbol-object-create-null-depth-throws.js", false)]
    [TestCase("built-ins/Array/prototype/flat/symbol-object-create-null-depth-throws.js", true)]
    public void Array_flat_symbol_depth(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
