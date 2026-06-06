using System;
using Xunit;
using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

/// <summary>
///     STANDING COVERAGE RATCHET (burn-down Phase D / D5 seed). A canary corpus of shapes that are already
///     admitted to the production unified-bytecode VM (sync <c>Execute</c>, the script route, and the
///     resumable <c>ExecuteResumable</c> generator/async routes). Each entry asserts the shape still emits
///     its production/resumable fast-path log — i.e. it has NOT regressed back to an interpreter fallback.
///
///     This is a tripwire: when a shape is newly admitted, ADD it here; never remove an entry without a
///     deliberate, documented reason. A red test here means a previously-admitted shape silently fell back
///     (a coverage regression) — the class of bug a single per-PR test would miss because it only guards its
///     own slice. It complements (does not replace) the per-shape correctness tests and the
///     allowlist⊆ExecuteResumable drift guard.
/// </summary>
[Category(TestCategories.RuntimeSemantics)]
public sealed class ProductionRouteCoverageRatchetTests(ITestOutputHelper output) : InternalTestBase(output)
{
    private const string ProductionScriptFastPathLog = "unified-bytecode-production-fast-path script";

    // Sync function route — `unified-bytecode-production-fast-path func=<name>`.
    [Theory]
    [InlineData("function f(a,b){ return a+b; } f(1,2);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return o.a.b; } f({a:{b:1}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,v){ o.a=v; return o.a; } f({},9);", "unified-bytecode-production-fast-path func=f")]
    // PROPERTY-WRITE complex RHS (part of A37 / complements A7/A19): the RHS of a named/computed
    // write may be any already-admitted value expression — binary, nested call, member-read chain,
    // or composition thereof — lowered onto the operand stack with left-to-right eval order.
    [InlineData("function f(o,a,b){ o.x = a*2 + b; return o.x; } f({},3,4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,g,z){ o.x = g(z); return o.x; } f({},v=>v+1,5);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,s){ o.x = s.a.b; return o.x; } f({},{a:{b:9}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k,a,b){ o[k] = a*b; return o[k]; } f({},'z',6,7);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,b){ this.x = a*b; return this.x; } f.call({},4,5);", "unified-bytecode-production-fast-path func=f")]
    // COMPOUND-WRITE complex RHS (the += analogue of the plain-write admission above): the RHS of a
    // named/computed compound write may be any already-admitted value expression, evaluated AFTER the
    // old-value read; receiver/key are evaluated once before the read. Spec order is preserved.
    [InlineData("function f(o,a,b){ o.x += a + b; return o.x; } f({x:1},2,3);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,g,z){ o.x -= g(z); return o.x; } f({x:10},v=>v,3);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,s){ o.x += s.a.b; return o.x; } f({x:1},{a:{b:9}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k,a,b){ o[k] *= a + b; return o[k]; } f({v:2},'v',2,1);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k,g,z){ o[k] += g(z); return o[k]; } f({v:4},'v',v=>v,6);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,a){ o.x += a; return o.x; } f({x:1},4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ o.a++; return o.a; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return delete o.a; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return o?.a; } f(null);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,c){ o[c?'a':'b']=1; return o.a; } f({},true);", "unified-bytecode-production-fast-path func=f")]
    // A30: optional-computed-start member call (o?.[k]()) and double-optional named-then-computed
    // call (a?.b?.[k]()) — short-circuiting variants that must keep routing through the sync VM.
    [InlineData("function f(o,k){ return o?.[k](); } f(null,'m');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k){ var b={m(){return 7;}}; return o?.[k](); } f({m(){return 7;}},'m');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,k){ return a?.b?.[k](); } f(null,'c');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,k){ return a?.b?.[k](); } f({b:{c(){return 9;}}},'c');", "unified-bytecode-production-fast-path func=f")]
    // A33: array spread with a NON-simple source — bare identifier call ([...g()]),
    // named property read off a call ([...g().items]), and a mix with normal elements.
    [InlineData("function f(g){ return [...g()]; } f(()=>[1,2,3]);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return [...g().items]; } f(()=>({items:[4,5]}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,g,c){ return [a, ...g(), c]; } f(0,()=>[1,2],3);", "unified-bytecode-production-fast-path func=f")]
    // A34: object spread with a NON-simple source — bare identifier call ({...g()}),
    // named property read off a call ({...g().inner}), and a mix with normal properties.
    [InlineData("function f(g){ return {...g()}; } f(()=>({a:1,b:2}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return {...g().inner}; } f(()=>({inner:{x:4,y:5}}));", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(a,g){ return { a, ...g(), c: 3 }; } f(0,()=>({b:2}));", "unified-bytecode-production-fast-path func=f")]
    // A35: COMPLEX object-literal members outside the simple span. (1) a property VALUE that is a
    // bare-identifier call — `{x: g()}`, `{a: g(arg)}` (the member-call value form `{a: o.m()}` already
    // routed). (2) a shorthand-method / accessor member followed by a LATER member — most importantly a
    // trailing object-spread `{m(){}, ...o}` / `{get a(){}, ...o}` — which previously terminated the
    // literal-span measurement at the method/accessor define. Computed-key + value subexpressions
    // evaluate left-to-right (PropertyDefinitionEvaluation order preserved).
    [InlineData("function f(g,z){ return {x: g(z)}; } f(v=>v+1,5);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return {a: g(), b: 2}; } f(()=>1);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g){ return {[g()]: g()}; } f(()=>'k');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return { m(){return 1;}, ...o }; } f({z:9});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return { get a(){return 1;}, ...o }; } f({z:9});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(k,o){ return { x:1, [k]:2, m(){return 3;}, get g(){return 4;}, set s(v){}, ...o }; } f('y',{z:9});", "unified-bytecode-production-fast-path func=f")]
    // A23: property UPDATE with a computed receiver prefix — computed-update terminal
    // (box[k1].child[k2]++), named-update terminal (box[k1].child++), and prefix decrement.
    [InlineData("function f(o,k1,k2){ return o[k1].child[k2]++; } f({a:{child:{x:1}}},'a','x');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k1){ return o[k1].child++; } f({a:{child:1}},'a');", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k1,k2){ return --o[k1].child[k2]; } f({a:{child:{x:5}}},'a','x');", "unified-bytecode-production-fast-path func=f")]
    // Free-identifier (DynamicLookupDependency) family — a free name escaping the activation's slots
    // resolves as a dynamic-identifier op walking the threaded environment chain.
    // A13: free READ of a declared global (LoadDynamicIdentifier).
    [InlineData("var g=7; function f(){ return g; } f();", "unified-bytecode-production-fast-path func=f")]
    // A15: typeof of a free identifier (TypeOfDynamicIdentifier; never throws for an unbound name).
    [InlineData("var g=7; function f(){ return typeof g; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return typeof undeclaredX; } f();", "unified-bytecode-production-fast-path func=f")]
    // A22: identifier UPDATE on a free name (UpdateDynamicIdentifier) — postfix and prefix.
    [InlineData("var c=5; function f(){ return c++; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("var c=5; function f(){ return ++c; } f();", "unified-bytecode-production-fast-path func=f")]
    // A24: delete of a free identifier (DeleteDynamicIdentifier).
    [InlineData("var g=1; function f(){ return delete g; } f();", "unified-bytecode-production-fast-path func=f")]
    // A16: computed-property delete with a FREE identifier as the key (free computed delete key).
    [InlineData("var k='a'; function f(o){ return delete o[k]; } f({a:1});", "unified-bytecode-production-fast-path func=f")]
    // A14: free identifier STORE — sloppy-mode global creation through the dynamic reference pair.
    [InlineData("function f(){ createdGlobalRatchet=99; return createdGlobalRatchet; } f();", "unified-bytecode-production-fast-path func=f")]
    // A11: calls with COMPLEX arguments — nested call (g(h(x))), binary with a nested-call operand
    // (g(a + h(b))), member call argument (g(o.m(x))), and computed-member call argument
    // (g(o[k](x))). The argument expressions lower onto the existing operand stack with strict
    // left-to-right evaluation preserved.
    [InlineData("function f(g,h,x){ return g(h(x)); } f(a=>a+1,b=>b*10,5);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,h,a,b){ return g(a + h(b)); } f(x=>x,h=>h*2,3,4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,o,x){ return g(o.m(x)); } f(v=>v+1, {m(n){return n*3;}}, 4);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,o,k,x){ return g(o[k](x)); } f(v=>v+1, {m(n){return n*5;}}, 'm', 2);", "unified-bytecode-production-fast-path func=f")]
    // A12: MEMBER/COMPUTED call-target past the first invocation boundary — chained method calls
    // (a.b().c()) where the final call's target is a member access on an earlier call's result. The
    // receiver chain lowers in source order onto the operand stack and the final call applies with
    // this = the immediate receiver (the inner call's result). Named chain, named chain with a final
    // argument, and computed final chain.
    [InlineData("function f(o){ return o.a().b(); } f({a(){return {b(){return 7;}};}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ return o.get().m(2); } f({get(){return {m(x){return x*3;}};}});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o,k){ return o.a()[k](); } f({a(){return {z(){return 9;}};}}, 'z');", "unified-bytecode-production-fast-path func=f")]
    // A10: FREE/global identifier call target (helper(x)) — the callee is not an activation slot, so it
    // lowers to PrepareDynamicIdentifierCallTarget walking the threaded environment chain (this=undefined,
    // coerced per the callee's own mode). Free call, free callee with a nested free-call argument, and a
    // free call whose enclosing function has no other dynamic dependency.
    [InlineData("function helper(x){ return x+1; } function f(){ return helper(4); } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function g(x){ return x*2; } function helper(x){ return x+1; } function f(){ return helper(g(2)); } f();", "unified-bytecode-production-fast-path func=f")]
    // A9: identifier call past the FIRST invocation boundary — the second/third sequential call in a body
    // (a(); return b();) is admitted (free callees and slot callees alike), with side-effect order preserved.
    [InlineData("function a(){ return 1; } function b(){ return 2; } function f(){ a(); return b(); } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("var log=''; function p(t){ log+=t; } function f(){ p('a'); p('b'); p('c'); return log; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(g,h){ g(); return h(); } f(()=>0, ()=>7);", "unified-bytecode-production-fast-path func=f")]
    // A8: a call RETURNED FROM INSIDE a finally block is a tail position, but in NON-STRICT (sloppy) mode no
    // tail-call optimization applies anywhere (the IR runner's restart requires strict mode), so the VM is no
    // worse and the shape is admitted (the A8 guard is now strict-only). A finally-return MEMBER call
    // (member callees are never tail-call-optimized), a finally-return free call whose try body carries a
    // reachable dynamic dependency that enables the dynamic-name path, and a finally-return overriding a throw
    // (the `new Error(...)` read enables the dynamic-name path). Strict finally-return calls stay declined (TCO).
    [InlineData("function f(o){ try { return 1; } finally { return o.m(); } } f({ m(){ return 9; } });", "unified-bytecode-production-fast-path func=f")]
    [InlineData("var seed=3; function g(){ return 7; } function f(){ try { return seed; } finally { return g(); } } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function g(){ return 5; } function f(){ try { throw new Error('boom'); } finally { return g(); } } f();", "unified-bytecode-production-fast-path func=f")]
    // A1 (closure Stage 0): FLAT multi-statement closures that capture an enclosing activation
    // binding now route through the production VM — captured names lower to dynamic-identifier ops
    // over the threaded closure environment. Captured READ (config), captured WRITE (counter n++),
    // and a captured object property write. Nested-lexical-scope closures (shadowing hazard) are
    // intentionally NOT admitted yet (HasOnlyRootFlatSlotMappings guard) — no ratchet entry for them.
    [InlineData("function mk(){ let n=0; function inc(){ n++; return n; } return inc; } var f=mk(); f(); f();", "unified-bytecode-production-fast-path func=inc")]
    [InlineData("function mk(c){ function read(){ return c.x + c.y; } return read; } mk({x:1,y:2})();", "unified-bytecode-production-fast-path func=read")]
    [InlineData("function mk(o){ return function set(){ o.value=43; return o.value; }; } mk({value:42})();", "unified-bytecode-production-fast-path func=set")]
    // A1 (Option A, collision-only guard): nested-scope captured closures whose nested-block lexical
    // bindings do NOT collide with any captured name route through the production VM. Captured READ
    // with a disjoint nested `let step`, multi-level disjoint nesting, and a disjoint nested `const`.
    [InlineData("function mk(){ let acc=0; function inc(n){ if(n>0){ let step=n*2; acc+=step; } return acc; } return inc; } var f=mk(); f(1); f(2);", "unified-bytecode-production-fast-path func=inc")]
    [InlineData("function mk(){ let acc=1; function go(n){ if(n>0){ let mid=n+1; if(mid>1){ let deep=mid*2; acc+=deep; } } return acc; } return go; } var f=mk(); f(1); f(2);", "unified-bytecode-production-fast-path func=go")]
    [InlineData("function mk(){ let acc=0; function add(n){ if(n>0){ const factor=3; acc+=n*factor; } return acc; } return add; } var f=mk(); f(2); f(1);", "unified-bytecode-production-fast-path func=add")]
    // A1 (Option B, Stage 5): COLLIDING nested-scope captured closures — a nested block declares a
    // `let`/`const` that SHADOWS a captured enclosing name — now route too, because
    // SlotAssignmentRewriter no longer mis-stamps the captured read to the off-stack block slot (the
    // captured read past the block lowers to a dynamic-identifier op over the env chain). Single-level
    // `let` shadow, partial collision (shadowed `outer` + disjoint `captured`), two-level-deep shadow,
    // and a `const` shadow. These are the shapes the retired collision guard used to decline.
    [InlineData("function outer(seed){ let baseValue=seed; function inner(flag){ if(flag){ let baseValue=seed+100; return baseValue; } return baseValue; } return inner; } var fn=outer(10); fn(false); fn(true); fn(false);", "unified-bytecode-production-fast-path func=inner")]
    [InlineData("function make(){ const captured=2; let outer=5; function shadower(flag){ if(flag){ let outer=99; return outer+captured; } return outer+captured; } return shadower; } var fn=make(); fn(false); fn(true);", "unified-bytecode-production-fast-path func=shadower")]
    [InlineData("function mk(base){ function deep(c){ if(c){ let mid=1; if(mid>0){ let base=7; return base; } } return base; } return deep; } var f=mk(100); f(true); f(false);", "unified-bytecode-production-fast-path func=deep")]
    [InlineData("function mk(limit){ function clamp(flag){ if(flag){ const limit=7; return limit; } return limit; } return clamp; } var f=mk(50); f(true); f(false);", "unified-bytecode-production-fast-path func=clamp")]
    // A46: the `**` exponentiation binary operator (BinaryOperator.Power) is in the production operator
    // subset (IsProductionBinaryOperator) and evaluates via JsOps.Exp. Integer base/exponent, the
    // right-associative chain (2**3**2 === 512), and the `**=` compound form all keep routing sync.
    [InlineData("function f(a,b){ return a**b; } f(2,10);", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return 2**3**2; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ let x=3; x**=2; return x; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ return 2 + 3 ** 2; } f();", "unified-bytecode-production-fast-path func=f")]
    // A6 (multi-statement arrow Stage 0): arrows with a FLAT multi-statement body now route through the
    // production VM (previously SimpleReturnProgram-only). Arrows log anonymously (func=<anonymous>).
    // Local-const-then-return, captured enclosing local across statements, and lexical `this` inside an
    // object method. Nested-lexical-scope arrows (shadowing hazard) are intentionally NOT admitted yet
    // (HasOnlyRootFlatSlotMappings guard) — no ratchet entry for them.
    [InlineData("const f = (a,b) => { const s = a+b; return s*2; }; f(3,4);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("function mk(base){ return (k) => { var t = base*2; return t+k; }; } mk(100)(1);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("var o={ x:10, m:function(){ var f=()=>{ var b=5; return this.x+b; }; return f(); } }; o.m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    // A6 (Option A, collision-only guard): nested-scope arrows whose nested-block lexical bindings do NOT
    // collide with any captured name now route. Non-captured nested `let y`, and a captured-`base` arrow
    // with a disjoint nested `let bonus`. Colliding nested-scope arrows STAY declined — no entry.
    [InlineData("const f = (c) => { if (c) { let y = 1; return y; } return 0; }; f(true);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("function mk(base){ return (n) => { if(n>0){ let bonus=n*10; return base+bonus; } return base; }; } mk(5)(2);", "unified-bytecode-production-fast-path func=<anonymous>")]
    // A52: `debugger;` lowers to an EmptyStatement no-op, so functions/scripts containing it keep
    // routing through the production VM (in the body, after a side effect, and inside a loop body).
    [InlineData("function f(){ debugger; return 1; } f();", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(o){ o.a=1; debugger; return o.a; } f({});", "unified-bytecode-production-fast-path func=f")]
    [InlineData("function f(){ var s=0; for(var i=0;i<2;i++){ debugger; s+=i; } return s; } f();", "unified-bytecode-production-fast-path func=f")]
    // A7 (base-class constructor, general body): a BASE-class constructor (no extends/super) with a
    // general multi-statement FLAT body routes through the production VM identically to an ordinary
    // function constructor — the class-ctor activation predicate gates on the activation-slot shape, not
    // a SimpleReturnProgram-only body. Multi plain `this`-property stores, a local declaration feeding a
    // plain `this`-store, `new.target` inside the body, and a nested-lexical-scope body (`this`-stores
    // resolve through the receiver, so the captured-name shadowing hazard that bounds the closure/arrow
    // lifts does not apply here). The log func is the class name. Derived (super) ctors, instance-field
    // ctors, and private-name ctors stay declined downstream — no ratchet entry for them.
    [InlineData("class C { constructor(a,b){ this.x=a; this.y=b; } } new C(1,2);", "unified-bytecode-production-fast-path func=C")]
    [InlineData("class C { constructor(n){ let t=n+1; this.v=t; } } new C(2);", "unified-bytecode-production-fast-path func=C")]
    [InlineData("class C { constructor(){ this.nt=new.target; } } new C();", "unified-bytecode-production-fast-path func=C")]
    [InlineData("class C { constructor(n){ let s=0; if(n>0){ s=n+10; } this.s=s; } } new C(3);", "unified-bytecode-production-fast-path func=C")]
    // A37 (private-named mutation): private-field MUTATION inside a class method routes through the
    // production VM identically to the ordinary-property counterpart — the private name is just a name
    // gated by the private-name scope (PropertyHandle.Resolve(allowPrivate: true) performs the brand
    // check + spec TypeError). The method logs func=<anonymous> (class methods carry no IR-runner name),
    // so that log proves the METHOD body routed (the auto-generated ctor logs func=C separately). Plain
    // private write (this.#x = v), private postfix/prefix UPDATE (this.#x++ / ++this.#x), private SIMPLE
    // compound write (this.#x += 5), a cross-instance private write (other.#x = v), a private write with
    // an ordinary complex RHS (this.#x = a*b+c — carried by the 830236be0 complex-RHS widening, which is
    // private-name-agnostic), and a private write with a call RHS (this.#x = g(z)). The remaining declines
    // (a private READ used as a nested value operand, e.g. this.#x = this.#x + a; and compound-assignment
    // with a COMPLEX rhs, e.g. x += a+b — which declines for ORDINARY properties too) are NOT private-
    // mutation-specific gaps and are tracked as separate burn-down items.
    [InlineData("class C{ #x=1; m(){ this.#x = 5; return this.#x; } } new C().m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; m(){ this.#x++; return this.#x; } } new C().m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; m(){ ++this.#x; return this.#x; } } new C().m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; m(){ this.#x += 5; return this.#x; } } new C().m();", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; setOther(o,v){ o.#x = v; return o.#x; } } var a=new C(); var b=new C(); a.setOther(b, 9);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; m(a,b,c){ this.#x = a*b + c; return this.#x; } } new C().m(2,3,4);", "unified-bytecode-production-fast-path func=<anonymous>")]
    [InlineData("class C{ #x=1; m(g,z){ this.#x = g(z); return this.#x; } } new C().m(v=>v+1,5);", "unified-bytecode-production-fast-path func=<anonymous>")]
    // Sync generator route — `unified-bytecode-resumable-generator-fast-path func=<name>`.
    [InlineData("function* g(o){ yield o.a; yield -o.b; } var it=g({a:1,b:2}); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(o){ o.x=1; yield o.x; } g({}).next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield {a:1}; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield [1,2]; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield 'ready'; var C=class{ static seed=41; value=this.constructor.seed+1; }; return new C().value; } var it=g(); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield new.target; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield /ab+c/gi; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    // Resumable switch body: switch-style breakable markers are compiler metadata only and must keep
    // routing through ExecuteResumable instead of falling back to the resumable IR runner.
    [InlineData("function* g(n){ yield 'start'; switch(n){ case 1: yield 'one'; break; default: yield 'other'; } yield 'done'; } var it=g(1); it.next(); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    // B37: an UNTAGGED template literal with a substitution (`` `v${x}` ``) inside a resumable generator
    // body. The per-hole String(value) coercion lowers to the ToString opcode, now admitted to the resumable
    // allowlist + ExecuteResumable handler (literal twin of the sync JsOps.ToJsString). The substitution
    // value sits on the operand stack and is restored across the yield. (Substitution across a suspension and
    // the explicit-coercion forms are pinned in UnifiedBytecodeResumableTemplateToStringTests.)
    [InlineData("function* g(){ let x=5; yield `v${x}`; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("function* g(){ yield `n=${42}`; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    // Resumable captured-closure WRITE: a generator nested in `mk` mutates `mk`'s enclosing local `n`
    // (`n++`) across yields. `n` escapes the generator's own activation slots, so the update lowers to the
    // UpdateDynamicIdentifier opcode and resolves against the threaded CallingEnvironment, aliasing the same
    // enclosing heap slot across each suspension. (Captured READ of an enclosing local already routed.)
    [InlineData("function mk(){ let n=0; function* g(){ n++; yield n; n++; yield n; } return g; } var it=mk()(); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    // Resumable captured-closure WRITE across await: an async function nested in `mk` mutates `mk`'s
    // enclosing local `n` (`n++`) on both sides of an await; the update aliases the enclosing slot across the
    // await suspension via UpdateDynamicIdentifier over the threaded CallingEnvironment.
    [InlineData("function mk(){ let n=1; async function run(){ n++; await Promise.resolve(0); n++; return n; } return run; } mk()();", "unified-bytecode-resumable-async-fast-path func=run")]
    // B27: free identifier UPDATE in a resumable body. A generator updates a FREE (module/script-level)
    // binding (`c++`) across a yield; the update lowers to UpdateDynamicIdentifier and mutates the SAME global
    // resolved against the threaded CallingEnvironment on each suspension. (Async free-update variant below.)
    [InlineData("var c=5; function* g(){ yield c++; } var it=g(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("var c=10; async function run(){ c++; await Promise.resolve(0); c++; return c; } run();", "unified-bytecode-resumable-async-fast-path func=run")]
    // B28: free identifier DELETE in a resumable body. A generator deletes a FREE global (`delete gx`) across a
    // yield; the self-contained DeleteDynamicIdentifier opcode resolves against the threaded CallingEnvironment
    // and never uses the transient reference array. (Async free-delete variant below.)
    [InlineData("globalThis.gx=1; function* g(){ yield delete gx; } g().next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("globalThis.gx=5; async function run(){ await Promise.resolve(0); return delete gx; } run();", "unified-bytecode-resumable-async-fast-path func=run")]
    // B1: an AWAITED value bound into a flat slot and read back across the suspension. `var x = await p`
    // lowers to `<awaited ops>` -> AwaitValue -> InitializeSlot; the store lands after the suspension so the
    // later `x + 1` reads the settled value.
    [InlineData("async function f(p){ var x=await p; return x+1; } f(Promise.resolve(9));", "unified-bytecode-resumable-async-fast-path func=f")]
    // B44: an AWAITED value bound into an array destructuring binding and read back across the suspension.
    // `let [a,b] = await p` lowers to `<awaited ops>` -> AwaitValue -> ApplyDeclarationBindingTarget.
    [InlineData("async function f(p){ let [a,b]=await p; return a+b; } f(Promise.resolve([1,2]));", "unified-bytecode-resumable-async-fast-path func=f")]
    // B44 object form: `let {x} = await p` routes through the same await-into-binding lowering.
    [InlineData("async function f(p){ let {x}=await p; return x; } f(Promise.resolve({x:7}));", "unified-bytecode-resumable-async-fast-path func=f")]
    // Direct function-body sync `using` declarations in resumable bodies register through
    // RegisterDisposable and dispose when the resumable frame completes.
    [InlineData("function* g(r){ using value=r; yield 1; return 2; } var it=g({[Symbol.dispose](){}}); it.next(); it.next();", "unified-bytecode-resumable-generator-fast-path func=g")]
    [InlineData("async function f(r){ await Promise.resolve(0); using value=r; return 2; } f({[Symbol.dispose](){}});", "unified-bytecode-resumable-async-fast-path func=f")]
    public async Task AdmittedShape_StillRoutesThroughProduction(string source, string expectedLog)
    {
        await using var engine = CreateEngine();
        await engine.Evaluate(source);
        AssertRouted(expectedLog);
    }

    // Top-level script route — `unified-bytecode-production-fast-path script`.
    [Theory]
    [InlineData("1 + 2;", 3d)]
    [InlineData(
        """
        var source = { head: 2, tail: 3, extra: 5 };
        var { head, ...rest } = source;
        let box = { value: head, add(n) { return this.value + n; } };
        let total = Math.pow(box.add(rest.tail), 2);

        if (rest.extra > 4) {
            total += Math.sqrt(16);
        }

        total;
        """,
        29d)]
    public async Task AdmittedScript_StillRoutesThroughProductionScriptRoute(string source, double expected)
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(source);

        Assert.Equal(expected, result);
        AssertRouted(ProductionScriptFastPathLog);
    }

    [Fact]
    public async Task DynamicResidueScriptEvalInjectedRuntimeBinding_DoesNotRouteThroughProductionScriptRoute()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate("""
            eval("var injected = 1");
            injected;
            """);

        Assert.Equal(1d, result);
        AssertNotRouted(ProductionScriptFastPathLog);
    }

    [Fact]
    public async Task ScriptLoop_StillRoutesThroughProductionScriptRoute()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("var s=0; for (var i=0;i<3;i++){ s+=i; } s;");
        AssertRouted(ProductionScriptFastPathLog);
    }

    // A52: a top-level `debugger;` no-op must keep the script on the production script route.
    [Fact]
    public async Task ScriptWithDebuggerStatement_StillRoutesThroughProductionScriptRoute()
    {
        await using var engine = CreateEngine();
        await engine.Evaluate("debugger; var x = 5; x;");
        AssertRouted(ProductionScriptFastPathLog);
    }

    // Resumable async route — `unified-bytecode-resumable-async-fast-path func=<name>`.
    [Fact(Timeout = 5000)]
    public async Task AsyncAwait_StillRoutesThroughResumableAsync()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateAndAwait("""
            var r = 0;
            async function run(o){ await o.gate; return o.v; }
            run({ gate: Promise.resolve(0), v: 7 }).then(x => r = x);
            r;
            """);
        AssertRouted("unified-bytecode-resumable-async-fast-path func=run");
    }

    // B20: `import.meta` inside a resumable async body, in MODULE context (import.meta is only bound in a
    // module environment). The LoadImportMeta opcode is admitted to the resumable allowlist + ExecuteResumable
    // handler, resolving the Symbol.ImportMeta binding against the threaded CallingEnvironment (the captured
    // module env, stable across await). The async function must route resumable and observe the stable
    // per-module import.meta object after the await suspension.
    [Fact(Timeout = 5000)]
    public async Task ResumableAsyncImportMetaInModule_StillRoutesThroughResumableAsync()
    {
        await using var engine = CreateEngine();
        await engine.EvaluateModule("""
            globalThis.metaType = "PENDING";
            async function run(p){ await p; return typeof import.meta; }
            run(Promise.resolve(0)).then(v => globalThis.metaType = v);
            "started";
            """, "b20-ratchet-module.js");
        Assert.Equal("object", await engine.Evaluate("globalThis.metaType"));
        AssertRouted("unified-bytecode-resumable-async-fast-path func=run");
    }

    private void AssertRouted(string expectedLog)
    {
        Assert.Contains(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(expectedLog, StringComparison.Ordinal));
    }

    private void AssertNotRouted(string unexpectedLog)
    {
        Assert.DoesNotContain(
            CurrentLogger!.Collector.Snapshot(),
            record => record.Message.Contains(unexpectedLog, StringComparison.Ordinal));
    }
}
