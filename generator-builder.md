# SyncGeneratorIrBuilder review and refactor notes

Here is what I would do with this if I could refactor it freely.

I will focus on patterns and structure, not on changing semantics.

---

## 1. Split the job into two passes

Right now `SyncGeneratorIrBuilder` is doing two things at once:

1. **Lowering weird `yield` shapes**
   (e.g. `if (yield x)`, `a = yield x`, `let x = yield y`, `for (...; condWithYield; incWithYield)`).

2. **Emitting generator IR**
   (append instructions, wire jumps, maintain loop scopes).

That is why `TryBuildStatement` and friends are so big: they are half rewriter, half codegen.

I would split this:

```csharp
// Pass 1: lower yields into simple, supported shapes or fail.
internal static bool TryLowerToGeneratorFriendlyAst(
    FunctionExpression function,
    out FunctionExpression lowered,
    out string? failureReason);

// Pass 2: assume the AST only has simple yields and build IR.
internal sealed class SyncGeneratorIrBuilder
{
    public static bool TryBuild(FunctionExpression lowered, out GeneratorPlan plan, out string? failureReason);
}
```

Pass 1 would:

- Rewrite:
    - `if (yield expr)` into `yield expr` + `if (resumeSlot)`.
    - `return yield expr` into `yield expr` + `return resumeSlot`.
    - `a = yield expr` into `yield expr` + `a = resumeSlot`.
    - `let x = yield expr` similarly.
    - `while (condWithYield)` and `do/while` and `for` condition/increment into sequences with explicit resume slots.

- Reject "complex" shapes:
    - multiple yields in one expression,
    - delegated yields where not supported,
    - nested yields inside the yielded expression when you do not want to handle that yet.

Then the builder itself becomes much simpler:

- `TryBuildStatement` no longer needs all the `TryBuildXxxWithConditionYield` variants.
- Most of the "unsupported yield shape" checks disappear because the lowering pass either rewrote them or rejected them.

You already have most of the logic for this in:

- `TryRewriteConditionWithSingleYield`
- `TryLowerYieldingDeclaration`
- `TryLowerYieldingAssignment`
- the various `TryBuild*WithConditionYield` helpers

Those could move into a dedicated "yield lowering" visitor/rewriter instead of being baked into the IR builder.

This single change would reduce complexity more than any other.

---

## 2. Introduce an instruction rollback scope

You have a very common pattern:

```csharp
var instructionStart = _instructions.Count;

// ... do some nested building ...

if (!somethingBuilt)
{
    _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
    entryIndex = -1;
    return false;
}
```

This appears in many places:

- `TryBuildIfWithRewrittenCondition`
- `TryBuildIfStatement`
- `TryBuildWhileWithRewrittenCondition`
- `TryBuildWhileLoop`
- `TryBuildDoWhileStatement`
- `TryBuildDoWhileWithRewrittenCondition`
- `TryBuildForStatement`
- `TryBuildTryStatement`
- `TryBuildSwitchStatement`
- `TryBuildForOfStatement`
- `TryBuildForAwaitStatement`

I would wrap that in a small scope helper so the pattern is standard and hard to get wrong.

For example:

```csharp
private readonly struct InstructionScope : IDisposable
{
    private readonly List<GeneratorInstruction> _instructions;
    private readonly int _start;
    private bool _committed;

    public InstructionScope(List<GeneratorInstruction> instructions)
    {
        _instructions = instructions;
        _start = instructions.Count;
        _committed = false;
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (!_committed)
        {
            _instructions.RemoveRange(_start, _instructions.Count - _start);
        }
    }
}
```

Usage:

```csharp
private bool TryBuildIfStatement(
    IfStatement statement,
    int nextIndex,
    out int entryIndex,
    Symbol? activeLabel)
{
    using var scope = new InstructionScope(_instructions);

    var elseEntry = nextIndex;
    if (statement.Else is not null)
    {
        if (!TryBuildStatement(statement.Else, nextIndex, out elseEntry, activeLabel))
        {
            entryIndex = -1;
            return false;
        }
    }

    if (!TryBuildStatement(statement.Then, nextIndex, out var thenEntry, activeLabel))
    {
        entryIndex = -1;
        return false;
    }

    var branchIndex = Append(new BranchInstruction(statement.Condition, thenEntry, elseEntry));
    entryIndex = branchIndex;
    scope.Commit();
    return true;
}
```

