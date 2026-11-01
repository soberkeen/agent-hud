// S1 spotlight-hero-card(适配:主角=HUD 面板;rotY 14° 竖长面板可读性优先 Q6)
// S2 归位+标注+吸附演示(产品交互真人速度 R3) —— 节拍全部来自 spec.ts(口播推导)
import React from 'react';
import { AbsoluteFill, useCurrentFrame, interpolate } from 'remotion';
import { Hud, PANEL_W, closedH } from '../Hud';
import { HUD_CX, HUD_CY, HUD_X0, HUD_Y0, ROW1_C, ROW2_C } from '../layout';
import { S1, S2 as B2 } from '../spec';
import { seg, lerp, outQuint, outBack, inOutSine, outCubic } from '../lib/easing';

export const Backdrop: React.FC<{ frame: number; hdr?: number; dim?: number }> = ({ frame, hdr = 0, dim = 0 }) => {
  const t = frame / 60;
  return (
    <AbsoluteFill>
      <div style={{ position: 'absolute', inset: -30, background: 'linear-gradient(#0B0E1A, #171D34)' }} />
      {[
        { bx: 300, by: 240, sc: 0.9, col: '70,110,255', sp: 0.13, ph: 0 },
        { bx: 1560, by: 300, sc: 0.7, col: '150,110,255', sp: 0.1, ph: 1.3 },
        { bx: 480, by: 860, sc: 0.8, col: '40,190,200', sp: 0.08, ph: 2.2 },
        { bx: 1380, by: 820, sc: 1.0, col: '80,100,255', sp: 0.11, ph: 3.9 },
        { bx: 980, by: 140, sc: 0.6, col: '150,110,255', sp: 0.09, ph: 5.1 },
      ].map((b, i) => {
        const x = b.bx + Math.sin(t * b.sp * 2 * Math.PI + b.ph) * 60;
        const y = b.by + Math.cos(t * b.sp * 1.6 * Math.PI + b.ph) * 40;
        const s = 360 * b.sc;
        return (
          <div key={i} style={{
            position: 'absolute', left: x - s / 2, top: y - s / 2, width: s, height: s, borderRadius: s / 2,
            background: `radial-gradient(circle, rgba(${b.col},${0.13 + hdr * 0.09}) 0%, rgba(${b.col},0.05) 55%, transparent 72%)`,
          }} />
        );
      })}
      {hdr > 0 && (
        <div style={{
          position: 'absolute', inset: 0,
          background: 'linear-gradient(rgba(24,190,178,0.5), rgba(110,80,255,0.5))',
          opacity: 0.14 * hdr, mixBlendMode: 'screen',
        }} />
      )}
      {dim > 0 && <div style={{ position: 'absolute', inset: 0, background: '#05060C', opacity: dim }} />}
    </AbsoluteFill>
  );
};

export const Cursor: React.FC<{ x: number; y: number; alpha?: number }> = ({ x, y, alpha = 1 }) => (
  <svg width={42} height={54} viewBox="0 0 42 54" style={{ position: 'absolute', left: x, top: y, opacity: alpha, filter: 'drop-shadow(1px 2px 1px rgba(0,0,0,0.4))' }}>
    <polygon points="4,4 4,44 16,33 25,52 32,48.5 23,31 38,31" fill="#F5F8FC" stroke="#0A0C12" strokeWidth="3.4" strokeLinejoin="round" />
  </svg>
);

export const Ripple: React.FC<{ x: number; y: number; k: number }> = ({ x, y, k }) => {
  const r = lerp(10, 52, k);
  return <div style={{ position: 'absolute', left: x - r, top: y - r, width: r * 2, height: r * 2, borderRadius: r, border: '3px solid rgba(255,255,255,' + 0.86 * (1 - k) + ')' }} />;
};

const SLIVER = 12 * 2.4;
const cl01 = (x: number) => Math.max(0, Math.min(1, x));

