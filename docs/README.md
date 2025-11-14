# Architecture Documentation Index

This directory contains comprehensive documentation about the Asynkron.JsEngine architecture and various implementation approaches.

## 📁 Documentation Organization

- **[investigations/](investigations/)** - Investigation notes, debugging plans, and analysis from development work
- **[archive/](archive/)** - Historical documentation from completed implementation efforts
- **Current docs** - Active documentation for architecture, features, and implementation details

## Core Implementation Documentation

### [CPS_TRANSFORMATION_PLAN.md](CPS_TRANSFORMATION_PLAN.md)
**Status: Implemented**

Detailed plan for implementing Continuation-Passing Style (CPS) transformation to support generators and async/await. This approach transforms S-expressions into a form where control flow is explicit through continuations.

**Key Topics:**
- CPS fundamentals and benefits
- Integration into the compilation pipeline
- Generator implementation (`function*`, `yield`)
- Async/await implementation
- Phase-by-phase implementation guide

### [ASI_IMPLEMENTATION.md](ASI_IMPLEMENTATION.md)
**Status: Implemented**

Implementation of Automatic Semicolon Insertion (ASI) following ECMAScript specification (Section 11.9).

**Key Topics:**
- ECMAScript ASI rules (offending token, end of input, restricted productions)
- Modified parser methods for ASI handling
- Test coverage for various ASI scenarios

### [DYNAMIC_IMPORT_IMPLEMENTATION.md](DYNAMIC_IMPORT_IMPLEMENTATION.md)
**Status: Implemented**

Implementation of dynamic import (`import()`) functionality.

**Key Topics:**
- Parser support for dynamic imports
- Promise-based module loading
- Integration with existing module system

### [SOURCE_REFERENCE_IMPLEMENTATION.md](SOURCE_REFERENCE_IMPLEMENTATION.md)
**Status: Implemented**

Implementation of source reference tracking for better error messages and debugging.

### [PARSER_FIXES_SUMMARY.md](PARSER_FIXES_SUMMARY.md)
**Status: Implementation Summary**

Summary of parser fixes for ASI handling, ternary operators, and minified code parsing.

**Results:** 5 of 6 SunSpider tests fixed - parse errors converted to runtime errors.

### [SIGNAL_PATTERN_IMPLEMENTATION.md](SIGNAL_PATTERN_IMPLEMENTATION.md)
**Status: Implemented**

Implementation summary of typed signal objects replacing enum-based state machine for control flow.

**Test Results:** 1,064 passing tests, clean build, 0 CodeQL alerts.

---

## 🔍 Investigations & Debugging

For detailed investigation notes and debugging documentation, see **[investigations/README.md](investigations/README.md)**.

**Key Investigations:**
- [PARSER_VS_CPS_ANALYSIS.md](investigations/PARSER_VS_CPS_ANALYSIS.md) - Analysis of parser vs CPS transformer issues with method shorthand
- [PROMISE_REJECTION_INVESTIGATION.md](investigations/PROMISE_REJECTION_INVESTIGATION.md) - Promise rejection handling in async loops
- [EXCEPTION_CHANNEL_RESULTS.md](investigations/EXCEPTION_CHANNEL_RESULTS.md) - Exception channel implementation for debugging
- [CPS_ASYNC_ITERATION_STATUS.md](investigations/CPS_ASYNC_ITERATION_STATUS.md) - Status of async iteration in CPS transformer
- [ASYNC_ITERABLE_SCOPE_COMPARISON.md](investigations/ASYNC_ITERABLE_SCOPE_COMPARISON.md) - Global vs local scope behavior
- [DEBUGGING_PLAN.md](investigations/DEBUGGING_PLAN.md) - Global scope iterator failure debugging
- SunSpider test suite analysis documents

---

## 📦 Archived Documentation

For historical documentation from completed implementation efforts, see **[archive/README.md](archive/README.md)**.

**Archived Documents:**
- [IMPLEMENTATION_COMPLETE.md](archive/IMPLEMENTATION_COMPLETE.md) - November 2025 milestone snapshot (96% compatibility achieved)
- [IMPLEMENTATION_SUMMARY.md](archive/IMPLEMENTATION_SUMMARY.md) - Earlier implementation summary
- [DYNAMIC_IMPORT_ASYNC_ITERATION_SUMMARY.md](archive/DYNAMIC_IMPORT_ASYNC_ITERATION_SUMMARY.md) - Dynamic import + async iteration implementation
- [FEATURE_IMPLEMENTATION_ANALYSIS.md](archive/FEATURE_IMPLEMENTATION_ANALYSIS.md) - Feasibility analysis for multiple features

