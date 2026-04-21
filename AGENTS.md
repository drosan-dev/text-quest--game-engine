# Repo Workflow

## Branching

- Before starting implementation on a new task, ensure the repository is on the latest `dev` branch state.
- If not already on a dedicated task branch, use `/start-task <task-name>` to switch to `dev`, sync it with `origin/dev` when that remote exists, and create `task/<task-name>` from `dev`.
- Keep feature work off `main` and `dev`.
- Only merge a task branch after the user confirms review has passed.
- After review passes, use `/finish-task` from the reviewed task branch to merge it back into `dev`.

## Merge Rules

- `/finish-task` merges the current task branch into `dev` with `--no-ff`.
- `main` remains the release branch; do not merge task branches directly into `main` unless the user explicitly asks for that release flow.

## Conventions

- Use the `repository-conventions` skill when creating commits or writing tests in this repository.
