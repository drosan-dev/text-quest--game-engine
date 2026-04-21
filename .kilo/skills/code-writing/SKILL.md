---
name: code-writing
description: This skill should be used when implementing or refactoring source code in this repository, especially when public APIs, domain models, or non-trivial logic are touched and the resulting code must be documented as part of the change.
---

# Code Writing

## Overview

Apply this skill when work primarily involves writing or changing production code. Keep changes minimal, align with the existing architecture, and treat documentation as part of the definition of done rather than an optional follow-up.

## Workflow

1. Inspect the touched area before editing and identify the public types, contracts, and non-obvious logic affected by the change.
2. Implement the smallest correct code change that satisfies the task without introducing speculative abstractions.
3. Document the result in the same change set.

## Documentation Rules

1. Add or update XML documentation for public interfaces, classes, records, enums, and public members whose intent is not already obvious from nearby documentation.
2. Document behavior and responsibility, not syntax. Explain what the type or member represents, when it is used, and any important invariants.
3. Add short inline comments only for logic that would otherwise take noticeable time to parse. Avoid narrating trivial assignments or control flow.
4. When modifying existing code, bring the touched public surface into documentation compliance when the scope stays local and low-risk.
5. Do not create separate documentation files for small code changes when source-level documentation is sufficient.

## C# Guidance

When working in C# projects:

1. Prefer XML documentation comments above public declarations.
2. Use `<summary>` for the main responsibility and add `<param>` or `<returns>` only where they add signal.
3. Prefer `<inheritdoc />` in implementation members when the interface already provides the authoritative contract.
4. Keep summaries concise and specific to the domain model or runtime behavior.

## Definition Of Done

Treat the task as complete only when both conditions are true:

1. The code change satisfies the requested behavior.
2. The touched code is documented enough that another engineer can understand the public surface and the non-obvious logic without reconstructing intent from implementation details.
