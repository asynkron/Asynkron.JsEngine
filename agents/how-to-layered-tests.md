# Layered Tests Methodology

Verify each pipeline stage separately before full end-to-end runs.

## Layers
```
Source → Lexer → Parser → AST → Analyzers → Evaluator → Result
   L0      L1       L2      L3       L4          L5
```

## When to Use
- Bug manifests at runtime but stage is unclear
- Need to inspect AST/metadata before evaluation
- Validate scope/slot metadata, CPS, loop normalization
- Debug closure capture, environment chains, or slot lookups

## Pattern
- L1 Parser: assert AST structure
- L2 Analyzers: assert metadata (SlotMap, ScopeId, PerIterationBindings)
- L3 Plans: assert plan contents (e.g., `LoopPlan`)
- L4 Runtime: enable debug logging and assert internal operations
- L5 Result: full evaluation assertion

## Example Snippets
```csharp
var pipeline = AstTestHelpers.ParseAndAnalyze("source");
var node = AstTestHelpers.FindFirst<ForStatement>(pipeline.Analyzed);
var plan = ((IAstCacheable<LoopPlan>)node).GetOrCreateCache();
var bindingNames = plan.PerIterationBindings.Select(b => b.Name).ToArray();
Assert.Contains("i", bindingNames);
```

## Layered vs Test Bombs
- Layered tests isolate failing pipeline stages.
- Test bombs isolate failing hypotheses.
- Use together: test bomb to find component, layered tests to pinpoint the stage.
