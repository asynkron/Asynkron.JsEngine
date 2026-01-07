# Refactoring Plan for Asynkron.JsEngine

This document outlines refactoring opportunities identified in the Asynkron.JsEngine codebase. Priorities are based on impact, effort, and technical debt reduction.

## Table of Contents
- [CRITICAL (Address Immediately)](#critical-address-immediately)
- [HIGH (Address Soon)](#high-address-soon)
- [MEDIUM (Address in Iterations)](#medium-address-in-iterations)
- [LOWER (Address Long-term)](#lower-address-long-term)
- [Summary of Prioritized Work](#summary-of-prioritized-work)

---

## CRITICAL (Address Immediately)

### 1. Extract JsEngine Constructor Logic

**Location**: `JsEngine.cs:60-320`

**Issues**:
- Massive constructor (260+ lines) handling multiple concerns
- Options processing (lines 60-80)
- Realm creation (lines 81-90)
- Standard library initialization (lines 91-320)
- Global object property setup
- Built-in object creation

**Current Structure**:
```csharp
public JsEngine(IJsEngineOptions? options = null)
{
    // 260+ lines of initialization logic
    // - Options validation
    // - Realm setup
    // - Standard library initialization
    // - Global object creation
    // - Built-in object setup
}
```

**Refactoring Plan**:
1. Create `IStandardLibraryInitializer` interface
2. Extract standard library initialization to `JsStandardLibraryInitializer` class
3. Create separate methods for global object setup
4. Use dependency injection pattern for components

**New Structure**:
```csharp
public interface IStandardLibraryInitializer
{
    void Initialize(JsEngine engine, RealmState realm);
}

public sealed class JsStandardLibraryInitializer : IStandardLibraryInitializer
{
    private readonly IJsEngineOptions _options;

    public JsStandardLibraryInitializer(IJsEngineOptions options)
    {
        _options = options;
    }

    public void Initialize(JsEngine engine, RealmState realm)
    {
        InitializeObjects(engine, realm);
        InitializeGlobalProperties(engine, realm);
        InitializeBuiltinConstructors(engine, realm);
    }

    private void InitializeObjects(JsEngine engine, RealmState realm) { }
    private void InitializeGlobalProperties(JsEngine engine, RealmState realm) { }
    private void InitializeBuiltinConstructors(JsEngine engine, RealmState realm) { }
}

public sealed class JsEngine
{
    private readonly IStandardLibraryInitializer _stdLibInitializer;

    public JsEngine(IJsEngineOptions? options = null)
    {
        _options = options ?? new JsEngineOptions();
        Realm = CreateRealm();

        _stdLibInitializer = new JsStandardLibraryInitializer(_options);
        _stdLibInitializer.Initialize(this, Realm);
    }
}
```

**Benefits**:
- Easier to test initialization components independently
- Can swap different standard library configurations
- Constructor becomes much cleaner
- Follows Single Responsibility Principle

**Estimated Effort**: 8 hours

---

### 2. Extract JsEngine.StatementContainsImportMeta Method

**Location**: `JsEngine.cs:1043-1449` (~400 lines)

**Issues**:
- Massive method with deep nesting
- Repetitive pattern checking for import.meta in different statement types
- Multiple while loops inside switch cases inside if statements
- Hard to understand and maintain
- Difficult to add new statement type support

**Current Structure**:
```csharp
private bool StatementContainsImportMeta(IStatement statement)
{
    // 400 lines of nested switch/while/if statements
    // checking for import.meta usage in various contexts
    switch (statement)
    {
        case BlockStatement block:
            // nested logic
            break;
        case IfStatement ifStmt:
            // nested logic
            break;
        // ... many more cases
    }
}
```

**Refactoring Plan**:
1. Create `ImportMetaVisitor` class inheriting from `AstVisitor`
2. Let visitor pattern handle traversal
3. Track import.meta usage through visitor

**New Structure**:
```csharp
internal sealed class ImportMetaVisitor : AstVisitor
{
    public bool FoundImportMeta { get; private set; }

    public override void Visit(IdentifierExpression identifier)
    {
        if (identifier.Name == "import" &&
            identifier.Parent is MemberExpression member &&
            member.PropertyName == "meta")
        {
            FoundImportMeta = true;
        }
        base.Visit(identifier);
    }
}

// Usage
private bool StatementContainsImportMeta(IStatement statement)
{
    var visitor = new ImportMetaVisitor();
    visitor.Visit(statement);
    return visitor.FoundImportMeta;
}
```

**Benefits**:
- Method becomes clear and declarative
- Easier to add new statement type support
- Reduces nesting depth
- Leverages existing visitor pattern
- More testable

**Estimated Effort**: 6 hours

---

### 3. Split JsAstParser.ParseForStatement Method

**Location**: `JsAstParser.cs:1597-1842` (~250 lines)

**Issues**:
- Extremely long method handling multiple for-loop variants
- Deep nesting for variant detection (for, for-in, for-of, for-await-of)
- Multiple concerns: initializer parsing, condition parsing, increment parsing
- Hard to understand control flow
- Difficult to test each variant independently

**Current Structure**:
```csharp
private ForStatement ParseForStatement()
{
    // 250 lines handling:
    // - Regular for loop parsing
    // - for-in loop parsing
    // - for-of loop parsing
    // - for-await-of loop parsing
    // - Initializer parsing
    // - Condition parsing
    // - Increment parsing
    // - Binding pattern validation
}
```

**Refactoring Plan**:
1. Extract loop variant detection to `DetectForLoopVariant`
2. Create separate methods for each variant: `ParseFor`, `ParseForIn`, `ParseForOf`, `ParseForAwaitOf`
3. Extract common parsing logic to helper methods

**New Structure**:
```csharp
private enum ForLoopVariant
{
    For,
    ForIn,
    ForOf,
    ForAwaitOf
}

private ForLoopVariant DetectForLoopVariant(Token token)
{
    // Detect which for variant based on tokens
}

private ForStatement ParseForStatement()
{
    var variant = DetectForLoopVariant(_currentToken);

    return variant switch
    {
        ForLoopVariant.For => ParseFor(),
        ForLoopVariant.ForIn => ParseForIn(),
        ForLoopVariant.ForOf => ParseForOf(),
        ForLoopVariant.ForAwaitOf => ParseForAwaitOf(),
        _ => throw new InvalidOperationException($"Unknown for variant: {variant}")
    };
}

private ForStatement ParseFor()
{
    // Regular for loop parsing (~50 lines)
}

private ForStatement ParseForIn()
{
    // for-in loop parsing (~40 lines)
}

private ForStatement ParseForOf()
{
    // for-of loop parsing (~40 lines)
}

private ForStatement ParseForAwaitOf()
{
    // for-await-of loop parsing (~40 lines)
}

private IExpression ParseForInitializer() { }
private IExpression? ParseForCondition() { }
private IExpression? ParseForIncrement() { }
```

**Benefits**:
- Each method has single clear responsibility
- Easier to understand control flow
- Simpler to add new for-loop variants
- Reduced cognitive complexity
- More testable in isolation

**Estimated Effort**: 4 hours

---

### 4. Split JsAstParser.ParseClassElements Method

**Location**: `JsAstParser.cs:784-1106` (~320 lines)

**Issues**:
- Massive method handling all class element types
- Multiple concerns: field declarations, method declarations, getters/setters, validation
- Deep nesting for different element kinds
- Hard to maintain and extend
- Difficult to test individual element parsing

**Current Structure**:
```csharp
private ClassElementDeclaration[] ParseClassElements()
{
    // 320 lines handling:
    // - Instance fields
    // - Static fields
    // - Private fields
    // - Public methods
    // - Private methods
    // - Getters
    // - Setters
    // - Static methods
    // - Class fields
    // - Validation
}
```

**Refactoring Plan**:
1. Extract field parsing to `ParseFieldDeclaration`
2. Extract method parsing to `ParseMethodDeclaration`
3. Extract getter/setter parsing to `ParseAccessorDeclaration`
4. Use method dispatch based on element kind

**New Structure**:
```csharp
private ClassElementDeclaration[] ParseClassElements()
{
    var elements = new List<ClassElementDeclaration>();

    while (!IsClassEndToken())
    {
        var element = ParseClassElement();
        elements.Add(element);
    }

    return elements.ToArray();
}

private ClassElementDeclaration ParseClassElement()
{
    var modifiers = ParseModifiers();
    var kind = DetectElementKind();

    return kind switch
    {
        ClassElementKind.Field => ParseFieldDeclaration(modifiers),
        ClassElementKind.Method => ParseMethodDeclaration(modifiers),
        ClassElementKind.Getter => ParseAccessorDeclaration(modifiers, isGetter: true),
        ClassElementKind.Setter => ParseAccessorDeclaration(modifiers, isGetter: false),
        _ => throw new InvalidOperationException($"Unknown class element kind: {kind}")
    };
}

private ClassElementDeclaration ParseFieldDeclaration(ClassElementModifiers modifiers)
{
    // Field-specific parsing (~40 lines)
}

private ClassElementDeclaration ParseMethodDeclaration(ClassElementModifiers modifiers)
{
    // Method-specific parsing (~50 lines)
}

private ClassElementDeclaration ParseAccessorDeclaration(ClassElementModifiers modifiers, bool isGetter)
{
    // Getter/setter-specific parsing (~40 lines)
}
```

**Benefits**:
- Clear separation of concerns
- Easier to add new element types
- Better encapsulation of parsing logic
- More testable
- Reduced method complexity

**Estimated Effort**: 5 hours

---

### 5. Extract Magic Numbers to JsConstants Class

**Locations** (scattered throughout codebase):
- `JsEnvironment.cs:291-292`: `5000` (recursion depth)
- `EvaluationContext.cs:112`: `1000` (MaxCallDepth)
- `JsValue.cs:52`: `100000` (integer cache size)
- `JsValueCache.cs:22`: `10000` (IndexStringCacheSize)
- `JsArray.cs:1108-1114`: Array length thresholds (1000, 10000, 100000, etc.)

**Issues**:
- Magic numbers scattered across 15+ files
- No central place to understand constant values
- Difficult to tune performance parameters
- Risk of inconsistent values
- Poor code documentation

**Current Examples**:
```csharp
// JsEnvironment.cs:291
if (depth > 5000)
    throw new InvalidOperationException("Maximum recursion depth exceeded");

// EvaluationContext.cs:112
if (CallStackDepth > 1000)
    throw new InvalidOperationException("Maximum call depth exceeded");

// JsValue.cs:52
private const int IntegerCacheSize = 100000;

// JsArray.cs:1108
if (length < 1000) { /* fast path */ }
else if (length < 10000) { /* medium path */ }
```

**Refactoring Plan**:
1. Create `JsConstants.cs` class with all magic numbers
2. Add XML documentation for each constant
3. Replace all magic numbers with named constants
4. Add any missing constant definitions

**New Structure**:
```csharp
namespace Asynkron.JsEngine;

/// <summary>
/// Centralized constants used throughout the JavaScript engine implementation.
/// </summary>
public static class JsConstants
{
    /// <summary>
    /// Maximum recursion depth allowed before throwing an exception.
    /// Prevents stack overflow from deeply nested function calls.
    /// </summary>
    public const int MaxRecursionDepth = 5000;

    /// <summary>
    /// Maximum call stack depth for JavaScript execution.
    /// Limits the number of nested function calls.
    /// </summary>
    public const int MaxCallDepth = 1000;

    /// <summary>
    /// Size of the integer cache for frequently used integer values.
    /// Caching integers from -CacheSize to +CacheSize improves performance.
    /// </summary>
    public const int IntegerCacheSize = 100000;

    /// <summary>
    /// Cache size for string representations of array indices.
    /// Improves performance when accessing array properties.
    /// </summary>
    public const int IndexStringCacheSize = 10000;

    /// <summary>
    /// Thresholds for array length optimizations.
    /// Different algorithms are used based on array size.
    /// </summary>
    public static readonly int[] ArrayLengthThresholds =
    {
        1000,
        10000,
        100000,
        1000000,
        10000000,
        100000000,
        1000000000
    };
}
```

**Migration Example**:
```csharp
// Before
if (depth > 5000)
    throw new InvalidOperationException("Maximum recursion depth exceeded");

// After
if (depth > JsConstants.MaxRecursionDepth)
    throw new InvalidOperationException($"Maximum recursion depth of {JsConstants.MaxRecursionDepth} exceeded");
```

**Files to Update**:
- JsEnvironment.cs
- EvaluationContext.cs
- JsValue.cs
- JsValueCache.cs
- JsArray.cs
- Any other files with magic numbers

**Benefits**:
- Clear, self-documenting code
- Easy to tune performance parameters
- Consistent values across codebase
- Better code documentation
- Reduced risk of inconsistencies

**Estimated Effort**: 3 hours

---

### 6. Create Centralized Error Helper

**Location**: EvalHostFunction.cs and other files - inconsistent error throwing

**Issues**:
- Duplicate error messages across multiple files
- Inconsistent error throwing patterns
- Hard to maintain error messages
- No central error handling strategy

**Current Examples**:
```csharp
// EvalHostFunction.cs:87
throw StandardLibrary.ThrowSyntaxError("Unexpected reserved identifier in strict eval", realm);

// EvalHostFunction.cs:267
throw StandardLibrary.ThrowSyntaxError("super calls are not allowed in eval inside class field initializers", realm);

// SimpleInstanceConstructorBase.cs
throw ThrowTypeError($"Constructor {ConstructorName} requires 'new'", realm: Realm);
```

**Refactoring Plan**:
1. Create `JsErrorMessages.cs` for error message constants
2. Create `JsErrorHelper.cs` for error creation helpers
3. Replace all error throwing with centralized helpers

**New Structure**:
```csharp
public static class JsErrorMessages
{
    // Syntax errors
    public const string UnexpectedReservedIdentifierInStrictEval =
        "Unexpected reserved identifier in strict eval.";

    public const string SuperCallsNotAllowedInClassFieldInitializer =
        "super calls are not allowed in eval inside class field initializers.";

    // Type errors
    public const string ConstructorRequiresNew =
        "Constructor requires 'new' operator.";

    public const string CannotConvertUndefinedToNumber =
        "Cannot convert undefined to number.";

    public const string CannotConvertNullToNumber =
        "Cannot convert null to number.";

    // Constructor errors
    public const string ConstructorRequiresNew =
        "Constructor requires 'new' operator.";
}

public static class JsErrorHelper
{
    public static ThrowSignal SyntaxError(string message, EvaluationContext? context, RealmState? realm)
    {
        return new ThrowSignal(StandardLibrary.CreateSyntaxError(message, context, realm));
    }

    public static ThrowSignal TypeError(string message, EvaluationContext? context, RealmState? realm)
    {
        return new ThrowSignal(StandardLibrary.CreateTypeError(message, context, realm));
    }

    public static ThrowSignal ReferenceError(string message, EvaluationContext? context, RealmState? realm)
    {
        return new ThrowSignal(StandardLibrary.CreateReferenceError(message, context, realm));
    }

    public static ThrowSignal RangeError(string message, EvaluationContext? context, RealmState? realm)
    {
        return new ThrowSignal(StandardLibrary.CreateRangeError(message, context, realm));
    }

    public static ThrowSignal UnexpectedReservedIdentifierInStrictEvalError(EvaluationContext? context, RealmState? realm)
    {
        return SyntaxError(JsErrorMessages.UnexpectedReservedIdentifierInStrictEval, context, realm);
    }

    public static ThrowSignal SuperCallsNotAllowedInClassFieldInitializerError(EvaluationContext? context, RealmState? realm)
    {
        return SyntaxError(JsErrorMessages.SuperCallsNotAllowedInClassFieldInitializer, context, realm);
    }
}
```

**Migration Example**:
```csharp
// Before
throw StandardLibrary.ThrowSyntaxError("Unexpected reserved identifier in strict eval", realm);

// After
throw JsErrorHelper.UnexpectedReservedIdentifierInStrictEvalError(context: null, realm: realm);

// Before
throw new ThrowSignal(StandardLibrary.CreateTypeError($"Constructor {ConstructorName} requires 'new'", context, Realm));

// After
throw JsErrorHelper.CreateTypeError(JsErrorMessages.ConstructorRequiresNew, context, Realm);
```

**Files to Update**:
- EvalHostFunction.cs
- JsValueExtensions.cs
- JsOps.cs
- SimpleInstanceConstructorBase.cs
- Any other files throwing errors

**Benefits**:
- Consistent error messages
- Easier to maintain error handling
- Clear separation of error types
- Better testability
- Reduced code duplication

**Estimated Effort**: 4 hours

---

## HIGH (Address Soon)

### 7. Extract Timer Management from JsEngine

**Location**: `JsEngine.cs` - timer-related methods scattered throughout

**Issues**:
- Timer management mixed with core engine logic
- No clear abstraction for timer operations
- Hard to test timer functionality independently
- Difficult to optimize timer execution

**Current Structure**:
```csharp
public sealed class JsEngine
{
    // Timer-related methods intermingled with other concerns
    private readonly Dictionary<int, Timer> _timers = new();
    private int _nextTimerId = 1;

    public int SetTimeout(JsValue handler, double delay) { }
    public void ClearTimeout(int id) { }
    public int SetInterval(JsValue handler, double delay) { }
    public void ClearInterval(int id) { }
}
```

**Refactoring Plan**:
1. Create `ITimerManager` interface
2. Extract timer logic to `JsTimerManager` class
3. Inject timer manager into JsEngine

**New Structure**:
```csharp
public interface ITimerManager
{
    int SetTimeout(JsValue handler, double delay);
    void ClearTimeout(int id);
    int SetInterval(JsValue handler, double delay);
    void ClearInterval(int id);
    void ClearAll();
}

public sealed class JsTimerManager : ITimerManager
{
    private readonly Dictionary<int, Timer> _timers = new();
    private int _nextTimerId = 1;

    public int SetTimeout(JsValue handler, double delay)
    {
        var id = _nextTimerId++;
        // Implementation
        return id;
    }

    public void ClearTimeout(int id)
    {
        // Implementation
    }

    // Other methods...
}

public sealed class JsEngine
{
    private readonly ITimerManager _timerManager;

    public JsEngine(IJsEngineOptions? options = null)
    {
        _timerManager = new JsTimerManager();
        // Other initialization
    }
}
```

**Benefits**:
- Clear separation of concerns
- Easier to test timer functionality
- Supports custom timer implementations
- Better encapsulation
- Easier to maintain

**Estimated Effort**: 3 hours

---

### 8. Extract Microtask Scheduling from JsEngine

**Location**: `JsEngine.cs` - microtask-related methods scattered throughout

**Issues**:
- Microtask scheduling mixed with core engine logic
- No clear abstraction for microtask queue
- Hard to test microtask behavior independently
- Difficult to optimize microtask execution

**Current Structure**:
```csharp
public sealed class JsEngine
{
    // Microtask-related methods intermingled with other concerns
    private readonly List<IMicrotask> _microtasks = new();

    public void EnqueueMicrotask(IMicrotask microtask)
    {
        _microtasks.Add(microtask);
    }

    private async Task DrainMicrotasksAsync(CancellationToken cancellationToken)
    {
        // Implementation
    }
}
```

**Refactoring Plan**:
1. Create `IMicrotaskScheduler` interface
2. Extract microtask logic to `JsMicrotaskScheduler` class
3. Inject scheduler into JsEngine

**New Structure**:
```csharp
public interface IMicrotaskScheduler
{
    void Enqueue(IMicrotask microtask);
    Task DrainAsync(CancellationToken cancellationToken = default);
    void Clear();
    int Count { get; }
}

public sealed class JsMicrotaskScheduler : IMicrotaskScheduler
{
    private readonly List<IMicrotask> _microtasks = new();

    public void Enqueue(IMicrotask microtask)
    {
        _microtasks.Add(microtask);
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (_microtasks.Count > 0)
        {
            var task = _microtasks[0];
            _microtasks.RemoveAt(0);

            try
            {
                await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Handle error
            }
        }
    }

    public void Clear() => _microtasks.Clear();

    public int Count => _microtasks.Count;
}
```

**Benefits**:
- Clear separation of concerns
- Easier to test microtask behavior
- Supports custom scheduling strategies
- Better encapsulation
- Potential for performance optimizations

**Estimated Effort**: 2 hours

---

### 9. Extract Module Loading from JsEngine

**Location**: `JsEngine.cs` - module-related methods scattered throughout

**Issues**:
- Module loading logic mixed with core engine logic
- No clear abstraction for module system
- Hard to test module loading independently
- Difficult to support different module systems (ESM, CommonJS)

**Current Structure**:
```csharp
public sealed class JsEngine
{
    // Module-related methods intermingled with other concerns
    public async Task<object?> EvaluateModuleAsync(string source)
    {
        // Module evaluation logic
    }

    private ProgramNode ParseModule(string source)
    {
        // Module parsing logic
    }
}
```

**Refactoring Plan**:
1. Create `IModuleSystem` interface
2. Extract module logic to `EsModuleSystem` class
3. Inject module system into JsEngine

**New Structure**:
```csharp
public interface IModuleSystem
{
    Task<object?> EvaluateModuleAsync(string source, CancellationToken cancellationToken = default);
    ProgramNode ParseModule(string source);
    JsValue? GetModuleExport(string moduleName, string exportName);
}

public sealed class EsModuleSystem : IModuleSystem
{
    private readonly JsEngine _engine;
    private readonly Dictionary<string, JsValue> _moduleCache = new();

    public EsModuleSystem(JsEngine engine)
    {
        _engine = engine;
    }

    public async Task<object?> EvaluateModuleAsync(string source, CancellationToken cancellationToken = default)
    {
        // Implementation
    }

    public ProgramNode ParseModule(string source)
    {
        // Implementation
    }

    public JsValue? GetModuleExport(string moduleName, string exportName)
    {
        // Implementation
    }
}

public sealed class JsEngine
{
    private readonly IModuleSystem _moduleSystem;

    public JsEngine(IJsEngineOptions? options = null)
    {
        _moduleSystem = new EsModuleSystem(this);
        // Other initialization
    }
}
```

**Benefits**:
- Clear separation of concerns
- Easier to test module loading
- Supports different module systems
- Better encapsulation
- Easier to maintain

**Estimated Effort**: 4 hours

---

### 10. Split EvalHostFunction.Invoke Method

**Location**: `EvalHostFunction.cs:48-300+` (~250+ lines)

**Issues**:
- Massive method handling multiple concerns
- Validation, parsing, execution, analysis all mixed
- Deep nesting for different eval scenarios
- Hard to test individual components
- Difficult to maintain

**Current Structure**:
```csharp
public JsValue Invoke(JsValue thisBinding, JsValue[] arguments)
{
    // 250+ lines handling:
    // - Strict mode validation
    // - Reserved identifier checking
    // - Super call validation
    // - Variable declaration analysis
    // - Source parsing
    // - Code execution
}
```

**Refactoring Plan**:
1. Create `EvalValidator` class for validation logic
2. Create `VariableDeclarationAnalyzer` class
3. Extract error handling to helper methods
4. Simplify Invoke method to orchestrate validation and execution

**New Structure**:
```csharp
public sealed class EvalValidator
{
    public void ValidateStrictMode(JsEnvironment env, ProgramNode program)
    {
        // Strict mode validation
    }

    public void ValidateReservedIdentifiers(ProgramNode program, RealmState realm)
    {
        // Reserved identifier validation
    }

    public void ValidateSuperCalls(ProgramNode program, RealmState realm)
    {
        // Super call validation
    }
}

public sealed class VariableDeclarationAnalyzer
{
    public HashSet<Symbol> AnalyzeVariableDeclarations(ProgramNode program)
    {
        // Variable declaration analysis
    }
}

public sealed class EvalHostFunction
{
    private readonly EvalValidator _validator = new();
    private readonly VariableDeclarationAnalyzer _variableAnalyzer = new();

    public JsValue Invoke(JsValue thisBinding, JsValue[] arguments)
    {
        var source = GetSource(arguments);
        var env = GetCurrentEnvironment();

        _validator.ValidateStrictMode(env, program);
        _validator.ValidateReservedIdentifiers(program, realm);
        _validator.ValidateSuperCalls(program, realm);

        var declaredVars = _variableAnalyzer.AnalyzeVariableDeclarations(program);

        return ExecuteEval(env, program, declaredVars);
    }

    private JsValue ExecuteEval(JsEnvironment env, ProgramNode program, HashSet<Symbol> declaredVars)
    {
        // Execution logic
    }
}
```

**Benefits**:
- Clear separation of validation and execution
- Easier to test individual components
- Reduced method complexity
- Better code organization
- Easier to maintain

**Estimated Effort**: 5 hours

---

### 11. Extract Binding Resolution Strategies

**Location**: `JsEnvironment.cs` - multiple identifier resolution methods

**Issues**:
- Binding resolution logic mixed with environment management
- No clear abstraction for resolution strategies
- Difficult to optimize or change resolution behavior
- Tight coupling between environment and resolution

**Current Structure**:
```csharp
public sealed class JsEnvironment
{
    // Multiple methods for binding resolution with similar patterns
    private bool TryResolveGlobalObjectBinding(Symbol name, out JsValue value)
    {
        // Implementation
    }

    private bool TryResolveWithBinding(Symbol name, out JsValue value)
    {
        // Implementation
    }

    private bool TryResolveLexicalBinding(Symbol name, out JsValue value)
    {
        // Implementation
    }
}
```

**Refactoring Plan**:
1. Create `IBindingResolver` interface
2. Create concrete resolver implementations
3. Use composite pattern for chained resolvers
4. Inject resolvers into JsEnvironment

**New Structure**:
```csharp
public interface IBindingResolver
{
    bool TryResolve(Symbol name, out JsValue value);
}

public sealed class GlobalBindingResolver : IBindingResolver
{
    private readonly JsObject _globalObject;

    public GlobalBindingResolver(JsObject globalObject)
    {
        _globalObject = globalObject;
    }

    public bool TryResolve(Symbol name, out JsValue value)
    {
        // Implementation
    }
}

public sealed class WithBindingResolver : IBindingResolver
{
    private readonly JsObject _withObject;
    private readonly IBindingResolver? _parent;

    public WithBindingResolver(JsObject withObject, IBindingResolver? parent = null)
    {
        _withObject = withObject;
        _parent = parent;
    }

    public bool TryResolve(Symbol name, out JsValue value)
    {
        // Implementation
    }
}

public sealed class LexicalBindingResolver : IBindingResolver
{
    private readonly Dictionary<Symbol, JsValue> _bindings;

    public LexicalBindingResolver(Dictionary<Symbol, JsValue> bindings)
    {
        _bindings = bindings;
    }

    public bool TryResolve(Symbol name, out JsValue value)
    {
        // Implementation
    }
}

public sealed class CompositeBindingResolver : IBindingResolver
{
    private readonly List<IBindingResolver> _resolvers = new();

    public void AddResolver(IBindingResolver resolver)
    {
        _resolvers.Add(resolver);
    }

    public bool TryResolve(Symbol name, out JsValue value)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.TryResolve(name, out value))
            {
                return true;
            }
        }

        value = JsValue.Undefined;
        return false;
    }
}
```

**Benefits**:
- Clear separation of concerns
- Easier to test resolution strategies
- Supports custom resolution behavior
- Better encapsulation
- More flexible architecture

**Estimated Effort**: 6 hours

---

### 12. Reduce HashSet Allocations with Object Pools

**Location**: EvalHostFunction.cs, JsAstParser.cs, and other files

**Issues**:
- Frequent HashSet allocations in parsing and evaluation hot paths
- Increased GC pressure
- Performance impact on large scripts
- No object pooling strategy

**Current Examples**:
```csharp
// EvalHostFunction.cs:324
var varDeclaredNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

// EvalHostFunction.cs:465
var lexDeclaredNames = new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance);

// JsAstParser.cs:3080
var cookedStrings = new List<string?>();
var rawStrings = new List<string>();
```

**Refactoring Plan**:
1. Create object pools for frequently used collections
2. Use `using` pattern for pooled resources
3. Pool HashSet<Symbol>, List<string>, etc.

**New Structure**:
```csharp
// Create ObjectPool infrastructure
public static class CollectionPools
{
    private static readonly ObjectPool<HashSet<Symbol>> SymbolHashSetPool =
        new ObjectPool<HashSet<Symbol>>(
            () => new HashSet<Symbol>(ReferenceEqualityComparer<Symbol>.Instance),
            64); // Pool size

    private static readonly ObjectPool<List<string>> StringListPool =
        new ObjectPool<List<string>>(
            () => new List<string>(),
            64);

    public static PooledHashSet<Symbol> GetSymbolHashSet()
    {
        var hashSet = SymbolHashSetPool.Get();
        return new PooledHashSet<Symbol>(hashSet, SymbolHashSetPool);
    }

    public static PooledList<string> GetStringList()
    {
        var list = StringListPool.Get();
        return new PooledList<string>(list, StringListPool);
    }
}

public sealed class PooledHashSet<T> : IDisposable
{
    private readonly HashSet<T> _hashSet;
    private readonly ObjectPool<HashSet<T>> _pool;

    public PooledHashSet(HashSet<T> hashSet, ObjectPool<HashSet<T>> pool)
    {
        _hashSet = hashSet;
        _pool = pool;
    }

    public HashSet<T> Value => _hashSet;

    public void Dispose()
    {
        _hashSet.Clear();
        _pool.Return(_hashSet);
    }
}

public sealed class PooledList<T> : IDisposable
{
    private readonly List<T> _list;
    private readonly ObjectPool<List<T>> _pool;

    public PooledList(List<T> list, ObjectPool<List<T>> pool)
    {
        _list = list;
        _pool = pool;
    }

    public List<T> Value => _list;

    public void Dispose()
    {
        _list.Clear();
        _pool.Return(_list);
    }
}

// Usage
using var varDeclaredNames = CollectionPools.GetSymbolHashSet();
// Use varDeclaredNames.Value
// Automatically returned to pool when disposed
```

**Files to Update**:
- EvalHostFunction.cs
- JsAstParser.cs
- Any other files with frequent collection allocations

**Benefits**:
- Reduced GC pressure
- Improved performance in hot paths
- Less memory allocation
- Better resource management
- Automatic cleanup with using pattern

**Estimated Effort**: 3 hours

---

### 13. Extract Yield Rewrite Patterns to Separate Classes

**Location**: `GeneratorYieldLowerer.cs:40-299` - multiple yield rewrite patterns

**Issues**:
- All yield rewrite logic in one large file
- Similar patterns repeated for different yield types
- Hard to add new yield rewrite types
- Difficult to test individual rewrite patterns

**Current Structure**:
```csharp
public sealed class GeneratorYieldLowerer
{
    // Multiple switch cases and methods for different yield patterns
    private IStatement TryRewriteClassExpressionDeclaration(...)
    {
        // Class field yield rewrite
    }

    private IStatement TryRewriteObjectLiteralDeclaration(...)
    {
        // Object literal yield rewrite
    }

    private IStatement RewriteForOfStatement(...)
    {
        // for-of yield rewrite
    }
}
```

**Refactoring Plan**:
1. Create abstract `YieldRewriter` base class
2. Create concrete rewriters for each pattern
3. Use strategy pattern for rewrite selection
4. Register rewriters in factory

**New Structure**:
```csharp
public abstract class YieldRewriter
{
    public abstract bool TryRewrite(IStatement statement, out IStatement? rewritten);
}

public sealed class ClassYieldRewriter : YieldRewriter
{
    public override bool TryRewrite(IStatement statement, out IStatement? rewritten)
    {
        // Class field yield rewrite
    }
}

public sealed class ObjectYieldRewriter : YieldRewriter
{
    public override bool TryRewrite(IStatement statement, out IStatement? rewritten)
    {
        // Object literal yield rewrite
    }
}

public sealed class ForOfYieldRewriter : YieldRewriter
{
    public override bool TryRewrite(IStatement statement, out IStatement? rewritten)
    {
        // for-of yield rewrite
    }
}

public sealed class YieldRewriterFactory
{
    private readonly List<YieldRewriter> _rewriters = new();

    public YieldRewriterFactory()
    {
        _rewriters.Add(new ClassYieldRewriter());
        _rewriters.Add(new ObjectYieldRewriter());
        _rewriters.Add(new ForOfYieldRewriter());
    }

    public IStatement? TryRewrite(IStatement statement)
    {
        foreach (var rewriter in _rewriters)
        {
            if (rewriter.TryRewrite(statement, out var rewritten))
            {
                return rewritten;
            }
        }

        return null;
    }
}
```

**Benefits**:
- Clear separation of rewrite patterns
- Easier to add new yield types
- Better testability of individual rewriters
- Extensible architecture
- Cleaner code organization

**Estimated Effort**: 4 hours

---

## MEDIUM (Address in Iterations)

### 14. Extract Slot Management to JsSlotManager

**Location**: `JsEnvironment.cs` - slot management mixed with binding resolution

**Issues**:
- Slot allocation and management mixed with environment logic
- No clear abstraction for slot operations
- Difficult to optimize slot usage
- Tight coupling between environment and slots

**Current Structure**:
```csharp
public sealed class JsEnvironment
{
    // Slot-related operations intermingled with other concerns
    private int _nextSlotIndex = 0;

    public int AllocateSlot()
    {
        return _nextSlotIndex++;
    }
}
```

**Refactoring Plan**:
1. Create `JsSlotManager` class
2. Move slot allocation and management to manager
3. Inject manager into JsEnvironment

**New Structure**:
```csharp
public sealed class JsSlotManager
{
    private readonly List<JsValue> _slots = new();
    private readonly Stack<int> _freeSlots = new();
    private int _nextSlotIndex = 0;

    public int AllocateSlot()
    {
        if (_freeSlots.TryPop(out var freeSlot))
        {
            return freeSlot;
        }

        var slot = _nextSlotIndex++;
        _slots.Add(JsValue.Undefined);
        return slot;
    }

    public void FreeSlot(int slot)
    {
        _freeSlots.Push(slot);
        _slots[slot] = JsValue.Undefined;
    }

    public JsValue GetSlotValue(int slot)
    {
        return _slots[slot];
    }

    public void SetSlotValue(int slot, JsValue value)
    {
        _slots[slot] = value;
    }
}
```

**Benefits**:
- Clear separation of concerns
- Easier to optimize slot management
- Better testability
- Potential for slot pooling

**Estimated Effort**: 3 hours

---

### 15. Implement or Document TODOs

**Locations**:
- `ExecutionPlanBuilder.cs` - TODO comment about key method
- `GeneratorYieldLowerer.cs` - TODO for rest element lowering (lines 768, 789)
- `TemporalNowPrototype.cs` - 11 TODOs for unimplemented methods
- `TemporalZonedDateTimePrototype.cs` - 2 TODOs for unimplemented methods

**Issues**:
- 20+ TODO comments in codebase
- Incomplete implementations
- Unclear roadmap for completion

**Refactoring Plan**:
1. Create GitHub issues for each TODO
2. Implement or document why incomplete
3. Throw `NotImplementedException` for stub methods

**Example**:
```csharp
// Before
public JsValue ToPlainDateTime(JsValue[] arguments)
{
    // TODO: Implement toPlainDateTime
    return JsValue.Undefined;
}

// After
public JsValue ToPlainDateTime(JsValue[] arguments)
{
    throw new NotImplementedException(
        "Temporal.PlainDate.prototype.toPlainDateTime is not yet implemented. " +
        "See issue: https://github.com/yourrepo/issues/XXX");
}
```

**Benefits**:
- Clear tracking of incomplete features
- Better documentation of limitations
- Explicit error messages for users
- Ability to prioritize work

**Estimated Effort**: 20 hours (varies by TODO)

---

### 16. Extract Temporal Operations to Separate Classes

**Location**: `TemporalHelper.cs` (2,243 lines), `TemporalNowPrototype.cs` (11 TODOs), `TemporalZonedDateTimePrototype.cs` (2 TODOs)

**Issues**:
- Monolithic temporal helper class
- Multiple responsibilities: date parsing, time zone conversions, calendar operations
- Hard to maintain and extend
- Many incomplete implementations

**Current Structure**:
```csharp
public static class TemporalHelper
{
    // 2000+ lines of temporal operations
    // - Date parsing
    // - Time zone conversions
    // - Calendar operations
    // - Duration calculations
}
```

**Refactoring Plan**:
1. Extract date parsing to `TemporalDateParser`
2. Extract time zone operations to `TemporalTimeZone`
3. Extract calendar operations to `TemporalCalendar`
4. Extract duration calculations to `TemporalDuration`

**New Structure**:
```csharp
public static class TemporalDateParser
{
    public static DateTime ParseDate(string isoString) { }
    public static DateTimeOffset ParseDateTime(string isoString) { }
}

public static class TemporalTimeZone
{
    public static DateTime ConvertToTimeZone(DateTime dateTime, string timeZone) { }
    public static TimeSpan GetOffset(DateTime dateTime, string timeZone) { }
}

public static class TemporalCalendar
{
    public static int GetDayOfWeek(DateTime date) { }
    public static int GetWeekOfYear(DateTime date) { }
}

public static class TemporalDuration
{
    public static TimeSpan Add(TimeSpan duration1, TimeSpan duration2) { }
    public static string Format(TimeSpan duration) { }
}
```

**Benefits**:
- Clear separation of concerns
- Easier to maintain temporal operations
- Better testability
- More focused classes
- Easier to extend

**Estimated Effort**: 8 hours

---

### 17. Improve Caching on AST Nodes

**Locations**:
- `JsEngine.cs:1539-1550`: `ContainsTopLevelAwait` - computed each time
- `JsEngine.cs:1043-1449`: Import.meta checking - repeated analysis
- Various other AST analyses

**Issues**:
- Repeated AST traversals for same computations
- No caching of frequently computed values
- Performance impact on large programs
- Redundant analysis operations

**Current Structure**:
```csharp
// Re-computed each time
private bool ContainsTopLevelAwait(ProgramNode program)
{
    return AstShapeAnalyzer.StatementsContainAwait(program.Body);
}
```

**Refactoring Plan**:
1. Add lazy cached properties to AST nodes
2. Cache computed results on first access
3. Use nullable bool for lazy initialization

**New Structure**:
```csharp
public abstract class ProgramNode : AstNode
{
    private bool? _containsTopLevelAwait;

    public bool ContainsTopLevelAwait
    {
        get
        {
            if (_containsTopLevelAwait.HasValue)
            {
                return _containsTopLevelAwait.Value;
            }

            _containsTopLevelAwait = AstShapeAnalyzer.StatementsContainAwait(Body);
            return _containsTopLevelAwait.Value;
        }
    }
}

public abstract class IStatement : AstNode
{
    private bool? _containsImportMeta;

    public bool ContainsImportMeta
    {
        get
        {
            if (_containsImportMeta.HasValue)
            {
                return _containsImportMeta.Value;
            }

            var visitor = new ImportMetaVisitor();
            visitor.Visit(this);
            _containsImportMeta = visitor.FoundImportMeta;
            return _containsImportMeta.Value;
        }
    }
}
```

**Benefits**:
- Avoid repeated AST traversals
- Better performance
- Lazy computation only when needed
- Clear caching strategy

**Estimated Effort**: 4 hours

---

### 18. Extract Dictionary Operations from JsObject

**Location**: `JsObject.cs:63-88` - IDictionary implementation mixed with JsObject concerns

**Issues**:
- IDictionary implementation exposes .NET collection API
- Violates JavaScript object semantics
- Tight coupling to .NET collections
- Confusing API surface

**Current Structure**:
```csharp
public sealed class JsObject : JsValue, IDictionary<string, object?>
{
    // IDictionary implementation mixed with JsObject logic
    public object? this[string key]
    {
        get => GetProperty(key).ToNativeValue();
        set => SetProperty(key, value.FromNativeValue());
    }

    public ICollection<string> Keys => GetOwnPropertyKeys().ToList();
}
```

**Refactoring Plan**:
1. Remove IDictionary implementation
2. Keep JavaScript-native property API
3. Create adapter class for .NET dictionary access if needed

**New Structure**:
```csharp
public sealed class JsObject : JsValue
{
    // Only JavaScript-native properties
    public JsValue GetProperty(Symbol name) { }
    public void SetProperty(Symbol name, JsValue value) { }
    public bool HasProperty(Symbol name) { }
    public bool DeleteProperty(Symbol name) { }
}

// Optional adapter for .NET compatibility
public sealed class JsObjectDictionaryAdapter : IDictionary<string, object?>
{
    private readonly JsObject _jsObject;

    public JsObjectDictionaryAdapter(JsObject jsObject)
    {
        _jsObject = jsObject;
    }

    public object? this[string key]
    {
        get => _jsObject.GetProperty(Symbol.Get(key)).ToNativeValue();
        set => _jsObject.SetProperty(Symbol.Get(key), value.FromNativeValue());
    }

    // Other IDictionary members...
}
```

**Benefits**:
- Cleaner API surface
- Better separation of concerns
- JavaScript-native semantics preserved
- Optional .NET dictionary access via adapter

**Estimated Effort**: 3 hours

---

### 19. Unify Evaluation Logic in JsEngine

**Location**: `JsEngine.cs` - duplicate evaluation logic across methods

**Issues**:
- `Evaluate` and `EvaluateAndAwait` have similar logic
- Code duplication in evaluation paths
- Hard to maintain consistency
- Risk of divergent behavior

**Current Structure**:
```csharp
public Task<object?> Evaluate(string source, CancellationToken cancellationToken = default)
{
    // ~80 lines of evaluation logic
}

public async Task<object?> EvaluateAndAwait(string source, CancellationToken cancellationToken = default)
{
    // Similar logic with minor differences
}
```

**Refactoring Plan**:
1. Create `EvaluationOptions` class
2. Extract common evaluation logic to private method
3. Use options parameter for differences

**New Structure**:
```csharp
public sealed class EvaluationOptions
{
    public bool AwaitMicrotasks { get; set; }
    public bool ForceModule { get; set; }
    public CancellationToken CancellationToken { get; set; }
}

public Task<object?> Evaluate(string source, CancellationToken cancellationToken = default)
{
    return EvaluateAsync(source, new EvaluationOptions
    {
        CancellationToken = cancellationToken
    });
}

public async Task<object?> EvaluateAndAwait(string source, CancellationToken cancellationToken = default)
{
    return await EvaluateAsync(source, new EvaluationOptions
    {
        AwaitMicrotasks = true,
        CancellationToken = cancellationToken
    }).ConfigureAwait(false);
}

private async Task<object?> EvaluateAsync(string source, EvaluationOptions options)
{
    var program = options.ForceModule
        ? _moduleSystem.ParseModule(source)
        : ParseScript(source);

    var containsTopLevelAwait = options.ForceModule && program.ContainsTopLevelAwait;

    var result = containsTopLevelAwait || options.AwaitMicrotasks
        ? await ExecuteProgramWithAwait(program, options).ConfigureAwait(false)
        : ExecuteProgram(program, options);

    return result;
}
```

**Benefits**:
- Reduced code duplication
- Easier to maintain
- Consistent behavior
- More flexible API

**Estimated Effort**: 2 hours

---

### 20. Add Abstraction Layers for Parsing and Execution

**Locations**: JsEngine.cs, JsAstParser.cs, TypedAstEvaluator.cs

**Issues**:
- No clear separation between parsing and execution
- Tight coupling between components
- Hard to test parsing independently
- Difficult to swap implementations

**Refactoring Plan**:
1. Create `ICodeParser` interface
2. Create `ICodeExecutor` interface
3. Inject interfaces into JsEngine

**New Structure**:
```csharp
public interface ICodeParser
{
    public ProgramNode Parse(string source, ParseMode mode);
}

public interface ICodeExecutor
{
    public async Task<JsValue> ExecuteAsync(ProgramNode program, CancellationToken cancellationToken);
}

public sealed class JsAstParser : ICodeParser
{
    public ProgramNode Parse(string source, ParseMode mode)
    {
        // Implementation
    }
}

public sealed class TypedAstEvaluator : ICodeExecutor
{
    public async Task<JsValue> ExecuteAsync(ProgramNode program, CancellationToken cancellationToken)
    {
        // Implementation
    }
}

public sealed class JsEngine
{
    private readonly ICodeParser _parser;
    private readonly ICodeExecutor _executor;

    public JsEngine(ICodeParser parser, ICodeExecutor executor)
    {
        _parser = parser;
        _executor = executor;
    }
}
```

**Benefits**:
- Clear separation of concerns
- Swappable parsers and executors
- Better testability
- More flexible architecture
- Easier to extend

**Estimated Effort**: 6 hours

---

## LOWER (Address Long-term)

### 21. Review All Methods >100 Lines

**Locations** (10+ methods identified):
- `JsEngine.cs:StatementContainsImportMeta` (~400 lines)
- `JsAstParser.cs:ParseForStatement` (~250 lines)
- `JsAstParser.cs:ParseExportStatement` (~200 lines)
- `JsAstParser.cs:ParseClassElements` (~320 lines)
- `JsEnvironment.cs:DefineJsValue` (~130 lines)
- `JsEnvironment.cs:DefineFunctionScoped` (~200 lines)
- `EvalHostFunction.cs:Invoke` (~250+ lines)

**Issues**:
- Large methods are hard to understand
- Difficult to test in isolation
- Increased cognitive complexity
- Risk of bugs in complex methods

**Refactoring Plan**:
1. Systematically identify all methods >100 lines
2. Extract helper methods for sub-concerns
3. Reduce nesting through early returns
4. Use Extract Method refactoring pattern

**Estimated Effort**: 15 hours

---

### 22. Improve String Handling

**Locations**:
- Multiple files doing `string.Equals(x, "NaN", StringComparison.Ordinal)`
- Inconsistent string comparison patterns
- String concatenation in error messages

**Issues**:
- Inconsistent string comparison patterns
- Potential allocations from string operations
- No centralization of common string operations

**Refactoring Plan**:
1. Create string comparison helpers
2. Use compiled regex for complex patterns
3. Optimize string concatenation
4. Cache frequently used strings

**Estimated Effort**: 3 hours

---

### 23. Standardize Naming Conventions

**Locations**:
- Inconsistent `Try*` patterns
- Inconsistent internal vs public access
- Inconsistent field naming

**Issues**:
- Mixed naming patterns
- Hard to discover APIs
- Confusing method names

**Refactoring Plan**:
1. Document naming conventions
2. Audit all public APIs
3. Rename methods for consistency
4. Update documentation

**Estimated Effort**: 4 hours

---

### 24. Extract JsEnvironment Responsibilities

**Location**: `JsEnvironment.cs` (3,951 lines)

**Issues**:
- Environment manages scopes, slots, bindings, caching, mutations
- Too many responsibilities
- Hard to maintain

**Refactoring Plan**:
1. Extract slot management to JsSlotManager (already outlined)
2. Extract binding resolution to JsBindingResolver (already outlined)
3. Extract cache management to JsEnvironmentCache
4. Create EnvironmentFactory for environment creation

**Estimated Effort**: 8 hours

---

### 25. Create Strategy Patterns

**Locations**:
- Binding resolution strategies
- Yield rewrite strategies
- Evaluation strategies
- Parsing strategies

**Issues**:
- Hardcoded strategies throughout code
- Difficult to add new strategies
- Tight coupling to specific implementations

**Refactoring Plan**:
1. Identify strategy use cases
2. Create strategy interfaces
3. Implement concrete strategies
4. Use factory or registry pattern

**Estimated Effort**: 10 hours

---

## Summary of Prioritized Work

### Total Estimated Effort
- **CRITICAL**: ~30 hours
- **HIGH**: ~27 hours
- **MEDIUM**: ~52 hours
- **LOWER**: ~40 hours
- **Total**: ~149 hours

### Quick Wins (<4 hours)
1. Extract magic numbers to `JsConstants.cs` - 3 hours
2. Create centralized error helper - 4 hours
3. Extract microtask scheduling - 2 hours
4. Extract timer management - 3 hours
5. Reduce HashSet allocations - 3 hours
6. Unify evaluation logic - 2 hours
7. Improve string handling - 3 hours

### High Impact (>8 hours)
1. Extract JsEngine constructor - 8 hours
2. Split JsAstParser.ParseClassElements - 5 hours
3. Extract module loading - 4 hours
4. Extract binding resolution strategies - 6 hours
5. Add abstraction layers - 6 hours
6. Extract Temporal operations - 8 hours
7. Extract JsEnvironment responsibilities - 8 hours

### Recommendations

**Immediate Actions** (Next Sprint):
1. Complete quick wins (items 1-7 above)
2. Start on CRITICAL items 1-2 (JsEngine constructor, StatementContainsImportMeta)
3. Create GitHub issues for TODOs

**Short-term Goals** (Next Month):
1. Complete all CRITICAL items
2. Start on HIGH priority items
3. Implement error helper and constants

**Medium-term Goals** (Next Quarter):
1. Complete all HIGH priority items
2. Address MEDIUM priority items
3. Improve test coverage

**Long-term Goals** (Next Year):
1. Address remaining technical debt
2. Establish refactoring cadence
3. Improve architecture and maintainability

### Success Metrics

1. **Code Quality**:
   - Number of methods >100 lines: Target <5
   - Number of files >1000 lines: Target <10
   - Code duplication percentage: Target <5%

2. **Performance**:
   - GC allocations per second: Target -20%
   - Parse time for large scripts: Target -15%
   - Evaluation time: Target -10%

3. **Maintainability**:
   - Cyclomatic complexity average: Target <10
   - Test coverage: Target >80%
   - Number of TODO comments: Target <5

4. **Architecture**:
   - Classes with >5 responsibilities: Target 0
   - Coupling score: Target <0.5
   - Cohesion score: Target >0.7
