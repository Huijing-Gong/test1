# Leaf Wisp 悬浮远程怪

2026-10-11 实装。场景：`Assets/_Demo/Scenes/OverworldBaseline.unity`，现有样本：
`_DomainPrototypeEncounters/LeafWisp_EastForest`。Prefab 仍是 `Generated/LeafWisp.prefab`，无需重新 Build 或摆放。

## 本轮变化

- 真正剑击：命中第一帧出现深色边缘的青蓝亮点，持续 0.16 秒。接触点从 Capsule 投到实际外壳顶点，跟随受击部件，既有剑击 hit-stop 期间冻结；不穿墙、不因空挥触发。
- 硬质反馈：最多 7 粒定向陶瓷碎屑／蓝色火花，共用原有 12 粒容量的受击组件。身体围绕悬浮中心倾斜，不像凝胶体整体压缩。
- 射击：去掉伸向玩家的瞄准激光，改为眼睛周围的四角青蓝瞄准框，随蓄力逐渐收拢；最终锁定时变成小型琥珀色菱形框，核心同帧变色并响一次确认声。颜色和形状共同提示，不连续频闪；不使用容易误解为范围伤害的地面圈。锁定后稳定瞄准，成功生成子弹才显示 0.10 秒枪口亮点和发射声。沿实际弹道反方向后坐，保留射后 0.45 秒停顿。
- 行动：完成一段选定的短后撤／绕掩体侧移，再停下来战斗，不在刚露出墙角时抢着开火；后方有墙允许侧移。候选点按最终朝向检查枪口位置，射击通道检查子弹实际厚度、起始重叠及 3 厘米安全余量（仅用于选位，不扩大伤害）。目标稳定，不逐帧换点。超出射击距离可在原活动边界内接近，不无限跨地图追逐。
- Shader：Leaf Wisp 专用不透明外壳，单主光源＋环境光＋克制的上部边缘亮度。陶瓷与深色机械继续响应地图光照；其它敌人不使用此修改。
- 能量镖：保留原小型模型、11 米／秒速度及逐帧扫掠碰撞。亮芯与短尾迹增加暗边；伤害真正成功后调用已有 `PlayerMovement.ApplyImpact`，只做 0.12 秒轻微反应，不叠加重撞惩罚、镜头抖动或已有受撞状态。
- 死亡：核心／攻击立即熄灭，翼片下垂、失稳下降，用采样地面与实际旋转后的视觉 bounds 避免沉入地面。落地后短暂保留尸体，1.6 秒结束清理。死亡、禁用、菜单与新游戏清理效果和子弹。

原 Prefab 血量 35、伤害 7、蓄力 0.72 秒、最终锁定提前量 0.22 秒、恢复计时 1.85 秒、理想距离 3.2 米、活动边界 4.5 米均未改。
移动扫掠改用实际 Capsule 半径，不扩大攻击判定；不修改玩家挥剑、镜头和移动源文件。

## 预算

新增分支限定 `Kind.LeafWisp`，不改变共用脚本中的 Prism 攻击。
`LeafWispPresentation` 自动安装，预建受击亮点、枪口亮点和瞄准框三个小提示物，共用现有 LocalImpact 材质。两个亮点共用原 17 顶点／24 面网格；瞄准框另共享两种 64 顶点／32 面网格，同一时刻只显示其中一种。瞄准框正常深度遮挡，不穿墙；远处也保留这项关键攻击预警。
没有新模型、贴图、音频、大型包或实时灯光。外壳材质按原材质共享，状态更新缓存；24 米外关闭细碎亮点和尾迹，不停止预警、AI 或伤害。
保留每场景 24 发子弹、8 组撞击、10 个共享声源上限；回收先清空尾迹。

## 复测与证据边界

保存场景并退出 Play 后：`Tools > Enemies > Check Leaf Wisp Combat And Presentation (Play)`。
使用正常 Unity 窗口、脚本控制的玩家动作和真实 `PlayerCombat.TryAttack`，不是纯 EditMode 检查，也不是声称真人 WASD 试玩。

- 原地图／原光照／正常跟随镜头的真实剑击与 hit-stop；同帧开关亮点比较，对照不透明墙遮挡。
- 自主蓄力、收拢、锁定、发射、恢复、死亡落地、清理与重置截图；检查核心与锁定框同帧变色、确认声仅一次、不透明掩体挡住瞄准框。
- 静止玩家受一次实际子弹伤害；锁定后侧移躲过；墙体挡子弹。
- 后墙侧移、稳定短后撤；左右两侧自主绕到遮挡物侧面的射击点，并实际命中静止玩家；旧激光持续禁用，40 米外瞄准框不被次要细节剔除。
- 空挥不取消准备；真正受击立刻清理瞄准框与锁定声；轻微玩家受击恢复；预热后不新增子弹对象；实际蓄力期间打开菜单，清理全部提示／声音／子弹；新游戏重置；Prism 不获得专用组件。

