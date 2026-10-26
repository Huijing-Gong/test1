# 主角动作更新版（2026-10-27）

本仓库包含当前完整 Unity 游戏工程：Assets、Packages、ProjectSettings 和相关项目说明。

使用 Unity **6000.3.22f1** 打开工程，启动场景为 `Assets/_Demo/Scenes/OverworldBaseline.unity`。

## 本次更改：主角动作

- 调整长剑攻击、攻击朝向和快速取剑，停下后将剑收至背后。
- 单击 Shift 进行方向闪避，WASD 控制方向；长按 Shift 奔跑。
- 调整完美闪避后的林克时间、连续攻击与攻击结束回退，并补充特效。
- 更换、校准主角死亡动作，修正悬空倒地。
- 配置抓墙沿、悬挂和翻上墙顶动作与触发条件；爬树使用普通攀爬动作。
- 统一矮台阶和较高台阶的行走表现，加入脚部贴地处理。
- 平滑上下台阶时的相机高度跟随，减少逐级颤抖。

## 工程说明

保留原游戏资源、场景和许可文件；Unity 缓存、临时录制和本机编辑器配置不纳入仓库，首次打开时 Unity 会重新导入资源。
上传版本移除了指向不存在本地目录的可选 Tripo 编辑器桥接依赖，避免其他电脑打开时发生包路径错误；游戏代码未引用该桥接。

原团队工程说明见 [TEAM_FOUNDATION_20261026.zh-CN.txt](TEAM_FOUNDATION_20261026.zh-CN.txt)。

---

# Lake Domain — Game Design Document

> 当前团队开发基座（2026-10-26）：Unity 6000.3.22f1，启动场景、最新按键、整合内容与待测项目见 [团队运行说明](TEAM_FOUNDATION_20261026.zh-CN.txt)。下方为2026-10-16版本的GDD，不能据此判断当前功能是否已经验收。

