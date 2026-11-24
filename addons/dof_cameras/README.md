# Cameras (`dof_cameras`)

Camera director that blends between rigs: RTS strategy view plus first-person, chase and orbit follow views with spring-arm collision.

**Depends on:** dof_core

Add a `CameraDirector` node. Call `ShowStrategy()` or `Follow(subject, viewIndex)`, where `subject` is anything implementing `ICameraSubject` (a body to follow plus look angles). Edit `FollowViews` to add, remove or reorder views; subclass `FollowRig` or implement `ICameraRig` for new ones. No dependency on units: works for any game.
