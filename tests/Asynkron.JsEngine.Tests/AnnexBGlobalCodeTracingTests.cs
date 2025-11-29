using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Tests.Tracing;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class AnnexBGlobalCodeTracingTests
{
    private readonly ITestOutputHelper _output;

    public AnnexBGlobalCodeTracingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GlobalIfElseFunctionDeclarationInitializesBinding()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Global.IfElse");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var reportDoc = await EvaluateReportAsync(engine, """
function snapshot(desc) {
    return desc ? {
        enumerable: desc.enumerable,
        writable: desc.writable,
        configurable: desc.configurable
    } : null;
}
var before = typeof f;
var descBefore = Object.getOwnPropertyDescriptor(this, "f");
if (false) ; else function f() { return "else"; }
var after = typeof f;
var descAfter = Object.getOwnPropertyDescriptor(this, "f");
var invocation = typeof f === "function" ? f() : null;
JSON.stringify({
    before,
    after,
    invocation,
    descBefore: snapshot(descBefore),
    descAfter: snapshot(descAfter)
});
""");

        root.Stop();

        using (reportDoc)
        {
            var rootElement = reportDoc.RootElement;
            Assert.Equal("undefined", rootElement.GetProperty("before").GetString());
            Assert.Equal("function", rootElement.GetProperty("after").GetString());
            Assert.Equal("else", rootElement.GetProperty("invocation").GetString());

            Assert.NotEqual(JsonValueKind.Null, rootElement.GetProperty("descBefore").ValueKind);
            Assert.True(rootElement.GetProperty("descBefore").GetProperty("enumerable").GetBoolean());
            Assert.True(rootElement.GetProperty("descBefore").GetProperty("writable").GetBoolean());
            Assert.False(rootElement.GetProperty("descBefore").GetProperty("configurable").GetBoolean());

            Assert.NotEqual(JsonValueKind.Null, rootElement.GetProperty("descAfter").ValueKind);
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("enumerable").GetBoolean());
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("writable").GetBoolean());
            Assert.False(rootElement.GetProperty("descAfter").GetProperty("configurable").GetBoolean());
        }

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:IfStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task GlobalSwitchCaseFunctionDeclarationInitializesBinding()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Global.SwitchCase.Init");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var reportDoc = await EvaluateReportAsync(engine, """
function snapshot(desc) {
    return desc ? {
        enumerable: desc.enumerable,
        writable: desc.writable,
        configurable: desc.configurable
    } : null;
}
var before = typeof f;
var descBefore = Object.getOwnPropertyDescriptor(this, "f");
switch (1) {
  case 1:
    function f() { return "case"; }
}
var after = typeof f;
var descAfter = Object.getOwnPropertyDescriptor(this, "f");
JSON.stringify({
    before,
    after,
    invocation: typeof f === "function" ? f() : null,
    descBefore: snapshot(descBefore),
    descAfter: snapshot(descAfter)
});
""");

        root.Stop();

        using (reportDoc)
        {
            var rootElement = reportDoc.RootElement;
            Assert.Equal("undefined", rootElement.GetProperty("before").GetString());
            Assert.Equal("function", rootElement.GetProperty("after").GetString());
            Assert.Equal("case", rootElement.GetProperty("invocation").GetString());
            Assert.NotEqual(JsonValueKind.Null, rootElement.GetProperty("descBefore").ValueKind);
            Assert.NotEqual(JsonValueKind.Null, rootElement.GetProperty("descAfter").ValueKind);
        }

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:SwitchStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task GlobalSwitchDefaultFunctionDeclarationInitializesBinding()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Global.SwitchDefault.Init");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var reportDoc = await EvaluateReportAsync(engine, """
function snapshot(desc) {
    return desc ? {
        enumerable: desc.enumerable,
        writable: desc.writable,
        configurable: desc.configurable
    } : null;
}
var before = typeof f;
var descBefore = Object.getOwnPropertyDescriptor(this, "f");
switch (1) {
  default:
    function f() { return "default"; }
}
var after = typeof f;
var descAfter = Object.getOwnPropertyDescriptor(this, "f");
JSON.stringify({
    before,
    after,
    invocation: typeof f === "function" ? f() : null,
    descBefore: snapshot(descBefore),
    descAfter: snapshot(descAfter)
});
""");

        root.Stop();

        using (reportDoc)
        {
            var rootElement = reportDoc.RootElement;
            Assert.Equal("undefined", rootElement.GetProperty("before").GetString());
            Assert.Equal("function", rootElement.GetProperty("after").GetString());
            Assert.Equal("default", rootElement.GetProperty("invocation").GetString());
        }

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:SwitchStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task GlobalSwitchCaseLegacyBindingPreservesExistingDescriptor()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate("Object.defineProperty(this, 'f', { value: 'x', enumerable: true, writable: true, configurable: false });");

        using var root = new Activity("AnnexB.Global.SwitchCase.Existing");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var reportDoc = await EvaluateReportAsync(engine, """
function snapshot(desc) {
    return desc ? {
        enumerable: desc.enumerable,
        writable: desc.writable,
        configurable: desc.configurable
    } : null;
}
var beforeValue = this.f;
var beforeDesc = Object.getOwnPropertyDescriptor(this, "f");
switch (1) {
  case 1:
    function f() { return "inner declaration"; }
}
var afterDesc = Object.getOwnPropertyDescriptor(this, "f");
JSON.stringify({
    beforeValue,
    afterInvocation: typeof f === "function" ? f() : null,
    descBefore: snapshot(beforeDesc),
    descAfter: snapshot(afterDesc)
});
""");

        root.Stop();

        using (reportDoc)
        {
            var rootElement = reportDoc.RootElement;
            Assert.Equal("x", rootElement.GetProperty("beforeValue").GetString());
            Assert.Equal("inner declaration", rootElement.GetProperty("afterInvocation").GetString());
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("enumerable").GetBoolean());
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("writable").GetBoolean());
            Assert.False(rootElement.GetProperty("descAfter").GetProperty("configurable").GetBoolean());
        }

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:SwitchStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task GlobalSwitchDefaultLegacyBindingPreservesExistingDescriptor()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate("Object.defineProperty(this, 'f', { value: 'x', enumerable: true, writable: true, configurable: false });");

        using var root = new Activity("AnnexB.Global.SwitchDefault.Existing");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var reportDoc = await EvaluateReportAsync(engine, """
function snapshot(desc) {
    return desc ? {
        enumerable: desc.enumerable,
        writable: desc.writable,
        configurable: desc.configurable
    } : null;
}
var beforeValue = this.f;
var beforeDesc = Object.getOwnPropertyDescriptor(this, "f");
switch (1) {
  default:
    function f() { return "inner declaration"; }
}
var afterDesc = Object.getOwnPropertyDescriptor(this, "f");
JSON.stringify({
    beforeValue,
    afterInvocation: typeof f === "function" ? f() : null,
    descBefore: snapshot(beforeDesc),
    descAfter: snapshot(afterDesc)
});
""");

        root.Stop();

        using (reportDoc)
        {
            var rootElement = reportDoc.RootElement;
            Assert.Equal("x", rootElement.GetProperty("beforeValue").GetString());
            Assert.Equal("inner declaration", rootElement.GetProperty("afterInvocation").GetString());
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("enumerable").GetBoolean());
            Assert.True(rootElement.GetProperty("descAfter").GetProperty("writable").GetBoolean());
            Assert.False(rootElement.GetProperty("descAfter").GetProperty("configurable").GetBoolean());
        }

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:SwitchStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    [Fact]
    public async Task LexicalDeclarationConflictsWithHoistedFunction()
    {
        await using var engine = new JsEngine();
        await engine.Evaluate("if (true) { function test262Fn() {} }");

        using var root = new Activity("AnnexB.Global.LexCollision");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var syntaxError = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("var x; let test262Fn;"));
        Assert.Equal("SyntaxError", ResolveErrorName(syntaxError.ThrownValue));

        var referenceError = await Assert.ThrowsAsync<ThrowSignal>(() => engine.Evaluate("x;"));
        Assert.Equal("ReferenceError", ResolveErrorName(referenceError.ThrownValue));

        root.Stop();

        Assert.Contains(recorder.Activities, activity => activity.DisplayName == "Statement:IfStatement");
        ActivityTimelineFormatter.Write(root,
            recorder.Activities,
            _output,
            predicate: activity => activity.DisplayName != "xUnit.net Test");
    }

    private static async Task<JsonDocument> EvaluateReportAsync(JsEngine engine, string script)
    {
        var result = await engine.Evaluate(script).ConfigureAwait(false);
        var json = Assert.IsType<string>(result);
        return JsonDocument.Parse(json);
    }

    private static string? ResolveErrorName(object? thrown)
    {
        if (thrown is JsObject jsObj && jsObj.TryGetProperty("name", out var name) && name is string nameString)
        {
            return nameString;
        }

        return thrown?.ToString();
    }
}
