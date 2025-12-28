# Jint vs JsEngine2: Architectural Comparison

This document analyzes the architectural differences between Jint and JsEngine2, identifying areas where each engine makes different trade-offs.

## Executive Summary

| Aspect | Jint | JsEngine2 | Performance Impact |
|--------|------|-----------|-------------------|
| **ExecutionContext** | Immutable struct | Sealed class | Jint: Lower GC pressure |
| **Value Types** | Class hierarchy with caching | `object?` boxing | Jint: Better cache locality |
| **Promise/Async** | Sync blocking (`ManualResetEventSlim`) | CPS transformation + microtask queue | JsEngine2: Spec-compliant but slower |
| **Property Storage** | HybridDictionary (adaptive) | Dictionary<string, object?> | Jint: Better for small objects |
| **Symbol/Key Handling** | Pre-hashed Key struct | Interned Symbol class | Both good, different trade-offs |
| **Object Pooling** | Extensive (arrays, refs, args) | Limited (HashSet<Symbol>, await state) | Jint: More comprehensive |

---

## 1. ExecutionContext Management

### Jint: Readonly Struct with RefStack
```csharp
// Jint: Runtime/Environments/ExecutionContext.cs
internal readonly struct ExecutionContext
{
    public readonly Environment LexicalEnvironment;
    public readonly Environment VariableEnvironment;
    // ... other fields
}
```

**Advantages:**
- Stack-allocated (no heap allocation per call)
- Copy-on-write semantics via `With*()` methods returning new struct
- `RefStack<ExecutionContext>` provides `ref readonly` access (zero-copy peek)

### JsEngine2: Sealed Class
```csharp
// JsEngine2: EvaluationContext.cs
public sealed class EvaluationContext(
    RealmState realmState,
    CancellationToken cancellationToken = default,
    ExecutionKind executionKind = ExecutionKind.Script)
{
    private readonly Stack<LabelStackEntry> _labelStack = [];
    private readonly Stack<PrivateNameScope> _privateNameScopes = [];
    // ... other fields
}
```

**Disadvantages:**
- Heap allocation per context
- Contains multiple `Stack<T>` fields requiring separate allocations
- Difficult to pool due to async lifetime spanning microtask boundaries

**Recommendation:** Consider splitting EvaluationContext into:
1. A poolable struct for short-lived synchronous execution
2. A separate async continuation object for promise callbacks

---

## 2. Value Representation

### Jint: Unified JsValue Class Hierarchy
```csharp
// Jint: Native/JsValue.cs
public abstract partial class JsValue : IEquatable<JsValue>
{
    internal InternalTypes _type;
}

// Pre-cached values:
// - JsNumber: integers 0-10,239 cached
// - JsString: ASCII chars 0-126 cached, integers 0-1023 as strings
// - Static singletons for NaN, Infinity, common strings
```

**Advantages:**
- Type-safe polymorphism
- Aggressive caching reduces allocations
- `InternalTypes` flags enable fast type discrimination

### JsEngine2: Raw object? with Symbol Sentinels
```csharp
// JsEngine2: Uses object? with special values
public static readonly Symbol Undefined = Intern("undefined");
public static readonly Symbol Null = Intern("null");

// Values are stored as:
// - double for numbers
// - string for strings
// - JsObject for objects
// - Symbol.Undefined for undefined
```

**Disadvantages:**
- Boxing for primitives (double → object)
- No caching for common values
- Type checks require `is` pattern matching (slower than flag comparison)

**Recommendation:** Consider implementing:
1. Pre-cached JsNumber instances for small integers
2. Pre-cached JsString instances for common strings ("", "undefined", "true", "false")
3. Static readonly arrays for frequently used single-element argument arrays

---

## 3. Promise/Async Handling

### Jint: Synchronous Blocking
```csharp
// Jint: Native/JsPromise.cs
internal sealed class JsPromise : ObjectInstance
{
    internal ManualResetEventSlim CompletedEvent { get; } = new();

    // Await blocks synchronously on CompletedEvent
}
```

**Advantages:**
- Simple execution model
- No CPS transformation overhead
- Predictable stack traces

