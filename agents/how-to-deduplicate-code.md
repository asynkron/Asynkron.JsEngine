

## Dealing with "almost duplicate" code

"Almost duplicate" code appears when two blocks share the same control flow and structure, but differ in a few details such as which function is called, which parameters are passed, or whether some contextual value like an index exists or not.

Your two snippets are a textbook case.

Both snippets do the following:

1. Iterate over an array
2. For each item, check if it has a then method
3. If it does, call then with resolve and reject functions
4. Otherwise, resolve immediately
5. Return the same result promise

The difference is not the algorithm, the difference is the context.

---

## With index vs without index

### Version with index

In the first snippet, each item is associated with an index. That index is captured and used later when resolving.

Key characteristics:

* Resolve and reject are index aware
* The index is part of the promise resolution logic
* The resolve function is created as CreateResolve(int index)
* Direct resolution calls Resolve(index, value, false)

This pattern is typical for Promise.all style logic, where order matters.

---

### Version without index

In the second snippet, there is no per-item index.

Key characteristics:

* Resolve and reject do not care about position
* Resolve is global, not tied to an index
* CreateResolve() takes no parameters
* Direct resolution calls Resolve(value)

This is typical for Promise.race or Promise.any style logic.

---

## Why this is "almost duplicate" and not real duplication

The code is not duplicated by accident, it is duplicated by structure.

* The loop is identical
* The branching logic is identical
* The then invocation is identical

Only the behavior injected into that structure differs.

This means the duplication should not be removed by merging the code paths with conditionals, because that would entangle the logic and reduce clarity.

Instead, the structure should be extracted, and the differences should be passed in.

---

## The refactoring pattern used

The refactoring uses three ideas:

1. Extract the shared control flow into one function
2. Parameterize the differences using delegates
3. Keep the semantic meaning of each variant intact

The shared function owns the "how".

The callers provide the "what".

---

## The extracted shared structure

The shared logic is:

* Iterate
* Detect thenables
* Invoke then or resolve directly

Everything else is behavior that can be injected.

This is why a helper like this works:

```csharp
void IterateAndResolve(
    JsArray array,
    Func<int, HostFunction> createResolve,
    Func<int, HostFunction> createReject,
    Action<int, JsValue> resolveDirect)
```

The loop stays stable, the behavior changes.

---

## How to deduplicate "almost duplicate" code

Here is a small practical guide you can apply anywhere.

### Step 1: Ignore the differences

Read both code blocks and mentally replace the differing lines with placeholders.

If the code still "reads the same", you have almost duplication.

---

### Step 2: Identify the invariant structure

Look for:

* Same loops
* Same branching
* Same method calls in the same order

That is the structure to extract.

---

### Step 3: Identify the varying behavior

Ask:

* What methods differ?
* What parameters differ?
* What context exists in one version but not the other, like an index?

These become delegates, lambdas, or strategy objects.

---

### Step 4: Extract, do not merge

Do not write:

```csharp
if (withIndex) { ... } else { ... }
```

Instead, extract the loop and pass behavior in.

---

### Step 5: Preserve meaning at call sites

The call site should still clearly express intent.

This is good:

```csharp
IterateAndResolve(array, i => CreateResolve(i), ...)
```

This is bad:

```csharp
IterateAndResolve(array, withIndex: true)
```

---

## Rule of thumb

If two methods differ only in what they do inside a shared structure, extract the structure.

If they differ in structure, keep them separate.

Your example is a clean case of structural duplication with contextual variation, which makes it ideal for this pattern.


Here is the same how to, but now explicitly using the correct and useful name for this category.

---

## Dealing with structural switch duplication

```
    {
        statement = elseBranch;
        continue;
    }

    break;

case WhileStatement whileStatement:
    statement = whileStatement.Body;
    continue;

case DoWhileStatement doWhileStatement:
    statement = doWhileStatement.Body;
    continue;

case WithStatement withStatement:
    statement = withStatement.Body;
    continue;

case ForStatement forStatement:
    if (forStatement.Body is not null)
    {
        statement = forStatement.Body;
        continue;
    }

    break;
```

Structural switch duplication happens when the same switch or pattern match appears in multiple places because the *domain structure* is the same, not because logic was copied carelessly.

This is common in AST, compiler, interpreter, and serializer code.

---

## What structural switch duplication looks like

* Repeated `switch` or pattern matching blocks
* Short, trivial case bodies
* Each case extracts or forwards structure
* Control flow like `continue`, `break`, or `return`
* No real algorithm, just classification

---

## Why it is usually OK

* The duplication documents the domain shape
* Each switch is readable in isolation
* Extraction adds indirection without reducing complexity
* The switch itself is the explanation

This is declarative code, not behavioral logic.

---

## When to leave it as is

Leave it when:

* Each case is one or two lines
* The code is stable and unlikely to drift
* The abstraction would just wrap a switch
* Removing it would hide control flow

---

## When to refactor structural switch duplication

Refactor only when:

