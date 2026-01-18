namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.ScopeAnalysis)]
public sealed class AnnexBCatchParameterConflictTests
{
    [Fact]
    public async Task SloppyMode_DirectEval_CatchDestructuringNameConflict_DisablesAnnexBHoisting()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            (function() {
              eval('try { throw {}; } catch ({ f }) { if (true) function f() {} else function _f() {} }');
              try { f; return 'no error'; } catch (e) { return e.name; }
            }())
        ");

        Assert.Equal("ReferenceError", result);
    }

    [Fact]
    public async Task SloppyMode_DirectEval_DestructuringCatchElsewhere_DoesNotDisableHoisting()
    {
        await using var engine = new JsEngine();
        var result = await engine.Evaluate(@"
            (function() {
              eval('try { throw {}; } catch ({ f }) { } if (true) function f() {}');
              try { f; return typeof f; } catch (e) { return e.name; }
            }())
        ");

        Assert.Equal("function", result);
    }
}
