---
name: testing-conventions
description: This skill should be used when creating or updating automated tests in this repository, especially to enforce Russian summary documentation and the Arrange/Act/Assert test structure.
---

# Testing Conventions

## Overview

Use this skill when writing or editing tests in this repository. Keep test code aligned with the local expectations for readability and maintenance.

If test changes accompany behavior changes in production code, keep the corresponding factual documentation in sync in the same change set instead of leaving docs to a follow-up task.

## Required Rules

1. Add summary documentation for tests in Russian when the language and framework support it.
2. Prefer concise, domain-specific summaries that describe the scenario or expected behavior.
3. Structure each test using `Arrange`, `Act`, `Assert` sections.
4. Keep the `Arrange`, `Act`, `Assert` split visible in the test body with clear comments or equivalent formatting already accepted by the file.
5. Avoid collapsing setup, execution, and verification into a single dense block when a readable AAA split is possible.

## C# Test Guidance

When working in C# tests:

1. Add XML documentation summaries in Russian above test methods when consistent with the file style.
2. Use short `// Arrange`, `// Act`, `// Assert` comments inside the test body unless the file already uses another explicit AAA convention.
3. Preserve the existing test framework style such as xUnit, NUnit, or MSTest attributes and assertion patterns.

Example shape:

```csharp
/// <summary>
/// Проверяет, что ...
/// </summary>
[Fact]
public void Should_DoSomething_WhenCondition()
{
    // Arrange

    // Act

    // Assert
}
```

## Editing Guidance

1. Apply these conventions to new tests by default.
2. When modifying existing tests, bring the touched tests into compliance when the change is local and low-risk.
3. Do not rewrite large unaffected test files only to add summaries or AAA formatting.
4. When the test change reflects new actual behavior, ensure the relevant factual docs such as `docs/current-state.md`, `docs/json-quest-format.md`, `docs/cli.md`, or `docs/adapter-extension-points.md` are updated together with the code.
