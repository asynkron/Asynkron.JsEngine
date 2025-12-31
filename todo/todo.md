# failing tests to fix:
when running tests, run with filters for these specific tests.
OR them together to a filter pattern and run.
---

# Eval code global variable initialization (separate issue)
EvalCode_direct("language/eval-code/direct/var-env-func-init-global-update-configurable.js",False)
EvalCode_direct("language/eval-code/direct/var-env-var-init-global-new.js",False)
EvalCode_indirect("language/eval-code/indirect/var-env-func-init-global-update-configurable.js",False)
EvalCode_indirect("language/eval-code/indirect/var-env-func-init-global-update-configurable.js",True)

---
