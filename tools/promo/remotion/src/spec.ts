// 时间轴 —— @60fps;本文件由 tools/gen_timings.py 从配音实测时长自动生成,勿手改
// 重排流程:换配音 -> 跑 gen_nar.py -> 跑 gen_timings.py -> 重渲
export const FPS = 60;
export const DUR = 5905; // 98.42s

// HUD 尺寸:DIP × UIK(2.4) = 设计 px —— 与 agent-hud.cs 实测一致
export const UIK = 2.4;
export const d = (v: number) => v * UIK;

// ---- 口播轴(帧) ----
export const NAR01 = { from: 30, end: 548 }; // 下面为大家介绍一款桌面小工具，Agent HUD。它常驻屏幕边缘，把最常用的信息放在随手可见的位置。
export const NAR02 = { from: 573, end: 897 }; // 它要解决的问题很简单：让重要信息随看随取，不用来回切换窗口。
export const NAR03 = { from: 922, end: 1395 }; // 面板上有三块内容。最上面是 HDR 开关，下面分别是 DeepSeek 余额，和 GLM 的订阅额度。
export const NAR04 = { from: 1421, end: 1860 }; // HDR 采用双击确认的设计。第一下进入确认状态，第二下才会真正切换，可以避免误触。
export const NAR05 = { from: 1885, end: 2178 }; // 开启之后，画面的亮度和色彩会有明显提升，区别一目了然。
export const NAR06 = { from: 2204, end: 2714 }; // 鼠标放到 DeepSeek 助手上，余额会自动展开；放到 GLM 助手上，五小时和本周两个额度圈同时展开。
export const NAR07 = { from: 2739, end: 3132 }; // 单击可以把抽屉固定住，双击则直接启动对应的程序，也就是 ZCode 或者 DSH。
export const NAR08 = { from: 3157, end: 3527 }; // 它还接入了 ZCode 的事件通知。任务完成时，角色头顶会弹出气泡来提醒你。
export const NAR09 = { from: 3553, end: 4014 }; // 遇到权限请求，直接在气泡上点击允许，决策会写回 hook，任务继续执行，不用切换窗口。
export const NAR10 = { from: 4039, end: 4508 }; // 额度的使用状态，也会通过角色表情来呈现。充足时平静，偏低时紧张，耗尽时发呆。
export const NAR11 = { from: 4533, end: 5003 }; // 安装很简单，运行安装向导即可完成，ZCode 的 hooks 会自动配置好，程序支持开机自启和系统托盘。
export const NAR12 = { from: 5029, end: 5334 }; // 它同时提供命令行接口，一条 notify 命令，就能把消息推送到桌面。
export const NAR13 = { from: 5359, end: 5773 }; // Agent HUD 完全开源，单文件，零依赖。可以在 GitHub 搜索 agent-hud 下载使用。

export const NARR = [
  { key: 'nar01', from: 30, dur: 518, text: "下面为大家介绍一款桌面小工具，Agent HUD。它常驻屏幕边缘，把最常用的信息放在随手可见的位置。", sc: 0 },
  { key: 'nar02', from: 573, dur: 324, text: "它要解决的问题很简单：让重要信息随看随取，不用来回切换窗口。", sc: 0 },
  { key: 'nar03', from: 922, dur: 473, text: "面板上有三块内容。最上面是 HDR 开关，下面分别是 DeepSeek 余额，和 GLM 的订阅额度。", sc: 0 },
  { key: 'nar04', from: 1421, dur: 439, text: "HDR 采用双击确认的设计。第一下进入确认状态，第二下才会真正切换，可以避免误触。", sc: 0 },
  { key: 'nar05', from: 1885, dur: 293, text: "开启之后，画面的亮度和色彩会有明显提升，区别一目了然。", sc: 0 },
  { key: 'nar06', from: 2204, dur: 510, text: "鼠标放到 DeepSeek 助手上，余额会自动展开；放到 GLM 助手上，五小时和本周两个额度圈同时展开。", sc: 0 },
  { key: 'nar07', from: 2739, dur: 393, text: "单击可以把抽屉固定住，双击则直接启动对应的程序，也就是 ZCode 或者 DSH。", sc: 0 },
  { key: 'nar08', from: 3157, dur: 370, text: "它还接入了 ZCode 的事件通知。任务完成时，角色头顶会弹出气泡来提醒你。", sc: 0 },
  { key: 'nar09', from: 3553, dur: 461, text: "遇到权限请求，直接在气泡上点击允许，决策会写回 hook，任务继续执行，不用切换窗口。", sc: 0 },
  { key: 'nar10', from: 4039, dur: 469, text: "额度的使用状态，也会通过角色表情来呈现。充足时平静，偏低时紧张，耗尽时发呆。", sc: 0 },
  { key: 'nar11', from: 4533, dur: 470, text: "安装很简单，运行安装向导即可完成，ZCode 的 hooks 会自动配置好，程序支持开机自启和系统托盘。", sc: 0 },
  { key: 'nar12', from: 5029, dur: 305, text: "它同时提供命令行接口，一条 notify 命令，就能把消息推送到桌面。", sc: 0 },
  { key: 'nar13', from: 5359, dur: 414, text: "Agent HUD 完全开源，单文件，零依赖。可以在 GitHub 搜索 agent-hud 下载使用。", sc: 0 },
] as const;
export const narF = (key: string) => (NARR as readonly { key: string; from: number }[]).find((n) => n.key === key)!.from;
export const narD = (key: string) => (NARR as readonly { key: string; dur: number }[]).find((n) => n.key === key)!.dur;

