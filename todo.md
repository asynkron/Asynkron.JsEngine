Your goal is to clean up the codebase so it becomes smaller, to your
disposal, you have:

Run: `cloc ./src/Asynkron.JsEngine/`
Gives you lines of C# code in the main project
The goal is to drive the LOC lower, without breaking anything.

Run: `quickdup --path ./src/Asynkron.JsEngine  --ext .cs --exclude ".g."`
Gives you candidates for code duplication that could be refactored into smaller parts.

Run: `dotnet test tests/Asynkron.JsEngine.Tests --nologo --verbosity minimal`
To verify nothing has broken, there were 2 failing tests preexisting, do not go beyond that
number.

Workflow.
1.
run cloc, get a LoC that you can compare with.
run quickdup, get ideas on what potential code duplicates there are.

2.
get to work, refactor code, turn complicated code into simple code, remove needless
abstractions, anything goes.
Expression bodied members do not count, those are cheating.
Extract methods, introduce base classes, default interface implementations, smarter algorithms,

3. run tests, do we have more than the initial 2 failures? if yes, git revert, drop changes
and end your task.

4.
completion, run cloc again, do we have more, ore less lines now?
if we have more lines, git revert, drop changes, if we have less lines now, commit pending
changes.

The end..
