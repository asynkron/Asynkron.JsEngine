# Agent Guidance

## .NET installation
To ensure the correct .NET runtime/SDK is available when working on this repository, use the official `dotnet-install` script.

On Linux/macOS:
```
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0
export PATH="$HOME/.dotnet:$PATH"
```

On Windows PowerShell:
```
irm https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
./dotnet-install.ps1 -Channel 10.0
$env:PATH = "$env:USERPROFILE\.dotnet;" + $env:PATH
```

These commands install the latest .NET 10 SDK (which includes the runtime) into the user profile so the repository can build and run tests without requiring global admin access.

## ECMAScript compliance

- All language and runtime behavior should follow the ECMAScript specification as closely as practical.
- Do **not** introduce non-standard language extensions (e.g., accepting syntactically invalid constructs or changing specified semantics) unless there is an explicit, documented requirement and matching tests.
- Both strict mode and non-strict (sloppy) mode must remain supported; changes should preserve their existing semantics and the spec-defined differences between them.

## Continue working

Read the continue.md file fully, alanyze it and understad the next steps described there.
Then continue working on the project as per the instructions given in that file.

Whenever some task is completed, remove it from the continue.md file and update the file with the new next steps.
So we get a form of rolling window of next steps to be done.

## Avoid long-winded answers

When answering, try to be concise and to the point. Avoid longwinded explanations and unnecessary details.

Focus on producing code, not driving conversations.

## Thread blocking

You may never ever ever use thread blocking calls like Task.Wait(), Task.Result, or Thread.Sleep().
If you ever think that is the way to go, then there is a larger design issue that needs to be resolved.

## Shared State

You may never ever ever use "thread static" or AsyncLocal<T> or any other mechanism that relies on shared state between different asynchronous calls.
If anything needs to be passed around, it must be passed explicitly as a parameter, or part of a parameter, e.g. JsEnvironment or similar.

## Unsupported features

When you encounter an unsupported language/runtime feature or AST shape, fail fast by throwing a `NotSupportedException` (with a clear reason) instead of silently degrading behaviour or falling back to partial implementations. All such unsupported paths should be explicit so issues surface upfront and can be fixed properly.

## Generated code

- Never edit files with `.generated.` in their names. They are produced by tooling and will be regenerated, so any manual change will be lost (and may break future syncs). Always apply fixes by editing the non-generated partials/helpers instead.
