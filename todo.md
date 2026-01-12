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
- Think deep about what could be improved, do not just do surface level changes.
- Extract methods, introduce base classes, default interface implementations, smarter algorithms.
- Are there other things we can code generate? move repetitive code into the Asynkron.JsEngine.Generator project? Bootstrapping JS types in the JsEngine?
- since we are trying to move away from AST walking, and use IR instead, are there entire areas of code that could be removed?
- Are there concepts that are similar that could be unified?
- Are there things with very few consumers, 3 or less? could those consumers be routed to something else? and the entire thing removed?

3. run tests, do we have more than the initial 2 failures? if yes, git revert, drop changes
and end your task.

4.
completion, run cloc again, do we have more, ore less lines now?
if we have more lines, git revert, drop changes, if we have less lines now, commit pending
changes.

The end..
