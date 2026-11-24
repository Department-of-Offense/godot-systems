# Contributing

## Workflow

1. Branch from `main`: `feature/<short-name>` or `fix/<short-name>`.
2. Keep PRs small and focused. One feature or fix per PR.
3. Open a PR. CI must pass and one teammate must approve before merging.
4. Squash-merge. The PR title becomes the commit message.

`main` is protected: no direct pushes, no force pushes.

## Ground rules

- **Engine stays generic.** Nothing named after a specific game's factions, units or maps goes in `addons/command_engine`. If only Fractured Horizons needs it, it lives in that repo.
- **Behaviour changes come with a test** in `tests/`. Most gameplay logic can be tested headless; see the existing tests for patterns.
- **Code style:** follow `.editorconfig` (file-scoped namespaces, `_camelCase` private fields). Nullable reference types are on; keep the build warning-free (CI builds with `-warnaserror`).
- **Units never read input directly.** Input goes through a controller that produces a `UnitIntent`. That keeps AI, pilot and future network control interchangeable.

## Releasing

Tag `main` with a semantic version (`v0.2.0`). Games pin to tags, so breaking API changes bump the minor version while we're pre-1.0 and get a note in the release.
