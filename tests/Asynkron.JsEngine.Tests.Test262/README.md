# Test262 ECMAScript Conformance Tests

This test project validates Asynkron.JsEngine's compatibility with the ECMAScript specification using the official Test262 test suite.

## Setup

To generate the test suite, run:

```bash
dotnet tool restore
dotnet test262 generate
```

This will download the Test262 test suite (commit `a073f479`) and generate test classes. The generation process creates ~71,877 test cases with ~29,129 tests excluded based on unsupported features.

## About Test262

Test262 is the official ECMAScript conformance test suite maintained by TC39. It contains thousands of tests that validate JavaScript engine compliance with the ECMAScript specification.

**Note:** This test suite was adapted from [Jint's Test262 implementation](https://github.com/sebastienros/jint/tree/main/Jint.Tests.Test262).

## Running Tests

After generating the test suite:

```bash
dotnet test
```

**Important:** Many tests are expected to fail initially. This is intentional and validates the current level of ECMAScript compatibility. The goal is to have the test infrastructure in place to track compatibility progress over time.

## Configuration

Test configuration and skipped tests are defined in `Test262Harness.settings.json`. The following features are currently excluded:

- `Array.fromAsync`, `async-iteration`, `Atomics`, `decorators`, `iterator-helpers`
- `regexp-lookbehind`, `regexp-v-flag`, `Temporal`, `SharedArrayBuffer`
- `FinalizationRegistry`, `WeakRef`, `ShadowRealm`, and others

Directories excluded:
- `annexB` (legacy compatibility features)
- `intl402` (Internationalization API)
- `staging` (proposed features)

## Generated Files

The `Generated/` directory contains auto-generated test files and is excluded from source control. These files are regenerated each time you run `dotnet test262 generate`.
