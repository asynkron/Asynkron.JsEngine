using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

[Category(TestCategories.StdLibObject)]
public sealed class StaticMethodsTests(ITestOutputHelper output) : InternalTestBase(output)
{
    // Object static methods tests
    [Fact(Timeout = 2000)]
    public async Task ObjectKeys()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 1, b: 2, c: 3 };
                                                       let keys = Object.keys(obj);
                                                       keys[0] + keys[1] + keys[2];

                                           """);
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectValues()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { a: 10, b: 20, c: 30 };
                                                       let values = Object.values(obj);
                                                       values[0] + values[1] + values[2];

                                           """);
        Assert.Equal(60d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectEntries()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let obj = { x: 1, y: 2 };
                                                       let entries = Object.entries(obj);
                                                       entries[0][0] + entries[0][1] + entries[1][0] + entries[1][1];

                                           """);
        Assert.Equal("x1y2", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectAssign()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let target = { a: 1 };
                                                       let source1 = { b: 2 };
                                                       let source2 = { c: 3 };
                                                       Object.assign(target, source1, source2);
                                                       target.a + target.b + target.c;

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ObjectAssignOverwrites()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let target = { a: 1, b: 2 };
                                                       let source = { b: 20, c: 30 };
                                                       Object.assign(target, source);
                                                       target.a + target.b + target.c;

                                           """);
        Assert.Equal(51d, result);
    }

    // Array static methods tests
    [Fact(Timeout = 2000)]
    public async Task ArrayIsArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = [1, 2, 3];
                                                       let obj = { a: 1 };
                                                       Array.isArray(arr) && !Array.isArray(obj);

                                           """);
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayIsArrayString()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = 'hello';
                                                       Array.isArray(str);

                                           """);
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayIsArrayThrowsOnRevokedProxy()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const handle = Proxy.revocable([], {});
                                                       handle.revoke();
                                                       try {
                                                         Array.isArray(handle.proxy);
                                                         return "no throw";
                                                       } catch (err) {
                                                         return err.name;
                                                       }

                                           """);
        Assert.Equal("TypeError", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayFrom()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let str = 'abc';
                                                       let arr = Array.from(str);
                                                       arr[0] + arr[1] + arr[2];

                                           """);
        Assert.Equal("abc", result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayFromArray()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let original = [1, 2, 3];
                                                       let copy = Array.from(original);
                                                       copy[0] + copy[1] + copy[2];

                                           """);
        Assert.Equal(6d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayOf()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = Array.of(1, 2, 3, 4);
                                                       arr[0] + arr[1] + arr[2] + arr[3];

                                           """);
        Assert.Equal(10d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayOfSingle()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       let arr = Array.of(5);
                                                       arr[0];

                                           """);
        Assert.Equal(5d, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayOfReturnsArrayWhenReceiverIsNotConstructor()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       const receivers = [Array, undefined, Math.cos, Math.cos.bind(Math)];
                                                       return receivers.every(receiver => {
                                                         const arr = Array.of.call(receiver);
                                                         return Array.isArray(arr) &&
                                                           Object.getPrototypeOf(arr) === Array.prototype;
                                                       });

                                           """);
        Assert.Equal(true, result);
    }

    [Fact(Timeout = 2000)]
    public async Task ArrayOfThrowsWhenElementsCannotBeDefined()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""

                                                       function Frozen() {
                                                         this.length = 0;
                                                         Object.preventExtensions(this);
                                                       }
                                                       function Locked() {
                                                         Object.defineProperty(this, 0, {
                                                           configurable: false,
                                                           writable: true
                                                         });
                                                       }
                                                       const ctors = [Frozen, Locked];
                                                       return ctors.every(ctor => {
                                                         try {
                                                           Array.of.call(ctor, "x");
                                                           return false;
                                                         } catch (err) {
                                                           return err instanceof TypeError;
                                                         }
                                                       });

                                           """);
        Assert.Equal(true, result);
    }
}