Then all the other methods that do the same "checkpoint + rollback" dance become cleaner and harder to break.

---

## 3. Factor repeated yield checks into helpers

You repeatedly do:

```csharp
if (yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression))
{
    entryIndex = -1;
    return false;
}
```

or variants of:

- "yield must not be delegated"
- "nested yield in yield operand is not allowed right now"

Examples:

- `TryBuildIfWithYieldCondition`
- `TryBuildIfWithRewrittenCondition`
- `TryBuildWhileWithYieldCondition`
- `TryBuildWhileWithRewrittenCondition`
- `TryBuildDoWhileWithConditionYield`
- `TryBuildDoWhileWithRewrittenCondition`
- `TryBuildReturnWithYield`
- `TryLowerYieldingDeclaration`
- `TryLowerYieldingAssignment`

I would centralize that in a helper:

```csharp
private static bool IsSimpleYieldOperand(YieldExpression y)
{
    return !y.IsDelegated && !ContainsYield(y.Expression);
}
```

Or with failure reasons:

```csharp
private bool TryValidateSimpleYieldOperand(
    YieldExpression y,
    out string? failure)
{
    if (y.IsDelegated)
    {
        failure = "Delegated yield* is not supported in this context.";
        return false;
    }

    if (ContainsYield(y.Expression))
    {
        failure = "Nested yield in operand is not supported yet.";
        return false;
    }

    failure = null;
    return true;
}
```

Then your methods can read like:

```csharp
if (!TryValidateSimpleYieldOperand(yieldExpression, out var reason))
{
    entryIndex = -1;
    _failureReason ??= reason;
    return false;
}
```

This avoids copy paste bugs and makes future policy changes about yields centralized.

Also, in a few places you are checking the same condition twice in the same flow:

- `TryRewriteConditionWithSingleYield` guarantees that the yield it finds is not delegated and has no nested yield, but `TryBuildIfWithRewrittenCondition` still rechecks `yieldExpression.IsDelegated || ContainsYield(yieldExpression.Expression)`.

Once you have `IsSimpleYieldOperand`, you can confidently rely on it once and drop the extra checks.

---

## 4. Unify yield analysis and rewriting using visitors

`ContainsYield` and `TryRewriteConditionWithSingleYield` both traverse expressions and both know about all the same node types. They are tightly coupled: any new `ExpressionNode` subclass has to be added in both places.

Instead of hand rolling two big pattern matches, I would build a small visitor-style helper that can both:

- Count yields.
- Optionally rewrite exactly one yield into a resume slot.

Sketch:

```csharp
private sealed class YieldUsageAnalyzer
{
    private readonly Symbol _resumeSlot;
    public bool FoundYield { get; private set; }
    public bool MultipleYields { get; private set; }
    public YieldExpression? SingleYield { get; private set; }

    public YieldUsageAnalyzer(Symbol resumeSlot)
    {
        _resumeSlot = resumeSlot;
    }

    public ExpressionNode Visit(ExpressionNode expr)
    {
        // Switch on expr type, recurse, and:
        //  - when you see YieldExpression, update FoundYield / MultipleYields,
        //    remember SingleYield, and return an IdentifierExpression(resumeSlot)
        //  - for other nodes, rebuild with transformed children
    }
}
```

Then `TryRewriteConditionWithSingleYield` becomes:

```csharp
private bool TryRewriteConditionWithSingleYield(
    ExpressionNode expression,
    Symbol resumeSlot,
    out YieldExpression yieldExpression,
    out ExpressionNode rewrittenCondition)
{
    var analyzer = new YieldUsageAnalyzer(resumeSlot);
    rewrittenCondition = analyzer.Visit(expression);

    if (!analyzer.FoundYield || analyzer.MultipleYields)
    {
        yieldExpression = null!;
        return false;
    }

    yieldExpression = analyzer.SingleYield!;
    return true;
}
```

And `ContainsYield` can just be:

```csharp
private static bool ContainsYield(ExpressionNode? expression)
{
    if (expression is null) return false;
    var analyzer = new YieldUsageAnalyzer(resumeSlot: default! /* unused in this mode */);
    analyzer.Visit(expression);
    return analyzer.FoundYield;
}
```

You can keep it as one implementation with two modes:

- "check only"
- "rewrite first yield"

That way you never forget a node type in one and not in the other.

