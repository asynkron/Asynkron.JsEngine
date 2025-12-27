# JavaScript Built-in Objects and Methods to Implement

Look here
`~/git/asynkron/Asynkron.JsEngine/src/Asynkron.JsEngine/StdLib`
and
`~/git/asynkron/Asynkron.JsEngine/src/Asynkron.JsEngine/JsTypes`

## 1. Temporal (≈ 200+)

**Main objects:**

* Temporal.Duration
* Temporal.Instant
* Temporal.Now
* Temporal.PlainDate
* Temporal.PlainDateTime
* Temporal.PlainMonthDay
* Temporal.PlainTime
* Temporal.PlainYearMonth
* Temporal.ZonedDateTime

**Methods:**

* `compare`, `from`, `toString`, `toJSON`, `toLocaleString`, `equals`, `round`, `since`, `subtract`, `add`, `until`, `valueOf`
* `prototype.add`, `prototype.subtract`, `prototype.toString`, `prototype.toJSON`, `prototype.toLocaleString`, `prototype.equals`, `prototype.round`
* Duration-specific: `prototype.total`, `prototype.sign`, `prototype.days`, `prototype.hours`, `prototype.minutes`, `prototype.seconds`, `prototype.nanoseconds`, `prototype.years`, `prototype.months`, `prototype.weeks`, `prototype.with`
* Instant-specific: `prototype.toZonedDateTimeISO`, `prototype.toInstant`, `prototype.toZonedDateTime`, `prototype.since`, `prototype.epochMilliseconds`, `prototype.epochNanoseconds`
* PlainDate/DateTime/YearMonth: `prototype.calendarId`, `prototype.era`, `prototype.eraYear`, `prototype.inLeapYear`, `prototype.day`, `prototype.month`, `prototype.year`, `prototype.toPlainDateTime`, `prototype.toPlainMonthDay`, `prototype.toPlainYearMonth`, `prototype.toPlainTime`, `prototype.with`, `prototype.withCalendar`, `prototype.toZonedDateTime`, `prototype.weekOfYear`, `prototype.daysInMonth`, `prototype.daysInYear`, `prototype.daysInWeek`
* ZonedDateTime-specific: `prototype.offset`, `prototype.offsetNanoseconds`, `prototype.timeZoneId`, `prototype.withTimeZone`, `prototype.getTimeZoneTransition`, `prototype.hoursInDay`, `prototype.startOfDay`, `prototype.withPlainTime`, `prototype.withCalendar`

---

## 2. TypedArray / TypedArrayConstructors (≈ 140+)

**Main entries:**

* TypedArray, TypedArray.prototype.*, TypedArrayConstructors.*
* Includes: `Int8Array`, `Uint8Array`, `Uint8ClampedArray`, `Int16Array`, `Uint16Array`, `Int32Array`, `Uint32Array`, `Float32Array`, `Float64Array`, `BigInt64Array`, `BigUint64Array`

**Methods:**

* `TypedArray.from`, `TypedArray.of`, `TypedArray.Symbol.species`
* `TypedArray.prototype.at`, `buffer`, `byteLength`, `byteOffset`
* `copyWithin`, `every`, `fill`, `filter`, `forEach`, `includes`, `join`, `length`, `map`, `reduce`, `reduceRight`, `set`, `slice`, `some`, `sort`, `subarray`, `toLocaleString`, `toReversed`, `toSorted`, `toString`, `with`
* BigInt variants for all above (e.g. `filter.BigInt`, `reduce.BigInt`)
* `TypedArrayConstructors.from`, `from.BigInt`, `of`, `of.BigInt`
* Constructors: `ctors.noArgs`, `ctors.lengthArg`, `ctors.objectArg`, `ctors.bufferArg`, `ctors.typedarrayArg`
* BigInt constructors: `ctorsBigint.noArgs`, `ctorsBigint.lengthArg`, `ctorsBigint.objectArg`, `ctorsBigint.bufferArg`, `ctorsBigint.typedarrayArg`
* Internals: `internals.Get`, `internals.Set`, `internals.DefineOwnProperty`, `internals.Delete`, `internals.HasProperty`, `internals.GetOwnProperty`, `internals.OwnPropertyKeys` (+ `BigInt` versions)
* Prototypes: `prototype.Symbol.toStringTag`, `prototype.Symbol.toStringTag.BigInt`

---

## 3. Date (≈ 60)

**Core:**

* `Date`, `Date.parse`, `Date.UTC`

**Prototype methods:**

