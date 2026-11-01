// S5 气泡(通知 timeout 收摊+确认回传) / S6 三态(odometer-digit-roll) —— 节拍来自 spec.ts
import React from 'react';
import { AbsoluteFill, staticFile, useCurrentFrame } from 'remotion';
import { Hud, TuckedHud } from '../Hud';
import { Bubble } from '../Bubble';
import { HUD_X0, HUD_Y0, ROW1_C, ROW2_C, SEP1_Y, SEP2_Y } from '../layout';
import { Backdrop, Cursor, Ripple } from './Scenes12';
import { Chip } from './Scenes34';
import { S5 as B5, S6 as B6 } from '../spec';
import { seg, lerp, outCubic, outBack, inOutSine } from '../lib/easing';
import { OdometerDigitRoll } from '../lib/OdometerDigitRoll';

const cl01 = (x: number) => Math.max(0, Math.min(1, x));

export const S5Bubbles: React.FC = () => {
  const f = useCurrentFrame();
  const { B1, B2, B1_CLOSE, CLICK, cursorIn } = B5;
  const k1 = outBack(seg(f, B1, B1 + 23), 1.3);
  const a1 = k1 * (1 - seg(f, B1_CLOSE, B1_CLOSE + 19));
  const k2 = outBack(seg(f, B2, B2 + 23), 1.3);
  const a2 = k2;
  const btnC = { x: HUD_X0 - 52 * 2.4, y: SEP1_Y + 75 * 2.4 };
  const cur = (() => {
    if (f < cursorIn[0]) return null;
    const k = inOutSine(seg(f, cursorIn[0], CLICK - 6));
    return { x: lerp(1500, btnC.x, k), y: lerp(950, btnC.y, k) };
  })();
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 2262} />
      <Hud x={HUD_X0} y={HUD_Y0} />
      {a1 > 0.01 && (
        <Bubble xRight={HUD_X0 - 24} top={SEP2_Y} tailY={ROW2_C - 12}
          title="任务完成" body="ZCode 会话已结束，快回来看一眼吧" alpha={a1} scale={lerp(0.86, 1, k1)} />
      )}
      {a2 > 0.01 && (
        <Bubble xRight={HUD_X0 - 24} top={SEP1_Y} tailY={ROW1_C - 12}
          title="权限请求" body="agent-hud.exe 请求执行构建"
          buttons={['拒绝', '允许']} clicked={f >= CLICK ? '允许' : undefined} alpha={a2} scale={lerp(0.86, 1, k2)} />
      )}
      {f >= CLICK + 7 && (
        <Chip center={{ x: HUD_X0 - 120 * 2.4, y: SEP1_Y + 116 * 2.4 }} text="已放行 · hook 退出码 0" color="#34D399" alpha={outCubic(seg(f, CLICK + 7, CLICK + 21))} />
      )}
      {cur && <Cursor x={cur.x} y={cur.y} />}
      {(f >= CLICK && f < CLICK + 18) && <Ripple x={btnC.x} y={btnC.y} k={seg(f, CLICK, CLICK + 18)} />}
    </AbsoluteFill>
  );
};

const MOODS = [
  { cx: 480, pct: 80, mood: '标准', rng: '>50%', ch: ['df-normal', 'zc-normal'], col: '#7CFC9E' },
  { cx: 960, pct: 40, mood: '紧张', rng: '20~50%', ch: ['df-worry', 'zc-worry'], col: '#FFD24A' },
  { cx: 1440, pct: 10, mood: '痴呆', rng: '<20%', ch: ['df-daze', 'zc-daze'], col: '#FF6B6B' },
];

export const S6Moods: React.FC = () => {
  const f = useCurrentFrame();
  const tuckK = outCubic(seg(f, B6.tuck[0], B6.tuck[1]));
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 3160} />
      {tuckK < 0.999 ? (
        <Hud x={lerp(HUD_X0, 1920 - 28.8, tuckK)} y={HUD_Y0} />
      ) : (
        <TuckedHud />
      )}
      {MOODS.map((m, i) => {
        const t0 = B6.moods[i];
        const k = outBack(seg(f, t0, t0 + 30), 1.2);
        if (k <= 0.02) return null;
        const bob = Math.sin(((f + m.cx) / 40) * Math.PI * 2) * 3;
        return (
          <div key={i} style={{ opacity: cl01(k * 2) }}>
            <div style={{ position: 'absolute', left: m.cx - 96, top: 330 - 96, width: 192, height: 192 }}>
              <svg width={192} height={192} style={{ transform: 'rotate(-90deg)' }}>
                <circle cx={96} cy={96} r={87} fill="none" stroke="rgba(255,255,255,0.18)" strokeWidth={13} />
                <circle cx={96} cy={96} r={87} fill="none" stroke={m.col} strokeWidth={13}
                  strokeDasharray={`${2 * Math.PI * 87 * (m.pct / 100) * outCubic(seg(f, t0 + 9, t0 + 48))} ${2 * Math.PI * 87}`} />
              </svg>
              <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                <OdometerDigitRoll value={`${m.pct}%`} delay={t0 + 9} fontSize={40} />
              </div>
            </div>
            <div style={{ position: 'absolute', left: m.cx - 80 - 90, top: 560 + bob - 90, width: 180, height: 180, opacity: cl01(k * 2) }}>
              <img src={staticFile(`gifs/anim-${m.ch[0]}.gif`)} alt="" style={{ width: 180, height: 180 }} />
            </div>
            <div style={{ position: 'absolute', left: m.cx + 80 - 90, top: 560 - bob - 90, width: 180, height: 180, opacity: cl01(k * 2) }}>
              <img src={staticFile(`gifs/anim-${m.ch[1]}.gif`)} alt="" style={{ width: 180, height: 180 }} />
            </div>
            <div style={{ position: 'absolute', left: m.cx, top: 712, transform: 'translateX(-50%)', fontSize: 30, fontWeight: 700, color: m.col, fontFamily: "'Microsoft YaHei'" }}>{m.mood}</div>
            <div style={{ position: 'absolute', left: m.cx, top: 762, transform: 'translateX(-50%)', fontSize: 19, color: '#9AA7BD', fontFamily: "'Microsoft YaHei'" }}>{m.rng}</div>
          </div>
        );
      })}
    </AbsoluteFill>
  );
};
