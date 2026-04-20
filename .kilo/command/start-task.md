---
description: Sync dev and create a task branch
---
Use the `git-task-flow` skill for this workflow.

Start a new task branch for `$ARGUMENTS`.

Required behavior:

1. Verify this is a git repository and stop with a clear error if it is not.
2. Refuse to continue if there are staged, unstaged, or untracked changes.
3. Normalize the task name into a lowercase kebab-case slug and use branch name `task/<slug>`.
4. Check out `dev`.
5. If remote `origin` and branch `origin/dev` exist, fetch `origin` and fast-forward local `dev` to `origin/dev`. Do not create merge commits for sync.
6. Create the new task branch from `dev` and switch to it. If the branch already exists, switch to it instead of recreating it.
7. Report the resulting branch name and base commit.

Prefer direct git commands over explanations. Do not make code changes as part of this command.