---

## Control Flow & Signal Pattern Documentation

### [CONTROL_FLOW_ALTERNATIVES.md](CONTROL_FLOW_ALTERNATIVES.md)
**Status: Analysis & Recommendations**

Comprehensive analysis of six different approaches to implementing control flow (return, break, continue) in expression-first interpreters.

**Approaches Covered:**
1. Exception-Based Signals (current implementation)
2. Result Wrapper Pattern
3. CPS-Based Approach
4. State Machine with Control Flags (recommended)
5. Trampoline Pattern
6. Goto/Label Simulation

**Key Insights:**
- Performance comparison matrix
- Implementation complexity analysis
- Migration strategies

### [SIGNAL_PATTERN.md](SIGNAL_PATTERN.md)
**Status: Documentation**

Usage guide for the typed signal pattern used for control flow management.

**Key Topics:**
- Signal types (ReturnSignal, BreakSignal, ContinueSignal, YieldSignal, ThrowFlowSignal)
- Pattern matching with switch expressions
- Usage examples

### [SIGNAL_PATTERN_ANALYSIS.md](SIGNAL_PATTERN_ANALYSIS.md)
**Status: Analysis**

Detailed analysis comparing signal pattern vs state machine approach for control flow.

**Conclusion:** Typed signal approach is significantly better than enum-based state machine.

### [ASYNC_AWAIT_IMPLEMENTATION.md](ASYNC_AWAIT_IMPLEMENTATION.md)
**Status: Implementation Details**

Details about the actual implementation of async/await using CPS transformation.

## Educational Documentation

### [BYTECODE_COMPILATION.md](BYTECODE_COMPILATION.md)
**Status: Educational - Not Implemented**

Comprehensive guide explaining how the recursive evaluator could be transformed to use bytecode compilation and a virtual machine.

**Key Topics:**
- What bytecode is and how it differs from tree-walking
- Stack-based vs Register-based VM architectures
- Complete instruction set design
- Full implementation approach in C#
- Optimization opportunities (constant folding, inline caching)
- Real-world examples (Python, Lua, JVM, V8, Wasm)
- Performance analysis (3-5x speedup potential)

**Recommendation:** Consider only if performance profiling shows tree-walking is a bottleneck and use cases justify the complexity.

### [ITERATIVE_EVALUATION.md](ITERATIVE_EVALUATION.md)
**Status: Educational - Not Implemented**

Comprehensive guide explaining how to transform the recursive evaluator into an iterative (loop-based) evaluator using explicit stacks.

**Key Topics:**
- Explicit stack approach with implementation
- Work queue approach for parallel evaluation
- Trampoline pattern for tail recursion
- State machine pattern
- Performance comparison (memory: 40x less, speed: 2-4x slower)
- Complete code examples

**Recommendation:** Consider only if stack overflow becomes a real problem. Current CPS transformation already handles generators/async which are the main use cases for iterative evaluation.

## Supporting Documentation

### [DESTRUCTURING_IMPLEMENTATION_PLAN.md](DESTRUCTURING_IMPLEMENTATION_PLAN.md)
**Status: Implemented**

Implementation plan for destructuring assignment in arrays and objects.

### [CONTROL_FLOW_STATE_MACHINE_CLARIFICATION.md](CONTROL_FLOW_STATE_MACHINE_CLARIFICATION.md)
**Status: Clarification**

Clarifies terminology around "state machine" pattern in control flow documentation.

### [RESULT_WRAPPER_VS_STATE_MACHINE.md](RESULT_WRAPPER_VS_STATE_MACHINE.md)
**Status: Comparison**

Detailed comparison between Result Wrapper and State Machine approaches for control flow.

### [CPS_PIPELINE_INTEGRATION.md](CPS_PIPELINE_INTEGRATION.md)
**Status: Integration Guide**

Guide for integrating CPS transformation into the evaluation pipeline.

### [MISSING_FEATURES.md](MISSING_FEATURES.md)
**Status: Feature Roadmap**

Comprehensive catalog of JavaScript features not yet implemented in Asynkron.JsEngine, organized by priority and category.

**Key Topics:**
- Medium-priority features (additional array/string methods, object rest/spread)
- Low-priority features (Proxy, BigInt, Typed Arrays, WeakMap/WeakSet)
- Implementation complexity estimates
- Recommended implementation phases

