# Plan: Refactor Inline Visitors to Use AstVisitor Base

## Phase 0: Fix AstVisitor Base Class (MUST DO FIRST)

### Current Issues

**1. BlockStatement shortcut (critical)**
```csharp
// Line 156-159: VisitBlock immediately delegates to VisitBlockStatement
// No chance to override for BlockStatement itself
protected virtual StatementNode? VisitBlock(BlockStatement node)
{
    VisitBlockStatement(node);  // WRONG: skips hook
    return null;
}
```

Also happens in:
- `VisitTryStatement` (lines 241, 249, 254) - calls `VisitBlockStatement` directly
- `VisitSwitchStatement` (line 270) - calls `VisitBlockStatement` directly
- `VisitFunctionExpression` (line 393) - calls `VisitBlockStatement` directly

**2. Missing expression hooks** - these just recurse with no override point:
- `UnaryExpression` (line 80-82) - just `expression = node.Operand; continue`
- `BinaryExpression` (line 76-79) - visits children inline
- `YieldExpression` (line 127-134) - just recurses
- `AwaitExpression` (line 135-137) - just recurses

**3. Missing node types entirely:**

Statements:
- `EmptyStatement`
- `ImportStatement`
- `ExportAllStatement`, `ExportDefaultStatement`, `ExportDeclarationStatement`
- `ExportDefaultDeclaration`, `ExportNamespaceAsStatement`, `ExportNamedStatement`
- `ClassDeclaration` (if exists)

Expressions:
- `ClassExpression`
- `DecoratorExpression`
- `TaggedTemplateExpression`
- `TemplateLiteralExpression`
- `LiteralExpression`
- `PrivateIdentifierExpression`
- `ImportMetaExpression`
- `ThisExpression`
- `SuperExpression`
- `NewTargetExpression`
- `RegexLiteralExpression`

### Fix Strategy

**Pattern for each node type:**
```csharp
// Hook method - overridable, calls traversal
protected virtual StatementNode? VisitBlockStatement(BlockStatement node)
{
    TraverseBlockStatement(node);  // Default: traverse children
    return null;
}

// Traversal method - handles children, not meant to be overridden
private void TraverseBlockStatement(BlockStatement node)
{
    foreach (var stmt in node.Statements)
    {
        VisitStatement(stmt);
    }
}
```

**For expressions that need hooks:**
```csharp
protected virtual void VisitUnaryExpression(UnaryExpression node)
{
    VisitExpression(node.Operand);
}

protected virtual void VisitBinaryExpression(BinaryExpression node)
{
    VisitExpression(node.Left);
    VisitExpression(node.Right);
}
```

**Change dispatch to use hooks:**
```csharp
// In VisitStatement switch:
BlockStatement node => VisitBlockStatement(node),  // Now goes through hook

// In VisitTryStatement:
VisitBlockStatement(node.TryBlock);  // Changed from direct call
```

**4. Add early termination support:**
```csharp
public abstract class AstVisitor
{
    protected bool ShouldStop { get; set; }

    protected virtual void VisitStatement(StatementNode statement)
    {
        if (ShouldStop) return;
        // ... dispatch
    }

    protected virtual void VisitExpression(ExpressionNode expression)
    {
        if (ShouldStop) return;
        // ... dispatch
    }
}
```

### Implementation Checklist for Phase 0

- [ ] Rename `VisitBlockStatement` to `TraverseBlockChildren` (private)
- [ ] Create proper `VisitBlockStatement` hook that calls traverse
- [ ] Fix `VisitBlock` to just return result from `VisitBlockStatement`
- [ ] Fix `VisitTryStatement`, `VisitSwitchStatement`, `VisitFunctionExpression` to call hooks
- [ ] Add `VisitUnaryExpression`, `VisitBinaryExpression`, `VisitYieldExpression`, `VisitAwaitExpression` hooks
- [ ] Add missing statement types to dispatch
- [ ] Add missing expression types to dispatch
- [ ] Add `ShouldStop` early termination pattern
- [ ] Write tests for visitor coverage

## Problem