The same idea applies to `StatementContainsYield` and `ContainsTryStatement`: consider a statement visitor with booleans like `ContainsYield`, `ContainsTry`, etc, instead of two separate recursive functions.

---

## 5. Factor common loop patterns

The while, do/while and for lowering share a lot of structure:

- Allocate a `JumpInstruction` for the condition.
- Push a `LoopScope` with `continue` and `break` targets.
- Build the body.
- Emit a `BranchInstruction` from condition to body or exit.
- Possibly have a yield sequence wrapping the condition (or increment, or both).

There are two levels of duplication:

1. `while` vs `do/while` vs `for` all manually repeat the same jump + branch pattern.
2. Each of them has a special case for "condition contains exactly one simple yield" that is very similar.

Given you are already comfortable with small helpers, I would factor the "loop with condition yield" case into something generic.

For example, for while and do/while:

```csharp
private bool TryBuildLoopWithConditionYield<TLoopNode>(
    TLoopNode loopNode,
    ExpressionNode condition,
    YieldExpression yieldExpression,
    int nextIndex,
    Symbol? label,
    Func<TLoopNode, ExpressionNode, TLoopNode> withRewrittenCondition,
    Func<TLoopNode, int, Symbol?, int, out int, bool> buildLoopCore,
    out int entryIndex)
    where TLoopNode : StatementNode
{
    if (!IsSimpleYieldOperand(yieldExpression))
    {
        entryIndex = -1;
        return false;
    }

    var resumeSymbol = CreateResumeSlotSymbol();
    var conditionIdentifier = new IdentifierExpression(yieldExpression.Source, resumeSymbol);
    var rewrittenLoop = withRewrittenCondition(loopNode, conditionIdentifier);

    var instructionStart = _instructions.Count;

    if (!buildLoopCore(rewrittenLoop, nextIndex, label, out var loopEntry))
    {
        _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
        entryIndex = -1;
        return false;
    }

    entryIndex = AppendYieldSequence(yieldExpression.Expression, loopEntry, resumeSymbol);
    return true;
}
```

Then `TryBuildWhileWithYieldCondition` and `TryBuildDoWhileWithConditionYield` become thin wrappers that call this helper with the right `withRewrittenCondition` and `buildLoopCore` delegate (which can be `TryBuildWhileLoop` or a `TryBuildDoWhileCore` that just handles the pure loop shape).

You do not have to go fully generic if that feels overkill, but even a shared helper for "loop with condition yield" that handles the yield prefix/postfix and rollback would cut a lot of repeated code.

---

## 6. Unify `for..of` and `for await..of` code

`TryBuildForOfStatement` and `TryBuildForAwaitStatement` are almost identical apart from the instruction types:

- `ForOfMoveNextInstruction` vs `ForAwaitMoveNextInstruction`
- `ForOfInitInstruction` vs `ForAwaitInitInstruction`

You can abstract this pattern with a generic helper:

```csharp
private bool TryBuildIteratorLoop<TInit, TMoveNext>(
    ForEachStatement statement,
    int nextIndex,
    out int entryIndex,
    Symbol? label,
    Func<Symbol, Symbol, int, int, TMoveNext> createMoveNext,
    Func<ExpressionNode, Symbol, int, TInit> createInit)
    where TInit : GeneratorInstruction
    where TMoveNext : GeneratorInstruction
{
    var instructionStart = _instructions.Count;
    var iteratorSymbol = Symbol.Intern($"__iter_{instructionStart}");
    var valueSymbol = Symbol.Intern($"__value_{instructionStart}");

    var moveNextIndex = Append(createMoveNext(iteratorSymbol, valueSymbol, nextIndex, -1));

    var perIterationBlock = CreateForOfIterationBlock(statement, valueSymbol);
    var scope = new LoopScope(label, moveNextIndex, nextIndex);
    _loopScopes.Push(scope);

    var bodyBuilt = TryBuildStatement(perIterationBlock, moveNextIndex, out var iterationEntry, label);
    _loopScopes.Pop();

    if (!bodyBuilt)
    {
        _instructions.RemoveRange(instructionStart, _instructions.Count - instructionStart);
        entryIndex = -1;
        return false;
    }

    _instructions[moveNextIndex] = (TMoveNext)_instructions[moveNextIndex] with { Next = iterationEntry };

    var initIndex = Append(createInit(statement.Iterable, iteratorSymbol, moveNextIndex));
    entryIndex = initIndex;
    return true;
}
```