### [COMPLETED_FEATURES.md](COMPLETED_FEATURES.md)
**Status: Feature Documentation**

Comprehensive catalog of JavaScript features that have been successfully implemented in Asynkron.JsEngine.

**Key Topics:**
- ES6 modules (import/export)
- Modern syntax (single quotes, object shorthand, computed properties)
- Iteration (for...of, for...in)
- Operators (bitwise, exponentiation, increment/decrement, compound assignment)
- Collections (Symbol, Map, Set)
- Advanced features (optional chaining, private class fields)

---

## Quick Reference: Which Document Should I Read?

### "I want to understand how async/await works"
→ Start with [CPS_TRANSFORMATION_PLAN.md](CPS_TRANSFORMATION_PLAN.md)  
→ Then [ASYNC_AWAIT_IMPLEMENTATION.md](ASYNC_AWAIT_IMPLEMENTATION.md)

### "I want to improve performance"
→ Read [BYTECODE_COMPILATION.md](BYTECODE_COMPILATION.md) for 3-5x speedup through compilation  
→ Read [CONTROL_FLOW_ALTERNATIVES.md](CONTROL_FLOW_ALTERNATIVES.md) for control flow optimization

### "I'm getting stack overflow errors"
→ Read [ITERATIVE_EVALUATION.md](ITERATIVE_EVALUATION.md) for stack-safe evaluation  
→ Consider implementing trampoline pattern for tail recursion

### "I want to understand control flow implementation"
→ Read [CONTROL_FLOW_ALTERNATIVES.md](CONTROL_FLOW_ALTERNATIVES.md) for comprehensive analysis  
→ Read [RESULT_WRAPPER_VS_STATE_MACHINE.md](RESULT_WRAPPER_VS_STATE_MACHINE.md) for detailed comparison

### "I'm implementing a new feature"
→ Check [MISSING_FEATURES.md](MISSING_FEATURES.md) for prioritized feature list  
→ Check if similar patterns exist in [CPS_TRANSFORMATION_PLAN.md](CPS_TRANSFORMATION_PLAN.md)  
→ Review [CONTROL_FLOW_ALTERNATIVES.md](CONTROL_FLOW_ALTERNATIVES.md) for architectural patterns

### "I want to know what features to implement next"
→ Read [MISSING_FEATURES.md](MISSING_FEATURES.md) for comprehensive feature list with priorities  
→ See [COMPLETED_FEATURES.md](COMPLETED_FEATURES.md) for already implemented features  
→ See recommended implementation phases and quick wins

### "I want to know what features are already implemented"
→ Read [COMPLETED_FEATURES.md](COMPLETED_FEATURES.md) for complete list of implemented features  
→ Check [FEATURE_STATUS_SUMMARY.md](FEATURE_STATUS_SUMMARY.md) for overall compatibility status

### "I'm debugging async iteration issues"
→ Check [investigations/CPS_ASYNC_ITERATION_STATUS.md](investigations/CPS_ASYNC_ITERATION_STATUS.md) for known issues  
→ See [investigations/DEBUGGING_PLAN.md](investigations/DEBUGGING_PLAN.md) for debugging approach  
→ Review [investigations/PARSER_VS_CPS_ANALYSIS.md](investigations/PARSER_VS_CPS_ANALYSIS.md) for parser vs CPS analysis

### "I want to understand historical implementation decisions"
→ See [archive/README.md](archive/README.md) for archived implementation documents  
→ Check [archive/IMPLEMENTATION_COMPLETE.md](archive/IMPLEMENTATION_COMPLETE.md) for milestone snapshot

---

## Document Status Legend

- **Implemented**: Feature is implemented in the codebase
- **Analysis & Recommendations**: Analysis document with recommendations
- **Implementation Details**: Details about actual implementation
- **Educational**: Explains concepts without requiring implementation
- **Clarification**: Clarifies terminology or concepts
- **Comparison**: Compares different approaches
- **Feature Roadmap**: Catalog of missing features with priorities

---

## Contributing to Documentation

When adding new documentation:

1. Add entry to this index with appropriate status
2. Include "Key Topics" summary
3. Add to "Quick Reference" section if applicable
4. Update README.md if user-facing

When documenting implementation approaches:

1. Explain the concept clearly
2. Provide code examples
3. Include performance analysis
4. Discuss trade-offs
5. Give clear recommendations
6. Link to related documents

---

**Last Updated**: 2025-11-14
