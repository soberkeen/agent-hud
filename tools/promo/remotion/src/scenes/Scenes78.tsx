// S7 deck-deal 两卡+终端打字 / S8 合影(字标 stamp) —— 节拍来自 spec.ts
import React from 'react';
import { AbsoluteFill, useCurrentFrame, staticFile } from 'remotion';
import { Hud } from '../Hud';
import { Bubble } from '../Bubble';
import { HUD_X0, HUD_Y0, ROW1_C, SEP1_Y } from '../layout';
import { Backdrop, Cursor } from './Scenes12';
import { Chip } from './Scenes34';
import { S7 as B7, S8 as B8 } from '../spec';
import { seg, lerp, outCubic, outBack, inOutSine, dampedSettle } from '../lib/easing';

const cl01 = (x: number) => Math.max(0, Math.min(1, x));

// ---- 发牌卡(deck-deal-flyin 适配:卡量 2,保留硬加速/预备拍/落地 press) ----
const CARD_W = 430, CARD_H = 300;
const FLY = 16, SETTLE = 8;

const DeckCard: React.FC<{ cx: number; cy: number; k: number; children: React.ReactNode }> = ({ cx, cy, k, children }) => {
  const flyK = Math.min(1, k);
  const zArc = Math.sin(Math.PI * Math.min(1, flyK)) * 54;
  const press = k > 1 ? 1 + dampedSettle((k - 1) * 24, 0.09, 0.28) * 0.02 : 1;
  const scale = lerp(0.92, 1, outCubic(flyK)) * press;
  return (
    <div style={{
      position: 'absolute', left: cx - CARD_W / 2, top: cy - CARD_H / 2 - zArc, width: CARD_W, height: CARD_H,
      transform: `scale(${scale})`, transformOrigin: 'center',
      borderRadius: 16, background: 'rgba(18,23,40,0.92)', border: '1px solid rgba(255,255,255,0.17)',
      boxShadow: `0 ${32 * (1 - flyK) + 10}px ${60 * (1 - flyK) + 20}px rgba(0,0,0,${0.4 * (1 - flyK * 0.6)})`,
      padding: 26, boxSizing: 'border-box', fontFamily: "'Microsoft YaHei'",
    }}>{children}</div>
  );
};

const CheckRow: React.FC<{ text: string; k: number }> = ({ text, k }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: 12, opacity: cl01(k * 1.6), fontFamily: "'Microsoft YaHei'" }}>
    <svg width={22} height={22} viewBox="0 0 22 22" style={{ flexShrink: 0 }}>
      <circle cx={11} cy={11} r={10} fill="none" stroke="#7CFC9E" strokeWidth={2} opacity={cl01(k * 2)} />
      {k > 0.35 && <polyline points="6,11.5 9.6,15 16,7.5" fill="none" stroke="#7CFC9E" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round" />}
    </svg>
    <span style={{ fontSize: 18, color: '#EDF1F8', whiteSpace: 'nowrap' }}>{text}</span>
  </div>
);

