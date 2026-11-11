# Unity project working instructions

- Preserve the user's maps, scenes, prefab edits, and unsaved Editor work. Never close, replace, save, or reload an open scene as part of automated checks without the user's instruction.
- Use the project's Unity version from `ProjectSettings/ProjectVersion.txt` and preserve existing package and project settings unless the task requires a change.
- Choose focused verification for the change: allow script compilation, and use relevant EditMode/PlayMode or isolated batch checks as needed. Do not produce a Windows player build for every change.
- Windows builds are appropriate for build configuration, native plugins, Steam runtime/overlay, release checks, or when requested. Explain what was actually verified; local Editor checks do not establish real Steam multiplayer behavior.
- `ShapeCoop.Editor.PrototypeVerification.Run` runs the gameplay checks only; `ShapeCoop.Editor.PrototypeVerification.BuildWindows` explicitly builds the player. Both entry points are batch-only and should run in an isolated project copy, not the developer's live project.
- Existing `*Verification` editor scripts may open an empty scene, change physics settings, enter Play mode, or exit the Editor. Do not call them in a live Editor session without first adapting them to preserve all user state. Separate project copies must not overwrite the user's maps or project assets.
- When Unity CLI or an MCP connection is available, use it only for operations compatible with the user's live Editor state; select the correct project and prefer non-destructive checks. Avoid concurrent Editor instances on the same project.