另运行 `Check Combat Ground And Hit Foundations (Play)` 回归原五类小怪，及 `RunEnvironmentAutomation` 比较原地图光照下前后表现。
日志／截图在系统临时目录 `anca-wisp-polish.*`、`anca-leaf-wisp-play`、`anca-enemy-readability/wisp-before` 和 `wisp-after`，不进入 Assets。
正常编辑器 Play 验证不能当作独立 Mac 或 WebGL 性能成绩。本机未安装匹配的 WebGL Build Support，浏览器效果和帧时间仍须正式构建验证。

本轮最终专项、原五类完整战斗／声音／视觉回归和对象池预算检查均 `FINAL PASS`，原存档及场景／Prefab 未保存修改。正常跟随镜头下，同帧开关受击亮点有 252 个高对比变化像素（1280×720），不透明墙遮挡后未超过检查阈值；这只是可读性检查，不等同于玩家体验评分。

本轮 Editor 微基准：96 发直接创建 10.32 ms，预热后 Spawn/Retire 1.70 ms、新增子弹 GameObject 为 0；24 发／8 组上限及卸载清理通过。GC 计数接口在本机不可用，不能宣称零 GC，也不能将此结果当作整张地图帧率。

最终日志：`/tmp/anca-wisp-polish.KZsiy3/wisp-final-both-sides.log`、`foundation-final.log`、`performance-final.log`。实景截图：系统临时目录 `anca-leaf-wisp-play/20261011-113337-064`。临时文件可能被系统清理。

瞄准预警专项后续复测：`/tmp/anca-wisp-warning.4ccZG5/warning-final.log` 为 `FINAL PASS`。原地图／跟随镜头下，青蓝收拢框与琥珀锁定框分别有 589／203 个高对比变化像素（1280×720，同帧开关比较）；不透明墙隐藏提示，真正命中立即取消，锁定后侧移仍可躲开子弹。截图目录 `anca-leaf-wisp-play/20261011-124623-695`。这是脚本控制的真实 Play 与画面检查，不是新增真人测试或浏览器测试。
本次随后运行原五类完整战斗／声音／Shader 回归，`/tmp/anca-wisp-warning.4ccZG5/foundation-final.log` 为 `FINAL PASS`，含 Prism 四种配色及波前／跳跃躲避、10 声源上限、维修与新游戏清理；未修改或保存场景、Prefab、玩家移动／战斗／镜头源文件。
最后再次运行专项并人工查看截图，`warning-map-final.log` 为 `FINAL PASS`，截图目录 `anca-leaf-wisp-play/20261011-125103-299`。同帧暂时隐藏敌人全部 Renderer 的额外诊断图证实背景浅色斜带是地图几何，并非残留的敌人激光。首次启动检查曾在新游戏门禁关闭处失败（尚未进入敌人测试）；已保留该日志并补充门禁诊断信息，后续三次专项及完整回归通过，未绕过生产门禁或更改开场流程。

## 参考

- [Guerrilla 开发者：Horizon Forbidden West 敌人战斗设计](https://blog.playstation.com/2021/12/06/horizon-forbidden-west-outsmart-your-enemies/)：通过动作意图及远程武器蓄能声音，让玩家提前判断威胁。本项目据此组合翼片动作、收拢／锁定形状与短声音；具体瞄准框造型是本项目设计，不声称复制该游戏。
- [Riot：Shader 与游戏可读性](https://www.riotgames.com/en/news/valorant-shaders-and-gameplay-clarity)：克制的边缘亮度、明暗层次与背景对比。
- [GDC：Evolving Combat in God of War](https://media.gdcvault.com/gdc2019/presentations/Sheth_Mihir_EvolvingCombat.pdf)：稳定、受约束的战斗位置；本项目仍使用少量候选点，不照搬大型系统。
- [Unity：TrailRenderer.Clear](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TrailRenderer.Clear.html)、[ObjectPool](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/pool/objectpool1)：尾迹清理及有限复用。
- [Unity：Physics.SphereCast](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SphereCast.html)：考虑弹体厚度的通道检查，并单独检查起始重叠。