// S1 暗场聚光 → HUD 滑入正中 → 锁定/推进/悬浮 → 轮廓光束两圈 → hold(R1)
export const S1Spotlight: React.FC = () => {
  const f = useCurrentFrame();
  const stations = [
    { at: 60, x: 620, y: 380, r: 620 },
    { at: 140, x: 1210, y: 640, r: 420 },
    { at: 230, x: 1000, y: 300, r: 360 },
  ];
  const LOCK = S1.LOCK;
  const locked = f >= LOCK;
  let lx = stations[0].x, ly = stations[0].y, lr = stations[0].r;
  for (let i = 0; i < stations.length; i++) {
    const a = stations[i];
    const b = stations[(i + 1) % stations.length];
    const from = a.at;
    const to = i < stations.length - 1 ? b.at : LOCK;
    if (f >= from && f < to) {
      const k = outCubic(seg(f, from, to));
      lx = lerp(a.x, i < stations.length - 1 ? b.x : HUD_CX + PANEL_W / 2, k);
      ly = lerp(a.y, i < stations.length - 1 ? b.y : HUD_CY + closedH / 2, k);
      lr = lerp(a.r, i < stations.length - 1 ? b.r : 300, k);
    }
  }
  if (locked) { lx = HUD_CX + PANEL_W / 2; ly = HUD_CY + closedH / 2; lr = 300; }
  const pulse = locked ? 1 + 0.06 * Math.max(0, 1 - (f - LOCK) / 8) : 1;
  const vig = interpolate(f, [0, LOCK], [0.16, 0.42], { extrapolateRight: 'clamp' });

  const { SL0, SL1 } = S1;
  let hx = 1980, hy = HUD_CY;
  const ghosts: { x: number; a: number }[] = [];
  if (f >= SL0) {
    const k = Math.min(1, outQuint(seg(f, SL0, SL1)));
    const travel = 1980 - HUD_CX;
    hx = 1980 - travel * k;
    const v = (1 - k) * travel / 240;
    if (v > 0.3) ghosts.push({ x: hx + v * 24, a: 0.12 }, { x: hx + v * 12, a: 0.24 });
  }
  const push = outCubic(seg(f, LOCK, LOCK + 40));
  const rise = outBack(seg(f, S1.rise0, S1.rise0 + 10), 1.25);
  const bob = f > S1.rise0 + 10 ? Math.sin(((f - S1.rise0 - 10) / 40) * 2 * Math.PI) * 4 : 0;
  const lift = rise;
  const camZoom = lerp(1, 1.14, push);
  const camRotY = lerp(0, 14, push);

  const laps = [
    { f0: S1.lap1[0], f1: S1.lap1[1], w: 12, op: 1 },
    { f0: S1.lap2[0], f1: S1.lap2[1], w: 8.4, op: 0.62 },
  ];
  const pad = 26;
  const bw = PANEL_W + pad * 2;
  const bh = closedH + pad * 2;
  const C = 2 * (bw + bh) - 4 * pad;

  return (
    <AbsoluteFill>
      <Backdrop frame={f} dim={0.25} />
      <div style={{ position: 'absolute', inset: 0, background: 'radial-gradient(circle at ' + lx + 'px ' + ly + 'px, transparent ' + lr * pulse * 0.55 + 'px, rgba(0,0,0,' + (0.25 + vig) + ') ' + lr * 1.25 + 'px)' }} />
      <div style={{
        position: 'absolute', left: lx - lr * pulse, top: ly - lr * pulse, width: lr * 2 * pulse, height: lr * 2 * pulse,
        background: 'radial-gradient(circle, rgba(235,240,255,0.24) 0%, rgba(190,205,255,0.05) 55%, transparent 72%)',
      }} />
      <div style={{
        position: 'absolute', inset: 0, perspective: 1200,
        transform: `scale(${camZoom}) rotateY(${camRotY}deg)`,
        transformOrigin: `${HUD_CX + PANEL_W / 2}px ${HUD_CY + closedH / 2}px`,
      }}>
        {ghosts.map((g, i) => (
          <div key={i} style={{ position: 'absolute', left: 0, top: 0, opacity: g.a }}>
            <Hud x={g.x} y={hy} t={f} />
          </div>
        ))}
        <div style={{ position: 'absolute', left: 0, top: bob, transform: `translateY(${(1 - rise) * 46}px)` }}>
          <div style={{
            position: 'absolute', left: hx + 10, top: hy + closedH - 6 + 8 * lift, width: PANEL_W - 20, height: 14,
            borderRadius: '50%', background: `rgba(0,0,0,${0.32 * (1 - lift * 0.55)})`, filter: `blur(${6 + 14 * lift}px)`,
          }} />
          <Hud x={hx} y={hy} t={f} />
          {f >= S1.lap1[0] && (
            <svg width={bw} height={bh} style={{ position: 'absolute', left: hx - pad, top: hy - pad, overflow: 'visible' }}>
              {laps.map((lp, i) => {
                const kk = seg(f, lp.f0, lp.f1);
                if (kk <= 0 || kk >= 1) return null;
                return (
                  <rect key={i} x={pad} y={pad} width={PANEL_W} height={closedH} rx={24 * 2.4}
                    fill="none" stroke="rgba(210,225,255,1)" strokeWidth={lp.w}
                    strokeDasharray={`${C * kk} ${C}`} opacity={lp.op}
                    style={{ filter: 'drop-shadow(0 0 6px rgba(140,180,255,0.8))' }} />
                );
              })}
            </svg>
          )}
        </div>
      </div>
      <TitleBlock f={f} />
    </AbsoluteFill>
  );
};

