# Agent HUD 宣传片 · 设计 spec（video-shotcraft 自主自由创作模式）

> v3（Remotion 版）更新:全片 85.33s/5120f@60fps 4K;配音=GPT-SoVITS v2 预训练
> (零样本参考:MeloTTS 中文女声,MIT);时间轴由口播实测时长推导(spec.ts NARR 表),
> 所有场景节拍 = 口播起点+偏移的公式,改配音改 spec 即可全片重排。
> 分镜帧表以下面 77.7s 旧轴为准的结构描述,新轴每场景内节拍比例不变、整体拉宽。

模式记录：用户点名参考 video-shotcraft 重置宣传片、配音 GPT-SoVITS 女声、环境自建 →
按 skill 边界走**自主自由创作**（阶段 0–3 由 Agent 判断并记录，不逐级确认）。
用户既有要求是硬约束，优先级高于本 spec 的默认选择。

## 阶段 0 · 产品简报与决策表

- 产品：Agent HUD——Win11 桌面悬浮侧边栏（HDR 开关 + DeepSeek 余额 + GLM 额度，
  双角色 GIF 动画、hooks 通知/权限气泡、贴边半隐藏）。
- 用途/受众：B 站/社媒产品宣传片；桌面工具玩家、AI agent 用户。
- 硬约束（用户多轮确认）：4K 3840×2160@60fps；HUD 形态 1:1 对齐源码与真机截图；
  开场 HUD 居中独展、功能段归位右缘；必须有吸附演示；音轨**无 BGM**
  （后期自配），圆润 UI 音效；口播 GPT-SoVITS 女声进片，文案按
  `docs/宣传文案.md` 时间轴（13 句，全片 77.72s = 4663f@60fps）。
- 数据口径：余额 ¥42.13、额度 66%/57% 为演示数据（公开演示口径）；无任何密钥。

## 阶段 1 · 视觉方向与动效 tokens

