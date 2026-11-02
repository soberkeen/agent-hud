// 时间轴 —— @60fps;本文件由 tools/gen_timings.py 从配音实测时长自动生成,勿手改
// 重排流程:换配音 -> 跑 gen_nar.py -> 跑 gen_timings.py -> 重渲
export const FPS = 60;
export const DUR = 4865; // 81.08s

// HUD 尺寸:DIP × UIK(2.4) = 设计 px —— 与 agent-hud.cs 实测一致
export const UIK = 2.4;
export const d = (v: number) => v * UIK;

// ---- 口播轴(帧) ----
export const NAR01 = { from: 30, end: 266 }; // 屏幕边上这块地方，你平时是不是完全没用过？
export const NAR02 = { from: 291, end: 417 }; // Agent HUD 就是干这个的。
export const NAR03 = { from: 443, end: 946 }; // HDR 开关、DeepSeek 余额、GLM 额度，天天要看的东西，全在这条边上。
export const NAR04 = { from: 971, end: 1243 }; // HDR 得双击才切，第一下只是确认，不怕手滑。
export const NAR05 = { from: 1269, end: 1501 }; // 开完 HDR，画面差别一眼就能看出来。
export const NAR06 = { from: 1526, end: 1898 }; // 鼠标放大肥鱼上，余额直接弹出来；放噶漏姆上，两个额度圈就展开。
export const NAR07 = { from: 1923, end: 2325 }; // 单击能把抽屉钉住不动，双击直接开 ZCode 或者 DSH。
export const NAR08 = { from: 2350, end: 2697 }; // 最实用的是这个：ZCode 跑完活，角色头顶冒个气泡告诉你。
export const NAR09 = { from: 2723, end: 3072 }; // 需要授权就在气泡上点个允许，结果写回 hook，任务接着跑。
export const NAR10 = { from: 3097, end: 3519 }; // 额度剩多少看表情就知道：平时淡定，黄了发慌，红了直接痴呆。
export const NAR11 = { from: 3544, end: 3895 }; // 安装没难度，向导点完就装好，hooks 它自己接。
export const NAR12 = { from: 3920, end: 4202 }; // 它还留了命令行接口，一行 -notify 就能把消息发到桌面上。
export const NAR13 = { from: 4227, end: 4733 }; // 开源免费，就一个 exe。GitHub 搜 agent-hud，让 Agent 住进你桌面。

export const NARR = [
  { key: 'nar01', from: 30, dur: 236, text: "屏幕边上这块地方，你平时是不是完全没用过？", sc: 0 },
  { key: 'nar02', from: 291, dur: 126, text: "Agent HUD 就是干这个的。", sc: 0 },
  { key: 'nar03', from: 443, dur: 503, text: "HDR 开关、DeepSeek 余额、GLM 额度，天天要看的东西，全在这条边上。", sc: 0 },
  { key: 'nar04', from: 971, dur: 272, text: "HDR 得双击才切，第一下只是确认，不怕手滑。", sc: 0 },
  { key: 'nar05', from: 1269, dur: 232, text: "开完 HDR，画面差别一眼就能看出来。", sc: 0 },
  { key: 'nar06', from: 1526, dur: 372, text: "鼠标放大肥鱼上，余额直接弹出来；放噶漏姆上，两个额度圈就展开。", sc: 0 },
  { key: 'nar07', from: 1923, dur: 402, text: "单击能把抽屉钉住不动，双击直接开 ZCode 或者 DSH。", sc: 0 },
  { key: 'nar08', from: 2350, dur: 347, text: "最实用的是这个：ZCode 跑完活，角色头顶冒个气泡告诉你。", sc: 0 },
  { key: 'nar09', from: 2723, dur: 349, text: "需要授权就在气泡上点个允许，结果写回 hook，任务接着跑。", sc: 0 },
  { key: 'nar10', from: 3097, dur: 422, text: "额度剩多少看表情就知道：平时淡定，黄了发慌，红了直接痴呆。", sc: 0 },
  { key: 'nar11', from: 3544, dur: 351, text: "安装没难度，向导点完就装好，hooks 它自己接。", sc: 0 },
  { key: 'nar12', from: 3920, dur: 282, text: "它还留了命令行接口，一行 -notify 就能把消息发到桌面上。", sc: 0 },
  { key: 'nar13', from: 4227, dur: 506, text: "开源免费，就一个 exe。GitHub 搜 agent-hud，让 Agent 住进你桌面。", sc: 0 },
] as const;
export const narF = (key: string) => (NARR as readonly { key: string; from: number }[]).find((n) => n.key === key)!.from;
export const narD = (key: string) => (NARR as readonly { key: string; dur: number }[]).find((n) => n.key === key)!.dur;

// ---- 场景(帧) ----
export const SHOTS = {
  s1: { from: 0, dur: 398 }, // 聚光+滑入锁定
  s2: { from: 398, dur: 543 }, // 归位+标注+吸附
  s3: { from: 941, dur: 552 }, // HDR 双击
  s4: { from: 1493, dur: 835 }, // 抽屉
  s5: { from: 2328, dur: 733 }, // 气泡
  s6: { from: 3061, dur: 471 }, // 三态
  s7: { from: 3532, dur: 678 }, // 使用+终端
  s8: { from: 4210, dur: 655 }, // 合影
} as const;

// ---- 场景内节拍(相对 shot.from,由口播起点推导) ----
const rel = (shotFrom: number, absF: number) => absF - shotFrom;
const nRel = (key: string, shotFrom: number, plus = 0) => rel(shotFrom, narF(key)) + plus;

export const S1 = {
  SL0: 313, SL1: 370, LOCK: 370,
  rise0: 424, lap1: [382, 396], lap2: [402, 422],
  titleT0: nRel('nar02', 0, 33), titleOut: [379, 396],
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
  washIn: nRel('nar05', SHOTS.s3.from, 21), washOut: [529, 549],
  chip: nRel('nar05', SHOTS.s3.from, 42),
};
export const S4 = {
  DS0: nRel('nar06', SHOTS.s4.from, 60),
  ZC0: nRel('nar06', SHOTS.s4.from, 250),
  cur1: [30, 63], cur2: [250, 275],
  chips: [nRel('nar07', SHOTS.s4.from, 40), nRel('nar07', SHOTS.s4.from, 90)],
  close: [775, 805],
};
export const S5 = {
  B1: nRel('nar08', SHOTS.s5.from, 70),
  B2: nRel('nar09', SHOTS.s5.from, 30),
  B1_CLOSE: nRel('nar09', SHOTS.s5.from, 10),
  CLICK: nRel('nar09', SHOTS.s5.from, 165),
  cursorIn: [491, 554],
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
