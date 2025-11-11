To generate test suite, run:

```bash
dotnet tool restore
dotnet test262 generate
```

This will download the Test262 test suite and generate test classes.

## About Test262

Test262 is the official ECMAScript conformance test suite maintained by TC39. It contains thousands of tests that validate JavaScript engine compliance with the ECMAScript specification.

## Running Tests

After generating the test suite:

```bash
dotnet test
```

Note: Many tests are expected to fail initially as this validates ECMAScript compatibility. The goal is to adapt the test infrastructure from Jint to work with Asynkron.JsEngine's API.

## Configuration

Test configuration and skipped tests are defined in `Test262Harness.settings.json`.
