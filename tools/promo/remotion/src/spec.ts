// 时间轴 —— @60fps;所有"配音驱动"节拍由口播实测时长推导(配音 wav 在 public/audio/nar/)
// 改配音后:改 NARR.from(或跑 tools/promo/remotion/gen_timings.py)即可全片重排
export const FPS = 60;
export const DUR = 5120; // 85.33s

// HUD 尺寸:DIP × UIK(2.4) = 设计 px —— 与 agent-hud.cs 实测一致
export const UIK = 2.4;
export const d = (v: number) => v * UIK;

// ---- 口播轴(帧)——实测 wav 时长(首尾静音已修剪) ----
const N = (key: string, fromF: number, durS: number, text: string, sc: number) =>
  ({ key, from: fromF, dur: Math.round(durS * FPS), text, sc });
export const NARR = [
  N('nar01', Math.round(0.50 * 60), 4.133, '你的屏幕边缘，是全桌面最容易被浪费的黄金位置。', 1),
  N('nar02', Math.round(5.05 * 60), 2.323, '今天，把它交给 Agent HUD。', 1),
  N('nar03', Math.round(7.79 * 60), 7.136, 'HDR 开关、DeepSeek 余额、GLM 额度，最常瞄的三样，全在这一条边上。', 2),
  N('nar04', Math.round(16.16 * 60), 5.676, 'HDR 单击只是待确认，双击才真的切，防误触刻在骨子里。', 3),
  N('nar05', Math.round(22.26 * 60), 3.475, '开的一瞬间，整个画面直接上了个档次。', 3),
  N('nar06', Math.round(26.16 * 60), 6.897, '鼠标扫过大肥鱼，余额自己弹出来；扫过噶漏姆，两个额度圈一起展开。', 4),
  N('nar07', Math.round(33.48 * 60), 4.162, '单击是固定，双击直接拉起 ZCode 或 DSH。', 4),
  N('nar08', Math.round(38.06 * 60), 5.059, '我最喜欢的部分：ZCode 干完活，角色头顶直接冒气泡告诉你。', 5),
  N('nar09', Math.round(43.54 * 60), 9.314, '权限确认，气泡上点个允许，原路写回 hook，人不用挪窝。', 5),
  N('nar10', Math.round(53.27 * 60), 7.516, '额度还剩多少？看脸就知道：绿色淡定，黄色慌张，红色——它先替你痴呆。', 6),
  N('nar11', Math.round(62.10 * 60), 6.270, 'Setup 双击装完，hooks 自动接好；托盘、自启、实时预览设置，全都有。', 7),
  N('nar12', Math.round(68.79 * 60), 4.969, '命令行也留了门：一条 -notify，任何程序都能喂它消息。', 7),
  N('nar13', Math.round(75.36 * 60), 7.747, '开源、免费、单文件零依赖。GitHub 搜 agent-hud——让你的 Agent，也住进你的桌面。', 8),
] as const;
export const narF = (key: string) => NARR.find((n) => n.key === key)!.from;
export const narD = (key: string) => NARR.find((n) => n.key === key)!.dur;

// ---- 场景(帧) ----
export const SHOTS = {
  s1: { from: 0, dur: 443 },        // 聚光+滑入锁定(开场独展正中)
  s2: { from: 443, dur: 530 },      // 归位+标注+吸附演示
  s3: { from: 973, dur: 563 },      // HDR 双击
  s4: { from: 1536, dur: 726 },     // 抽屉
  s5: { from: 2262, dur: 898 },     // 气泡
  s6: { from: 3160, dur: 554 },     // 三态
  s7: { from: 3714, dur: 791 },     // 使用
  s8: { from: 4505, dur: 615 },     // 合影
} as const;

// ---- 场景内节拍(全部相对 shot.from,由口播起点推导) ----
const rel = (shotFrom: number, absF: number) => absF - shotFrom;
const nRel = (key: string, shotFrom: number, plusF = 0) => rel(shotFrom, narF(key)) + plusF;

export const S1 = {
  SL0: 313, SL1: 370, LOCK: 370,
  rise0: 424, lap1: [382, 396], lap2: [402, 422],
  titleT0: nRel('nar02', 0, 33), titleOut: [424, 441],
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
  washIn: nRel('nar05', SHOTS.s3.from, 21), washOut: [540, 560],
  chip: nRel('nar05', SHOTS.s3.from, 42),
};
export const S4 = {
  DS0: nRel('nar06', SHOTS.s4.from, 60),
  ZC0: nRel('nar06', SHOTS.s4.from, 250),
  cur1: [30, 63], cur2: [250, 275],
  chips: [nRel('nar07', SHOTS.s4.from, 40), nRel('nar07', SHOTS.s4.from, 90)],
  close: [666, 696],
};
export const S5 = {
  B1: nRel('nar08', SHOTS.s5.from, 70),
  B2: nRel('nar09', SHOTS.s5.from, 30),
  B1_CLOSE: nRel('nar09', SHOTS.s5.from, 10),
  CLICK: nRel('nar09', SHOTS.s5.from, 165),
  cursorIn: [446, 509],
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