**Disadvantages:**
- Not truly async
- Can't integrate with C# async/await naturally
- Potential for deadlocks in certain interop scenarios

### JsEngine2: CPS Transformation + Microtask Queue
```csharp
// JsEngine2: JsPromise.cs + AwaitScheduler.cs
private readonly Queue<(Action task, int epoch)> _microtaskQueue = new();

// AwaitScheduler uses:
// - Fast path for settled promises
// - SpinWait for short-running promises
// - Microtask draining with epoch tracking
```

**Advantages:**
- More spec-compliant Promise resolution ordering
- Supports non-blocking top-level await
- Better integration with C# async ecosystem

**Disadvantages:**
- CPS transformation adds parsing/compilation overhead
- SpinWait CPU usage
- Complex control flow

**Benchmark Impact:** The CPS transformation and microtask queue management contribute significantly to the ~26x async/await performance gap.

**Recommendation:**
1. Add fast-path for already-settled promises (partially done)
2. Consider synchronous blocking mode for simple cases (like Jint)
3. Cache CPS-transformed function bodies (already done at parse level)

---

## 4. Property Storage

### Jint: HybridDictionary (Adaptive)
```csharp
// Jint: Collections/HybridDictionary.cs
private const int CutoverPoint = 9;  // Switch at 9 items

private ListDictionary<TValue> _list;      // Small objects
internal StringDictionarySlim<TValue> _dictionary;  // Large objects
```

**Advantages:**
- ListDictionary is faster for ≤8 properties (no hashing)
- DictionarySlim optimized (no hash code storage, ref returns)
- Adaptive switching based on actual usage

### JsEngine2: Standard Dictionary
```csharp
// JsEngine2: JsTypes/JsObject.cs
public class JsObject : Dictionary<string, object?>, IJsObjectLike
{
    private readonly Dictionary<string, PropertyDescriptor> _descriptors;
    private readonly LinkedList<string> _propertyInsertionOrder;
}
```

**Disadvantages:**
- Full dictionary overhead even for small objects
- Separate LinkedList for insertion order (ECMAScript requirement)
- Three separate allocations per object

**Recommendation:**
1. Implement HybridDictionary-style adaptive storage
2. Consider embedding insertion order in dictionary entry
3. Use Key struct with pre-computed hash codes

---

## 5. Object Pooling Comparison

### Jint: Comprehensive Pooling
```csharp
// Jint has dedicated pools for:
ObjectPool<JsValue[]> _poolArray1, _poolArray2, _poolArray3;  // 1-3 element arrays
ReferencePool _referencePool;
ArgumentsInstancePool _argumentsInstancePool;
JsValueArrayPool _jsValueArrayPool;
```

- Pools explicitly null out elements before return (GC-friendly)
- Thread-local fast path for single item
- Leak detection in DEBUG builds

### JsEngine2: Limited Pooling
```csharp
// JsEngine2 pools:
private static readonly ConcurrentBag<HashSet<Symbol>> SymbolSetPool;  // SyncFunctionInvoker
[ThreadStatic] private static PromiseAwaitState? t_cachedState;  // AwaitScheduler
ArrayPool<IJsPropertyAccessor?>.Shared;  // Prototype traversal
```

**Missing pools that would help:**
1. Argument array pool (1-4 element arrays)
2. PropertyDescriptor pool
3. JsEnvironment pool (short-lived scopes)
4. Reference/binding resolution cache pool

---

## 6. Hash Code Optimization

### Jint: Pre-Computed Key Struct
```csharp
// Jint: Key.cs
internal readonly struct Key : IEquatable<Key>
{
    internal readonly string Name;
    internal readonly int HashCode;  // Computed once at construction

    private Key(string name)
    {
        Name = name;
        HashCode = Hash.GetFNVHashCode(name);
    }
}
```

**Advantage:** Hash code computed once, reused in all dictionary operations.

### JsEngine2: Interned Symbol
```csharp
// JsEngine2: Symbol.cs
public sealed class Symbol : IEquatable<Symbol>
{
    private static readonly ConcurrentDictionary<string, Symbol> Cache;

    // GetHashCode uses unique ID, not string hash
    public override int GetHashCode() => Id;
}
```

