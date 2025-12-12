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

## Parser and evaluator

- Treat the parser and evaluator as locked. Only touch them to fix a demonstrated bug; all other behavioural changes must flow through built-in functions and types (and their generators), not the parser/evaluator paths.

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

## Debugging

You have access powerful visualizations and debug outputs, check the ActivityTracingTests.EvaluatorActivitiesAttachToTestRoot test to see how you can leverage System.Diagnostics.Activity for tracing code execution paths.

## Logging

- When adding evaluator logging, use the realm logger directly with null-propagation, e.g. `realm.Logger?.LogInformation(...)`. Do not wrap this in helper methods.
- Never use `Console.WriteLine`/`Console.Error.WriteLine` for runtime logging; route diagnostics through `realm.Logger?.Log...`.

## Compilation

Never use "--no-build", always ensure you are working with the latest compiled code.

## MCP Agent Mail: coordination for multi-agent workflows

What it is
- A mail-like layer that lets coding agents coordinate asynchronously via MCP tools and resources.
- Provides identities, inbox/outbox, searchable threads, and advisory file reservations, with human-auditable artifacts in Git.

Why it's useful
- Prevents agents from stepping on each other with explicit file reservations (leases) for files/globs.
- Keeps communication out of your token budget by storing messages in a per-project archive.
- Offers quick reads (`resource://inbox/...`, `resource://thread/...`) and macros that bundle common flows.

How to use effectively
1) Same repository
    - Register an identity: call `ensure_project`, then `register_agent` using this repo's absolute path as `project_key`.
    - Reserve files before you edit: `file_reservation_paths(project_key, agent_name, ["src/**"], ttl_seconds=3600, exclusive=true)` to signal intent and avoid conflict.
    - Communicate with threads: use `send_message(..., thread_id="FEAT-123")`; check inbox with `fetch_inbox` and acknowledge with `acknowledge_message`.
    - Read fast: `resource://inbox/{Agent}?project=<abs-path>&limit=20` or `resource://thread/{id}?project=<abs-path>&include_bodies=true`.
    - Tip: set `AGENT_NAME` in your environment so the pre-commit guard can block commits that conflict with others' active exclusive file reservations.

2) Across different repos in one project (e.g., Next.js frontend + FastAPI backend)
    - Option A (single project bus): register both sides under the same `project_key` (shared key/path). Keep reservation patterns specific (e.g., `frontend/**` vs `backend/**`).
    - Option B (separate projects): each repo has its own `project_key`; use `macro_contact_handshake` or `request_contact`/`respond_contact` to link agents, then message directly. Keep a shared `thread_id` (e.g., ticket key) across repos for clean summaries/audits.

Macros vs granular tools
- Prefer macros when you want speed or are on a smaller model: `macro_start_session`, `macro_prepare_thread`, `macro_file_reservation_cycle`, `macro_contact_handshake`.
- Use granular tools when you need control: `register_agent`, `file_reservation_paths`, `send_message`, `fetch_inbox`, `acknowledge_message`.

Common pitfalls
- "from_agent not registered": always `register_agent` in the correct `project_key` first.
- "FILE_RESERVATION_CONFLICT": adjust patterns, wait for expiry, or use a non-exclusive reservation when appropriate.
- Auth errors: if JWT+JWKS is enabled, include a bearer token with a `kid` that matches server JWKS; static bearer is used only when JWT is disabled.