**Current implementation: 16 October 2026.** This revision describes the game delivered in school commit [`f34fef10`](https://github.com/feit-comp30019/2026s2-project-2-kingofgraphic/commit/f34fef10e19b2e48849e0c56df6a60a17046c168). *Folded Realm* was the earlier working title; the current title screen and promotional identity use **Lake Domain**. Implemented features, design intentions and remaining validation are distinguished below. The new images are real Unity captures; their capture conditions are recorded in the [media index](Docs/GDD/MEDIA_INDEX.md).

## Contents

1. [Game Overview](#1-game-overview)
2. [Story and Characters](#2-story-and-characters)
3. [Gameplay and Controls](#3-gameplay-and-controls)
4. [World and Level Design](#4-world-and-level-design)
5. [Progression and Combat](#5-progression-and-combat)
6. [Art and Audio](#6-art-and-audio)
7. [UI](#7-ui)
8. [Technology and Production](#8-technology-and-production)
9. [Evaluation Design and References](#9-evaluation-design-and-references)

## 1 Game Overview

### Game concept

**Lake Domain** is a short single-player action-adventure about a traveller drawn into a constructed world. The player explores a natural-fantasy landscape, learns traversal and combat, chooses optional preparation routes and decides when to face its Guardian.

**Central design question: How much preparation do I need before I am ready?**

The starter sword unlocks the final challenge. Neither Side World completion nor collecting every exploration reward is required. The main world acts as a compact introduction to a larger fictional network of worlds: distant floating landforms and framed vistas suggest possibilities beyond the playable map without promising additional playable regions.

![Current lake, island, shoreline houses and waterfall](Docs/GDD/Media/Current20261016/lake.jpg)

*Current Unity environment, captured with an authored promotional camera and HUD hidden. The island, shoreline houses, raised rear bank and waterfall form recognisable landmarks.*

### Design pillars and intended audience

- **Player-directed preparation:** landmarks and optional rewards support a direct or better-prepared Boss attempt.
- **Variety with clear boundaries:** 3D exploration, a platform fighter and a pixel-art parkour trial test different skills while sharing host-owned progress.
- **Readable action:** spacing, attack commitment, defensive timing and recovery openings matter alongside numerical upgrades.
- **Vertical discovery:** the low arrival route, central hub, low lake and high final courtyard establish a legible journey through a bounded map.
- **A constructed world with character:** a consistent architectural vocabulary contains deliberate virtual-world anomalies, changing skies and glimpses of other places.

The intended audience is desktop keyboard-and-mouse players with mixed action-game experience. The **ten-minute checkpoint** measures what first-time players discover and understand; it is not a completion deadline or a measured full-playthrough duration. The fun and learnability claims remain hypotheses for participant evaluation.

### Reference works and boundaries

| Reference | Design influence | Scope boundary |
|---|---|---|
| Sword Art Online | Virtual-world premise, desire to return home and a Guardian destination | Original story and characters; no real-world death rule, MMO or hundred-floor campaign |
| Ready Player One | A constructed world containing contrasting experiences and clues to a wider network | No licensed characters, copied worlds or promised multiplayer service |
| The Legend of Zelda: Breath of the Wild | Landmarks, exploration and a choice of preparation | One compact world; no comparable scale or full simulation system |
| Genshin Impact | Coordinated fantasy environments, contextual HUD, objective hierarchy and dialogue flow | No elemental party combat, gacha or reused protected assets |
| Super Smash Bros. | Trial A's damage percentage, knockback, recovery and ring-outs | A single-player trial, not a roster or multiplayer fighting service |
| Super Mario | Trial B's readable platforms, hazards and jump rhythm | Original course and local combat; no copied levels or characters |
| Souls-style action games | Boss commitment, defensive timing and recovery openings | Recoverable failure and optional preparation; no commitment to the references' full progression systems |

### Current scope and status

| System | Implemented baseline | Remaining validation or design work |
|---|---|---|
| Opening | Cloud Gate title, Continue/New Journey, settings, guide and starter sword | First-time comprehension and browser recovery |
| Main World | One layered environment, four broad districts reviewed as sixteen subregions, clues, chests and six enemy archetypes | Remaining collision, contact and route quality checks |
| Hero | Current black-clothed hero in exploration and the final arena; shared action library and combat logic | Animation naturalness, contact fidelity and full native-input acceptance |
| Trials | A: two selectable stages and two stocks; B: Easy/Normal/Hard maps, character choice and difficulty rewards | Complete current-build natural playthroughs |
| Final encounter | Humanoid Eclipse Warden, two phases, Black Sun ultimate, dialogue, retry and return | Full encounter readability, balance and performance acceptance |
| Navigation/UI | Book-style journal/map, paused menus, clickable destinations, golden objective guidance and contextual HUD | Player comprehension and resolution coverage |
| Persistence | Versioned journey save/recovery copy; quest, reward, trial, eclipse-stage and Boss flags | Cold browser reopen and storage-failure regression on the final hosted build |
| Narrative | Two-stage eclipse presentation and recorded Boss completion | The Return Home/Become the Guardian ending choice remains a design goal |

There is no commitment to a third trial, random loot, crafting, an economy, multiplayer, diving, underwater combat, cloud saves or multiple player save slots. **120 hero actions are mapped and have controlled observation coverage; this is not certification that all 120 gameplay uses are fully accepted.**

## 2 Story and Characters

### Setting and roles

Approximately 150 years in the future, brain-computer interfaces allow people to enter virtual environments called Domains. This Domain unexpectedly summons the protagonist. A guide teaches the immediate practical rules and gives a starter sword; the Guardian's larger purpose remains concealed.

![The current compass hub, guide and surrounding landscape](Docs/GDD/Media/Current20261016/hub.jpg)

*The compass plaza connects the opening route with exploration. This is a controlled Unity environment capture, with the interface hidden.*

The original narrative reveals that the Guardian protects a weakening boundary between civilisations and seeks a successor. Exploration and the trials express different forms of capability and judgement. Ordinary defeat preserves permanent progress; it does not enact a fictional real-world death or erase the player's journey.

| Character | Narrative/gameplay role | Current presentation |
|---|---|---|
| Protagonist | Learns the Domain and chooses how much to prepare | Black-clothed humanoid hero shared by exploration and the final arena; the former red mannequin is superseded |
| Guide and trial guides | Teach interaction, grant the sword and introduce optional activities | Existing dialogue, entry confirmation and reward flows |
| Eclipse Warden / Guardian | Final examiner and intended protector of the boundary | White-haired humanoid with contrasting dark-red and ivory armour; the old mech presentation is superseded |

### Narrative and gameplay flow

1. Enter from the title screen, arrive at the low entrance and meet the guide.
2. Receive the Practice Sword and choose exploration, either trial or a direct final attempt.
3. Complete optional discoveries and claim rewards; return to the same host world.
4. The first completed trial introduces twilight, a partial eclipse and gradually lit lamps. Completion of the other trial advances the sky to the final dark-red state, with enemy and tower camera shots.
5. Enter the separate final arena, complete its introduction and face the Eclipse Warden.
6. Retry, return to prepare further, or record Boss completion after victory.

The eclipse sequences mark optional trial progress; they **do not make both trials compulsory for Boss access**. Their presentation stage is saved so Continue does not repeatedly play completed sequences. Cinematic input ownership is released on completion or skipping.

![The integrated eclipse sky sequence](Docs/GDD/Media/Current20261016/eclipse-cg.jpg)

*Current in-engine cinematic capture. The sky and lighting changes are implemented presentation, not a concept illustration.*

![Current hero and Eclipse Warden during the arena introduction](Docs/GDD/Media/Current20261016/boss-introduction.jpg)

*Controlled GameView capture through the host-to-arena flow. The Guardian conversation precedes engagement.*

### Intended final choice

The intended **Return Home** ending reunites the protagonist with family while leaving uncertainty at the boundary. **Become the Guardian** accepts responsibility at the cost of ordinary life. Neither is labelled morally correct. Concise dialogue and illustrated/text panels are sufficient; two additional levels are outside scope. The current save records Boss completion, **not a selected ending**, so these outcomes remain narrative work rather than completed features.

## 3 Gameplay and Controls

### Player loop

**Orient → Explore or Enter a Trial → Learn and Earn → Return and Prepare → Attempt the Boss → Retry or Complete.**

The Main World and Boss arena share hero action timing, attack/charge input and blade-sweep logic through adapters. They retain context-specific movement and resource handling. A and B retain their native local characters and rules.

### Main-world controls

| Input | Current action |
|---|---|
| WASD / arrow keys | Camera-relative movement; directional movement on eligible climbs |
| Mouse | Look/orbit while the cursor is captured |
| Shift | Sprint; uses existing stamina |
| Space | Jump or detach from a climb |
| Left Ctrl | Dodge |
| C | Crouch |
| Q | Guard |
| Left mouse | Immediate first sword attack; combo input; hold to request charge, release for heavy/charged attack |
| Right mouse | Acquire/release target lock; does not itself draw the sword |
| R | Smooth camera recenter |
| H | Use field aid |
| E | Dialogue, clues, runes, chests and nearby eligible traversal/seat interactions |
| W / S after attaching | Move up/down an eligible ladder, rope or pole; Space detaches |
| J / M | Task journal / map; both pause the world |
| Esc | Close the active panel/conversation or access the journey menu as permitted |

Ordinary movement, equipment changes and Continue do not automatically draw the sword. An accepted left-mouse attack draws it as part of the attack, without a separate long equip delay; it stows after the combat action and idle timeout. Holding an attack records charge intent after the initial input threshold and enters charge at the allowed action handoff. Dodge, damage, death or travel can cancel the charge.

Ground movement, jumping, shallow water, swimming and wall/ledge traversal are implemented. The expanded action system supports lateral/vertical climbing and authored ladder, rope, pole, step, vault, scramble, mantle and seat/rest interactions. Those facilities require valid contact, exit space and geometry; source coverage alone does not prove every approach works. Diving and underwater combat remain outside scope.

### Side World A — Platform Fighter

Trial A is a single-player 2.5D platform fighter: 3D characters move on a side-facing plane. Accumulated damage percentage increases knockback; the objective is to ring out the opponent, not empty a conventional health bar. A shared dynamic camera frames both fighters.

Players select **Sunlit Ruins (Beginner)** or **Twilight Ruins (Intermediate)**. The stages use the same compact platform arrangement with different presentation. Each fighter starts with **two stocks**: the first ring-out respawns them, the second eliminates them. The second stage supports practice and does not provide a second permanent host reward.

![Trial A second stage with its current fighters and platforms](Docs/GDD/Media/Current20261016/side-a-stage2.jpg)

*Twilight Ruins, recorded in Unity with injected gameplay input and HUD hidden for the promotional capture. This shows the current stage, not a complete match acceptance result.*

| Input | Current action |
|---|---|
| A / D; Shift | Move; walk modifier |
| Space | Jump / double jump |
| S + Space | Drop through a platform; S alone lets go while ledge-hanging |
| Left mouse / J | Normal attack/combo; A/D modifies to side strike, W to uppercut, S to sweep |
| Right mouse / K | Secondary action; hold/release beam, A/D dash kick, W air leap, S grounded frontal counter |
| L | Timed perfect guard |
| H | Help/pause panel |
| R / B | Retry / return to Main World |

The opponent pursues, attacks, reacts, repositions and recovers to platforms. Winning the reward-bearing first stage records Trial A completion; the host's blade-core reward is claimed once. Practice and retry do not duplicate that reward.

### Side World B — Side-Scrolling Parkour

Trial B combines platform movement, moving surfaces, melee/ranged enemies, saws and other hazards, diamonds, health pickups and an environmental puzzle. Players select **Easy, Normal or Hard**, then choose the Princess's ranged magic or the Fighter's close-range attacks and higher health. Local ability upgrades, including the triple-jump chest, remain in Trial B.

![Current Side B platform route with saws, diamonds and a health pickup](Docs/GDD/Media/Current20261016/side-b.jpg)

*Controlled Unity capture with injected input and HUD hidden. The image demonstrates a route and hazards, not completion of the difficulty.*

| Input | Current action |
|---|---|
| A / D or left/right arrows | Horizontal movement |
| Space / W / up arrow | Jump; additional jumps depend on the local ability state |
| Shift | Speed-up / air dash according to movement state |
| Left mouse | Attack |
| F | Interact with relevant props |
| B | Return to the Main World |

The final portal records completion and presents victory options. Easy can lead to Normal/Hard and Normal can lead to Hard, or the player can return. The highest completed difficulty grants **+10 / +20 / +30 maximum health and stamina** in the Main World; these bonuses do not add together. The existing claimable B gate sigil is a separate equipment reward. Failure, respawn and local collectibles follow the trial's rules without replacing host inventory.

### Eclipse arena controls

| Input | Current action |
|---|---|
| Enter | Engage from ready / retry after a result |
| WASD / mouse / Shift | Move / look / sprint |
| Space or Left Ctrl | Dodge |
| Q held | Guard; correctly timed defence can create a counter opportunity |
| Left mouse | Sword combo, hold-charge/release attack or an available counterattack |
| Right mouse | Target lock / unlock |
| R | Recenter the camera |
| Esc | Release the cursor |
| B | Return through the host bridge |

The meaningful movement difference is **Space jumps in exploration but dodges in the arena**. Combat does not gain rewards merely by playing a defensive animation: a qualifying attack/defence event is required.

## 4 World and Level Design

### Layered overworld

The map is organised into four broad districts, each reviewed as four smaller regions during environment development. These sixteen subregions are a production/inspection division, not sixteen separate loading scenes.

| District | Four constituent spaces | Design role |
|---|---|---|
| Arrival and teaching hub | Low entrance, stepped garden, side-house frontage, compass hub | Orientation, guide, sword, introductory interactions and choice of route |
| Lake settlement | Shore entrance, houses/decks, island/boat, shallow-water edge | Water traversal, optional discoveries and readable shoreline landmarks |
| Rear banks and gardens | Western rock face, waterfall, raised rear-bank route, eastern terraces | Vertical alternatives, lake views, enemy encounters and exploration |
| Final ascent | Lower steps, middle rock terraces, upper courtyard, pavilion/vista frames | Visible progression toward the optional final entrance |

The **arrival is the lowest entrance, the hub occupies a middle level, the lake lies below the hub, the rear bank rises above the lake, and the final courtyard is the highest destination**. Elevation should explain progression while preserving usable routes.

![Arrival stairs and planted terraces](Docs/GDD/Media/Current20261016/arrival.jpg)

*Current environment capture. Repeated paving, handrails and planting frame the uphill entrance.*

![Upper eastern terraces leading to the summit](Docs/GDD/Media/Current20261016/upper-route.jpg)

*Current environment capture. Landings and repeated blue/gold landmarks connect the ascent.*

### Navigation and optional routes

The book map displays the player, camp, trial entrances, rest/return facilities, tracked tasks and final destination. Selecting a point and choosing **Guide here** establishes a waypoint; right-click clears it. Distance and relative height support orientation. A waypoint is **direction guidance, not automatic pathfinding**. Golden wisps guide tracked objectives, while hidden chest placement preserves discovery.

![Current book-style region map and destination information](Docs/GDD/Media/Current20261016/map.jpg)

*Controlled GameView capture of the current map presentation. The destination panel explicitly describes direction-only guidance.*

Water depth, entry/exit slopes, climbable faces, mantle space and camera clearance must agree with their visual cues. Route safety is evaluated separately from attractive screenshots. Remaining contact and traversal failures must be resolved through actual route checks, not by teleporting the player for a photograph.

### Final courtyard and separate arena

![Summit pavilion and three framed vistas beneath the changed sky](Docs/GDD/Media/Current20261016/summit.jpg)

*Current environment capture in the final eclipse state. The frames suggest other worlds; they are not three additional playable worlds.*

The pavilion leads into a **separate 34 × 34 metre combat court**. The host suspends its own input/camera, retains journey progress and restores a valid Main World position on return. Legacy source names still include `SAM_BossArena`; the visible encounter is now the **Eclipse Warden**.

The court uses dark stone, ivory arches, gold accents, distant suspended ruins, clouds and a black celestial disc. The central floor stays open for blade combat and camera clearance. The square corners provide escape space for the central Black Sun attack. The change from a bright garden to an exposed, dark court establishes the final encounter's pressure without expanding the playable overworld.

## 5 Progression and Combat

### Rewards and persistence

| Reward | Acquisition | Current effect |
|---|---|---|
| Practice Sword | Opening guide conversation | Enables sword combat and access to the final encounter |
| Island relic | Hidden island chest | Equipped sword-damage bonus |
| Ridge charm | Western hidden chest | Reduces Main World sprint stamina drain by 30% while equipped |
| Runic guard | Sealed eastern chest | Equipped sword-damage bonus; its name does not imply armour reduction |
| Trial A blade core | Win the first stage and claim through the result menu or Main World | Equipped sword-damage bonus; one permanent grant |
| B gate sigil (placeholder name) | Complete Trial B, return and claim | Existing configurable equipped sword-damage bonus |
| Trial B difficulty bonus | Complete Easy / Normal / Hard | Main World maximum health/stamina +10 / +20 / +30; retain the highest tier only |
| Recall beacon | Claim an eligible clue-task reward | Consumable return-to-camp resource with saved receipt/count |

Damage tuning remains configurable. Trial-local health, percentage damage and movement upgrades remain independent. Original proposals for four fixed equipment slots, dedicated armour/healing upgrades and dodge-recovery boots are not all current implemented effects.

The three clue/discovery tasks record accepted, visited and completed state. The eastern chest uses a three-input rune sequence: correct activation advances it, incorrect activation restarts it and completion releases the seal. Permanent reward identities and receipts prevent replay/reload duplication. A failed save must not be presented as a confirmed durable grant.

### Shared hero combat

Exploration and the arena use the same hero action definitions for light combos, heavy/charged attacks, guards and counter variants. An action clock coordinates animation, hit windows, recovery and transitions. Blade root/tip sweeps drive physical hit checks and per-swing target deduplication; an effect or animation alone does not independently award damage.

![Current hero attacking the Eclipse Warden with the updated combat HUD](Docs/GDD/Media/Current20261016/hero-combat.jpg)

*Controlled runtime attack capture. It shows the current character and HUD; it does not certify a complete natural-input encounter.*

Defence includes ordinary dodging, held guard, timely defence and a brief eligible counter opportunity. Main-world guard applies to eligible frontal damage. Precise defence gives an advantage while movement and ordinary defence remain useful. The player's sword stays stowed during ordinary exploration and appears on an accepted attack.

### Ordinary enemies

| Archetype | Intended local pressure |
|---|---|
| Stone Scout | Close pursuit, readable melee preparation and recovery |
| Leaf Wisp | Ranged aiming/shot warnings and repositioning |
| Ruin Prism | Telegraphing area pressure and rewarding repositioning |
| Repair Wisp | Supporting nearby enemies, encouraging target priority |
| Rail Beetle | Wind-up and committed charge, with recovery after a miss/obstacle |
| Gel Blob | Hopping pursuit, a marked landing and committed leap/landing attack |

The Training Dummy is a practice target, not an additional active archetype. Enemy movement should remain grounded, respect obstacles and stop under the appropriate pause/transition ownership. The proposed additional yellow attack-flash system is not part of this revision's accepted presentation.

### Eclipse Warden and Black Sun

The active humanoid Boss motor uses two phases. Phase one mixes jab, stomp, a two-hit sequence and dash punch. At half health a transition is queued; phase two selects the longer combo, dive kick, Black Sun/Meteor, dash punch and stomp. These replace the old mech-specific presentation while retaining the arena's progress and return integration.

The signature **Black Sun** raises the Boss approximately 8 metres during a roughly six-second charge. The foreground orb grows slowly beyond the apparent size of the background disc; that background disc fades while the camera pulls back toward the arena backdrop. The attack is thrown toward the **fixed court centre**, with a 19-metre damage radius that leaves the four square corners as escape areas. Valid ground and an active encounter are required; the expanding hit uses one damage application per target rather than stacking a separate visual explosion.

![Airborne Eclipse Warden beneath the charged Black Sun](Docs/GDD/Media/Current20261016/black-sun.jpg)

*Actual Unity effect sampled during controlled promotional recording, with HUD hidden and cinematic timing. The screenshot is presentation evidence, not a measure of gameplay frame rate or reaction time.*

The intent is to turn the long charge into a readable positioning decision, then leave an opening after impact. Pause, cancellation, death, retry and return must cleanly release its effect and camera state. Controlled centre/edge/corner and lifecycle checks exist; full natural play, balance and performance remain separate acceptance tasks.

## 6 Art and Audio

### Art direction

The Main World combines pale faceted rock, warm paving, blue-and-gold compass forms, orange-roofed lake houses and planted terraces. The folded-space direction uses simple geometric silhouettes, clouds and framed vistas to suggest scale beyond the bounded map. The Eclipse court deliberately darkens that vocabulary through black stone, ivory structures and gold light, matching the current humanoid Boss.

The new hero and generated environment assets are integrated, but animation naturalness, material consistency and some intersections remain refinement work. The project retains third-party dependencies, especially within the trials; it is not described as entirely original or fully licence-audited. Sources and remaining provenance work are recorded in the [asset register](Docs/GDD/GAME_ASSET_REGISTER.md).

### Audio and presentation

Game audio supports footsteps, traversal, combat preparation, impact, defence, damage and rewards. Trials retain local audio; the host must restore the appropriate listener and ambience when returning. Clip presence does not establish complete mix quality or warning audibility on every platform.

Promotional music selection is separate from the in-game soundtrack. Candidate external music and effects are not described as integrated merely because they were researched. The existing silent promotional recordings use real game scenes and the two-stage eclipse CG; their edited camera transitions and Logo are not gameplay features.

## 7 UI

### Title and exploration

![Current Cloud Gate title screen](Docs/GDD/Media/Current20261016/title.jpg)

*Unity GameView title capture from 15 October; this Cloud Gate design remains in the current build. Continue/start, New Journey, settings and credits lead into play without spawning directly into the map.*

The title distinguishes existing progress from a new journey and confirms replacement before clearing it. Settings include volume and reduced motion; desktop builds also expose exit confirmation. A corrupt or unreadable save must not silently become a new game.

Exploration places the minimap and tracked objective at upper left, J/M/menu access at upper right, health near bottom centre and a contextual stamina arc near the character. The combat HUD carries the same visual language into the arena, adding a slim top Boss-health bar and concise lower-right controls. Dialogue and rewards temporarily take input ownership.

### Book journal and map

![Current two-page task journal](Docs/GDD/Media/Current20261016/journal.jpg)

*Controlled GameView capture. The journal separates the selected task, immediate action, supporting notes and tracking control; the footer explicitly states PAUSED.*

Both **J and M pause the world**. Moving between journal and map transfers pause ownership without briefly resuming play. The journal prioritises the next action and completion/claim status; the map prioritises the selected destination, distance and elevation. The journey book retains equipment, controls, explicit save and session actions.

| Mode/state | Essential information |
|---|---|
| Exploration | Health, contextual stamina, field aid, interaction and tracked destination |
| Conversation/reward | Speaker or item, actual effect, advance/cancel and clear confirmation |
| Trial A | Both percentages/stocks, stage/result, help, retry and return |
| Trial B | Local health, gems, abilities, difficulty/hero choices and victory options |
| Eclipse encounter | Boss/player health, lock state, defence/counter and Black Sun warning |
| Death/save problem | Recovery or actionable storage notice without falsely confirming progress |

Controls must match the active mode, particularly Space, H/R and trial attack mappings. Only the active mode should own gameplay input, camera, audio and HUD. Clear text, silhouette and timing should complement colour; prompts and panels must not obscure escape space.

## 8 Technology and Production

### Technical baseline

| Area | Current decision |
|---|---|
| Engine | Unity **6000.3.22f1**, matching `ProjectSettings/ProjectVersion.txt` |
| Target | Desktop-browser WebGL; Windows player used for the current recording/build checks |
| Startup | `Assets/_Demo/Scenes/OverworldBaseline.unity` |
| Build scenes | Eight enabled entries: host, StartScene, BattleScene, original Side B scene, Eclipse arena, and Side B Easy/Normal/Hard |
| Mode architecture | Host-owned progress; shared hero action/combat core for Main World and Eclipse, with separate mode adapters; native trial controllers |
| Travel | Save before entry, suspend the host, load the selected scene and restore host state on return |
| Input/camera/audio | One intended active gameplay context per mode |
| Progress | One local versioned journey save with a recovery copy |
| Performance goal | At least 30 FPS on a declared representative laptop/browser; measured evidence still required |
| Asset size | School game snapshot: **242,999,600 bytes (243.00 MB)** in Assets; local working Assets: 244,538,560 bytes. GDD images are outside Assets. These figures are not download size or runtime memory |

| Tool | Use |
|---|---|
| Unity | Scenes, scripting, assets and WebGL builds |
| GitHub | Source control, integration and the submitted GDD/report |
| Jira | Tasks, owners, dependencies and deadlines |
| Confluence | Joint design discussion, meeting notes and supporting evaluation materials |
| Figma / image tools | Interface planning and visual asset production |
| Google Forms / shared spreadsheet | Planned questionnaire collection, observation records and coordinated analysis |

### Build and validation status — 16 October

The compressed source snapshot produced a non-Development Windows build with **zero errors and sixteen existing warnings**. Controlled runtime checks cover both eclipse stages, save-store round trips, Continue without replaying completed CG, tower tracking and host/Boss return. Current screenshots document visible presentation and selected interactions.

These checks do not establish a new WebGL build, a fresh portable-clone runtime, full natural keyboard/mouse completion of every mode, or measured performance on representative hardware. Hero contact/animation fidelity and the full 120-action interruption/route matrix remain incomplete. Earlier hosted WebGL screenshots are retained as history, not relabelled as the current release.

### Save and recovery contract

`BaselineFlow` coordinates automatic save calls, including periodic saving and important progress changes, plus an explicit save action. `BaselineSaveStore` performs versioned local storage and recovery-copy handling. Saved data includes:

- opening completion, guide progress and sword ownership;
- task acceptance, discovery, completion and tracking;
- opened chests, rune progress and solved-seal state;
- both trial completions, reward-claim flags and the highest Side B difficulty bonus;
- task reward receipts, consumed recall beacons and equipped/unequipped states;
- a valid Main World recovery position and map version;
- eclipse presentation stage and Boss completion.

Transient attacks, input locks, effects, trial/arena coordinates and a mid-fight Boss phase are not durable journey progress. A restored journey uses a safe Main World standing point rather than resuming a half-restored encounter. Health, stamina and field-aid supplies are restored through the host recovery flow; the Eclipse encounter retains arena-specific combat resources.

Starting a new journey requires confirmation before replacing existing local progress. Save problems must be reported rather than displayed as confirmed success. Local browser storage is not a cloud backup and can be lost when site data is cleared. The full narrative ending choice is not currently part of the saved schema.

### Joint design and team collaboration

**Zixin Xu, Cen Cheuk Pan, Siyan Liu and Huijing Gong develop the methods and game design through joint discussion.** Ideas, alternatives and integration decisions are reviewed together. Implementation tasks can be divided according to the system, dependencies and available workload; that does not imply that each person designed only one isolated feature.

The shared work covers the overall player loop, Main World, both trials, combat, interactions, rewards, progression, presentation and evaluation. Individual contribution records should describe actual proposals, implementation, integration, review and fixes, supported by commits and task records. A planned coordinating role is not a substitute for evidence of completed work.

All four members will participate in preparation, recruitment, observation, permitted assistance, post-task questions and joint evidence review. Testing is expected to use **up to four computers in parallel**, with an identified observer and separate record at each station. The team will compare observations after each batch before dividing the subsequent processing work.

| Member | Planned follow-up coordination after shared testing |
|---|---|
| **Zixin Xu** | Combine evidence from all methods; review it with the team; make the final decision on which issues warrant modification or deferral and their priority; record the rationale and coordinate the improvement list |
| **Cen Cheuk Pan** | Organise observation records and summarise task progress, timing, hesitation, errors, retries and assistance; help verify gameplay issues |
| **Siyan Liu** | Organise questionnaire responses, check missing/duplicate/not-applicable answers and summarise ratings, difficulty and open feedback |
| **Huijing Gong** | Organise walkthrough/interview notes and consented recordings; code qualitative responses and retain examples/explanations |

These roles coordinate later work; they do not restrict anyone to one method or claim completed testing/analysis. All members will contribute to analysis, design review, implementation and follow-up checks and understand the full evaluation plan. The shared spreadsheet will record actual tasks and workload so support can be redistributed where needed.

### Communication and integration

Group chat handles everyday coordination; Jira tracks tasks; GitHub records source changes; Confluence retains supporting discussion. This README remains the submitted GDD, and REPORT.md contains the evaluation plan. Formal course documentation is in English.

- Read the latest shared baseline before cross-system work and coordinate changes affecting another contributor's implementation.
- Keep branches/commits scoped and record dependencies between gameplay, scene flow, UI and progress.
- Check affected Unity scenes and mode transitions before merging functional changes; successful compilation alone does not establish correct behaviour.
- Preserve each trial's local rules while connecting completion, return and reward flow.
- Keep experimental features away from the stable evaluation build and identify the exact build used by participants.
- Record external sources and licence evidence when importing assets.

### Current production and evaluation schedule

Dates below are in **October 2026, Melbourne time**. The evaluation dates remain the team's planning schedule, not claims that recruitment, sessions or analysis have already finished.

| Date | Work |
|---|---|
| 9 October | Milestone 1: current GDD, evaluation plan and functional WebGL demonstration ready by 11:59 pm |
| 9–10 October | Finalise the session materials, observation sheet, questionnaire/interview guide and evaluation build |
| 10–11 October | Pilot the procedure and check instructions, timing and recording consistency |
| 12–18 October | Planned observation/interview and distinct questionnaire groups using the same identified evaluation build |
| 16 October | Milestone 3 gameplay video and current game/GDD delivery; teammates finalise the YouTube link and course submission |
| 19–20 October | Organise records, clean responses and code qualitative evidence |
| 21–22 October | Combine findings, discuss design implications and prioritise improvements |
| From 23 October | Implement agreed changes, rebuild and conduct follow-up checks |

### Custom shader integration

The earlier shader work retains its individual attribution:

| Contributor | Shader theme |
|---|---|
| Zixin Xu | Digital Chest Disintegration |
| Cen Cheuk Pan | Reward Sword Hologram |
| Huijing Gong | Domain Portal / Virtual Seam |
| Siyan Liu | Pixel Barrier |

The original source discussion, parameter comparisons and captures are retained in the [earlier shader report](https://github.com/feit-comp30019/2026s2-project-2-kingofgraphic/blob/fded5d27543532549b245d17fb8c02af2f76d171/REPORT.md). That historical report does not establish current scene behaviour. A chest dissolve can communicate collection, a portal can signal a transition and a barrier can define a combat boundary, but each effect's gameplay connection must be checked in the active scene rather than inferred from a showcase capture. Current Project 2 shader reporting belongs in REPORT.md's designated section when prepared.

## 9 Evaluation Design and References

### Questions the design needs to answer

The evaluation will examine tutorial understanding, first-NPC arrival and first-task completion; discovery of optional content; each trial's controls and difficulty; ten-minute content coverage; confusion and assistance; feedback; final-encounter readability; and the meaning of preparation rewards and return flow.

The plan uses structured observation followed by a walkthrough/interview, plus a separate post-play questionnaire group. Each group targets about ten participants and requires at least five, with **at least ten different participants overall**. Questionnaire participants will not duplicate the on-site group. These are recruitment targets, not completed sample counts.

The same build, starting conditions, instructions, recording rules and ten-minute checkpoint will support comparison across parallel stations. The detailed tasks, assistance thresholds, questions, measures, analysis approach and consent procedure are in [REPORT.md — Evaluation Plan](REPORT.md#evaluation-plan).

### Evidence-to-change decisions

**Problem → Evidence → Design Decision → Improvement → Result.**

The team will compare what participants did with what they said they understood or expected. Frequency, location and impact will support the choice to modify or defer a feature. Zixin will combine the evidence, review it with the team and make the final priority decision; all members will contribute their findings and help implement/check agreed improvements.

| Priority | Planned meaning |
|---|---|
| Critical | Prevents progress through the tutorial, a trial, a transition or the final encounter |
| High | Affects several players or seriously harms the main experience |
| Medium | Recurs, but players can continue |
| Low | Minor problem or individual preference |

This GDD does not include evaluation results. The team's demonstration and initial testing on 9 October provide context for preparing the next round; participant counts, ratings, conclusions and completed improvements belong in the later evaluation report after records are organised and analysed.

### Release criteria and remaining concerns

| Area | Required behaviour / remaining check |
|---|---|
| Learnability | Actual controls, objectives and the first task can be understood without advance route explanations |
| Traversal | Water entry/exit, stamina, eligible climbs, detachment, mantle and camera clearance remain usable |
| Mode flow | A/B/Eclipse entry, retry and return restore the correct player, camera, audio, cursor and host progress |
| Rewards | Actual effects are described accurately; permanent rewards do not duplicate on revisit/replay/reload |
| Combat | Ordinary defensive play is viable and telegraphs/recovery openings remain readable |
| Persistence | Browser reopen and storage errors preserve a valid distinction between session progress and confirmed saved progress |
| Presentation | Menus, health, dialogue, rewards and Boss cues remain readable without obscuring hazards |
| Distribution | The chosen hosted build loads and operates on declared representative browser/hardware; performance is measured separately |

Current risks include mode-specific controls confusing first-time players, unclear optional routes or reward claims, placeholder reward effects, visual/audio differences between modes, save/return inconsistencies and browser performance. Revisions should follow evidence and production capacity rather than automatically expanding the game.

### Sources, media and documentation assistance

- [Current evaluation plan](REPORT.md#evaluation-plan)
- [School Project 2 specification](https://github.com/feit-comp30019/project-2-specification)
- [GDD media index and provenance](Docs/GDD/MEDIA_INDEX.md) — identifies current Unity captures, controlled recording conditions and historical media.
- [Game asset register](Docs/GDD/GAME_ASSET_REGISTER.md) — retain and extend source/licence records as assets are added.
- Current implementation references: [build settings](ProjectSettings/EditorBuildSettings.asset), [host flow](Assets/_Demo/Scripts/Baseline/BaselineFlow.cs), [loadout](Assets/_Demo/Scripts/Baseline/BaselineLoadout.cs), [save schema](Assets/_Demo/Scripts/Baseline/BaselineSaveData.cs), [trial travel](Assets/_Demo/Scripts/Interaction/SideWorldTravel.cs), [SAM bridge](Assets/_Demo/Scripts/Baseline/BaselineSamBridge.cs), [arena controls](Assets/SAM/BossArena/Runtime/ArenaSession.cs), [active Eclipse Boss motor](Assets/SAM/BossArena/Runtime/ArenaEclipseBossMotor.cs), [shared hero combat](Assets/OwnHero/Runtime/HeroCombatShared.cs) and [eclipse sequence](Assets/_Demo/Scripts/Baseline/BaselineEclipseSequence.cs).

OpenAI ChatGPT / Codex assisted with English-language editing, documentation consolidation and checking this GDD against the repository's scenes/scripts and the team's existing evaluation plan. Earlier concept-board, integration and implementation assistance disclosures remain part of the existing project provenance. This update does not supply participant observations or analysis and does not change individual shader attribution.
