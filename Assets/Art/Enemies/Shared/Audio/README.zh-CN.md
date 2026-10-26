# 六类小怪共享音效库

24 个原创程序合成短音效，单声道、22,050 Hz、16-bit PCM；WAV 源文件合计 **422,654 字节（约 413 KiB）**。原有 20 个保持不变，凝胶体新增 4 个约 57,948 字节。没有下载录音、第三方音效包、运行时音频合成或持续循环声。源码、音频按本项目的许可管理；这不是外部素材授权声明。

## 听什么、何时响

| 小怪 | 音效／实际事件 | 声音设计 |
|---|---|---|
| 石甲怪 | `StoneScrape` 抬手蓄力；`StonePunch` 出拳；`StoneHit` 真正受击／拳头命中；`StoneFall` 倒地 | 颗粒摩擦＋石块低中频共鸣，出拳有短气流；关机与落地分开 |
| 远程悬浮怪 | `CoreLock` 蓄力瞄准；`DartFire` 能量镖发射；`ShellHit` 外壳受击；`CorePowerDown` 死亡停机 | 中频上升锁定音、快速下滑镖声、短陶瓷共鸣、下降停机音 |
| Ruin Prism | `PrismCharge` 蓄力；`PlateOpen` 蓄力进度到 35% 时甲片展开；`Pulse` 波纹实际释放；`ShellHit` 受击；`CorePowerDown` 核心熄灭 | 低频与中频并用，普通电脑扬声器仍有预警；干脆甲片机械声与宽短脉冲分开 |
| 冲锋兽 | `BeetleCharge` 蓄力；`BeetleRush` 开始冲撞；`WallCrash` 实际撞墙；`Stun` 恢复／眩晕；石甲受击与倒地声音共享 | 蓄力机械张力、短冲撞气流、撞墙更重；撞墙后延迟 0.32 秒播放短停机尾声，不每帧重复 |
| 维修萤 | `RepairPrepare` 真正准备维修；`RepairLink` 连接开始；`RepairSuccess` 实际恢复生命；`RepairBreak` 有效战斗中准备／连接被中断；`RepairEmpty` 最后一次治疗后 | 软上升双音、短连接声、三音成功提示；中断下降并带少量杂音，耗尽是低双音 |
| 凝胶体 | `GelCharge` 压縮蓄力；`GelJump` 真正起跳；`GelSplat` 落地/死亡散开；`GelHit` 真实受击；死亡散开后短延迟共享关机声 | 短促弹性上升音、轻弹跳声、低频湿润散开、柔软受击音；不复用碎石撞击 |

`CoreLock` 表示正在瞄准，不是声称子弹已命中或进入自动追踪。石甲／冲锋兽死亡先停机，约 0.65 秒后才有倒地声。维修成功与中断互斥，普通目标搜索失败没有中断声；耗尽提示延迟 0.52 秒，成功音可先完整结束。受击音只来自真实 `Health.Damaged`，不会因为玩家空挥而播放。

动作音高只轻微随机浮动（±3%）；警告保持稳定。Prism 受击略低、维修萤略高，共享外壳素材仍能区分体型。警告在实际蓄力时开始，有限音高缩放适配动作长度，不修改玩法计时。

## 开销与混音约束

- `EnemySoundEmitter` 随六类 AI 初始化自动安装；原场景样本与 Prefab 不必重建。
- 原五类小怪的通用受击、死亡和临时攻击声音被运行时替换，不与新声音双播。玩家／Boss、拾取、环境和队友挥剑声音不变；旧 Prefab 可选音频字段仅保留作兼容，不是当前小怪播放源。
- `EnemyAudio` 全场共享 **10 个 AudioSource**：4 个预警位＋6 个动作／反馈位。每只最多一条预警和一条动作声，不使用每次命中新建物件或 `PlayOneShot` 无限制堆叠。
- 预警位不会被普通受击声挤占；超过容量按距离与优先级取舍。存在预警时，池内普通反馈音降低至 58%；不改环境音和其他同学的混音器。
- 全部为 3D 线性衰减，近距离 2–3 米；预警最远 32 米，重脉冲／撞墙 26 米，其余 20 米。范围外不占声音位；不使用远处仍响的 2D 混合，也不启用冲锋多普勒变调。
- 音效文件峰值最高 0.8，混音池总音量增益上限 1.15，为本池叠加留余量；这不替代整个游戏的最终混音／限幅。保留一个 `Shared enemy audio (10 bounded voices)` Inspector 总音量滑块。
- 菜单、死亡／停止的玩家、副世界切换、怪物关闭／卸载、新游戏清掉相应声音与延迟提示；真实剑击 Hit Stop 暂停并恢复声音，不当作菜单取消。
- WAV 在导入时使用预载 PCM，避免短预警解码延迟。24 个全部加载的浮点样本理论占约 823 KiB，实际 Unity 内存与构建压缩另计；同种素材共享，不乘以小怪数量。

实现参考 Unity 官方 [AudioSource](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiosource) 与 [AudioImporterSampleSettings](https://docs.unity3d.com/cn/6000.0/ScriptReference/AudioImporterSampleSettings.html)。声源数量上限、抢占与混音余量由本代码直接控制，不只依靠引擎 priority 设置。

## 试听与测试

1. 打开新版项目的 `Assets/_Demo/Scenes/OverworldBaseline.unity`，进入游戏，在隐藏宝箱／塔前看守卫与维修萤，在冲锋兽处比较普通冲完和撞墙。先确认 Game 视图未静音、系统音量非零。
2. 想单独听某个声音，在 Project 选择 `Shared/Audio/Resources/EnemyAudio/*.wav`，用 Unity 音频 Inspector 的播放按钮。只比较电脑实际播放的听感，波形检查不能替代人工听觉评价。
3. `Tools > Enemies > Check Five Enemy Audio Events And Budget (Play)`：自动验证真实受击、死亡、攻击、最后一次维修顺序、旧音不双播、18 个敌人下的 10 声源上限、3D 距离、Hit Stop／菜单清理与玩家声音不变；沿用存档隔离，不保存场景／Prefab。
4. `Check Combat Ground And Hit Foundations (Play)` 也包含音效检查，用于确认接声音没有改变原伤害、移动或地形交互。输入测试使用正常 Unity 窗口，不用无图形 batchmode 评价操作结果。

在 `Audio/Editor` 下运行 `python3 generate_enemy_audio.py` 可重建原始 WAV。`--check` 校验现有 WAV 与生成源码一致；同时在系统临时目录 `anca-enemy-audio` 输出音效串联试听 `enemy-cues-audition.wav` 与逐文件信号报告，不进入 Assets／构建。试听顺序与上表的素材枚举顺序相同，每个短音重复两次后再进入下一种。

自动验证覆盖触发、导入、声源播放和资源开销，不声称已完成多人主观听感评估；最终应在实际地图以常用扬声器／耳机听一轮，再调共享总音量。
