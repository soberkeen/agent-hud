# -*- coding: utf-8 -*-
"""由配音 wav 实测时长推导整条时间轴,重写 remotion/src/spec.ts
规则(与 v3 手工轴一致):句间 gap 0.42s,lead 0.5s,场景边界=特定句起点-垫值,
吸附演示/呼吸位固定帧预算,总长=末句结束+2.2s。
用法: python gen_timings.py"""
import json, os, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
NAR_DIR = os.path.join(HERE, '..', '..', 'HDR 开关', 'tools', 'promo', 'remotion', 'public', 'audio', 'nar')
SPEC_OUT = os.path.join(HERE, '..', '..', 'HDR 开关', 'tools', 'promo', 'remotion', 'src', 'spec.ts')
FPS = 60
LEAD = 0.50
GAP = 0.42
ORDER = ['nar01', 'nar02', 'nar03', 'nar04', 'nar05', 'nar06', 'nar07',
         'nar08', 'nar09', 'nar10', 'nar11', 'nar12', 'nar13']

def dur_of(key):
    p = os.path.join(NAR_DIR, key + '.wav')
    out = subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'format=duration',
                          '-of', 'csv=p=0', p], capture_output=True, text=True).stdout.strip()
    return float(out)

def main():
    LINES = json.load(open(os.path.join(HERE, 'nar_lines.json'), encoding='utf-8'))
    texts = {l['key']: l['text'] for l in LINES}
    durs = {k: dur_of(k) for k in ORDER}
    # 口播轴
    t = LEAD
    axis = {}
    for k in ORDER:
        start = t
        axis[k] = (start, durs[k])
        t = start + durs[k] + GAP
    f = lambda sec: int(round(sec * FPS))
    N = {k: (f(v[0]), f(v[0] + v[1])) for k, v in axis.items()}
    total_speech = sum(durs.values())
    # 场景边界(秒)
    s2 = axis['nar03'][0] - 0.75
    s3 = axis['nar04'][0] - 0.50
    s4 = axis['nar06'][0] - 0.56
    s5 = axis['nar08'][0] - 0.36
    s6 = axis['nar10'][0] - 0.60
    s7 = axis['nar11'][0] - 0.20
    s8 = axis['nar13'][0] - 0.28
    dur_total = axis['nar13'][0] + durs['nar13'] + 2.2
    DUR = f(dur_total)
    # 帧,场景尾部夹取
    S = {
        's1': (0, f(s2)),
        's2': (f(s2), f(s3)),
        's3': (f(s3), f(s4)),
        's4': (f(s4), f(s5)),
        's5': (f(s5), f(s6)),
        's6': (f(s6), f(s7)),
        's7': (f(s7), f(s8)),
        's8': (f(s8), DUR),
    }
    def rel(key, shot, plus=0):
        return N[key][0] - S[shot][0] + plus
    # 最小场景时长:s2 吸附演示(330-500+呼吸) / s3 HDR 节拍到 ~557 / s7 echo+chip+hold
    # 不足则顺延下一场景边界;s7 会顶到 nar13 → 直接顺延 nar13(尾锚句,无级联)
    axis_l = dict(axis)
    if (f(s3) - f(s2)) < 530:
        s3 = s2 + 530 / FPS
    if (f(s4) - f(s3)) < 545:
        s4 = s3 + 545 / FPS
    if (f(s8) - f(s7)) < 700 / FPS:
        axis_l['nar13'] = (f(s7) / FPS + 700 / FPS + 0.28, durs['nar13'])
    N = {k: (f(v[0]), f(v[0] + v[1])) for k, v in axis_l.items()}
    S = {
        's1': (0, f(s2)),
        's2': (f(s2), f(s3)),
        's3': (f(s3), f(s4)),
        's4': (f(s4), f(s5)),
        's5': (f(s5), f(s6)),
        's6': (f(s6), f(s7)),
        's7': (f(s7), f(s8)),
        's8': (f(s8), DUR),
    }
    for key, sc in [('nar03', 's2'), ('nar04', 's3'), ('nar06', 's4'), ('nar08', 's5'),
                    ('nar10', 's6'), ('nar11', 's7'), ('nar13', 's8')]:
        assert N[key][0] - S[sc][0] >= 0, '%s starts before its scene %s' % (key, sc)

    cur1 = N['nar09'][0] - S['s5'][0] + 165 - 69
    cur2 = N['nar09'][0] - S['s5'][0] + 165 - 6
    s3_dur = S['s3'][1] - S['s3'][0]
    s4_dur = S['s4'][1] - S['s4'][0]
    s1_end = S['s1'][1]
    s7_from = S['s7'][0]
    spec = f"""// 时间轴 —— @60fps;本文件由 tools/gen_timings.py 从配音实测时长自动生成,勿手改
// 重排流程:换配音 -> 跑 gen_nar.py -> 跑 gen_timings.py -> 重渲
export const FPS = {FPS};
export const DUR = {DUR}; // {DUR/FPS:.2f}s

// HUD 尺寸:DIP × UIK(2.4) = 设计 px —— 与 agent-hud.cs 实测一致
export const UIK = 2.4;
export const d = (v: number) => v * UIK;

// ---- 口播轴(帧) ----
"""
    for k in ORDER:
        st, en = N[k]
        spec += "export const %s = { from: %d, end: %d }; // %s\n" % (k.upper(), st, en, texts[k])
    spec += "\nexport const NARR = [\n"
    for k in ORDER:
        st, en = N[k]
        spec += "  { key: '%s', from: %d, dur: %d, text: %s, sc: 0 },\n" % (
            k, st, en - st, json.dumps(texts[k], ensure_ascii=False))
    spec += "] as const;\nexport const narF = (key: string) => (NARR as readonly { key: string; from: number }[]).find((n) => n.key === key)!.from;\nexport const narD = (key: string) => (NARR as readonly { key: string; dur: number }[]).find((n) => n.key === key)!.dur;\n"
    spec += "\n// ---- 场景(帧) ----\nexport const SHOTS = {\n"
    names = {'s1': '聚光+滑入锁定', 's2': '归位+标注+吸附', 's3': 'HDR 双击', 's4': '抽屉',
             's5': '气泡', 's6': '三态', 's7': '使用+终端', 's8': '合影'}
    for k, (a, b) in S.items():
        spec += "  %s: { from: %d, dur: %d }, // %s\n" % (k, a, b - a, names[k])
    spec += "} as const;\n"
    spec += f"""
// ---- 场景内节拍(相对 shot.from,由口播起点推导) ----
const rel = (shotFrom: number, absF: number) => absF - shotFrom;
const nRel = (key: string, shotFrom: number, plus = 0) => rel(shotFrom, narF(key)) + plus;

export const S1 = {{
  SL0: 313, SL1: 370, LOCK: 370,
  rise0: 424, lap1: [382, 396], lap2: [402, 422],
  titleT0: nRel('nar02', 0, 33), titleOut: [{s1_end-19}, {s1_end-2}],
}};
export const S2 = {{
  dock: [0, 37],
  callouts: [nRel('nar03', SHOTS.s2.from, 45), nRel('nar03', SHOTS.s2.from, 117), nRel('nar03', SHOTS.s2.from, 189)],
  calFade: [300, 320],
  cursorIn: [260, 330],
  GRAB: 330, DRAG: 346, REL: 376, TUCK: 402, WAKE: 468, WAKE_END: 500,
}};
export const S3 = {{
  kIn: [9, 39],
  ARM: nRel('nar04', SHOTS.s3.from, 135),
  ON: nRel('nar04', SHOTS.s3.from, 219),
  washIn: nRel('nar05', SHOTS.s3.from, 21), washOut: [{s3_dur-23}, {s3_dur-3}],
  chip: nRel('nar05', SHOTS.s3.from, 42),
}};
export const S4 = {{
  DS0: nRel('nar06', SHOTS.s4.from, 60),
  ZC0: nRel('nar06', SHOTS.s4.from, 250),
  cur1: [30, 63], cur2: [250, 275],
  chips: [nRel('nar07', SHOTS.s4.from, 40), nRel('nar07', SHOTS.s4.from, 90)],
  close: [{s4_dur-60}, {s4_dur-30}],
}};
export const S5 = {{
  B1: nRel('nar08', SHOTS.s5.from, 70),
  B2: nRel('nar09', SHOTS.s5.from, 30),
  B1_CLOSE: nRel('nar09', SHOTS.s5.from, 10),
  CLICK: nRel('nar09', SHOTS.s5.from, 165),
  cursorIn: [{cur1}, {cur2}],
}};
export const S6 = {{
  tuck: [0, 22],
  moods: [nRel('nar10', SHOTS.s6.from, 170), nRel('nar10', SHOTS.s6.from, 280), nRel('nar10', SHOTS.s6.from, 370)],
}};
export const S7 = {{
  t11: nRel('nar11', SHOTS.s7.from),
  DEAL: [nRel('nar11', SHOTS.s7.from, 36), nRel('nar11', SHOTS.s7.from, 66)],
  t12: nRel('nar12', SHOTS.s7.from),
  fadeAB: [nRel('nar12', SHOTS.s7.from, 6), nRel('nar12', SHOTS.s7.from, 27)],
  kT: [nRel('nar12', SHOTS.s7.from, 30), nRel('nar12', SHOTS.s7.from, 60)],
  type: [nRel('nar12', SHOTS.s7.from, 90), nRel('nar12', SHOTS.s7.from, 198)],
  ok: nRel('nar12', SHOTS.s7.from, 218),
  echo: nRel('nar12', SHOTS.s7.from, 240),
  chip: nRel('nar12', SHOTS.s7.from, 285),
}};
export const S8 = {{
  STAMP: nRel('nar13', SHOTS.s8.from, 156),
  riser: [23, 150],
  photoT0: [3, 18, 33, 48, 63, 78],
  charT0: [0, 12],
  ina: [17, 41],
  cta: 257,
}};

// ---- SFX 钉帧表(相对 shot 起点推导;音源 public/audio/sfx/) ----
const S = (from: number, src: string, volume: number, dur?: number) => ({{ from, src, volume, dur }});
export const SFX: {{ from: number; src: string; volume: number; dur?: number }}[] = [
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
"""
    io = open(SPEC_OUT, 'w', encoding='utf-8')
    io.write(spec); io.close()
    print('spec.ts written: DUR=%df (%.2fs)' % (DUR, DUR / FPS))
    for k in ORDER:
        print('  %s  %.2f -> %.2f' % (k, N[k][0] / FPS, N[k][1] / FPS))
    print('  shots:', {k: (v[0], v[1]) for k, v in S.items()})

if __name__ == '__main__':
    main()
