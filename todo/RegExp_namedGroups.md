# RegExp_namedGroups

FQN:
`Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups`

Remaining failures: 6 (70/76 passing)

## Duplicate named groups — quantified backref outside quantifier (.NET group reset limitation) — 4 tests
.NET does not reset capturing groups on quantifier re-entry. When `\k<x>` appears outside
a quantified group containing duplicate-named groups, the backreference sees stale captures
from previous iterations instead of undefined.
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/duplicate-names-exec.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/duplicate-names-exec.js",True)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/duplicate-names-match.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/duplicate-names-match.js",True)

## Lookbehind with named groups — 2 tests
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/lookbehind.js",False)
- Asynkron.JsEngine.Tests.Test262.BuiltInsTests.RegExp_namedGroups("built-ins/RegExp/named-groups/lookbehind.js",True)
