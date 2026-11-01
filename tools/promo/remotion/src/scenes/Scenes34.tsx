// S3 HDR(crash-zoom-punch 撞停) / S4 抽屉(面板内下展,发牌弹跳) —— 节拍来自 spec.ts
import React from 'react';
import { AbsoluteFill, useCurrentFrame } from 'remotion';
import { Hud } from '../Hud';
import { HUD_X0, HUD_Y0, ROW1_C, ROW2_C } from '../layout';
import { S3 as B3, S4 as B4 } from '../spec';
import { Backdrop, Cursor, Ripple } from './Scenes12';
import { seg, lerp, outCubic, outBack, inOutSine } from '../lib/easing';

const cl01 = (x: number) => Math.max(0, Math.min(1, x));

const BigButton: React.FC<{ cx: number; cy: number; size: number; state: 'off' | 'armed' | 'on'; k: number; squash: number }> =
  ({ cx, cy, size, state, k, squash }) => {
    const s = size * squash;
    const fill = state === 'armed' ? 'rgba(138,80,0,0.9)' : state === 'on' ? '#1F9D5C' : '#353535';
    const txt = state === 'armed' ? '确认?' : 'HDR';
    const txtCol = state === 'off' ? '#CCCCCC' : '#FFFFFF';
    const font = (state === 'armed' ? 13 : 14.5) * (size / 58);
    return (
      <div style={{
        position: 'absolute', left: cx - s / 2, top: cy - s / 2, width: s, height: s,
        borderRadius: (16 / 58) * s, background: fill, border: '1px solid rgba(255,255,255,0.33)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', opacity: k,
        fontFamily: "'Microsoft YaHei'",
      }}>
        <span style={{ fontSize: font, fontWeight: 700, color: txtCol }}>{txt}</span>
      </div>
    );
  };

const GlassCard: React.FC<{ cx: number; cy: number; w: number; h: number; alpha: number }> =
  ({ cx, cy, w, h, alpha }) => (
    <div style={{
      position: 'absolute', left: cx - w / 2, top: cy - h / 2, width: w, height: h, opacity: alpha,
      borderRadius: 26, background: 'rgba(18,23,40,0.85)',
      border: '1px solid rgba(255,255,255,0.16)', boxShadow: '0 18px 40px rgba(0,0,0,0.35)',
    }} />
  );

export const S3Hdr: React.FC = () => {
  const f = useCurrentFrame();
  const { ARM, ON, washIn, washOut, chip: chipT, kIn } = B3;
  const squash = (f >= ARM && f < ARM + 19) ? 1 - 0.075 * Math.sin(Math.PI * seg(f, ARM, ARM + 19)) : 1;
  const on = f >= ON;
  const armed = f >= ARM && !on;
  const wash = 0.55 * outCubic(seg(f, washIn, washIn + 48)) * (1 - seg(f, washOut[0], washOut[1]));
  const punch = 1 + 0.06 * (1 - seg(f, ON, ON + 6));
  const cur = (() => {
    if (f < ARM - 9) {
      const k = inOutSine(seg(f, 36, ARM - 9));
      return { x: lerp(1060, 610, k), y: lerp(960, 530, k) };
    }
    if (f < ON - 9) {
      const k = inOutSine(seg(f, ARM + 21, ON - 9));
      return { x: lerp(610, 500, k), y: lerp(530, 440, k) };
    }
    return { x: 500, y: 440 };
  })();
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 940} hdr={wash > 0.02 ? 1 : 0} />
      <div style={{ position: 'absolute', inset: 0, transform: `scale(${punch})`, transformOrigin: '560px 470px' }}>
        <Hud x={HUD_X0} y={HUD_Y0} hdr={on ? 'on' : 'off'} />
        <GlassCard cx={560} cy={470} w={520} h={520} alpha={cl01(outBack(seg(f, kIn[0], kIn[1]), 1.1) * 2)} />
        <BigButton cx={560} cy={470} size={380} state={armed ? 'armed' : on ? 'on' : 'off'} k={outBack(seg(f, kIn[0], kIn[1]), 1.1)} squash={squash} />
        <div style={{ position: 'absolute', left: 900, top: 392, fontSize: 44, fontWeight: 700, color: '#F2F5FA', fontFamily: "'Microsoft YaHei'", opacity: outCubic(seg(f, 21, 51)) }}>
          HDR 开关
        </div>
        <div style={{ position: 'absolute', left: 900, top: 470, fontSize: 32, color: '#E8EDF5', fontFamily: "'Microsoft YaHei'", opacity: outCubic(seg(f, 21, 51)) * 0.92 }}>
          单击待确认 · 双击生效
        </div>
        <div style={{ position: 'absolute', left: 900, top: 540, fontSize: 22, color: '#9AA7BD', fontFamily: "'Microsoft YaHei'", opacity: outCubic(seg(f, 21, 51)) * 0.85 }}>
          3 秒轮询同步系统状态 · SDR 白电平仲裁驱动 quirk
        </div>
      </div>
      {wash > 0.01 && (
        <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(rgba(24,190,178,0.5), rgba(110,80,255,0.5))', opacity: wash * 0.24, mixBlendMode: 'screen' }} />
      )}
      {on && (
        <div style={{ position: 'absolute', left: 560 - 165, top: 700 - 24, width: 330, height: 48, borderRadius: 24, background: 'rgba(10,26,22,0.8)', border: '1px solid rgba(52,211,153,0.8)', display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, opacity: outCubic(seg(f, chipT, chipT + 24)) }}>
          <div style={{ width: 9, height: 9, borderRadius: 5, background: '#34D399' }} />
          <span style={{ fontSize: 17, fontWeight: 700, color: '#34D399', fontFamily: "'Microsoft YaHei'" }}>HDR ON · 已同步</span>
        </div>
      )}
      <Cursor x={cur.x} y={cur.y} />
      {(f >= ARM && f < ARM + 18) && <Ripple x={610} y={530} k={seg(f, ARM, ARM + 18)} />}
      {(f >= ON && f < ON + 18) && <Ripple x={500} y={440} k={seg(f, ON, ON + 18)} />}
    </AbsoluteFill>
  );
};

