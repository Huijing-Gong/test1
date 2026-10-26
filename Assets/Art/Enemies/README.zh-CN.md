# 六种小怪交付与接入说明

基于课程仓库 `main` 的 `1b10e590308562daf993671e49c58d4d20751c5a` 合入。
保留此版本的主世界、玩家挥剑、HUD、菜单、存档和 Boss 工作；仅增加小怪依赖、碰撞约束的玩家受撞接口以及地图类型颜色。

## 查看与摆放

打开 `Assets/_Demo/Scenes/OverworldBaseline.unity`，从现有流程进入游戏。

| 类型 | Prefab | 场景分组 | 地图点颜色 |
|---|---|---|---|
| 石甲怪 | `Assets/_Demo/Prefabs/Combat/StoneScoutEnemy.prefab` | `_EnemyCombatPreview` | 绿色（所有外观变体相同） |
| 远程悬浮怪 Leaf Wisp | `Assets/Art/Enemies/DomainPrototypes/Generated/LeafWisp.prefab` | `_DomainPrototypeEncounters` | 青色 |
| Ruin Prism | `Assets/Art/Enemies/DomainPrototypes/Generated/RuinPrism.prefab` | `_DomainPrototypeEncounters` | 粉紫色 |
| 冲锋兽 Rail Beetle | `Assets/Art/Enemies/RailBeetle/RailBeetle.prefab` | `_RailBeetleEncounters` | 橙色 |
| 维修萤 Repair Wisp | `Assets/Art/Enemies/RepairWisp/Generated/RepairWisp.prefab` | `_RepairWispEncounters` | 黄绿色 |
| 弹跳凝胶体 Gel Blob | `Assets/Art/Enemies/GelBlob/Generated/GelBlob.prefab` | `_GelBlobEncounters` | 蓝色 |

悬浮远程怪已补充局部剑击亮点／定向碎屑、弹道后坐、有限后撤与落地停机表现，原 Prefab 引用不变。数值、预算和真实 Play 复测见 [Leaf Wisp 说明](DomainPrototypes/README.LeafWisp.zh-CN.md)。

凝胶体是新增的独立路边敌人，先放三只，不加入维修萤绑定组。详细数值、坐标、地形规则与专项测试见 [凝胶体说明](GelBlob/README.zh-CN.md)。原 `Install Five Enemy Integration` 安装器仍处理原五类；新类型使用 `Build And Place Gel Blobs`，不要误认为旧安装器会自动放入凝胶体。

维修萤各绑定一只西侧隐藏宝箱／塔前 Ruin Prism；不能复活，成功治疗最多三次，治疗时停下并显示黄绿色连接线与局部维修扫描。扫描不是无敌护盾或伤害减免。新增维修萤需要在 `RepairWispGroup.Guards` 中绑定同组守卫。

## 保留的关键交互

- 空挥剑不自动打断小怪；真正命中才触发其受击规则。
- Ruin Prism 的地面波在玩家实际离地、游泳、攀爬或翻越时不伤害玩家；波前判定在玩家移动更新后执行。
- 冲锋兽锁定直线冲撞，障碍限制预警和移动，冲撞后提供恢复空档；受撞位移走现有 CharacterController 碰撞，不瞬移。
- 远程子弹使用扫掠碰撞，墙体挡住子弹；保持有限活动范围和射后空档。
- 新游戏与场景切换清理攻击、掉落、治疗连接和表现状态。

## 修改地形之后

`Tools > Enemies > Bake Shared Safe Navigation` 为当前主世界重烘焙四种体型的导航数据；不要从其他版本地图复制烘焙结果。
随后运行 `Check Enemy Spawn Footprints And Clearance`。不安全出生点必须调整到有完整支撑和碰撞净空的位置。

