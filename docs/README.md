# Architecture Documentation Index

This directory contains comprehensive documentation about the Asynkron.JsEngine architecture and various implementation approaches.

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
→ Check if similar patterns exist in [CPS_TRANSFORMATION_PLAN.md](CPS_TRANSFORMATION_PLAN.md)  
→ Review [CONTROL_FLOW_ALTERNATIVES.md](CONTROL_FLOW_ALTERNATIVES.md) for architectural patterns

---

## Document Status Legend

- **Implemented**: Feature is implemented in the codebase
- **Analysis & Recommendations**: Analysis document with recommendations
- **Implementation Details**: Details about actual implementation
- **Educational**: Explains concepts without requiring implementation
- **Clarification**: Clarifies terminology or concepts
- **Comparison**: Compares different approaches

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

**Last Updated**: 2025-11-08