export const TitleBlock: React.FC<{ f: number; alpha?: number }> = ({ f, alpha = 1 }) => {
  const T0 = S1.titleT0;
  const a = (seg(f, T0, T0 + 24) * (1 - seg(f, S1.titleOut[0], S1.titleOut[1]))) * alpha;
  if (a <= 0.01) return null;
  const k2 = outBack(seg(f, T0, T0 + 30), 1.3);
  const shine = seg(f, T0 + 12, T0 + 69);
  return (
    <div style={{ position: 'absolute', left: 960, top: 878, transform: `translate(-50%,-50%) scale(${lerp(1.1, 1, k2)})`, opacity: a }}>
      <div style={{ position: 'relative', fontSize: 74, fontWeight: 700, fontFamily: "'Microsoft YaHei'", color: '#F2F5FA', whiteSpace: 'nowrap' }}>
        <span>Agent </span>
        <span style={{ background: 'linear-gradient(90deg,#56DCFF,#A778FF)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>HUD</span>
        {shine > 0 && shine < 1 && (
          <div style={{ position: 'absolute', inset: 0, overflow: 'hidden' }}>
            <div style={{
              position: 'absolute', top: -20, bottom: -20, left: lerp(-160, 760, shine), width: 130,
              background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.55), transparent)',
              transform: 'skewX(-18deg)', mixBlendMode: 'overlay',
            }} />
          </div>
        )}
      </div>
      <div style={{ textAlign: 'center', marginTop: 14, fontSize: 30, color: '#E8EDF5', opacity: seg(f, T0 + 24, T0 + 48) * 0.88, fontFamily: "'Microsoft YaHei'" }}>
        把最常看的信息，钉在屏幕边上
      </div>
    </div>
  );
};