export const S7Usage: React.FC = () => {
  const f = useCurrentFrame();
  const { t11, DEAL, t12, fadeAB, kT, type, ok, echo, chip } = B7;
  const DEAL1 = DEAL[0], DEAL2 = DEAL[1];
  const antic = f > DEAL1 - 20 && f < DEAL1 ? Math.sin(Math.PI * seg(f, DEAL1 - 20, DEAL1)) : 0;
  const k1 = seg(f, DEAL1, DEAL1 + FLY + SETTLE + 8);
  const k2 = seg(f, DEAL2, DEAL2 + FLY + SETTLE + 8);
  const fadeK = 1 - outCubic(seg(f, fadeAB[0], fadeAB[1]));
  const TCX = 1060, TTY = 500;
  const kTerm = outBack(seg(f, kT[0], kT[1]), 1.15);
  const typed = '-notify "构建完成" "473 项测试全部通过" zcode';
  const nk = Math.floor(seg(f, type[0], type[1]) * typed.length + 0.999);
  const shown = typed.slice(0, Math.max(0, Math.min(typed.length, nk)));
  const cursorOn = nk < typed.length && (f * 2.7) % 1 < 0.5;
  const kEcho = outBack(seg(f, echo, echo + 23), 1.3);
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 3714} />
      <Hud x={HUD_X0} y={HUD_Y0} />
      {fadeK > 0.02 && (
        <div style={{ position: 'absolute', left: 210, top: 780 + antic * 24, opacity: fadeK * (1 - Math.max(k1, k2) * 0.9) }}>
          <div style={{ position: 'absolute', left: 0, top: 6, width: 150, height: 104, borderRadius: 12, background: 'rgba(24,30,50,0.9)', border: '1px solid rgba(255,255,255,0.14)' }} />
          <div style={{ position: 'absolute', left: 3, top: 3, width: 150, height: 104, borderRadius: 12, background: 'rgba(28,35,58,0.94)', border: '1px solid rgba(255,255,255,0.18)' }} />
          <div style={{ position: 'absolute', left: 6, top: 0, width: 150, height: 104, borderRadius: 12, background: 'rgba(34,42,70,0.97)', border: '1px solid rgba(255,255,255,0.22)' }} />
        </div>
      )}
      {fadeK > 0.02 && k1 > 0 && (
        <div style={{ opacity: fadeK }}>
          <DeckCard cx={560} cy={430} k={k1}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <span style={{ fontFamily: 'Consolas', fontSize: 20, color: '#F2F5FA' }}>AgentHUD-Setup.exe</span>
              <span style={{ fontSize: 13, color: '#34D399', border: '1.5px solid #34D399', borderRadius: 999, padding: '2px 12px' }}>向导</span>
            </div>
            <div style={{ marginTop: 36, display: 'flex', flexDirection: 'column', gap: 28 }}>
              <CheckRow text="双击安装 · per-user 免管理员" k={seg(f, DEAL1 + 20, DEAL1 + 47)} />
              <CheckRow text="自动接入 ZCode hooks" k={seg(f, DEAL1 + 33, DEAL1 + 60)} />
              <CheckRow text="双击启动路径自动探测" k={seg(f, DEAL1 + 46, DEAL1 + 73)} />
            </div>
          </DeckCard>
        </div>
      )}
      {fadeK > 0.02 && k2 > 0 && (
        <div style={{ opacity: fadeK }}>
          <DeckCard cx={1030} cy={430} k={k2}>
            <div style={{ fontSize: 23, fontWeight: 700, color: '#F2F5FA' }}>常驻体验</div>
            <div style={{ marginTop: 24, display: 'flex', flexDirection: 'column', gap: 20 }}>
              <CheckRow text="托盘 + 开机自启" k={seg(f, DEAL2 + 14, DEAL2 + 41)} />
              <CheckRow text="全屏让位 · 不挡游戏" k={seg(f, DEAL2 + 26, DEAL2 + 53)} />
              <CheckRow text="贴边半隐藏 · Home Indicator" k={seg(f, DEAL2 + 38, DEAL2 + 65)} />
              <CheckRow text="设置实时预览 · 皮肤 ×3" k={seg(f, DEAL2 + 50, DEAL2 + 77)} />
            </div>
          </DeckCard>
        </div>
      )}
      {kTerm > 0.02 && (
        <div style={{
          position: 'absolute', left: TCX - 490, top: TTY - 140 - (1 - kTerm) * 26, width: 980, height: 280,
          borderRadius: 18, background: 'rgba(12,14,20,0.92)', border: '1px solid rgba(255,255,255,0.14)',
          opacity: cl01(kTerm * 2), padding: 28, boxSizing: 'border-box', fontFamily: 'Consolas',
        }}>
          <div style={{ display: 'flex', gap: 10, marginBottom: 20 }}>
            {[0, 1, 2].map((i) => <div key={i} style={{ width: 12, height: 12, borderRadius: 6, background: 'rgba(90,96,108,0.8)' }} />)}
            <span style={{ fontFamily: "'Microsoft YaHei'", fontSize: 13.5, color: '#9AA7BD', marginLeft: 12 }}>agent-hud.exe — 命令行</span>
          </div>
          <div style={{ fontSize: 19 }}>
            <span style={{ color: '#7CFC9E' }}>&gt; </span>
            <span style={{ color: '#F2F5FA' }}>{shown}</span>
            {cursorOn && <span style={{ display: 'inline-block', width: 10, height: 22, background: '#F0F4FA', marginLeft: 3, verticalAlign: 'text-bottom' }} />}
          </div>
          {f >= ok && (
            <div style={{ fontFamily: "'Microsoft YaHei'", fontSize: 16, color: '#7CFC9E', marginTop: 26, opacity: outCubic(seg(f, ok, ok + 21)) }}>
              ✓ 事件已投递 · 气泡 8s 后自动收摊
            </div>
          )}
        </div>
      )}
      {kEcho > 0.02 && (
        <Bubble xRight={HUD_X0 - 24} top={SEP1_Y} tailY={ROW1_C - 12}
          title="构建完成" body="473 项测试全部通过" alpha={cl01(kEcho * 2.5)} scale={lerp(0.86, 1, kEcho)} />
      )}
      {f >= chip && (
        <Chip center={{ x: HUD_X0 - 120 * 2.4, y: SEP1_Y + 76 * 2.4 }} text="任何程序 · 一行接入" color="#6D8AFF" alpha={outCubic(seg(f, chip, chip + 21))} />
      )}
      {f < kT[0] && <Cursor x={HUD_X0 + 125} y={ROW1_C} />}
    </AbsoluteFill>
  );
};

