using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class SetMethodsTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Set_Difference_ReturnsElementsInFirstSetOnly()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([2, 3, 4]);
            const diff = a.difference(b);
            diff.has(1) && !diff.has(2) && !diff.has(3) && !diff.has(4) && diff.size === 1;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Intersection_ReturnsCommonElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([2, 3, 4]);
            const inter = a.intersection(b);
            !inter.has(1) && inter.has(2) && inter.has(3) && !inter.has(4) && inter.size === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Union_ReturnsAllElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([3, 4, 5]);
            const union = a.union(b);
            union.has(1) && union.has(2) && union.has(3) && union.has(4) && union.has(5) && union.size === 5;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_SymmetricDifference_ReturnsNonOverlappingElements()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([2, 3, 4]);
            const symDiff = a.symmetricDifference(b);
            symDiff.has(1) && !symDiff.has(2) && !symDiff.has(3) && symDiff.has(4) && symDiff.size === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsSubsetOf_ReturnsTrueForSubset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2]);
            const b = new Set([1, 2, 3]);
            a.isSubsetOf(b);
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsSubsetOf_ReturnsFalseForNonSubset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 4]);
            const b = new Set([1, 2, 3]);
            a.isSubsetOf(b);
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsSupersetOf_ReturnsTrueForSuperset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([1, 2]);
            a.isSupersetOf(b);
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsSupersetOf_ReturnsFalseForNonSuperset()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([1, 2, 4]);
            a.isSupersetOf(b);
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsDisjointFrom_ReturnsTrueForDisjointSets()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2]);
            const b = new Set([3, 4]);
            a.isDisjointFrom(b);
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_IsDisjointFrom_ReturnsFalseForOverlappingSets()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([3, 4]);
            a.isDisjointFrom(b);
        ");
        Assert.False((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Difference_WithEmptySet()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([]);
            const diff = a.difference(b);
            diff.size === 3 && diff.has(1) && diff.has(2) && diff.has(3);
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Intersection_WithEmptySet()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set([1, 2, 3]);
            const b = new Set([]);
            const inter = a.intersection(b);
            inter.size === 0;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Set_Union_WithStrings()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const a = new Set(['a', 'b']);
            const b = new Set(['b', 'c']);
            const union = a.union(b);
            union.size === 3 && union.has('a') && union.has('b') && union.has('c');
        ");
        Assert.True((bool)result!);
    }
}