`Install Five Enemy Integration` 是显式、可重复的安装入口，仅补缺失样本及导航；不会重建地图，不会替换已有小怪。已有场景修改须先保存。
`Check Combat Ground And Hit Foundations (Play)` 使用临时运行时夹具测试，不保存地图或 Prefab，并检查原存档未变。

未附带 Unity 缓存、构建输出或第三方动作包。模型、短动画、材质、Shader 与编辑器检查代码保持共享复用。

## 五类小怪统一状态特效（2026-10-10）

- 蓄力：核心亮度随准备进度提升；沿用短蓄力音效与已有肩膀、甲片、翼片动作。远程怪也使用现有轻量蓄力提示音。
- 出手：保留能量镖短尾迹、Prism 实际波前、冲锋兽蓝色冲撞轨迹；石甲怪新增随真实拳头运动的短线，最长 0.3 米，绝不修改伤害判定。
- 受击：真实命中位置附近 0.42 米内短闪光（0.18 秒）和 5 片小碎屑；暖白中心、琥珀色中层和细暗边在浅色石甲上保持对比，闪光跟随受击部件，不把整个身体、苔藓或核心统一染色。这里的半径仅用于表现，不改变命中或伤害范围。
- 眩晕／受击停顿：核心不规则低亮闪烁，配合现有低头、倾斜或下垂姿态；不另加控制失效或改变眩晕数值。
- 维修：黄绿色细线连接目标，带细暗边与流动光点，宽约 0.05–0.062 米；实际躯干／可见核心附近最多 0.75 米的维修区域上移动一条窄扫描纹。扫描有暗边与高对比内层，不使用全身轮廓、网格和底色；不再把扫描中心放在 Prism 底座上方的空隙。成功维修短暂淡出，仍是有限治疗，不能复活。
- 死亡：核心先熄灭，随后保留各自倒地／收缩；中止蓄力提示、冲撞线、命中闪光和维修扫描。石甲怪沿用落地碎屑，其余类型使用少量延时碎屑。

优先级：死亡／关闭／世界重置清除所有临时效果；真实受击的短闪光暂时压过维修扫描，结束后仅在仍有效的维修连接下恢复。菜单会清掉连接和命中特效，真正的剑击 Hit Stop 保持冻结，不当作菜单取消。

`Shared/EnemyImpactVfx.cs` 由五种 AI 的初始化自动安装，现有 Prefab 与场景样本无需重新摆放或点击 Build。
它使用一份共享的 `Shared/Resources/EnemyLocalImpact.shader` 材质处理命中、碎屑与维修连接线，复用原模型网格，每只最多两个命中覆盖层和 12 个同时存活的碎屑，特效覆盖层本身无新增实时灯光或贴图；新增音频见下节。
`Shared/Resources/EnemyReadableCore.shader` 为机械核心保留饱和色与细暗边，限制峰值亮度，避免阳光下核心一直发白而看不出蓄力进度。运行时按原核心材质共享替代材质，不为每只怪物生成一套；石甲怪的原配色 Shader 同样补充有色核心与暖色蓄力变化。
维修扫描不修改目标原材质；石甲／陶瓷／苔藓仍使用原地图光照。原 `CombatFeedback` 的玩家／Boss 行为保持不变，不改攻击数值和碰撞判定。

测试入口：`Tools > Enemies > Check Five Enemy Visual State Priorities (Play)`。
测试在临时运行时场地内检查五种核心蓄力、眩晕亮度、局部闪光、碎屑上限、死亡／复位／关闭清理和受击／维修优先级，并通过 GPU 截图比较局部覆盖区域；不保存地图或 Prefab，检查原存档不变。
`Check Combat Ground And Hit Foundations (Play)` 也包含这些检查。鼠标锁定相关测试须使用正常 Unity 窗口，不能把 batchmode 下的输入锁定失败当作战斗功能失败。