* The same switch appears 3+ times
* The list of cases starts diverging
* Case logic grows beyond trivial access
* The switch represents a named domain operation

At that point, duplication becomes a maintenance risk.

---

## How to refactor if needed

1. Extract the *smallest possible* helper
2. Return data, do not hide control flow
3. Use pattern matching, not flags
4. Keep call sites explicit

Example shape:

```csharp
Statement? TryUnwrapBody(Statement s)
```

Not:

```csharp
HandleStatement(s, mode)
```

---

## Rule of thumb

If the switch *describes structure*, duplication is documentation.
If the switch *implements behavior*, consider extraction.

Structural switch duplication is often the right tradeoff.

This has a name too, and it is a very common one.

I would call this **parameter bundle duplication**, sometimes also referred to as **context parameter duplication**.

---

## Dealing with parameter bundle duplication

```
thisValue,
this,
RealmState,
_isLexicallyStrict,
_hasFunctionNameEnvironment,
_homeObject,
PrivateNameScope,
_capturedPrivateNameScopes);
```

This is not algorithm duplication and not structural switch duplication.

This is the same *set of context values* being passed together in multiple places.

Key characteristics:

* Same parameters
* Same order
* Same meaning
* Usually forwarded to another object or constructor
* Reads like a “context snapshot”

The duplication exists because the *context itself* is duplicated conceptually.

---

## Why it happens

This pattern usually appears when:

* A function, invoker, or evaluator needs a full execution context
* The context is implicit in the surrounding class
* The callee expects the context explicitly
* No single object currently represents that context

In other words, the code is missing a name for something real.

---

## Why this is not just “duplication”

Each individual argument is fine.

What is duplicated is the *bundle*, not the logic.

This is a classic case of **missing abstraction**, not a DRY violation.

---

## What it wants to become

This strongly suggests a value object, often named something like:

* `ExecutionContext`
* `FunctionExecutionContext`
* `EvaluatorContext`
* `InvocationContext`

Example shape:

```csharp
new FunctionExecutionContext(
    thisValue,
    this,
    RealmState,
    _isLexicallyStrict,
    _hasFunctionNameEnvironment,
    _homeObject,
    PrivateNameScope,
    _capturedPrivateNameScopes);
```

Or even better, constructed once and passed around.

---

## When to refactor it

Refactor when:

* This parameter list appears 2–3 times
* Parameters are always passed together
* Order matters and is fragile
* Adding a new parameter would require editing many call sites

---

## When to leave it

Leaving it inline is acceptable if:

* It appears once
* It is highly localized
* The lifetime is extremely short

That is not the case here.

---

## Dealing with argument unpacking duplication

```
    var executor = new HostFunction((_, execArgs) =>
    {
        IJsCallable? resolve = null;
        IJsCallable? reject = null;

        if (execArgs.Count >= 1 && execArgs[0].TryUnwrap(out IJsCallable? res))
        {
            resolve = res;
        }

        if (execArgs.Count >= 2 && execArgs[1].TryUnwrap(out IJsCallable? rej))
        {
            reject = rej;
        }
```        

This is **argument unpacking duplication**.

More specifically: duplicated defensive parsing of callback arguments.

Characteristics:

* Same local variables
* Same guards
* Same TryUnwrap logic
* Same positional meaning
* Appears inside lambdas, which makes it noisy

This is not structural documentation like a switch, and not a missing value object like the parameter bundle. It is low level mechanics repeated verbatim.

---

## Why refactoring makes sense here

* The logic is purely mechanical
* The intent is obscured by boilerplate
* Lambdas amplify duplication cost
* Any bug fix would need to be applied twice

This is exactly the kind of duplication DRY is meant to address.

---

## Minimal, clean refactor

Extract a small helper whose only job is to unpack resolve and reject.

### Helper method

```csharp
static void UnwrapResolveReject(
    IReadOnlyList<JsValue> args,
    out IJsCallable? resolve,
    out IJsCallable? reject)
{
    resolve = null;
    reject = null;

    if (args.Count >= 1 && args[0].TryUnwrap(out IJsCallable? res))
    {
        resolve = res;
    }

    if (args.Count >= 2 && args[1].TryUnwrap(out IJsCallable? rej))
    {
        reject = rej;
    }
}
```

### Usage at call sites

```csharp
var executor = new HostFunction((_, execArgs) =>
{
    UnwrapResolveReject(execArgs, out var resolve, out var reject);

    // rest of logic
});
```


This removes duplication while keeping intent obvious.

---

## Why this is the right level of abstraction

* No control flow is hidden
* No domain meaning is invented
* The helper does exactly one thing
* Call sites become shorter and clearer

Importantly, the lambda now reads as "get resolve and reject" instead of "manually decode argument positions".

---

## When not to refactor this pattern

Leave it inline only if:

* It appears once
* It is immediately adjacent to usage
* It is unlikely to be reused

That is not the case here.

---

## Rule of thumb

If you copy the same argument decoding logic twice, extract it.
If the extraction reads like English, you picked the right abstraction.

This one does.