// ---- 场景(帧) ----
export const SHOTS = {
  s1: { from: 0, dur: 877 }, // 聚光+滑入锁定
  s2: { from: 877, dur: 530 }, // 归位+标注+吸附
  s3: { from: 1407, dur: 763 }, // HDR 双击
  s4: { from: 2170, dur: 965 }, // 抽屉
  s5: { from: 3135, dur: 868 }, // 气泡
  s6: { from: 4003, dur: 518 }, // 三态
  s7: { from: 4521, dur: 821 }, // 使用+终端
  s8: { from: 5342, dur: 563 }, // 合影
} as const;

// ---- 场景内节拍(相对 shot.from,由口播起点推导) ----
const rel = (shotFrom: number, absF: number) => absF - shotFrom;
const nRel = (key: string, shotFrom: number, plus = 0) => rel(shotFrom, narF(key)) + plus;

export const S1 = {
  SL0: 313, SL1: 370, LOCK: 370,
  rise0: 424, lap1: [382, 396], lap2: [402, 422],
  titleT0: nRel('nar02', 0, 33), titleOut: [858, 875],
};
export const S2 = {
  dock: [0, 37],
  callouts: [nRel('nar03', SHOTS.s2.from, 45), nRel('nar03', SHOTS.s2.from, 117), nRel('nar03', SHOTS.s2.from, 189)],
  calFade: [300, 320],
  cursorIn: [260, 330],
  GRAB: 330, DRAG: 346, REL: 376, TUCK: 402, WAKE: 468, WAKE_END: 500,
};
export const S3 = {
  kIn: [9, 39],
  ARM: nRel('nar04', SHOTS.s3.from, 135),
  ON: nRel('nar04', SHOTS.s3.from, 219),
  washIn: nRel('nar05', SHOTS.s3.from, 21), washOut: [740, 760],
  chip: nRel('nar05', SHOTS.s3.from, 42),
};
export const S4 = {
  DS0: nRel('nar06', SHOTS.s4.from, 60),
  ZC0: nRel('nar06', SHOTS.s4.from, 250),
  cur1: [30, 63], cur2: [250, 275],
  chips: [nRel('nar07', SHOTS.s4.from, 40), nRel('nar07', SHOTS.s4.from, 90)],
  close: [905, 935],
};
export const S5 = {
  B1: nRel('nar08', SHOTS.s5.from, 70),
  B2: nRel('nar09', SHOTS.s5.from, 30),
  B1_CLOSE: nRel('nar09', SHOTS.s5.from, 10),
  CLICK: nRel('nar09', SHOTS.s5.from, 165),
  cursorIn: [514, 577],
};
export const S6 = {
  tuck: [0, 22],
  moods: [nRel('nar10', SHOTS.s6.from, 170), nRel('nar10', SHOTS.s6.from, 280), nRel('nar10', SHOTS.s6.from, 370)],
};
export const S7 = {
  t11: nRel('nar11', SHOTS.s7.from),
  DEAL: [nRel('nar11', SHOTS.s7.from, 36), nRel('nar11', SHOTS.s7.from, 66)],
  t12: nRel('nar12', SHOTS.s7.from),
  fadeAB: [nRel('nar12', SHOTS.s7.from, 6), nRel('nar12', SHOTS.s7.from, 27)],
  kT: [nRel('nar12', SHOTS.s7.from, 30), nRel('nar12', SHOTS.s7.from, 60)],
  type: [nRel('nar12', SHOTS.s7.from, 90), nRel('nar12', SHOTS.s7.from, 198)],
  ok: nRel('nar12', SHOTS.s7.from, 218),
  echo: nRel('nar12', SHOTS.s7.from, 240),
  chip: nRel('nar12', SHOTS.s7.from, 285),
};
export const S8 = {
  STAMP: nRel('nar13', SHOTS.s8.from, 156),
  riser: [23, 150],
  photoT0: [3, 18, 33, 48, 63, 78],
  charT0: [0, 12],
  ina: [17, 41],
  cta: 257,
};

