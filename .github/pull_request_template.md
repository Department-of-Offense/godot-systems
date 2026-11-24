## What

<!-- What does this change and why? Link the issue if there is one. -->

## How to check

<!-- Steps for a reviewer: scene to open, keys to press, test to run. -->

- [ ] Tests pass locally (`godot --headless --fixed-fps 60 --path . res://tests/TestRunner.tscn`)
- [ ] New behaviour has a test, or the PR says why not
- [ ] No game-specific code in `addons/` (that belongs in the game repo)
- [ ] New cross-module imports are listed in that module's `module.cfg` (`depends`)