**Advantage:** Reference equality enables fastest possible equality checks.
**Disadvantage:** String-keyed dictionaries don't benefit from Symbol interning.

**Recommendation:** Use Symbol as dictionary key where possible, or implement Key struct pattern.

---

## 7. Fast Path Optimizations

### Jint: PlainObject Fast Path
```csharp
// Jint: ObjectInstance.cs
public override JsValue Get(JsValue property, JsValue receiver)
{
    // Fast path: common case optimized
    if ((_type & InternalTypes.PlainObject) != InternalTypes.Empty &&
        ReferenceEquals(this, receiver) &&
        property.IsString())
    {
        // Direct dictionary lookup, skip descriptor protocol
    }
}
```

### JsEngine2: Two-Tier Lookup
```csharp
// JsEngine2: JsObject.cs
public bool TryGetProperty(string name, out object? value)
{
    // Fast path: own property first
    if (TryGetOwnProperty(name, this, null, out value))
        return true;

    // Fast path: no prototype
    if (Prototype is null && _prototypeAccessor is null)
        return false;

    // Slow path: prototype chain
}
```

Both engines have fast paths, but Jint's type flags enable faster type discrimination.

---

## 8. Areas Where JsEngine2 is Better

### Generator IR System
JsEngine2's Generator IR approach compiles generators to a flat instruction list:
```csharp
internal sealed record GeneratorPlan(
    ImmutableArray<GeneratorInstruction> Instructions,
    int EntryPoint);
```
This avoids AST replay and provides clean resumption semantics.

### Signal-Based Control Flow
```csharp
public interface ICompletionSignal { }
// ReturnCompletionSignal, ThrowFlowCompletionSignal, YieldCompletionSignal, etc.
```
Avoids exception overhead for non-exceptional control flow.

### Identifier Binding Cache
```csharp
// Per-context caching of resolved identifiers
private Dictionary<Symbol, (JsEnvironment env, object? value)>? _identifierBindingCache;
```
Avoids repeated scope chain walks for frequently accessed variables.

---

## 9. Actionable Optimization Opportunities

### High Impact
1. **Pool argument arrays** - Add JsValueArrayPool equivalent for 1-4 element arrays
2. **Pre-cache common values** - Cache small integers, empty string, common strings
3. **HybridDictionary for properties** - Use list for small objects, dict for large

### Medium Impact
4. **Pre-computed property keys** - Key struct with cached hash code
5. **Fast path for settled promises** - Skip microtask queue when already resolved
6. **PlainObject type flag** - Enable fast property access path

### Lower Impact
7. **Pool PropertyDescriptor** - Reuse descriptor objects
8. **Inline small arrays** - Use stackalloc for ≤4 arguments where safe
9. **Ref returns for dictionary access** - Avoid copying values on lookup

---

## 10. Benchmark Comparison (Async/Await Focus)

From prior benchmarks, the async/await gap was approximately 26x:
- **Jint:** Synchronous blocking with ManualResetEventSlim
- **JsEngine2:** CPS transformation + microtask queue + SpinWait

The primary contributors to this gap:
1. CPS transformation overhead at parse time
2. Microtask queue management
3. SpinWait CPU cycles
4. More allocations per await (callbacks, state objects)

Jint's approach trades spec compliance for raw performance. JsEngine2's approach is more correct but slower.

---

## Conclusion

Jint's performance advantages come primarily from:
1. **Struct-based ExecutionContext** (no heap allocation)
2. **Aggressive value caching** (integers, strings)
3. **Comprehensive object pooling** (arrays, references)
4. **Adaptive data structures** (HybridDictionary)
5. **Synchronous promise blocking** (simpler but less correct)

JsEngine2's strengths are:
1. **Better spec compliance** (proper microtask ordering)
2. **Generator IR** (cleaner than AST replay)
3. **Signal-based control flow** (no exception overhead)
4. **Symbol interning** (fast identifier equality)

The most impactful optimizations for JsEngine2 would focus on reducing allocations through pooling and caching, particularly for argument arrays and common values.