Then:

```csharp
private bool TryBuildForOfStatement(ForEachStatement statement, int nextIndex, out int entryIndex, Symbol? label)
{
    if (ContainsYield(statement.Iterable) || !IsSimpleForOfBinding(statement))
    {
        entryIndex = -1;
        return false;
    }

    return TryBuildIteratorLoop(
        statement,
        nextIndex,
        out entryIndex,
        label,
        (iter, value, exit, next) => new ForOfMoveNextInstruction(iter, value, exit, next),
        (iterable, iter, moveNext) => new ForOfInitInstruction(iterable, iter, moveNext));
}

private bool TryBuildForAwaitStatement(ForEachStatement statement, int nextIndex, out int entryIndex, Symbol? label)
{
    if (ContainsYield(statement.Iterable))
    {
        entryIndex = -1;
        return false;
    }

    return TryBuildIteratorLoop(
        statement,
        nextIndex,
        out entryIndex,
        label,
        (iter, value, exit, next) => new ForAwaitMoveNextInstruction(iter, value, exit, next),
        (iterable, iter, moveNext) => new ForAwaitInitInstruction(iterable, iter, moveNext));
}
```

This keeps the semantics but removes duplication and makes both loops obviously symmetric.

---

## 7. Smaller cleanups and consistency tweaks

A few small things that together make the code easier to maintain:

1. **Use a named constant for "invalid index"**

   Instead of magic `-1` everywhere:

   ```csharp
   private const int InvalidIndex = -1;
   ```

   Then:

   ```csharp
   entryIndex = InvalidIndex;
   return false;
   ```

   and in instructions:

   ```csharp
   new JumpInstruction(InvalidIndex);
   ```

   It makes intent clearer and makes it easier to change representation if needed.

2. **Centralize failure reasons where possible**

   You already use `_failureReason ??= "...";` which is good. You could also put the messages in a static class or constants so they are reused and kept consistent:

   ```csharp
   private static class FailureMessages
   {
       public const string IfYieldShape = "If condition contains unsupported yield shape.";
       public const string WhileYieldShape = "While condition contains unsupported yield shape.";
       // ...
   }
   ```

3. **Remove redundant checks when guarded by callers**

   Example: `TryBuildIfWithConditionYield` checks `statement.Condition is null || !ContainsYield`, but the caller already ensured `Condition is not null` and `ContainsYield` is true. These defensive checks are fine but make the control flow slightly harder to read. Once you are confident in the call sites you can simplify them.

4. **Consider a small wrapper for `_loopScopes` operations**

   You repeatedly do:

   ```csharp
   var scope = new LoopScope(label, continueTarget, breakTarget);
   _loopScopes.Push(scope);
   var bodyBuilt = TryBuildStatement(...);
   _loopScopes.Pop();
   ```

   That could be another small scope struct:

   ```csharp
   private readonly struct LoopScopeGuard : IDisposable
   {
       private readonly Stack<LoopScope> _stack;

       public LoopScopeGuard(Stack<LoopScope> stack, LoopScope scope)
       {
           _stack = stack;
           _stack.Push(scope);
       }

       public void Dispose() => _stack.Pop();
   }
   ```

   Then:

   ```csharp
   using (new LoopScopeGuard(_loopScopes, new LoopScope(label, continueTarget, breakTarget)))
   {
       bodyBuilt = TryBuildStatement(...);
   }
   ```

5. **Check for unused helpers**

   In the snippet you sent, `ContainsTryStatement` is never called. If it is genuinely unused, removing it (or moving to a more appropriate utility location) reduces noise.

---

## Summary

If I had to prioritize:

1. **First**: separate "yield lowering" from "IR codegen". That is the big architectural win and it naturally eliminates a lot of repetition and special cases.
2. **Second**: add `InstructionScope` rollback and small helpers for `IsSimpleYieldOperand`, `LoopScopeGuard`, and `TryBuildIteratorLoop`. This cuts a lot of boilerplate and makes the core algorithms clearer.
3. **Third**: unify yield analysis (`ContainsYield` / `TryRewriteConditionWithSingleYield`) into a single visitor so future AST changes are less error prone.

All of these can be done incrementally, but they compose nicely: once yield lowering is a separate pass and you have rollback scopes, the builder itself becomes quite lean and focused on just "turn this normalized AST into instructions".
