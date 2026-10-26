# Side Game B

`Game/Scenes/SideGameB.unity` is the playable Side Game B entry scene imported from Project10.

This folder contains the curated, editable Project10 assets required by the integrated game, including both playable heroes, active enemies, textures, audio, animations, prefabs, scripts, input actions, and retained settings. The 2026-09-28 reuse pass reduced this folder to about 16.21 MB; unused samples, alternate art and authoring source media are archived outside Assets. Builds do not depend on that archive or an existing Library cache.

`Game/Scenes/SideGameB.unity` is the renamed playable scene. The original duplicate `PlayerTest.unity` was intentionally not copied because both files would share the same Unity GUID. Unused original sample scenes were subsequently archived.

In the integrated overworld, use the existing Side World B guide. `SideWorldTravel` loads B additively and suspends the original host; return and retry preserve that host instead of reloading build index 0. Completion is reported by B's victory trigger. Previously collected placeholder equipment remains collected.

The 26 original standalone scripts under `Game/Editor` are preserved behind the explicit `SIDE_GAME_B_AUTHORING` compilation symbol, disabled by default. They contain automatic scene installers with paths for the original Project10 and an automatic project-wide Brotli optimizer; neither is appropriate in this combined project. Use these only after adapting paths and restoring any required archived authoring assets in a dedicated project. Normal runtime scripts remain enabled. The later reuse pass resized selected static source images and replaced animated tutorial atlases with procedural keycap cards; character/enemy sprite atlases and gameplay parameters remain intact.

Use `ArtLab/Baseline/Build WebGL` for the combined overworld/A/B build. `Side Game B/Validate Integration` performs a scoped static additive-scene inspection; it does not claim a full gameplay test. See `Docs/SideGameBQuality.md` for the original integration and `Docs/AssetsReuse130/README.md` for current sizes, source changes, validation and manual test gaps.
