using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class StackOverflowReproTest(ITestOutputHelper output) : InternalTestBase(output)
{
    [Fact(Timeout = 5000)]
    public async Task SimpleClassCreation()
    {
        await using var engine = CreateEngine();
        var code = @"
class A {}
class B extends A {
  constructor() {
    super();
  }
}
'done';
";
        var result = await engine.Evaluate(code);
        Assert.Equal("done", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ClassInstantiation()
    {
        await using var engine = CreateEngine();
        var code = @"
class A {}
class B extends A {
  constructor() {
    super();
  }
}
var b = new B();
'done';
";
        var result = await engine.Evaluate(code);
        Assert.Equal("done", result);
    }

    [Fact(Timeout = 5000)]
    public async Task ClassInheritancePrototypeChain()
    {
        await using var engine = CreateEngine();
        var code = @"
class A {}
class B extends A {
  constructor() {
    super();
  }
}

var b = new B();
var result1 = Object.getPrototypeOf(b) === B.prototype;
var result2 = Object.getPrototypeOf(B.prototype) === A.prototype;
var result3 = Object.getPrototypeOf(A.prototype) === Object.prototype;
[result1, result2, result3].join(',');
";
        var result = await engine.Evaluate(code);
        Assert.Equal("true,true,true", result);
    }

    [Fact(Timeout = 5000)]
    public async Task NewTargetViaSuperCall()
    {
        await using var engine = CreateEngine();
        var code = @"
class A {
  constructor() {
    this.newTarget = new.target;
  }
}

class B extends A {
  constructor() {
    super();
  }
}

var b = new B();
b.newTarget === B;
";
        var result = await engine.Evaluate(code);
        Assert.Equal(true, result);
    }
}