Multiple places in the codebase have hand-rolled stack-based AST traversal instead of using the existing `AstVisitor` base class. This leads to:
- Duplicated traversal logic
- Easy to miss node types when adding new AST nodes
- Inconsistent handling across different visitors
- Verbose code that's hard to maintain

## Current Inline Visitors

### ScopeDynamicnessAnalyzer.cs (7 visitors)
1. `ContainsWithOrDirectEval(BlockStatement)` - line 63
2. `ContainsDirectEval(BlockStatement)` - line 329
3. `ContainsDirectEval(ExpressionNode)` - line 488
4. `ContainsDirectEval(BindingTarget)` - line 619
5. `ContainsArgumentsReference(BlockStatement)` - line 888
6. `ContainsInnerFunctionExpression(ExpressionNode)` - line 1069
7. `CollectVarDeclaredNames(BlockStatement)` - line 1223

### TypedAstEvaluator.SyncFunctionInvoker.cs (1 visitor)
- `ContainsWithOrDirectEval(BlockStatement)` - line 1758 (duplicate of analyzer)

### HoistableDeclarationsPlan.cs (1 visitor)
- Hoisting traversal - line 14

## Proposed Solution

Create specialized `AstVisitor` subclasses that:
1. Override specific `Visit*` methods to detect conditions
2. Use a `Found` flag for early termination
3. Leverage the base class's complete traversal logic

### Example Refactored Visitor

```csharp
/// <summary>
/// Visitor that detects 'with' statements or direct eval calls.
/// </summary>
private sealed class DynamicScopeDetector : AstVisitor
{
    public bool Found { get; private set; }

    protected override StatementNode? VisitWithStatement(WithStatement node)
    {
        Found = true;
        return null; // Don't traverse children
    }

    protected override void VisitCallExpression(CallExpression node)
    {
        if (Found) return;

        if (node.Callee is IdentifierExpression { Name.Value: "eval" })
        {
            Found = true;
            return;
        }

        base.VisitCallExpression(node);
    }

    // Override other Visit methods to short-circuit when Found is true
    protected override void VisitExpression(ExpressionNode expression)
    {
        if (Found) return;
        base.VisitExpression(expression);
    }
}
```

### Usage

```csharp
internal static bool ContainsWithOrDirectEval(BlockStatement block)
{
    if (block.TryGetContainsDynamicScope(out var cached))
    {
        return cached;
    }

    var detector = new DynamicScopeDetector();
    detector.Visit(block);

    block.CacheContainsDynamicScope(detector.Found);
    return detector.Found;
}
```

## Implementation Steps

### Phase 1: Enhance AstVisitor Base
1. Add `ShouldStop` property pattern for early termination
2. Consider adding `AstVisitor<TResult>` variant for visitors that return values
3. Ensure all node types are covered (ClassDeclaration, ImportDeclaration, etc.)

### Phase 2: ScopeDynamicnessAnalyzer
1. Create `DynamicScopeDetector` visitor (replaces ContainsWithOrDirectEval)
2. Create `DirectEvalDetector` visitor (replaces ContainsDirectEval variants)
3. Create `ArgumentsReferenceDetector` visitor
4. Create `InnerFunctionDetector` visitor
5. Create `VarNameCollector` visitor (for CollectVarDeclaredNames)

### Phase 3: Consolidate Duplicates
1. Remove duplicate `ContainsWithOrDirectEval` from SyncFunctionInvoker
2. Use shared visitors across files

### Phase 4: HoistableDeclarationsPlan
1. Create `HoistingVisitor` that collects hoistable declarations

## Benefits

1. **Single source of truth** for AST traversal
2. **Automatic coverage** of new node types (just add to AstVisitor)
3. **Cleaner code** - visitors are ~20 lines instead of ~100
4. **Easier testing** - can test visitors in isolation
5. **Consistent behavior** - all visitors traverse the same way

## Risks

1. **Performance** - Virtual dispatch vs direct switch. Mitigation: benchmark critical paths
2. **Early termination** - Need to add short-circuit support. Mitigation: `ShouldStop` pattern
3. **Pooling** - Some visitors may need pooling for hot paths. Mitigation: Add ObjectPool if needed

## Estimated Reduction

- ~1000 lines of inline visitor code → ~200 lines of visitor classes
- Net reduction: ~800 lines
