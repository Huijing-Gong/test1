# Main-world inventory

Installed on BaselineWorld in `Assets/_Demo/Scenes/OverworldBaseline.unity`.
The view reads GameSession, BaselineLoadout and BaselineFlow.Progress; it does not
seed test rewards or create a second save system. Equipment and recall actions
use the existing BaselineFlow methods and persistence.

## Controls and presentation

I opens/switches to Bag; U opens/switches to Character. Repeated I/U stays open.
Esc closes (first cancels an active recall confirmation); X also closes.
Equipment can only be changed on Character. Bag links to the corresponding slot.
Opening either page pauses gameplay, blocks combat/camera input and hides world HUD.
Closing restores gameplay without letting the same Escape open the Journey menu.
The old quick-gear component is disabled where InventoryLab is installed. Away-world
B return behavior is retained. The top-right J/M/Esc badges were removed.

Both pages share a 1440x860 frame in 1920x1080 reference coordinates, padding 40,
column gaps 32, side columns 280/400. The 6x4 bag grid scales to fit the middle column.
The panel starts at y=128, 24 below the launcher hit areas. HUD avoidance takes
precedence over vertical centering. Capacity is visual, not a new gameplay limit.
Launchers have no background or border: 80x80 hit areas, top margin 24, right margin
48, gap 32. Visible artwork fits 64x64, independently of source transparent padding.
Hover reveals the label and shortcut. The loading-screen reference informs the shared
palette: warm ivory #F7F2E1, forest-green text #183E35, muted green #5B7262 and
antique-gold accents #B49755. Layout and font sizes are retained.

## Character and ownership

The preview uses the P09 modular hero and BattleIdle from combat-sandbox commit
`4d564d44` (branch `codex/combat-sandbox-warden-link-time-20261024`). Its visual
prefab and dependencies are under `SandboxHero`, with source-relative paths retained.
The preview copy has a new prefab GUID and no combat controller; the source model,
materials, avatar and idle pose are preserved. Existing PracticeSword dependencies
are identical to the source commit and reused. This imports the character presentation
only, not the separate combat project's game logic, Boss or project settings.
An isolated camera frames the sampled pose; head and feet fit the preview bounds.
Sword maps to Main hand, guard to Off hand, relic to Relic, ridge charm to Charm,
Trial A core to Necklace, B sigil to Sigil. Necklace is a presentation slot for the
core so the world's independently equipped bonuses can coexist with the sword.
Only the sword has a wearable mesh; no other body attachments or effects are invented.
Recall beacons are the only persistent supplies. Healing shards and side-world
local counters are not added as new inventory items.

## Assets

Selected icons and PixelSplitter-Bold.ttf come from the user-supplied Dark Pixel UI
V1.0 package. Original package scripts, scenes and project settings were not imported.
Retain applicable third-party licenses for team distribution.
BagParcel.png and CharacterEquipment.png were generated using the user's parcel
and sword/banner references: transparent background, no lettering or frame, brown
cloth and muted gold/ivory palette. They are imported at 512px with point filtering.
HeroPortrait.png is the earlier generated featureless black-haired head, retained
as an alternate asset. All Unity metadata is included.

## Validation

Tools > Wayfarer Inventory provides behavior and world snapshot checks.
The Play Mode main-world check uses a separate validation save prefix. It checks
empty new-game inventory, repeated open shortcuts, pause/input/HUD locking, Esc
closure, reward synchronization, equip/unequip persistence across reload and rejected
recall use without consumption. Shortcut handlers are invoked directly by this test.
Play Mode screenshots were inspected for character framing and HUD spacing.
Complete manual mouse acceptance and side-world travel regression remain pending.
No standalone delivery zip has been created.