实际地图截图入口：`Tools > Enemies > Capture Enemy States In Actual Map (Play)`。在已有 14 个地图样本上，以原相机视野和约 8 米观察距离分别截取待机、蓄力、受击、维修，再检查隐藏宝箱组合中真实治疗连接；不使用临时平地替代地图，不改变太阳、曝光或 Bloom。截图输出到系统临时目录 `anca-enemy-readability`，不进入 Assets 或构建。

本轮在实景进行了修改前后截图对比及两轮颜色、暗边、扫描位置调整。静态 Shader 检查、五类特效专项 Play、正常窗口的完整基础／战斗角色 Play 回归及真实地图治疗连接检查通过；验证版本为本机 Unity `6000.3.21f1`，原存档未变，未保存场景或 Prefab。这是自动回归与视觉截图检查，不是完整人工游玩评价。树叶或墙体遮挡仍正常遮挡特效，不以穿透显示来掩盖布置问题。

## 五类小怪音效（2026-10-10）

新增 20 个原创共享短音效，WAV 合计约 356 KiB。石甲摩擦与落地、远程锁定与能量镖、Prism 蓄能／甲片／脉冲、冲锋兽撞墙／眩晕，以及维修准备／连接／成功／中断／耗尽按实际事件分别接入。全场最多 10 个 3D 声源（含 4 个保护预警位），没有持续循环或每只复制一套音频；不修改玩家与 Boss 声音，也不改变攻击数值。

菜单、世界重置、关闭与死亡清理声音；剑击 Hit Stop 保持同步暂停／恢复。完整素材、空间开销、试听与回归说明见 [共享音效库](Shared/Audio/README.zh-CN.md)。测试入口：`Tools > Enemies > Check Five Enemy Audio Events And Budget (Play)`。

Unity `6000.3.21f1` 的声音专项与完整战斗／Shader 回归通过：20 个素材导入、五类真实事件、最后一次治疗顺序、18 个对象的声源预算，以及新游戏／场景卸载清理均检查通过，原存档未变，未保存场景或 Prefab。自动触发与波形验证不等于主观听感评估。

## 小怪性能与体积控制（2026-10-10）

远程子弹使用每场景 24 发／8 个撞击特效的小对象池；Prism 波纹复用地面采样，平台或身体移动时重新采样；维修扫描限制为最多 4 个主要甲片覆盖层。24 米外关闭细碎粒子和次要表现，但保留实际伤害、AI 与重要预警，不停用参与拳头判定的 Animator。本轮没有导入新模型、贴图或特效包。

完整战斗回归、性能专项、透明重叠截图以及独立 Mac 夹具已通过。WebGL 因缺少构建模块尚未实测，不以编辑器成绩代替。预算、实测数据、范围限制与测试入口见 [小怪性能说明](Shared/README.Performance.zh-CN.md)。

## 集成验证记录

- 14 个地图样本：6 石甲怪、1 远程悬浮怪、4 Ruin Prism、1 冲锋兽、2 维修萤；出生体积有地面支撑和碰撞净空，无缺失脚本。
- 新版主地形已替换并关闭旧灰盒地形；出生点和检查使用当前批准的行走表面，不依赖旧地形名称。部分既有小怪仅调整落脚高度，塔前 Prism 移到附近安全支撑处。
- 四种导航体型绕墙、不可到达目标、断层与重新寻路检查通过；冲锋兽扫掠碰撞、实际地图移动与九组角落／坡面检查通过。
- 独立 Unity 窗口的真实 Play 回归通过：空挥与 Hit Stop、实际拳头轨迹、波纹遮挡和真实 Space 起跳／下落／落地、子弹碰撞和清理、新游戏复位、五类战斗角色及两个守卫＋维修萤的自主战斗。原存档未变，测试没有保存场景或 Prefab；这是自动回归，不等同于人工完整通关。
- 验证环境是本机已安装的 Unity `6000.3.21f1`。仓库原定 `6000.3.22f1` 保持不变；未上传自动版本迁移或 Package Lock 变动。