视觉语言从产品自身生长（核心理念 2）：全部色板/圆角/尺寸来自 agent-hud.cs 实测——
面板 #101216@50% r24(DIP)、HDR 绿 #1F9D5C、琥珀 #8A5000/#FFD24A、
状态点 #7CFC9E/#FFD24A/#FF6B6B、气泡 #2A2A2E@E8 r12、
DeepSeek 蓝 #6D8AFF、噶漏姆紫 #A78BFA；字体 Microsoft YaHei（正文/粗体）+ Consolas（代码）；
底色深蓝夜幕 (#0B0E1A→#171D34) + 品牌色 bokeh。
动效性格（两轴定位：能量中高、调性活泼但工程感）→「活泼愉悦」预设微调：
主时长 27f，入场 bezier(0.34,1.56,0.64,1)，过冲 1.08，squash 0.18；落地弹 y1>1。
styleframe 跳过（理由：v1/v2 PIL 版已经过用户 5 轮帧级评审，视觉方向已定案）。

## 阶段 2 · 功能→镜头映射（Gallery 卡）

| 功能 | 镜头卡 | 变体/适配 |
|---|---|---|
| 开场单主角 | `spotlight-hero-card` | 主角=HUD 面板本身；推进 rotY 34°→14°（竖长面板 45° 破坏可读性，Q6）；reseat 段映射到 SC2 归位右侧 |
| 贴边吸附 | 自写（产品交互演示，R3 真人速度） | 音效 transition-snap/whoosh-fast |
| HDR 双击 | `crash-zoom-punch` | 撞停式；foly=switch-tap/switch-light（真实开关拟音） |
| 抽屉/额度 | 自写（面板内下展）+ `odometer-digit-roll`（仅 S6 大数字） | 抽屉数字小、上 DigitRoll 会失卡意，记为自定义 |
| 通知/权限气泡 | 自写（产品自身 UI 复刻） | 音效 ui/pop |
| 安装/常驻卡 | `deck-deal-flyin` | 卡量 26→2（只有两张卡），orbit 前半省略（由 SC6 直切），保留发牌硬加速+预备拍+落地 press |
| 命令行 | 自写打字 + `text/keyboard` 拟音（S4） | 长样本 Sequence 截断 |
| 结尾合影 | Q8 结构（自写）+ riser→impact→sparkle 句式 4.3 | 合影元素=HDR 钮/气泡/额度圈/角色/终端行各一 |

## 阶段 3 · 分镜帧表（@60fps，总 4663f）

| shot | from | dur | 内容 | 卡/音 |
|---|---|---|---|---|
| S1 spotlight | 0 | 489 | 暗场聚光三中间站扫过(30-300f)→HUD 自右缘滑入正中(313-370f)→锁定脉冲+rotY14°推进+rise/hover→轮廓光束两圈→hold | spotlight-hero / whoosh-big+sparkle |
| S2 dock+snap | 489 | 438 | 归位右缘(489-526, transition-snap)→三标注(531/603/675)→吸附演示(783-927: 抓取/拖离/磁吸/半隐藏/唤醒) | swoosh-quick×3 / snap 句式 |
| S3 hdr | 927 | 570 | 大钮台+文案；单击 1074(琥珀确认?)；双击 1158(crash zoom 撞停→ON 绿)；洗染+chip | crash-zoom / switch-tap+switch-light+sparkle |
| S4 drawers | 1497 | 666 | 悬停大肥鱼→余额抽屉弹开(1560,DigitRoll 数字)；悬停噶漏姆→双圈弹开(1770,spring 填充)；固定/双击 chips | pop 双连阶梯 |
| S5 bubbles | 2163 | 720 | 噶漏姆气泡 2265(timeout 2569 收摊)；大肥鱼确认气泡 2526；点允许 2745→chime+chip | pop / click |
| S6 moods | 2883 | 417 | HUD 滑回隐藏(2880-2905)；三列 80/40/10 DigitRoll+角色三态(2940/3000/3060) | whoosh+pop 三连阶梯 |
| S7 usage | 3300 | 723 | 发牌两张卡(3336+：安装/hooks、托盘/让位)；终端打字(3757-4005,keyboard 截断)+回声气泡 | deck-deal 句式+keyboard |
| S8 outro | 4023 | 640 | 合影：功能代表元素四方飞入围住字标(4080-4260)→wordmark stamp(4270)→hold≥1s→淡出 | riser-cine(4060)→impact-deep-whoosh(4270)→sparkle(4305) |

呼吸位预算：S1 hold 370-480f、S7 满板后静置 0.5s、S8 字标 hold 65f（≥1s ✓）。

## 阶段 6 · SFX 钉帧表（相对帧表达式见 src/spec.ts SFX_TABLE）

选音（全部 Mixkit 免费商用，AUDITION 表实测峰值）：transition-soft(场景切入)/
whoosh-big(弹起)/whoosh-fast(加速)/transition-snap(磁吸)/swoosh-quick(字卡)/
sparkle(光效)/riser-cine+impact-deep-whoosh(结尾三拍)/switch-tap+switch-light(HDR 真实开关
拟音)/switch-click-quick(抓取)/pop(弹出连发,阶梯递减)/keyboard(打字,19.6s 长样本截断)/
zoom-air-fast(crash zoom)/clock-tick-single(数位锁定,已预归一化)。
音量按实测峰值给，钉帧全部相对 SHOTS 起点（硬规则 4.5）。

## 有意偏差记录（按 aesthetic-rules 使用方式登记）

1. **Q1（复刻必须真实截图）**：HUD 是半透明+实时 GIF 动画的常驻组件，抽屉/气泡/
   状态点等状态无法冻结成截图；采用源码常量逐项复刻的 React 组件 + 官方 GIF 资产直出，
   并以 PrintWindow 真机截图（tools/promo/cap_hud.py）逐像素校准——材质/尺寸/颜色
   全部有源码与截图双重依据，经用户 5 轮帧级评审通过。真实截图用于校准而非贴图。
2. **无 BGM**（skill 默认配 BGM）：用户明确要求成片音轨只留圆润 UI 音效+口播——
   因此不渲带 BGM 双版本， riser/impact 句式在无 BGM 下依旧成立。
3. **spotlight-hero 相机**：PageCam(three) 换 CSS perspective 单层相机——本片无整页
   纹理多图层需求，参数语法（rotY 主导/zoom/persp）保留。
4. **ui/notify 类音色**：不用（无"系统在说话"叙事），交互拟音走真实开关/快门系。
5. **deck-deal 卡量**：26→2，发牌硬加速/预备拍/落地 press 保留，追逐 scroll 不适用。
6. **Q11 字号**：chip 类画面内标注 26px（<32px 线）——定位是画面内状态标注而非
   叙事字幕，且 4K 成片缩放后为 52px 物理像素；叙事信息全部由口播承担。
7. **S6 角色**：三态对比镜头角色用静态首帧（对比的是表情不是动作，动画会分散注意）；
   其余场景 HUD 内 GIF 走 @remotion/gif 全时间线。