// ---- SFX 钉帧表(相对 shot 起点推导;音源 public/audio/sfx/) ----
const S = (from: number, src: string, volume: number, dur?: number) => ({ from, src, volume, dur });
export const SFX: { from: number; src: string; volume: number; dur?: number }[] = [
  S(SHOTS.s1.from + 40, 'sfx/whoosh-big.mp3', 0.5),
  S(S1.SL0, 'sfx/whoosh-big.mp3', 0.55),
  S(S1.lap1[0], 'sfx/sparkle.mp3', 0.5, 100),
  S(SHOTS.s2.from + 2, 'sfx/transition-snap.mp3', 0.5),
  S(SHOTS.s2.from + S2.callouts[0], 'sfx/swoosh-quick.mp3', 0.4),
  S(SHOTS.s2.from + S2.callouts[1], 'sfx/swoosh-quick.mp3', 0.36),
  S(SHOTS.s2.from + S2.callouts[2], 'sfx/swoosh-quick.mp3', 0.32),
  S(SHOTS.s2.from + S2.GRAB, 'sfx/switch-click-quick.mp3', 0.55),
  S(SHOTS.s2.from + S2.DRAG, 'sfx/whoosh-fast.mp3', 0.5),
  S(SHOTS.s2.from + S2.REL, 'sfx/transition-snap.mp3', 0.5),
  S(SHOTS.s2.from + S2.TUCK, 'sfx/swoosh-quick.mp3', 0.4),
  S(SHOTS.s2.from + S2.WAKE, 'sfx/pop.mp3', 0.55),
  S(S3.ARM - 4, 'sfx/switch-tap.mp3', 0.6),
  S(S3.ON, 'sfx/zoom-air-fast.mp3', 0.5),
  S(S3.ON + 1, 'sfx/switch-light.mp3', 0.65),
  S(S3.ON + 14, 'sfx/sparkle.mp3', 0.45, 90),
  S(S4.DS0, 'sfx/pop.mp3', 0.55),
  S(S4.ZC0, 'sfx/pop.mp3', 0.42),
  S(S5.B1, 'sfx/pop.mp3', 0.55),
  S(S5.B2, 'sfx/pop.mp3', 0.45),
  S(S5.CLICK, 'sfx/switch-click-quick.mp3', 0.6),
  S(SHOTS.s6.from, 'sfx/swoosh-quick.mp3', 0.45),
  S(SHOTS.s6.from + S6.moods[0] + 4, 'sfx/pop.mp3', 0.45),
  S(SHOTS.s6.from + S6.moods[1] + 4, 'sfx/pop.mp3', 0.38),
  S(SHOTS.s6.from + S6.moods[2] + 4, 'sfx/pop.mp3', 0.31),
  S(SHOTS.s7.from + S7.DEAL[0], 'sfx/whoosh-big.mp3', 0.5),
  S(SHOTS.s7.from + S7.DEAL[0] + 20, 'sfx/transition-snap.mp3', 0.45),
  S(SHOTS.s7.from + S7.DEAL[1] + 20, 'sfx/transition-snap.mp3', 0.4),
  S(SHOTS.s7.from + S7.kT[0], 'sfx/swoosh-quick.mp3', 0.45),
  S(SHOTS.s7.from + S7.type[0], 'sfx/keyboard.mp3', 0.5, 108),
  S(SHOTS.s7.from + S7.echo, 'sfx/pop.mp3', 0.5),
  S(SHOTS.s8.from + S8.riser[0], 'sfx/riser-cine.mp3', 0.5, S8.riser[1]),
  S(SHOTS.s8.from + S8.STAMP, 'sfx/impact-deep-whoosh.mp3', 0.6),
  S(SHOTS.s8.from + S8.STAMP + 35, 'sfx/sparkle.mp3', 0.45, 120),
];
