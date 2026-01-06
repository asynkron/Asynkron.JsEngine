# Roadmap

## Bugs

* #420 - Bug: strict mode block function scoping (TypeError: null ref in FDI)
  * #421 - Bug: switch-case-decl-onlystrict.js
  * #423 - Bug: switch-dflt-decl-onlystrict.js

## Epic: IR-only Execution (#364)

* #398 - Task 0: Inventory + invariants
  * #415 - Task 0.1: Add AST-free assertion guard
  * #416 - Task 0.2: Inventory of all StatementInstruction usages
* #399 - Task 1: Remove statement-level AST delegation
  * #404 - Task 1.1: IR support for block statements with lexical bindings
  * #405 - Task 1.2: IR support for for-in loops
  * #406 - Task 1.3: IR support for with statements (full coverage)
  * #410 - Task 1.7: IR support for yield in binding target defaults
* #400 - Task 2: Introduce expression bytecode
  * #411 - Task 2.1: Design expression bytecode format
  * #412 - Task 2.2: Expression bytecode emitter
  * #413 - Task 2.3: Expression bytecode interpreter
  * #414 - Task 2.4: Replace ExpressionNode operands with bytecode in IR instructions
* #401 - Task 3: Remove / quarantine AST evaluators
* #402 - Task 4: IL backend for sync bytecode
* #403 - Task 5: IL backend for generator/async stepping

## Epic: Refactor ExecutionPlanRunner (#365)

* #366 - Task 1: Extract ExecutionPlanRunner profiling helpers
* #368 - Task 2: Split instruction handlers into partial file
