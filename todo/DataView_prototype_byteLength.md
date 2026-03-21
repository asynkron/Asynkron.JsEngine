# DataView_prototype_byteLength

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength`

Full test name:

- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/instance-has-detached-buffer.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/instance-has-detached-buffer.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/resizable-array-buffer-auto.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/resizable-array-buffer-auto.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/resizable-array-buffer-fixed.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.DataView_prototype_byteLength("built-ins/DataView/prototype/byteLength/resizable-array-buffer-fixed.js",True)

---
## Diagnosis (2026-01-22)

**Summary:** DataView.prototype.byteLength ignores detached/resizable buffer state and returns a cached length.

**Error Pattern:**
```
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw:
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: following grow Expected SameValue(«3», «4») to be true
Asynkron.JsEngine.ThrowSignal: Unhandled JavaScript throw: Expected a TypeError but got a Test262Error
```

**Analysis:**
Detached-buffer tests call `$DETACHBUFFER` then expect `dv.byteLength` to throw TypeError. Instead the getter returns normally and `assert.throws` fails, so the test aborts with an unhandled throw. Resizable ArrayBuffer tests show `byteLength` stays at the original size (auto-length view stays 3 when it should update to 4 after grow), and fixed-length views do not throw when the buffer shrinks below the view range (assert.throws sees a Test262Error instead of TypeError). This points to the getter using the creation-time length and skipping `IsDetachedBuffer`/`IsViewOutOfBounds` checks against the current buffer length.

**Fix Direction:**
In the DataView.prototype.byteLength getter, re-check `[[ViewedArrayBuffer]]` on every access: if `IsDetachedBuffer(buffer)` is true, throw TypeError. For resizable buffers, compute byteLength using current `buffer.byteLength` (length-tracking views: `buffer.byteLength - byteOffset`) and throw TypeError when `IsViewOutOfBounds` is true (e.g., buffer length shrinks below byteOffset or byteOffset+length for fixed views).

** DONE **
