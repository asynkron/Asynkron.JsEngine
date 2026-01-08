using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Direct tests for JsEnvironment slot operations without evaluation.
/// This isolates slot storage behavior from the evaluation pipeline.
/// </summary>
[Category(TestCategories.ScopeAnalysis)]
public sealed class JsEnvironmentSlotTests(ITestOutputHelper output)
{
    [Fact]
    public void DefineSlot_CreatesSlot()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(10), SlotFlags.None);

        ref var slot = ref env.TryGetSlotRef(name);
        Assert.False(System.Runtime.CompilerServices.Unsafe.IsNullRef(ref slot));
        Assert.Equal(10.0, slot.Value.AsDouble());
        output.WriteLine($"Slot value after define: {slot.Value.AsDouble()}");
    }

    [Fact]
    public void TryGetSlotRef_ModifyValue_Persists()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        ref var slot = ref env.TryGetSlotRef(name);
        Assert.False(System.Runtime.CompilerServices.Unsafe.IsNullRef(ref slot));

        output.WriteLine($"Initial value: {slot.Value.AsDouble()}");

        // Modify the slot value directly via ref
        slot.Value = JsValue.FromDouble(5);

        output.WriteLine($"After assignment: {slot.Value.AsDouble()}");

        // Re-read to verify persistence
        ref var slot2 = ref env.TryGetSlotRef(name);
        output.WriteLine($"Re-read value: {slot2.Value.AsDouble()}");

        Assert.Equal(5.0, slot2.Value.AsDouble());
    }

    [Fact]
    public void SetBindingValueDirect_Persists()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        output.WriteLine($"Initial value via GetBindingValueDirect: {env.GetBindingValueDirect(name).AsDouble()}");

        // Use SetBindingValueDirect
        env.SetBindingValueDirect(name, JsValue.FromDouble(5));

        var result = env.GetBindingValueDirect(name);
        output.WriteLine($"After SetBindingValueDirect: {result.AsDouble()}");

        Assert.Equal(5.0, result.AsDouble());
    }

    [Fact]
    public void MultipleWrites_AllPersist()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        for (var i = 1; i <= 5; i++)
        {
            ref var slot = ref env.TryGetSlotRef(name);
            slot.Value = JsValue.FromDouble(i);

            var readBack = env.GetBindingValueDirect(name);
            output.WriteLine($"Iteration {i}: wrote {i}, read back {readBack.AsDouble()}");
            Assert.Equal((double)i, readBack.AsDouble());
        }
    }

    [Fact]
    public void ResolvedIdentifierBinding_WriteJsValue_Persists()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        // Create a ResolvedIdentifierBinding (this is what the assignment code path uses)
        var binding = new JsEnvironment.ResolvedIdentifierBinding(env, name);

        output.WriteLine($"Initial value: {env.GetBindingValueDirect(name).AsDouble()}");

        // Write via ResolvedIdentifierBinding
        binding.WriteJsValue(JsValue.FromDouble(5), isStrictContext: false);

        var result = env.GetBindingValueDirect(name);
        output.WriteLine($"After WriteJsValue: {result.AsDouble()}");

        Assert.Equal(5.0, result.AsDouble());
    }

    [Fact]
    public void TryLocateBinding_FindsCorrectEnvironment()
    {
        var outer = new JsEnvironment();
        var inner = new JsEnvironment(enclosing: outer);

        var outerName = Symbol.Intern("x");
        outer.DefineSlot(outerName, JsValue.FromDouble(10), SlotFlags.None);

        // Verify inner can locate binding in outer
        var found = inner.TryLocateBindingInternal(outerName, out var bindingEnv);

        output.WriteLine($"Found: {found}, Environment: {(bindingEnv == outer ? "outer" : "inner")}");

        Assert.True(found);
        Assert.Same(outer, bindingEnv);
    }

    [Fact]
    public void TryLocateBinding_WriteToFoundEnvironment_Persists()
    {
        var outer = new JsEnvironment();
        var inner = new JsEnvironment(enclosing: outer);

        var name = Symbol.Intern("x");
        outer.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        output.WriteLine($"Initial value in outer: {outer.GetBindingValueDirect(name).AsDouble()}");

        // Locate from inner
        var found = inner.TryLocateBindingInternal(name, out var bindingEnv);
        Assert.True(found);
        Assert.Same(outer, bindingEnv);

        // Write via located environment
        bindingEnv!.SetBindingValueDirect(name, JsValue.FromDouble(5));

        // Verify the value persisted in outer
        var result = outer.GetBindingValueDirect(name);
        output.WriteLine($"After write via located env: {result.AsDouble()}");

        Assert.Equal(5.0, result.AsDouble());
    }

    [Fact]
    public void ResolvedIdentifierBinding_ViaLocatedEnvironment_WritesPersist()
    {
        var outer = new JsEnvironment();
        var inner = new JsEnvironment(enclosing: outer);

        var name = Symbol.Intern("x");
        outer.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        output.WriteLine($"Initial value in outer: {outer.GetBindingValueDirect(name).AsDouble()}");

        // Locate from inner
        var found = inner.TryLocateBindingInternal(name, out var bindingEnv);
        Assert.True(found);

        // Create ResolvedIdentifierBinding with the found environment
        var binding = new JsEnvironment.ResolvedIdentifierBinding(bindingEnv!, name);

        // Write via binding
        binding.WriteJsValue(JsValue.FromDouble(5), isStrictContext: false);

        // Verify the value persisted
        var result = outer.GetBindingValueDirect(name);
        output.WriteLine($"After WriteJsValue via binding: {result.AsDouble()}");

        Assert.Equal(5.0, result.AsDouble());
    }

    [Fact]
    public void GlobalEnvironment_VarBinding_WritesPersist()
    {
        // Simulate the environment structure for global scope
        var globalEnv = new JsEnvironment(
            isFunctionScope: true,
            treatAsGlobalFunctionScope: true);

        var name = Symbol.Intern("b");

        // Simulate 'var b = 0' - var declarations go in the function scope (global in this case)
        globalEnv.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        output.WriteLine($"After 'var b = 0': {globalEnv.GetBindingValueDirect(name).AsDouble()}");

        // Locate the binding (this is what assignment does)
        var found = globalEnv.TryLocateBindingInternal(name, out var bindingEnv);
        Assert.True(found);
        Assert.Same(globalEnv, bindingEnv);

        output.WriteLine($"Binding located in: {(bindingEnv == globalEnv ? "globalEnv" : "other")}");

        // Write via binding (simulating 'b = 1')
        var binding = new JsEnvironment.ResolvedIdentifierBinding(bindingEnv!, name);
        binding.WriteJsValue(JsValue.FromDouble(1), isStrictContext: false);

        // Verify
        var result = globalEnv.GetBindingValueDirect(name);
        output.WriteLine($"After 'b = 1': {result.AsDouble()}");

        Assert.Equal(1.0, result.AsDouble());
    }

    [Fact]
    public void TryFindBindingJsValue_Works()
    {
        var env = new JsEnvironment();
        var name = Symbol.Intern("x");

        env.DefineSlot(name, JsValue.FromDouble(0), SlotFlags.None);

        // This is what the assignment path uses
        var found = env.TryFindBindingJsValue(name, allowUninitialized: false, out var bindingEnv, out var value);

        output.WriteLine($"Found: {found}, BindingEnv: {(bindingEnv == env ? "same" : "different")}, Value: {value.AsDouble()}");

        Assert.True(found);
        Assert.Same(env, bindingEnv);
        Assert.Equal(0.0, value.AsDouble());
    }
}