// S8 合影(Q8):代表元素四方飞入 → 各归其位"围住"字标(落点手动散开,避开角色区/CTA/字标本体) → stamp
const PHOTO = [
  // kind, 落点中心(x,y), 出生偏移(fx,fy)
  { kind: 'hdr', x: 400, y: 175, fx: -420, fy: -420 },
  { kind: 'ring', x: 1516, y: 185, fx: 520, fy: -440 },
  { kind: 'bubble', x: 316, y: 402, fx: -560, fy: -80 },
  { kind: 'term', x: 1592, y: 420, fx: 600, fy: -40 },
  { kind: 'sliver', x: 150, y: 610, fx: -420, fy: 420 },
  { kind: 'balance', x: 1628, y: 648, fx: 480, fy: 430 },
];

const PhotoEl: React.FC<{ kind: string; x: number; y: number; fx: number; fy: number; k: number }> =
  ({ kind, x, y, fx, fy, k }) => {
    const px = lerp(x + fx, x, outCubic(k));
    const py = lerp(y + fy, y, outCubic(k));
    const u = 2.4;
    const body = (() => {
      switch (kind) {
        case 'hdr':
          return <div style={{ width: 58 * u, height: 58 * u, borderRadius: 16 * u, background: '#1F9D5C', border: '1px solid rgba(255,255,255,0.33)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 14.5 * u, fontWeight: 700, color: '#fff', fontFamily: "'Microsoft YaHei'" }}>HDR</div>;
        case 'ring':
          return <svg width={68 * u * 0.9} height={68 * u * 0.9} viewBox="0 0 100 100" style={{ transform: 'rotate(-90deg)' }}><circle cx={50} cy={50} r={43} fill="none" stroke="rgba(255,255,255,0.18)" strokeWidth={9} /><circle cx={50} cy={50} r={43} fill="none" stroke="#7CFC9E" strokeWidth={9} strokeDasharray={`${2 * Math.PI * 43 * 0.66} ${2 * Math.PI * 43}`} /></svg>;
        case 'bubble':
          return <div style={{ width: 200, height: 64, borderRadius: 12, background: 'rgba(42,42,46,0.91)', border: '1px solid rgba(255,255,255,0.3)', display: 'flex', alignItems: 'center', paddingLeft: 14, fontFamily: "'Microsoft YaHei'", fontSize: 14, fontWeight: 700, color: '#F4F6FA' }}>任务完成 ✓</div>;
        case 'term':
          return <div style={{ width: 220, height: 56, borderRadius: 12, background: 'rgba(12,14,20,0.92)', border: '1px solid rgba(255,255,255,0.14)', display: 'flex', alignItems: 'center', paddingLeft: 16, fontFamily: 'Consolas', fontSize: 16, color: '#7CFC9E' }}>&gt; -notify …</div>;
        case 'sliver':
          return <div style={{ width: 29, height: 220, borderRadius: 14, background: 'rgba(30,34,44,0.95)', border: '1px solid rgba(255,255,255,0.31)' }} />;
        default:
          return <div style={{ padding: '8px 22px', borderRadius: 999, background: 'rgba(18,23,40,0.92)', border: '1px solid rgba(255,255,255,0.2)', fontFamily: 'Consolas', fontSize: 18, color: '#F2F5FA' }}>¥ 42.13</div>;
      }
    })();
    return <div style={{ position: 'absolute', left: px, top: py, transform: 'translate(-50%,-50%)', opacity: cl01(k * 2) }}>{body}</div>;
  };

export const S8Outro: React.FC = () => {
  const f = useCurrentFrame();
  const { STAMP, photoT0, charT0, ina, cta } = B8;
  const stampK = seg(f, STAMP, STAMP + 14);
  const stampScale = lerp(1.6, 1, outCubic(stampK));
  const shake = f >= STAMP && f < STAMP + 10 ? dampedSettle((f - STAMP) * 1.0, 0.11, 0.3) * 5 : 0;
  const hudFade = 1 - outCubic(seg(f, 6, 36));
  const inaK = seg(f, ina[0], ina[1]) * (1 - seg(f, ina[1] + 60, ina[1] + 84));
  return (
    <AbsoluteFill>
      <Backdrop frame={f + 4505} />
      {hudFade > 0.02 && <Hud x={HUD_X0} y={HUD_Y0} alpha={hudFade} />}
      {PHOTO.map((p, i) => <PhotoEl key={i} kind={p.kind} x={p.x} y={p.y} fx={p.fx} fy={p.fy} k={seg(f, photoT0[i], photoT0[i] + 36)} />)}
      {[
        { cx: 700, ch: 'df-normal', t0: charT0[0] },
        { cx: 1220, ch: 'zc-normal', t0: charT0[1] },
      ].map((c, i) => {
        const k = outBack(seg(f, c.t0, c.t0 + 42));
        return (
          <div key={i} style={{ position: 'absolute', left: c.cx - 150, top: lerp(1140, 690, k) - 150, width: 300, height: 300, opacity: k }}>
            <img src={staticFile(`gifs/anim-${c.ch}.gif`)} alt="" style={{ width: 300, height: 300 }} />
          </div>
        );
      })}
      {stampK > 0 && (
        <div style={{
          position: 'absolute', left: 960 + shake, top: 320 + shake * 0.6, transform: `translate(-50%,-50%) scale(${stampScale})`,
          opacity: cl01(stampK * 2), textAlign: 'center', fontFamily: "'Microsoft YaHei'",
        }}>
          <div style={{ fontSize: 92, fontWeight: 700, color: '#F2F5FA', whiteSpace: 'nowrap', textShadow: '0 0 40px rgba(110,160,255,0.5)' }}>
            Agent <span style={{ background: 'linear-gradient(90deg,#56DCFF,#A778FF)', WebkitBackgroundClip: 'text', WebkitTextFillColor: 'transparent' }}>HUD</span>
          </div>
          <div style={{ marginTop: 16, fontSize: 30, color: '#E8EDF5', opacity: seg(f, STAMP + 30, STAMP + 54) }}>
            开源 · 免费 · 单文件零依赖 · 双角色动画
          </div>
          <div style={{ marginTop: 22, fontFamily: 'Consolas', fontSize: 34, color: '#78BEFF', opacity: seg(f, STAMP + 57, STAMP + 81) }}>
            github.com/soberkeen/agent-hud
          </div>
          {f >= cta && (
            <div style={{ marginTop: 26, display: 'flex', justifyContent: 'center' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 30px', borderRadius: 999, background: 'rgba(10,26,22,0.85)', border: '1.5px solid #34D399' }}>
                <div style={{ width: 10, height: 10, borderRadius: 5, background: '#34D399' }} />
                <span style={{ fontSize: 22, fontWeight: 700, color: '#34D399', fontFamily: "'Microsoft YaHei'" }}>AgentHUD-Setup.exe · 双击即装</span>
              </div>
            </div>
          )}
        </div>
      )}
      {inaK > 0.01 && stampK === 0 && (
        <div style={{ position: 'absolute', left: 960, top: 430, transform: 'translate(-50%,-50%)', opacity: inaK * 0.85, fontSize: 30, color: '#C9D3E4', fontFamily: "'Microsoft YaHei'" }}>
          开源 · 免费 · 单文件零依赖
        </div>
      )}
    </AbsoluteFill>
  );
};
