# Instruction Size Audit - Quick Reference

**Issue:** #465 - Task: Audit instruction struct sizes and usage  
**Status:** ✅ Audit Complete, Optimization Pending Profiling Data

---

## Key Findings

| Metric | Value |
|--------|-------|
| Total Instructions | 40 types |
| Average Fields | 4.92 per instruction |
| Average Size | ~30 bytes per instruction |
| Largest | PushEnvironmentInstruction (94 bytes) |
| Smallest | 4 types at 13 bytes each |

---

## Top 10 Largest Instructions

1. **PushEnvironmentInstruction** - 94 bytes (11 fields)
2. **ForInInitInstruction** - 62 bytes (9 fields)
3. **IteratorInitInstruction** - 54 bytes (8 fields)
4. **CompoundAssignmentSlotInstruction** - 47 bytes (10 fields)
5. **IteratorMoveNextInstruction** - 45 bytes (8 fields)
6. **ArrayDestructuringElementInstruction** - 41 bytes (7 fields)
7. **ArrayDestructuringRestInstruction** - 41 bytes (7 fields)
8. **EnterTryInstruction** - 41 bytes (8 fields)
9. **ForInMoveNextInstruction** - 41 bytes (7 fields)
10. **BinaryOpInstruction** - 38 bytes (6 fields)

---

## Optimization Opportunities Summary

### 🔴 High Impact
- **ALL instructions:** Convert `Next` field from `int` to `short` → saves 2 bytes each
- **~15 instructions:** Convert index fields from `int` to `short` → saves 2-8 bytes each

### 🟡 Medium Impact  
- **Tiny instructions:** Consider struct conversion (needs profiling)
- **ImmutableDictionary:** Custom implementation for small cases (needs profiling)

### 🟢 Low Impact
- Field reordering (likely already optimal)
- Instruction interning (high complexity, uncertain benefit)

---

## Required Before Optimization

**Need profiling data for:**
1. Maximum instruction count per execution plan
2. Maximum scope count per function
3. Maximum slot count per scope
4. Instruction type frequency distribution
5. Collection sizes in PushEnvironmentInstruction

**Verify safe ranges:**
- If instruction count < 32K → `short Next` is safe ✅
- If scope count < 32K → `short ScopeId` is safe ✅
- If slot count < 32K → `short SlotIndex` is safe ✅

---

## Tools & Documentation

**Audit Tool:** `playground/Program.cs`
```bash
cd playground
dotnet run
```

**Full Reports:**
- [INSTRUCTION_SIZE_AUDIT.md](./INSTRUCTION_SIZE_AUDIT.md) - Complete analysis
- [INSTRUCTION_OPTIMIZATION_PRIORITIES.md](./INSTRUCTION_OPTIMIZATION_PRIORITIES.md) - Action plan

---

## Next Steps

1. ⬜ Collect profiling data from Test262 suite
2. ⬜ Validate safe ranges for `short` conversion
3. ⬜ Implement `Next` field optimization (if safe)
4. ⬜ Implement index field optimizations (if safe)
5. ⬜ Measure memory impact
6. ⬜ Update this issue with findings

---

## Quick Field Type Reference

| Current | Alternative | Savings | Safe When |
|---------|------------|---------|-----------|
| `int Next` | `short Next` | 2 bytes | Count < 32K |
| `int ScopeId` | `short ScopeId` | 2 bytes | Scopes < 32K |
| `int SlotIndex` | `short SlotIndex` | 2 bytes | Slots < 32K |
| `int FlatSlotId` | `short FlatSlotId` | 2 bytes | Flat slots < 32K |

---

## Already Optimized ✅

- ✅ `InstructionKind` uses `enum : byte`
- ✅ `BinaryOperator` uses `enum : byte`  
- ✅ `BreakableKind` uses `enum : byte`
- ✅ Empty `ImmutableArray` uses singleton
- ✅ Boolean fields use 1 byte

---

## Notes

- Instructions are **records** (reference types), not structs
- Each instance has ~16 bytes CLR object header overhead
- Sizes shown are estimates for fields only
- Reference type fields (Symbol, ExpressionNode) point to heap objects
- Actual memory usage depends on referenced object sizes
