using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.Tracing;
using Xunit;

namespace Asynkron.JsEngine.Tests;

public class AnnexBOptionsTests
{
    [Fact]
    public async Task BlockFunctionLeaksWhenAnnexBEnabled()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
if (true) {
    function leaked() { return 1; }
}
typeof leaked;");
        Assert.Equal("function", result);
    }

    [Fact]
    public async Task BlockFunctionStaysLexicalWhenAnnexBDisabled()
    {
        await using var engine = new JsEngine(new JsEngineOptions
        {
            EnableAnnexBFunctionExtensions = false
        });

        var result = await engine.Evaluate(@"
if (true) {
    function lexicalOnly() { return 1; }
}
typeof lexicalOnly;");

        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task BlockFunctionDoesNotOverrideLexicalBinding()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let existing = 42;
if (true) {
    function existing() { return 0; }
}
typeof existing;");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task BlockFunctionDoesNotOverrideParameter()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
(function(param) {
    if (true) {
        function param() { return 0; }
    }
    return typeof param;
})(123);");
        Assert.Equal("number", result);
    }

    [Fact]
    public async Task BlockScopeActivitiesReportAnnexBModeWhenEnabled()
    {
        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Scope.Enabled");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var result = await engine.Evaluate("""

                                                   {
                                                       function hoisted() { return 1; }
                                                       hoisted();
                                                   }

                                       """);

        Assert.Equal(1d, result);
        root.Stop();

        var blockModes = recorder.Activities
            .Where(activity => activity.DisplayName == "Scope:Block")
            .Select(activity => activity.Tags.FirstOrDefault(tag => tag.Key == "js.scope.mode").Value)
            .ToArray();

        Assert.Contains(ScopeMode.SloppyAnnexB.ToString(), blockModes);
    }

    [Fact]
    public async Task BlockScopeActivitiesReportSloppyModeWhenAnnexBDisabled()
    {
        await using var engine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        using var root = new Activity("AnnexB.Scope.Disabled");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        var result = await engine.Evaluate("""

                                                   {
                                                       function hoisted() { return 1; }
                                                       hoisted();
                                                   }

                                       """);

        Assert.Equal(1d, result);
        root.Stop();

        var blockModes = recorder.Activities
            .Where(activity => activity.DisplayName == "Scope:Block")
            .Select(activity => activity.Tags.FirstOrDefault(tag => tag.Key == "js.scope.mode").Value)
            .ToArray();

        Assert.Contains(ScopeMode.Sloppy.ToString(), blockModes);
        Assert.DoesNotContain(ScopeMode.SloppyAnnexB.ToString(), blockModes);
    }

    [Fact]
    public async Task EvalBlockFunctionRespectsAnnexBOption()
    {
        const string Script = @"eval(""if (true) { function leaked() { return 'ok'; } }"");
typeof leaked;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions
        {
            EnableAnnexBFunctionExtensions = false
        });

        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task StrictFunctionDisablesAnnexBBlockFunctions()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
(function () {
    'use strict';
    if (true) {
        function strictScoped() { return 1; }
    }
    return typeof strictScoped;
})();");

        Assert.Equal("undefined", result);
    }

    [Fact]
    public async Task SloppyFunctionHoistsBlockFunction()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
(function () {
    if (true) {
        function sloppyScoped() { return 1; }
    }
    return typeof sloppyScoped;
})();");

        Assert.Equal("function", result);
    }

    [Fact]
    public async Task BlockEvalFollowsAnnexBOption()
    {
        const string Script = @"
eval(""if (true) { function blockEvalFn() { return 1; } }"");
typeof blockEvalFn;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions
        {
            EnableAnnexBFunctionExtensions = false
        });

        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task BlockFunctionInsideLoopRespectsAnnexBToggle()
    {
        const string Script = @"
for (var i = 0; i < 1; i++) {
    function loopLeak() { return i; }
}
typeof loopLeak;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task BlockEvalInsideLoopRespectsAnnexBToggle()
    {
        const string Script = @"
for (var i = 0; i < 1; i++) {
    eval(""if (true) { function loopEval() { return i; } }"");
}
typeof loopEval;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task LoopLexicalShadowPreventsAnnexBHoist()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
let shadowed = 1;
for (let i = 0; i < 1; i++) {
    function shadowed() { return i; }
}
typeof shadowed;");

        Assert.Equal("number", result);
    }

    [Fact]
    public async Task SwitchBlockFunctionRespectsAnnexBToggle()
    {
        const string Script = @"
switch (1) {
    case 1:
        function caseFn() { return 1; }
}
typeof caseFn;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task TryBlockFunctionRespectsAnnexBToggle()
    {
        const string Script = @"
try {
    function tryFn() { return 1; }
} finally {}
typeof tryFn;";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task NestedEvalRespectsAnnexBToggle()
    {
        const string Script = """
eval("if (true) { eval(\"if (true) { function nestedEvalFn() { } }\"); }");
typeof nestedEvalFn;
""";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined", strictResult);
    }

    [Fact]
    public async Task AnnexBBlockFunctionRedeclaresNonConfigGlobal()
    {
        const string Script = """
Object.defineProperty(this, "legacyFn", {
    value: function () { return "legacy"; },
    writable: true,
    configurable: false
});

if (true) {
    function legacyFn() { return "updated"; }
}

legacyFn();
""";

        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Block.GlobalRedeclare");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        try
        {
            var result = await engine.Evaluate(Script);
            Assert.Equal("updated", result);
        }
        finally
        {
            root.Stop();
        }

        AssertAnnexBFunctionActivities(recorder, ExecutionKind.Script);
    }

    [Fact]
    public async Task AnnexBEvalRedeclaresNonConfigGlobal()
    {
        const string Script = """
Object.defineProperty(this, "legacyEvalFn", {
    value: function () { return "legacy"; },
    writable: true,
    configurable: false
});

eval("if (true) { function legacyEvalFn() { return 'updated'; } }");

legacyEvalFn();
""";

        await using var engine = new JsEngine();
        using var root = new Activity("AnnexB.Eval.GlobalRedeclare");
        root.Start();
        using var recorder = EvaluatorActivityRecorder.Attach(root);

        try
        {
            var result = await engine.Evaluate(Script);
            Assert.Equal("updated", result);
        }
        finally
        {
            root.Stop();
        }

        AssertAnnexBFunctionActivities(recorder, ExecutionKind.Script, ExecutionKind.Eval);
    }

    [Fact]
    public async Task WithStatementBlocksAnnexBLeakWhenDisabled()
    {
        const string Script = """
var obj = {
    inner() {
        if (true) {
            function withFn() { return 1; }
        }
        return typeof withFn;
    }
};

var innerType;
with (obj) {
    innerType = inner();
}

innerType + ":" + typeof withFn;
""";

        await using var annexBEngine = new JsEngine();
        var annexBResult = await annexBEngine.Evaluate(Script);
        Assert.Equal("function:undefined", annexBResult);

        await using var strictEngine = new JsEngine(new JsEngineOptions { EnableAnnexBFunctionExtensions = false });
        var strictResult = await strictEngine.Evaluate(Script);
        Assert.Equal("undefined:undefined", strictResult);
    }

    private static void AssertAnnexBFunctionActivities(EvaluatorActivityRecorder recorder,
        params ExecutionKind[] expectedKinds)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        var activities = recorder.Activities;
        var functionActivities = activities
            .Where(activity => string.Equals(activity.DisplayName, "Statement:FunctionDeclaration",
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(functionActivities);

        var expectedKindStrings = new HashSet<string>(
            expectedKinds.Select(kind => kind.ToString()),
            StringComparer.Ordinal);

        Assert.Contains(functionActivities,
            activity => HasTag(activity, "js.scope.mode", ScopeMode.SloppyAnnexB.ToString()));

        if (expectedKindStrings.Count > 0)
        {
            Assert.Contains(functionActivities,
                activity => activity.Tags.Any(tag =>
                    tag.Key == "js.execution.kind" &&
                    expectedKindStrings.Contains(tag.Value?.ToString() ?? string.Empty)));
        }

        Assert.All(functionActivities,
            activity =>
                Assert.Contains(activity.Tags, tag => tag.Key == "code.span"));

        var blockScopes = activities
            .Where(activity => string.Equals(activity.DisplayName, "Scope:Block", StringComparison.Ordinal))
            .ToArray();
        if (blockScopes.Length > 0)
        {
            Assert.Contains(blockScopes,
                activity => HasTag(activity, "js.scope.mode", ScopeMode.SloppyAnnexB.ToString()));
        }
    }

    private static bool HasTag(Activity activity, string key, string expectedValue)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.Tags.Any(tag =>
            tag.Key == key &&
            string.Equals(tag.Value?.ToString(), expectedValue, StringComparison.Ordinal));
    }
}
