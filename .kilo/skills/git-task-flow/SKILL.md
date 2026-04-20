---
name: git-task-flow
description: This skill should be used when starting or finishing work on a task branch in this repository, especially for syncing `dev`, creating `task/<name>` branches, and merging reviewed task branches back into `dev`.
---

# Git Task Flow

## Overview

Use this skill to enforce the repository workflow for day-to-day task work. Start each implementation task from an up-to-date `dev` branch, do the work on a dedicated `task/<slug>` branch, and merge that branch back into `dev` only after review is complete.

## Start Task Workflow

Follow this workflow when a user begins implementation work for a new task.

1. Verify the current directory is inside a git repository.
2. Check for staged, unstaged, or untracked changes. If any exist, stop and report that the tree must be clean before creating a task branch.
3. Build a branch slug from the provided task name:
   - convert to lowercase
   - replace spaces and separators with `-`
   - remove characters that are awkward in git branch names
   - use final branch name `task/<slug>`
4. Check out `dev`.
5. If `origin/dev` exists, fetch `origin` and fast-forward local `dev` to `origin/dev`.
6. Create `task/<slug>` from `dev` and switch to it. If the branch already exists, switch to it and report that it was reused.
7. Report the active branch and the commit it was created from.

## Finish Task Workflow

Follow this workflow only after the user indicates the task branch already passed review.

1. Verify the current directory is inside a git repository.
2. Capture the current branch name and refuse to continue if it is `main` or `dev`.
3. Check for staged, unstaged, or untracked changes. If any exist, stop and report that the tree must be clean before merging.
4. Check out `dev`.
5. If `origin/dev` exists, fetch `origin` and fast-forward local `dev` to `origin/dev`.
6. Merge the reviewed source branch into `dev` using `git merge --no-ff`.
7. Use merge message `merge: <source-branch> into dev`.
8. Report the resulting merge commit and leave the source branch intact unless the user explicitly asks to delete it.

## Guardrails

- Never start normal feature work directly on `main` or `dev`.
- Never merge a task branch before the user says review passed.
- Never use destructive git commands such as hard reset or force push unless the user explicitly requests them.
- Prefer fast-forward sync for `dev` against `origin/dev`; do not create a sync merge commit.
- If `origin` is missing, continue with the local `dev` branch and report that no remote sync was possible.

## Command Mapping

- Use `/start-task <task-name>` to run the start workflow.
- Use `/finish-task` from the reviewed task branch to merge it back into `dev`.
