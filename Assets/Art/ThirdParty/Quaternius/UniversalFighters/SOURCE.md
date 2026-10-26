# Side A Quaternius Humanoid Fighters

Selected from the author's free **Standard** downloads on 13 October 2026.
All three source families are CC0; original license notices are included here.
No paid Source tier, other-engine project, environment pack, or demo scene was imported.

- Base characters: https://quaternius.itch.io/universal-base-characters
  - Superhero Male/Female FullBody FBX; only head/neck polygons plus eyes/brows are used.
- Clothes: https://quaternius.itch.io/modular-character-outfits-fantasy
  - Male/Female Ranger FBX. Their own skeletons/avatars drive the assembled fighters.
  - A shared ranger albedo is recolored by `HumanoidTeamLit.shader`: green cloth
    becomes blue/red, while leather/metal and independent skin retain source colors.
- Animations: https://quaternius.itch.io/universal-animation-library
  and https://quaternius.itch.io/universal-animation-library-2
  - 16 required free clips extracted into standalone Humanoid .anim assets.
  - Large source animation FBXs stay in the external task backup, not the game project.
  - Uppercut, low sweep, dash kick, recovery and beam hand placement use additive
    presentation IK; this does not replace combat hit queries or alter damage/timing.

The free body archive actually contains only the two Superhero bodies, not every
body shown in the full product gallery. The free UAL archives each contain 43
clips, not the full advertised 120+/130+ collection.

## Generated assets

`Generated/PlayerRanger.prefab` = blue male player.
`Generated/EnemyRanger.prefab` = red female computer opponent.

`BattleHumanoidSetupUtility` builds heads, materials, controllers and portraits,
and replaces **only** the visual children in `Assets/Scenes/BattleScene.unity`.
The original fighter Rigidbody, 2m capsule, tags/layers and J/K gameplay values remain.

Textures import at 1024 (cloth) / 512 (skin, eyes, brows); no 4K normal/ORM maps,
hair collection, dynamic-cloth solver, extra physics body or real-time light is added.

The original Bunny/Demon assets are retained as a recoverable fallback. The
migration backup and full downloaded archives are outside Assets and are not
included in a Unity build.
