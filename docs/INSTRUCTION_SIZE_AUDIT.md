# Instruction Struct Size Audit Report

**Date:** 2026-01-09  
**Issue:** [#465 - Task: Audit instruction struct sizes and usage](https://github.com/asynkron/Asynkron.JsEngine/issues/465)

## Executive Summary

This audit analyzes all 40 instruction types in the execution plan system. Instructions are implemented as C# records (reference types) rather than value types (structs), which means each instruction instance incurs object header overhead plus memory for its fields.

**Key Findings:**
- **40 total instruction types** identified
- **Average field count:** 4.92 fields per instruction
- **Average estimated size:** 30.32 bytes per instruction (excluding actual referenced object sizes)
- **Largest instruction:** `PushEnvironmentInstruction` with 11 fields (~94 bytes)
- **Smallest instructions:** 4 types with only 2 fields (~13 bytes each)

## Instruction Size Distribution

### Complete List (Sorted by Estimated Size)

| Instruction Type                         | Fields | Est. Size (bytes) |
|-----------------------------------------|--------|-------------------|
| PushEnvironmentInstruction              | 11     | 94                |
| ForInInitInstruction                    | 9      | 62                |
| IteratorInitInstruction                 | 8      | 54                |
| CompoundAssignmentSlotInstruction       | 10     | 47                |
| IteratorMoveNextInstruction             | 8      | 45                |
| ArrayDestructuringElementInstruction    | 7      | 41                |
| ArrayDestructuringRestInstruction       | 7      | 41                |
| EnterTryInstruction                     | 8      | 41                |
| ForInMoveNextInstruction                | 7      | 41                |
| BinaryOpInstruction                     | 6      | 38                |
| EnterCatchInstruction                   | 6      | 37                |
| EnterCatchWithDestructuringInstruction  | 6      | 37                |
| YieldStarInstruction                    | 5      | 37                |
| IncrementSlotInstruction                | 9      | 36                |
| SimpleVariableDeclarationInstruction    | 6      | 34                |
| ArrayDestructuringInitInstruction       | 5      | 33                |
| BreakableEnterInstruction               | 6      | 30                |
| BranchInstruction                       | 5      | 29                |
| EnterWithInstruction                    | 4      | 29                |
| ArrayDestructuringCloseInstruction      | 4      | 25                |
| EvaluateAndDiscardInstruction           | 4      | 22                |
| BreakInstruction                        | 4      | 21                |
| ClassDeclarationInstruction             | 3      | 21                |
| ComplexVariableDeclarationInstruction   | 3      | 21                |
| ContinueInstruction                     | 4      | 21                |
| ExpressionInstruction                   | 3      | 21                |
| FunctionDeclarationInstruction          | 3      | 21                |
| IteratorCloseInstruction                | 3      | 21                |
| LeaveWithInstruction                    | 3      | 21                |
| ReturnInstruction                       | 3      | 21                |
| StatementInstruction                    | 3      | 21                |
| StoreResumeValueInstruction             | 3      | 21                |
| ThrowInstruction                        | 3      | 21                |
| YieldInstruction                        | 3      | 21                |
| PopEnvironmentInstruction               | 4      | 18                |
| JumpInstruction                         | 3      | 17                |
| BreakableExitInstruction                | 2      | 13                |
| EndFinallyInstruction                   | 2      | 13                |
| LeaveTryInstruction                     | 2      | 13                |
| SetCompletionValueInstruction           | 2      | 13                |

### Statistics Summary

- **Total instruction types:** 40
- **Minimum field count:** 2
- **Maximum field count:** 11
- **Average field count:** 4.92
- **Average estimated size:** 30.32 bytes

## Top 10 Largest Instructions - Detailed Analysis

### 1. PushEnvironmentInstruction (94 bytes, 11 fields)

Creates a new lexical environment for block scopes, loop iterations, etc.

**Field Breakdown:**
- `PerIterationBindings` (ImmutableArray<Symbol>) - ~16 bytes
- `ScopeId` (int) - 4 bytes
- `SlotCount` (int) - 4 bytes
- `SlotMap` (ImmutableDictionary<Symbol, int>) - 8 bytes (reference)
- `AllowPooling` (bool) - 1 byte
- `LexicalBindings` (ImmutableHashSet<Symbol>) - 8 bytes (reference)
- `FlatSlotMappings` (ImmutableArray<(int, int)>) - ~16 bytes
- `SlotNames` (ImmutableArray<(Symbol, int)>) - ~16 bytes
- `SourceBlock` (BlockStatement) - 8 bytes (reference)
- `Kind` (InstructionKind enum) - 1 byte
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- SlotMap, LexicalBindings are reference types - actual size depends on contents
- Consider if all immutable collections are necessary or can be optimized
- ScopeId could use `short` if scope count < 32K

### 2. ForInInitInstruction (62 bytes, 9 fields)

Initializes for-in loop iteration over object properties.

**Field Breakdown:**
- `ObjectExpression` (ExpressionNode) - 8 bytes (reference)
- `StateSlot` (Symbol) - 8 bytes (reference)
- `StateSlotIndex` (int) - 4 bytes
- `ValueSlot` (Symbol) - 8 bytes (reference)
- `ValueSlotIndex` (int) - 4 bytes
- `TdzBindings` (ImmutableArray<Symbol>) - ~16 bytes
- `TdzIsConst` (bool) - 1 byte
- `Kind` (InstructionKind enum) - 1 byte
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- SlotIndex fields could use `short` if slot count < 32K
- TdzBindings is often empty - could optimize for empty case

### 3. IteratorInitInstruction (54 bytes, 8 fields)

Initializes for-of/for-await-of loop iteration.

**Field Breakdown:**
- `IteratorKind` (IteratorDriverKind enum) - 4 bytes
- `IterableExpression` (ExpressionNode) - 8 bytes (reference)
- `IteratorSlot` (Symbol) - 8 bytes (reference)
- `IteratorSlotIndex` (int) - 4 bytes
- `TdzBindings` (ImmutableArray<Symbol>) - ~16 bytes
- `TdzIsConst` (bool) - 1 byte
- `Kind` (InstructionKind enum) - 1 byte
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- Same as ForInInitInstruction - slot indices and TdzBindings

### 4. CompoundAssignmentSlotInstruction (47 bytes, 10 fields)

Handles compound assignments like `x += y`.

**Field Breakdown:**
- `TargetSymbol` (Symbol) - 8 bytes (reference)
- `Operator` (BinaryOperator enum) - 1 byte
- `RhsExpression` (ExpressionNode) - 8 bytes (reference)
- `SuppressCompletionValue` (bool) - 1 byte
- `ScopeId` (int) - 4 bytes
- `SlotIndex` (int) - 4 bytes
- `FlatSlotId` (int) - 4 bytes
- `RhsFlatSlotId` (int) - 4 bytes
- `Kind` (InstructionKind enum) - 1 byte
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- Many int fields (ScopeId, SlotIndex, FlatSlotId) could potentially use `short`
- BinaryOperator is already a byte enum ✓

### 5. IteratorMoveNextInstruction (45 bytes, 8 fields)

Advances iterator to next value in for-of/for-await-of loop.

**Field Breakdown:**
- `IteratorKind` (IteratorDriverKind enum) - 4 bytes
- `IteratorSlot` (Symbol) - 8 bytes (reference)
- `ValueSlot` (Symbol) - 8 bytes (reference)
- `IteratorSlotIndex` (int) - 4 bytes
- `ValueSlotIndex` (int) - 4 bytes
- `BreakIndex` (int) - 4 bytes
- `Kind` (InstructionKind enum) - 1 byte
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- Index fields could use `short`

### 6-9. Array Destructuring & Other Loop Instructions (41 bytes each)

Several instructions share similar sizes:
- ArrayDestructuringElementInstruction
- ArrayDestructuringRestInstruction
- EnterTryInstruction
- ForInMoveNextInstruction

Common pattern: Multiple slot/index fields (int) that could potentially be `short`.

### 10. BinaryOpInstruction (38 bytes, 6 fields)

Fast path for binary operations in generators/async functions.

**Field Breakdown:**
- `Operator` (BinaryOperator enum) - 1 byte ✓
- `Left` (ExpressionNode) - 8 bytes (reference)
- `Right` (ExpressionNode) - 8 bytes (reference)
- `ResultSlot` (Symbol) - 8 bytes (reference)
- `Kind` (InstructionKind enum) - 1 byte ✓
- `Next` (int) - 4 bytes

**Optimization Opportunities:**
- Already well-optimized with byte enums
- Next could use `short` if instruction count < 32K

## Optimization Opportunities by Category

### 1. Integer Field Sizes

**Current:** Most index/ID fields use `int` (4 bytes)  
**Potential:** Use `short` (2 bytes) if values < 32,767

**High-impact candidates:**
- ScopeId fields (appears in multiple instructions)
- SlotIndex fields (appears in many instructions)
- FlatSlotId fields
- Next instruction index (appears in ALL instructions)
- BreakIndex, ContinueIndex, etc.

**Analysis needed:** Determine maximum realistic values for:
- Number of scopes per function
- Number of slots per scope
- Number of instructions per execution plan

If these are typically < 32K, switching to `short` could save 2 bytes per field.

### 2. Immutable Collection Fields

**Large collections identified:**
- `PushEnvironmentInstruction.SlotMap` (ImmutableDictionary)
- `PushEnvironmentInstruction.PerIterationBindings` (ImmutableArray)
- `PushEnvironmentInstruction.FlatSlotMappings` (ImmutableArray)
- `PushEnvironmentInstruction.SlotNames` (ImmutableArray)
- Various `TdzBindings` (ImmutableArray)

**Considerations:**
- These are reference types - actual heap allocation depends on contents
- Empty ImmutableArray is singleton (good)
- Empty ImmutableDictionary/ImmutableHashSet still allocates
- Consider custom value types for small, fixed-size collections

### 3. Struct vs Record Conversion

**Current:** All instructions are `record` (reference type)  
**Potential:** Convert to `record struct` for smaller instructions

**Good candidates for struct conversion:**
- Instructions with 2-4 fields and no large collections
- Frequently created instructions in hot paths

**Candidates (13 bytes or less):**
- BreakableExitInstruction (2 fields)
- EndFinallyInstruction (2 fields)
- LeaveTryInstruction (2 fields)
- SetCompletionValueInstruction (2 fields)
- JumpInstruction (3 fields)

**Caution:** 
- Structs are copied on assignment
- Large structs (>16 bytes) can hurt performance
- Need profiling data to determine if this helps

## Profiling Requirements

To prioritize optimizations, we need profiling data showing:

1. **Instruction frequency:** Which instruction types are created most often?
2. **Execution plan sizes:** How many instructions per typical execution plan?
3. **Actual collection sizes:** How many items in SlotMap, PerIterationBindings, etc.?
4. **Hot paths:** Which instructions are in performance-critical code paths?

## Recommended Next Steps

### Phase 1: Measurement & Analysis
1. ✅ Complete this size audit
2. ⬜ Run profiling on representative workloads
3. ⬜ Measure actual instruction counts per execution plan
4. ⬜ Identify most frequently created instruction types
5. ⬜ Determine maximum values for index/ID fields

### Phase 2: Quick Wins (If Data Supports)
1. ⬜ Convert index fields to `short` where safe
2. ⬜ Optimize empty collection handling
3. ⬜ Consider struct conversion for tiny instructions (2-3 fields)

### Phase 3: Major Optimizations (If Needed)
1. ⬜ Custom compact collections for small fixed-size cases
2. ⬜ Instruction pooling/interning for common patterns
3. ⬜ Hybrid approach: struct for data, class wrapper if needed

## Priority Ranking Framework

When profiling data is available, rank optimization opportunities by:

**Score = (Instruction_Frequency × Bytes_Saved) / Implementation_Complexity**

Where:
- **Instruction_Frequency:** How often this instruction is created
- **Bytes_Saved:** Memory reduction per instance
- **Implementation_Complexity:** 1=simple, 3=moderate, 5=complex

## Important Notes

1. **Reference Types:** Instructions are records (classes), not structs. The sizes shown are estimates of field overhead, not total memory impact.

2. **Actual Memory:** Reference types (Symbol, ExpressionNode, ImmutableDictionary) point to heap objects. The actual memory usage depends on the referenced objects.

3. **Object Header:** Each record instance has ~16 bytes of CLR object header overhead (64-bit).

4. **Alignment:** Actual memory layout includes padding for alignment.

5. **Collection Overhead:** ImmutableArray has minimal overhead when empty (singleton), but ImmutableDictionary and ImmutableHashSet always allocate even when empty.

## Tool Usage

The audit tool is available in `playground/Program.cs`. To run:

```bash
cd playground
dotnet run
```

The tool uses reflection to analyze all instruction types and provides:
- Field count and type analysis
- Estimated size calculations
- Detailed field breakdowns for largest instructions
- Optimization suggestions based on field types

## Conclusion

This audit provides a baseline for instruction size optimization. The next critical step is gathering profiling data to identify which optimizations will have the most impact. Without usage frequency data, we risk optimizing rarely-used instructions while missing high-impact opportunities.

**Key recommendation:** Focus on profiling before making changes, as the benefit of any optimization depends heavily on instruction frequency in real-world execution plans.
