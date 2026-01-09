# Legacy AST Evaluators (Quarantined)

This directory contains AST tree-walking evaluation methods that have been quarantined for eventual deprecation.

## Purpose

The files in this directory implement direct AST evaluation through recursive tree-walking. This was the original execution model but has been largely superseded by the IR (Intermediate Representation) execution path via `ExecutionPlanRunner`.

## Current Usage

These evaluators are still used as a fallback in specific scenarios:
- Functions containing `with` statements
- Direct `eval()` calls
- Analysis and debugging tools
- Test scaffolding

## Primary Execution Path

The modern execution path uses:
1. **AST** → Parsed JavaScript syntax tree
2. **IR Lowering** → `ExecutionPlanBuilder` converts AST to flat instruction sequence
3. **IR Execution** → `ExecutionPlanRunner` interprets the instruction sequence

See `/agents/how-to-architecture.md` for detailed information about the execution model.

## Deprecation

Methods in these files are marked with `[Obsolete]` attributes to discourage new usage. The goal is to:
1. Minimize reliance on AST evaluation
2. Eventually remove or significantly reduce AST evaluation code
3. Maintain clear separation between IR execution (primary) and AST evaluation (fallback)

## File Organization

- Each `*Extensions.cs` file contains evaluation methods for specific AST node types
- All files remain in the `Asynkron.JsEngine.Ast` namespace (no breaking changes)
- Files were moved here from `Ast/` to clearly identify quarantined code