* Getters: `getDate`, `getDay`, `getFullYear`, `getHours`, `getMilliseconds`, `getMinutes`, `getMonth`, `getSeconds`, `getTime`, `getTimezoneOffset`, `getUTCDate`, `getUTCDay`, `getUTCFullYear`, `getUTCHours`, `getUTCMilliseconds`, `getUTCMinutes`, `getUTCMonth`, `getUTCSeconds`
* Setters: `setDate`, `setFullYear`, `setHours`, `setMilliseconds`, `setMinutes`, `setMonth`, `setSeconds`, `setTime`, `setUTCDate`, `setUTCHours`, `setUTCMilliseconds`, `setUTCMinutes`, `setUTCMonth`, `setUTCSeconds`
* Conversion: `toDateString`, `toISOString`, `toJSON`, `toString`, `toTimeString`, `toUTCString`, `toLocaleString`, `toTemporalInstant`, `valueOf`
* Symbol: `Symbol.toPrimitive`

---

## 4. Atomics (≈ 40)

**Core object:** `Atomics`

**Methods:**

* Arithmetic ops (normal and bigint): `add`, `sub`, `and`, `or`, `xor`
* Compare/exchange: `compareExchange`, `exchange`
* Load/store: `load`, `store`
* Lock/notify/wait: `notify`, `wait`, `waitAsync`
* Misc: `isLockFree`
* BigInt variants for every above: `add.bigint`, `sub.bigint`, etc.

---

## 5. DataView (≈ 30)

**Core:** `DataView`

**Prototype:**

* Properties: `buffer`, `byteLength`, `byteOffset`
* Getters: `getInt8`, `getUint8`, `getInt16`, `getUint16`, `getInt32`, `getUint32`, `getFloat16`, `getFloat32`, `getFloat64`, `getBigInt64`, `getBigUint64`
* Setters: `setInt8`, `setUint8`, `setInt16`, `setUint16`, `setInt32`, `setUint32`, `setFloat16`, `setFloat32`, `setFloat64`, `setBigInt64`, `setBigUint64`

---

## 6. Array (≈ 28)

**Core:** `Array`, `Array.from`, `Array.length`, `Array.Symbol.species`

**Prototype methods:**

* Mutation: `push`, `pop`, `shift`, `unshift`, `splice`, `sort`, `reverse`, `copyWithin`, `fill`, `with`
* Accessors: `slice`, `join`, `includes`, `indexOf`, `lastIndexOf`
* Iteration: `map`, `filter`, `flat`, `flatMap`
* String conversion: `toString`, `toLocaleString`
* New methods: `toReversed`, `toSorted`, `toSpliced`
* Symbol: `Symbol.unscopables`

---

## 7. Function / Generator / AsyncFunction / AsyncGenerator (≈ 25)

**Core types:** `Function`, `Function.prototype.*`, `GeneratorFunction`, `GeneratorFunction.prototype`, `GeneratorPrototype.*`, `AsyncFunction`, `AsyncGeneratorFunction`, `AsyncGeneratorFunction.prototype`, `AsyncGeneratorPrototype.*`

**Methods:**

* Function: `apply`, `bind`, `call`, `toString`, `Symbol.hasInstance`
* Generators: `next`, `return`, `throw`
* AsyncGenerator: `next`, `return`, `throw`
* Internals: `Function.internals.Call`, `Function.internals.Construct`

---

## 8. Object (≈ 20)

**Static methods:**
`assign`, `create`, `defineProperty`, `defineProperties`, `entries`, `fromEntries`, `getOwnPropertyDescriptor`, `getOwnPropertyDescriptors`, `getOwnPropertyNames`, `getOwnPropertySymbols`, `getPrototypeOf`, `groupBy`, `hasOwn`, `isFrozen`, `isSealed`, `keys`, `preventExtensions`, `seal`, `setPrototypeOf`, `values`

**Prototype methods:**
`hasOwnProperty`, `isPrototypeOf`, `propertyIsEnumerable`, `toLocaleString`, `toString`, `valueOf`, `.proto..`

---

## 9. RegExp (≈ 20)

**Core:** `RegExp`, `RegExp.Symbol.species`

**Prototype:**
`exec`, `test`, `toString`, `flags`, `source`, `dotAll`, `global`, `hasIndices`, `ignoreCase`, `multiline`, `sticky`, `unicode`, `unicodeSets`
**Symbols:** `Symbol.match`, `Symbol.matchAll`, `Symbol.replace`, `Symbol.search`, `Symbol.split`
**Features/tests:** `CharacterClassEscapes`, `dotall`, `lookBehind`, `namedGroups`, `propertyEscapes`, `regexpModifiers`, `unicodeSets.generated`

---

## 10. String (≈ 20)

**Core:** `String`, `String.fromCharCode`, `String.fromCodePoint`, `String.raw`

