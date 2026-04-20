---
description: Merge the reviewed task branch back into dev
---
Use the `git-task-flow` skill for this workflow.

Finish the current reviewed task branch.

Required behavior:

1. Verify this is a git repository and stop with a clear error if it is not.
2. Refuse to continue if the current branch is `main` or `dev`.
3. Refuse to continue if there are staged, unstaged, or untracked changes.
4. Remember the current branch as the source branch.
5. Check out `dev`.
6. If remote `origin` and branch `origin/dev` exist, fetch `origin` and fast-forward local `dev` to `origin/dev`. Do not create merge commits for sync.
7. Merge the source branch into `dev` with `--no-ff` and an explicit merge commit message in the form `merge: <source-branch> into dev`.
8. Report the merge result and current branch.

Do not delete the source branch unless the user explicitly asks.
