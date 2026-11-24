# Contributing

## Workflow

1. Branch from `main`: `feature/<short-name>` or `fix/<short-name>`.
2. Keep PRs small and focused. One feature or fix per PR.
3. Open a PR. CI must pass and one teammate must approve before merging.
4. Squash-merge. The PR title becomes the commit message.

`main` is protected: no direct pushes, no force pushes.

## Adding or changing a system

- **Pick the right module.** If nothing fits, create `addons/dof_<name>/` with a `module.cfg` (name, description, `depends`) and a `README.md` (what it does, how to use it, extension points). Namespace: `Dof.<Name>`.
- **Declare every dependency.** If `dof_x` code imports `Dof.Y`, then `dof_y` must be in `dof_x/module.cfg` `depends`. CI rejects undeclared imports and cycles, and compiles each module alone.
- **Depend downward, and keep it small.** Before adding a dependency, ask whether an interface in your module would do. That's how cameras follow units without knowing what a unit is (`ICameraSubject`).
- **Leave room for games to differ.** Prefer exported settings, interfaces, virtual methods and signals over hard-coded behaviour. Never hard-code a key: read a named action, give it a default with `InputDefaults`, and expose the name as a property.
- **Nothing game-specific.** If only one game needs it, it lives in that game.
- **Behaviour changes come with a test** in `tests/`. Most gameplay logic can be tested headless; see the existing tests for patterns.

## Code style

Follow `.editorconfig`: file-scoped namespaces, `_camelCase` private fields, nullable references on. Keep the build warning-free (CI uses `-warnaserror`).

## Releasing

Tag `main` with a semantic version (`v0.3.0`). Games pin to tags, so while we're pre-1.0 any breaking change bumps the minor version, and the release notes say what games must change.
