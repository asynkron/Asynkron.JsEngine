# Instruction Optimization Priority List

**Generated from:** Instruction Size Audit (Issue #465)  
**Date:** 2026-01-09

## Overview

This document provides a prioritized list of optimization opportunities identified during the instruction size audit. Priorities are based on estimated impact and implementation complexity.

⚠️ **Note:** Final prioritization requires profiling data to identify frequently-used instructions in real-world scenarios.

## Priority Levels

- 🔴 **HIGH:** Large potential impact, relatively simple to implement
- 🟡 **MEDIUM:** Moderate impact or moderate complexity
- 🟢 **LOW:** Small impact or high complexity

---

## 🔴 HIGH PRIORITY Optimizations

### 1. Convert `Next` Field to `short` in All Instructions
**Impact:** Affects ALL 40 instruction types  
**Savings:** 2 bytes per instruction  
**Complexity:** LOW (simple type change)  
**Risk:** LOW (if instruction count per plan < 32,767)

**Rationale:**
- Every single instruction has a `Next` field (int, 4 bytes)
- Execution plans are unlikely to have > 32K instructions
- Simple find-replace change across all instruction types
- Immediate 2-byte savings per instruction created

**Action Required:**
1. Verify maximum instruction count in real execution plans
2. If < 32K, change `int Next` to `short Next` in ExecutionInstruction base
3. Update instruction handler to use short for program counter if needed

**Estimated Impact:** If avg execution plan has 100 instructions, saves 200 bytes per plan.

---

### 2. Optimize Index Fields to `short` (Where Safe)
**Impact:** Affects ~15 instruction types with slot/scope index fields  
**Savings:** 2-8 bytes per affected instruction  
**Complexity:** MEDIUM (requires range validation)  
**Risk:** MEDIUM (need to verify max scope/slot counts)

**Target Fields:**
- `ScopeId` (appears in 7+ instructions)
- `SlotIndex` (appears in 10+ instructions)
- `FlatSlotId` (appears in 5+ instructions)
- `StateSlotIndex`, `ValueSlotIndex`, `IteratorSlotIndex` (various instructions)

**Instructions Affected:**
1. IncrementSlotInstruction (4 int → short fields = 8 bytes saved)
2. CompoundAssignmentSlotInstruction (4 int → short fields = 8 bytes saved)
3. ArrayDestructuringElementInstruction (2 int → short fields = 4 bytes saved)
4. ForInInitInstruction (2 int → short fields = 4 bytes saved)
5. IteratorInitInstruction (1 int → short field = 2 bytes saved)
6. And others...

**Action Required:**
1. Analyze real execution plans to determine:
   - Maximum scope count per function
   - Maximum slot count per scope
   - Maximum flat slot array size
2. If all < 32K, change int → short for these fields
3. Add runtime assertions to catch violations during development

**Estimated Impact:** High-use instructions with 4 int fields → 8 bytes saved each.

---

### 3. Optimize Empty ImmutableArray Handling
**Impact:** Affects instructions with ImmutableArray fields  
**Savings:** Improved cache locality, no extra allocations  
**Complexity:** LOW (ImmutableArray already uses singleton for empty)  
**Risk:** VERY LOW

**Target Instructions:**
- PushEnvironmentInstruction (PerIterationBindings, FlatSlotMappings, SlotNames)
- ForInInitInstruction (TdzBindings)
- IteratorInitInstruction (TdzBindings)

**Current State:** ✅ ImmutableArray.Empty is already a singleton, so this is already optimized.

**Action:** NONE - Already optimal

---

## 🟡 MEDIUM PRIORITY Optimizations

### 4. Optimize Enum Storage (Already Done ✅)
**Impact:** Multiple instructions  
**Savings:** Already using byte enums  
**Complexity:** N/A  
**Risk:** N/A

**Current State:**
- ✅ `InstructionKind` is already `enum : byte`
- ✅ `BinaryOperator` is already `enum : byte`
- ✅ `BreakableKind` is already `enum : byte`

**Action:** NONE - Already optimal

---

### 5. Consider Struct Conversion for Tiny Instructions
**Impact:** 4 instructions with only 2 fields  
**Savings:** Eliminates 16-byte object header  
**Complexity:** HIGH (structs have different semantics)  
**Risk:** MEDIUM (performance depends on usage patterns)

**Candidates:**
1. BreakableExitInstruction (2 fields: Kind, Next)
2. EndFinallyInstruction (2 fields: Kind, Next)
3. LeaveTryInstruction (2 fields: Kind, Next)
4. SetCompletionValueInstruction (2 fields: Kind, Next)

**Analysis:**
- As reference types: 8 bytes (object header) + 2 bytes (Kind + Next) = ~16 bytes + heap overhead
- As value types: 2 bytes stored inline (if Next becomes short)
- But: structs are copied on assignment, may hurt if passed frequently

**Action Required:**
1. Profile to determine creation frequency and passing patterns
2. If created frequently but passed rarely: struct may help
3. If passed frequently: struct may hurt (copying cost)
4. Consider `readonly record struct` for immutability benefits

**Recommendation:** DEFER until profiling data available

---

### 6. Review ImmutableDictionary Usage
**Impact:** 3 instructions  
**Savings:** Potentially significant for empty/small cases  
**Complexity:** HIGH (would need custom implementation)  
**Risk:** MEDIUM

**Target Instructions:**
1. PushEnvironmentInstruction (SlotMap)
2. EnterCatchInstruction (SlotMap)
3. EnterCatchWithDestructuringInstruction (SlotMap)

**Problem:**
- ImmutableDictionary allocates heap memory even when empty
- Empty ImmutableHashSet also allocates

**Potential Solutions:**
1. Use null for empty (if nullable is acceptable)
2. Create custom small dictionary for common cases (e.g., 1-4 entries)
3. Use arrays for small fixed-size cases
4. Benchmark to see if it matters

**Action Required:**
1. Profile to determine typical SlotMap sizes
2. Measure allocation frequency and impact
3. If significant, implement custom solution

**Recommendation:** INVESTIGATE with profiling

---

## 🟢 LOW PRIORITY Optimizations

### 7. Field Reordering for Alignment
**Impact:** Minimal (compiler likely already optimizes)  
**Savings:** Potentially a few bytes per instruction  
**Complexity:** LOW (just reorder fields)  
**Risk:** VERY LOW

**Rationale:**
- C# compiler may already optimize field layout
- Manual reordering could save padding bytes
- Largest fields first, then descending size

**Action Required:**
1. Use `dotnet-dump` or similar to inspect actual object layout
2. If significant padding found, reorder fields manually
3. Use `[StructLayout]` attribute if needed

**Recommendation:** LOW priority, minimal expected gains

---

### 8. Instruction Interning/Pooling
**Impact:** Could be high if many identical instructions created  
**Savings:** Reduces allocations for common patterns  
**Complexity:** VERY HIGH  
**Risk:** HIGH (thread safety, lifetime management)

**Examples:**
- Many `JumpInstruction(targetIndex)` with same target
- Many `PopEnvironmentInstruction(scopeId)` with same scope
- Common literal operations

**Challenges:**
- Need to identify which instructions are safe to intern
- Thread safety for shared instances
- Lifetime management (when to clear cache)
- Complexity doesn't justify unless profiling shows major impact

**Recommendation:** DEFER - Only consider if profiling shows severe allocation pressure

---

## Measurement & Validation Plan

### Phase 1: Profiling (REQUIRED BEFORE OPTIMIZATION)
1. ✅ Run instruction size audit tool
2. ⬜ Collect real execution plans from:
   - Test262 suite (representative JavaScript code)
   - Example applications
   - Benchmark suite
3. ⬜ Measure:
   - Instruction count per execution plan (max, avg, p95)
   - Instruction type frequency distribution
   - Scope count per function (max, avg, p95)
   - Slot count per scope (max, avg, p95)
   - SlotMap/collection sizes in PushEnvironmentInstruction
4. ⬜ Identify top 10 most frequently created instruction types

### Phase 2: Implementation
1. Start with HIGH priority items that have confirmed safe ranges
2. Implement one optimization at a time
3. Run full test suite after each change
4. Measure memory impact with before/after comparisons

### Phase 3: Validation
1. Run Test262 suite - must pass 100%
2. Run benchmark suite - performance must not regress
3. Memory profiling - verify expected savings
4. Create new tests for any edge cases

---

## Quick Reference: Field Size Reference

| Type    | Size | Max Value          | Use When...                    |
|---------|------|-------------------|--------------------------------|
| byte    | 1    | 255               | Enums, small counts            |
| short   | 2    | 32,767            | Indices, counts < 32K          |
| int     | 4    | 2.1B              | Large indices, general purpose |
| bool    | 1    | true/false        | Flags                          |
| enum    | *    | Varies            | Use `: byte` if < 256 values   |

*Enum size depends on underlying type specification

---

## Success Metrics

Track these metrics before and after optimization:

1. **Memory per execution plan** (measured via profiler)
2. **Allocation count** for instruction creation
3. **Instruction array size** for typical functions
4. **Test262 pass rate** (must remain 100%)
5. **Benchmark performance** (must not regress)

---

## Summary

**Immediately Actionable (Pending Profiling Validation):**
1. 🔴 Convert `Next` to `short` (saves 2 bytes × 40 instruction types)
2. 🔴 Convert index fields to `short` where safe (saves 2-8 bytes × 15+ types)

**Investigate After Profiling:**
1. 🟡 Struct conversion for tiny instructions
2. 🟡 Custom implementations for ImmutableDictionary in common cases

**Low Impact / Defer:**
1. 🟢 Field reordering (compiler likely already optimal)
2. 🟢 Instruction interning (high complexity, uncertain benefit)

**Next Step:** Run profiling to gather frequency and range data for prioritization.
