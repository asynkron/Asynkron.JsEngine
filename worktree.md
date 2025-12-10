# Git worktrees

1. ask the user for a task perform, $TASK
The user responds with something like, fix these tests, and a list of broken tests.

2. Extract a name from the task, $NAME

3. Run:
```
git worktree add ../jsengine-$NAME -b jsengine-$NAME
```
4. Navigate to the new worktree:
```
cd ../jsengine-$NAME
```
5. create a new .md file called todo.md:

# $NAME
$TASK