// S2 归位 → 三标注 → 吸附演示(抓取/拖离/磁吸/半隐藏/唤醒)
export const S2DockSnap: React.FC = () => {
  const f = useCurrentFrame();
  const { GRAB, DRAG, REL, TUCK, WAKE, WAKE_END } = B2;
  const X_TUCK = 1920 - SLIVER;

  let hx = HUD_X0, hy = HUD_Y0;
  const ghosts: { x: number; a: number }[] = [];
  let cursor: { x: number; y: number; a?: number } | null = null;
  let grabRing = false;

  if (f < GRAB) {
    if (f < B2.dock[1]) {
      const k = outQuint(seg(f, B2.dock[0], B2.dock[1]));
      hx = lerp(HUD_CX, HUD_X0, k);
      hy = lerp(HUD_CY, HUD_Y0, k);
      const v = k * (HUD_X0 - HUD_CX) / 240;
      if (v > 0.25) ghosts.push({ x: lerp(HUD_CX, HUD_X0, Math.max(0, k - 0.04)), a: 0.2 }, { x: lerp(HUD_CX, HUD_X0, Math.max(0, k - 0.02)), a: 0.1 });
    }
    if (f > B2.cursorIn[0]) {
      const k = inOutSine(seg(f, B2.cursorIn[0], B2.cursorIn[1]));
      cursor = { x: lerp(1500, HUD_X0 + 125, k), y: lerp(820, HUD_Y0 + 19, k) };
    }
  } else if (f < REL) {
    const k = inOutSine(seg(f, DRAG, REL));
    hx = lerp(HUD_X0, HUD_X0 - 340 * 2.4, k);
    grabRing = true;
    cursor = { x: hx + PANEL_W / 2, y: HUD_Y0 + 8 * 2.4 };
  } else if (f < TUCK) {
    const k = outBack(seg(f, REL, TUCK), 1.35);
    hx = lerp(HUD_X0 - 340 * 2.4, X_TUCK - PANEL_W, k);
    if (f > REL + 12 && f < REL + 24) cursor = { x: HUD_X0 + 125, y: HUD_Y0 + 19, a: 1 - (f - REL - 12) / 12 };
  } else if (f < WAKE) {
    hx = X_TUCK - PANEL_W;
    const k = inOutSine(seg(f, TUCK + 6, WAKE - 4));
    cursor = { x: lerp(HUD_X0 + 125, X_TUCK - 12, k), y: HUD_Y0 + 130 * 2.4 };
  } else {
    const k = outCubic(seg(f, WAKE, WAKE_END));
    hx = lerp(X_TUCK - PANEL_W, HUD_X0, k);
    const v = (1 - k) * (X_TUCK - PANEL_W - HUD_X0) / 240;
    if (v > 0.25) ghosts.push({ x: hx + v * 20, a: 0.18 });
  }

  const dd = 2.4;
  const callouts = [
    { t0: B2.callouts[0], cy: HUD_Y0 + 54 * dd, txt: 'HDR 开关 · 双击确认', col: '#34D399' },
    { t0: B2.callouts[1], cy: ROW1_C, txt: '大肥鱼 · DeepSeek 余额', col: '#6D8AFF' },
    { t0: B2.callouts[2], cy: ROW2_C, txt: '噶漏姆 · GLM 双额度圈', col: '#A78BFA' },
  ];
  const calA = 1 - seg(f, B2.calFade[0], B2.calFade[1]);

  return (
    <AbsoluteFill>
      {ghosts.map((g, i) => (
        <div key={i} style={{ position: 'absolute', left: 0, top: 0, opacity: g.a }}>
          <Hud x={g.x} y={hy} />
        </div>
      ))}
      <Hud x={hx} y={hy} />
      {grabRing && (
        <div style={{ position: 'absolute', left: hx + PANEL_W / 2 - 16 * dd, top: HUD_Y0 + 8 * dd - 16 * dd, width: 32 * dd, height: 32 * dd, borderRadius: 16 * dd, border: '1.6px solid rgba(255,255,255,0.5)' }} />
      )}
      {callouts.map((c, i) => {
        const k = outCubic(seg(f, c.t0, c.t0 + 27)) * calA;
        if (k <= 0.01) return null;
        const ln = k * 30 * dd;
        const tx = HUD_X0 - 14 * dd - ln;
        return (
          <div key={i}>
            <div style={{ position: 'absolute', left: tx, top: c.cy, width: HUD_X0 - 14 * dd - tx, height: 1.4, background: 'rgba(255,255,255,0.47)' }} />
            <div style={{ position: 'absolute', left: HUD_X0 - 16 * dd, top: c.cy - 2.4, width: 4.8, height: 4.8, borderRadius: 2.4, background: 'rgba(255,255,255,0.82)' }} />
            <div style={{ position: 'absolute', left: tx - 8, top: c.cy, transform: 'translate(-100%,-50%)', fontSize: 28, fontWeight: 700, color: c.col, fontFamily: "'Microsoft YaHei'", whiteSpace: 'nowrap' }}>{c.txt}</div>
          </div>
        );
      })}
      {cursor && <Cursor x={cursor.x} y={cursor.y} alpha={cursor.a ?? 1} />}
    </AbsoluteFill>
  );
};