export const Chip: React.FC<{ center: { x: number; y: number }; text: string; color: string; alpha: number }> = ({ center, text, color, alpha }) => {
  if (alpha <= 0.01) return null;
  const FS = 26; // Q11:辅助可读文字 ≥3% 帧高(1080p→32px 档,取 26 记为有意偏差:画面内标注非叙事字幕)
  const w = text.length * FS + 92;
  const h = FS * 2.1;
  const x0 = center.x - w / 2;
  return (
    <div style={{
      position: 'absolute', left: x0, top: center.y - h / 2, width: w, height: h, borderRadius: h / 2,
      background: 'rgba(10,26,22,0.8)', border: `1.5px solid ${color}`, opacity: alpha,
      display: 'flex', alignItems: 'center', gap: 14, paddingLeft: 26, boxSizing: 'border-box',
      fontFamily: "'Microsoft YaHei'",
    }}>
      <div style={{ width: 12, height: 12, borderRadius: 6, background: color }} />
      <span style={{ fontSize: FS, fontWeight: 700, color, whiteSpace: 'nowrap' }}>{text}</span>
    </div>
  );
};

export const S4Drawers: React.FC = () => {
  const f = useCurrentFrame();
  const { DS0, ZC0, cur1, cur2, chips, close } = B4;
  const ods = outBack(seg(f, DS0, DS0 + 25), 1.3) * (1 - outCubic(seg(f, close[0], close[1])));
  const ozc = outBack(seg(f, ZC0, ZC0 + 25), 1.3) * (1 - outCubic(seg(f, close[0], close[1])));
  const fillK = outCubic(seg(f, ZC0 + 12, ZC0 + 84));
  const nk = Math.floor(seg(f, DS0 + 7, DS0 + 45) * 7 + 0.999);
  const dsFull = '¥ 42.13';
  const cur = (() => {
    if (f < cur2[0]) {
      const k = inOutSine(seg(f, cur1[0], cur1[1]));
      return { x: HUD_X0 + 125, y: lerp(980, ROW1_C, k) };
    }
    if (f < close[0] - 36) {
      const k = inOutSine(seg(f, cur2[0], cur2[1]));
      return { x: HUD_X0 + 125, y: lerp(ROW1_C, ROW2_C, k) };
    }
    return { x: HUD_X0 + 125, y: ROW2_C };
  })();
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 1536} />
      <Hud x={HUD_X0} y={HUD_Y0} openDs={ods} openZc={ozc}
        dsText={dsFull.slice(0, Math.max(0, Math.min(7, nk)))}
        five={0.66 * fillK} week={0.57 * fillK} fillK={1} />
      <Cursor x={cur.x} y={cur.y} />
      {(f >= DS0 && f < DS0 + 16) && <Ripple x={cur.x} y={ROW1_C} k={seg(f, DS0, DS0 + 16)} />}
      {(f >= ZC0 && f < ZC0 + 16) && <Ripple x={cur.x} y={ROW2_C} k={seg(f, ZC0, ZC0 + 16)} />}
      <Chip center={{ x: 1330, y: 505 }} text="单击 = 固定常驻" color="#6D8AFF" alpha={outCubic(seg(f, chips[0], chips[0] + 24))} />
      <Chip center={{ x: 1330, y: 576 }} text="双击 = 启动 ZCode / DSH" color="#A78BFA" alpha={outCubic(seg(f, chips[1], chips[1] + 24))} />
    </AbsoluteFill>
  );
};