**Prototype:**
`includes`, `indexOf`, `endsWith`, `startsWith`, `match`, `matchAll`, `replace`, `replaceAll`, `split`, `substring`, `search`, `localeCompare`, `normalize`, `toLowerCase`, `toUpperCase`, `toLocaleLowerCase`, `toLocaleUpperCase`, `trim`, `trimStart`, `trimEnd`

---

## 11. Reflect (≈ 11)

`apply`, `construct`, `defineProperty`, `deleteProperty`, `get`, `getOwnPropertyDescriptor`, `getPrototypeOf`, `has`, `isExtensible`, `ownKeys`, `preventExtensions`, `set`, `setPrototypeOf`

---

## 12. Proxy (≈ 14)

`Proxy`, `Proxy.revocable`, plus traps:
`apply`, `construct`, `defineProperty`, `deleteProperty`, `get`, `getOwnPropertyDescriptor`, `getPrototypeOf`, `has`, `isExtensible`, `ownKeys`, `preventExtensions`, `set`, `setPrototypeOf`

---

## 13. Map / Set / Iterator (≈ 10–12 each)

**Map:**
`Map`, `Map.prototype.forEach`, `Map.prototype.size`, `Map.groupBy`, `Map.Symbol.species`, `MapIteratorPrototype.next`

**Set:**
`Set`, `Set.prototype.forEach`, `Set.prototype.values`, `Set.prototype.size`, `Set.prototype.union`, `intersection`, `difference`, `symmetricDifference`, `isSubsetOf`, `isSupersetOf`, `isDisjointFrom`, `Set.Symbol.species`, `SetIteratorPrototype.next`

**Iterator:**
`Iterator.from`, `Iterator.concat`, `Iterator.prototype.*`, including `drop`, `take`, `map`, `filter`, `reduce`, `every`, `some`, `find`, `forEach`, `toArray`, `Symbol.iterator`, `Symbol.dispose`, `Symbol.toStringTag`

---

## 14. Promise (≈ 10)

`Promise`, `Promise.all`, `Promise.allSettled`, `Promise.any`, `Promise.race`, `Promise.resolve`, `Promise.reject`, `Promise.withResolvers`, `Promise.prototype.then`, `Promise.prototype.catch`, `Promise.prototype.finally`, `Promise.Symbol.species`

---

## 15. Symbol (≈ 10)

`Symbol.for`, `Symbol.keyFor`, `Symbol.asyncIterator`, `Symbol.iterator`, `Symbol.match`, `Symbol.matchAll`, `Symbol.replace`, `Symbol.search`, `Symbol.split`, `Symbol.toPrimitive`, `Symbol.toStringTag`, `Symbol.unscopables`, `Symbol.asyncDispose`, `Symbol.dispose`, `Symbol.hasInstance`, `Symbol.isConcatSpreadable`, `Symbol.species`, `Symbol.prototype.Symbol.toPrimitive`

---

## 16. Error / NativeErrors (≈ 12)

`Error`, `Error.prototype`,
`NativeErrors.AggregateError`, `EvalError`, `RangeError`, `ReferenceError`, `SyntaxError`, `TypeError`, `URIError`, `SuppressedError` + each `prototype`

---

## 17. DisposableStack / AsyncDisposableStack (≈ 12)

**DisposableStack:**
`DisposableStack`, `DisposableStack.prototype`, `adopt`, `defer`, `dispose`, `disposal`, `move`, `use`

**AsyncDisposableStack:**
`AsyncDisposableStack`, `AsyncDisposableStack.prototype`, `adopt`, `defer`, `disposeAsync`, `disposal`, `move`, `use`

---

## 18. WeakMap / WeakSet / WeakRef (≈ 8)

**WeakMap:** `delete`, `get`, `has`, `set`
**WeakSet:** `add`, `delete`, `has`
**WeakRef:** `deref`

---

## 19. ShadowRealm (≈ 5)

`ShadowRealm`, `ShadowRealm.prototype.evaluate`, `ShadowRealm.prototype.importValue`, `ShadowRealm.WrappedFunction`

---

## 20. JSON (≈ 4)

`JSON.parse`, `JSON.stringify`, `JSON.rawJSON`, `JSON.isRawJSON`

---

## 21. BigInt (≈ 4)

`BigInt`, `BigInt.asIntN`, `BigInt.asUintN`, `BigInt.prototype.toString`

---

## 22. Number (≈ 6)

`Number`, `Number.prototype.toExponential`, `toFixed`, `toPrecision`, `toString`, `valueOf`

---

## 23. Boolean (≈ 3)

`Boolean.prototype.toString`, `Boolean.prototype.valueOf`

---

## 24. Misc built-ins (≈ 10 total)

`IsFinite`, `IsNaN`, `ParseInt`, `ParseFloat`, `EncodeURI`, `EncodeURIComponent`, `DecodeURI`, `DecodeURIComponent`, `ThrowTypeError`
