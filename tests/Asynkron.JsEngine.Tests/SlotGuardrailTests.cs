using Asynkron.JsEngine.Ast;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Runtime;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class SlotGuardrailTests : InternalTestBase
{
    public SlotGuardrailTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public void ReadSlotNameMismatchFallsBackToDynamicLookup()
    {
        var realm = new RealmState();
        var context = new EvaluationContext(realm);

        var outer = new JsEnvironment(null, isFunctionScope: true);
        outer.InitializeSlots(1, scopeId: 1);
        var symbolA = Symbol.Intern("a");
        outer.DefineJsValue(symbolA, new JsValue(123), isLexicalBinding: true);

        // Inner environment has a different binding at slot 0 but shares the same scopeId (collision).
        var inner = new JsEnvironment(outer, isFunctionScope: true);
        inner.InitializeSlots(1, scopeId: 1);
        inner.ScopeId = 1;
        inner._slots![0] = new JsSlot(Symbol.Intern("other"), new JsValue(999), SlotFlags.Lexical);

        Assert.True(inner.TryReadIdentifierWithSlot(symbolA, scopeId: 1, slotIndex: 0, context, out var value));
        Assert.Equal(123.0, value.AsDouble());
    }

    [Fact]
    public void WriteSlotNameMismatchFallsBackToDynamicLookup()
    {
        var realm = new RealmState();
        var context = new EvaluationContext(realm);

        var outer = new JsEnvironment(null, isFunctionScope: true);
        outer.InitializeSlots(1, scopeId: 10);
        var symbolA = Symbol.Intern("a");
        outer._slots![0] = new JsSlot(symbolA, new JsValue(1), SlotFlags.Lexical);

        var inner = new JsEnvironment(outer, isFunctionScope: true);
        inner.InitializeSlots(1, scopeId: 10);
        inner.ScopeId = 10;
        inner._slots![0] = new JsSlot(Symbol.Intern("shadow"), new JsValue(5), SlotFlags.Lexical);

        var updated = new JsValue(42);
        var identifier = new IdentifierExpression(Source: null, symbolA, SlotIndex: 0, ScopeId: 10);
        Assert.True(inner.TryWriteIdentifierWithSlot(identifier, updated, context));

        Assert.True(outer.TryReadSlotValue(symbolA, 0, context, out var outerValue));
        Assert.Equal(42.0, outerValue.AsDouble());

        // Inner mismatched slot remains untouched.
        Assert.True(inner.TryReadSlotValue(Symbol.Intern("shadow"), 0, context, out var shadowValue));
        Assert.Equal(5.0, shadowValue.AsDouble());
    }
}
