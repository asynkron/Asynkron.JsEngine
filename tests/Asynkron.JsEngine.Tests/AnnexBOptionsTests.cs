using System.Threading.Tasks;
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
        var result = await engine.Evaluate(Script);
        Assert.Equal("updated", result);
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
        var result = await engine.Evaluate(Script);
        Assert.Equal("updated", result);
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
}
