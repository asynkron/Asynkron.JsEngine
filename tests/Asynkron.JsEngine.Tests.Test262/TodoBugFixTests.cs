namespace Asynkron.JsEngine.Tests.Test262;

/// <summary>
/// Focused tests for fixing bugs from todo-builtins.md
/// </summary>
public sealed class TodoBugFixTests : Test262Test
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

    // FIXED: indexOf handles negative-zero fromIndex and strict equality between -0 and +0
    [TestCase("built-ins/Array/prototype/indexOf/15.4.4.14-5-9.js", false)]
    [TestCase("built-ins/Array/prototype/indexOf/15.4.4.14-5-9.js", true)]
    [TestCase("built-ins/Array/prototype/indexOf/15.4.4.14-9-b-ii-7.js", false)]
    [TestCase("built-ins/Array/prototype/indexOf/15.4.4.14-9-b-ii-7.js", true)]
    public void Array_indexOf_negative_zero_cases(string test, bool strict)
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

    // FIXED: Array.length valueOf coercion - use RealmState.Current for error creation
    [TestCase("built-ins/Array/length/S15.4.5.1_A1.3_T2.js", false)]
    [TestCase("built-ins/Array/length/S15.4.5.1_A1.3_T2.js", true)]
    public void Array_length_valueOf_coercion(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: join separator ToPrimitive conversion (separator converted before length check)
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A3.1_T2.js", false)]
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A3.1_T2.js", true)]
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A3.2_T2.js", false)]
    [TestCase("built-ins/Array/prototype/join/S15.4.4.5_A3.2_T2.js", true)]
    public void Array_join_separator_toPrimitive(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: flatMap/flat should not set length on custom species result (per ES spec)
    [TestCase("built-ins/Array/prototype/flatMap/this-value-ctor-object-species-custom-ctor.js", false)]
    [TestCase("built-ins/Array/prototype/flatMap/this-value-ctor-object-species-custom-ctor.js", true)]
    public void Array_flatMap_species_custom_ctor(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // Check if pop tests pass now
    [TestCase("built-ins/Array/prototype/pop/set-length-array-is-frozen.js", false)]
    [TestCase("built-ins/Array/prototype/pop/set-length-array-is-frozen.js", true)]
    [TestCase("built-ins/Array/prototype/pop/set-length-array-length-is-non-writable.js", false)]
    [TestCase("built-ins/Array/prototype/pop/set-length-array-length-is-non-writable.js", true)]
    public void Array_pop_tests(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: push should use 2^53 - 1 limit for array-like objects, not 2^32 - 1
    [TestCase("built-ins/Array/prototype/push/clamps-to-integer-limit.js", false)]
    [TestCase("built-ins/Array/prototype/push/clamps-to-integer-limit.js", true)]
    public void Array_push_clamps_to_integer_limit(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: push should use SetPropertyOrThrow to properly throw on non-writable properties
    [TestCase("built-ins/Array/prototype/push/length-near-integer-limit-set-failure.js", false)]
    [TestCase("built-ins/Array/prototype/push/length-near-integer-limit-set-failure.js", true)]
    [TestCase("built-ins/Array/prototype/push/length-near-integer-limit.js", false)]
    [TestCase("built-ins/Array/prototype/push/length-near-integer-limit.js", true)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A3.js", false)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A3.js", true)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A4_T1.js", false)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A4_T1.js", true)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A4_T2.js", false)]
    [TestCase("built-ins/Array/prototype/push/S15.4.4.7_A4_T2.js", true)]
    [TestCase("built-ins/Array/prototype/push/throws-with-string-receiver.js", false)]
    [TestCase("built-ins/Array/prototype/push/throws-with-string-receiver.js", true)]
    public void Array_push_setPropertyOrThrow_tests(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    // FIXED: splice and unshift should use 2^53 - 1 limit for array-like objects, not 2^32 - 1
    [TestCase("built-ins/Array/prototype/splice/clamps-length-to-integer-limit.js", false)]
    [TestCase("built-ins/Array/prototype/splice/clamps-length-to-integer-limit.js", true)]
    public void Array_splice_clamps_to_integer_limit(string test, bool strict)
    {
        RunTestCode(test, strict);
    }
}
