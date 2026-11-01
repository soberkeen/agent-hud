// HUD 面板 1:1 复刻 —— 尺寸/颜色全部来自 agent-hud.cs 实测(SPEC.md 阶段 0/1)
// 单位:设计 px(DIP×2.4);Q1 偏差见 SPEC.md:源码常量+真机截图双重校准
import React from 'react';
import { Gif } from '@remotion/gif';
import { staticFile } from 'remotion';

export const HUD_COLORS = {
  panel: 'rgba(16,18,22,0.69)',
  border: 'rgba(255,255,255,0.27)',
  sep: 'rgba(255,255,255,0.15)',
  pill: 'rgba(255,255,255,0.4)',
  on: '#1F9D5C',
  off: '#353535',
  armed: 'rgba(138,80,0,0.9)',
  btnBorder: 'rgba(255,255,255,0.33)',
  onTxt: '#FFFFFF',
  offTxt: '#CCCCCC',
  dotG: '#7CFC9E',
  dotY: '#FFD24A',
  dotR: '#FF6B6B',
  balance: '#F4F6FA',
  ringCap: 'rgba(255,255,255,0.6)',
};

export const PANEL_W = 104 * 2.4; // 249.6
export const closedH = (16 + 76 + 7 + 76 + 7 + 76 + 6) * 2.4; // 633.6
export const DS_H = 40 * 2.4;
export const ZC_H = 54 * 2.4;

const Dot: React.FC<{ cx: number; cy: number; dia: number; color: string }> = ({ cx, cy, dia, color }) => (
  <div style={{ position: 'absolute', left: cx - dia / 2, top: cy - dia / 2, width: dia, height: dia, borderRadius: dia / 2, background: color }} />
);

export const Hud: React.FC<{
  x: number; y: number;
  hdr?: 'on' | 'off' | 'err';
  armed?: boolean;
  openDs?: number; openZc?: number; // 0..1 展开量
  dsText?: string;
  five?: number; week?: number; // 0..1 填充
  fillK?: number; // 圈填充动画系数
  dotDs?: string; dotF?: string; dotW?: string;
  chDf?: string; chZc?: string;
  alpha?: number;
  t?: number; // 用于 GIF 相位(整片绝对帧)
}> = ({
  x, y, hdr = 'on', armed = false, openDs = 0, openZc = 0,
  dsText = '¥ 42.13', five = 0.66, week = 0.57, fillK = 1,
  dotDs = HUD_COLORS.dotG, dotF = HUD_COLORS.dotG, dotW = HUD_COLORS.dotG,
  chDf = 'df-normal', chZc = 'zc-normal', alpha = 1, t = 0,
}) => {
  const u = 2.4;
  const W = 104 * u;
  const dsH = DS_H * openDs;
  const zcH = ZC_H * openZc;
  const H = closedH + dsH + zcH;
  const row = (top: number) => top + 76 * u;
  const yHandle = 0;
  const yHdr = 16 * u;
  const ySep1 = yHdr + 76 * u;
  const yRow1 = ySep1 + 7 * u;
  const yDs = yRow1 + 76 * u;
  const ySep2 = yDs + dsH;
  const yRow2 = ySep2 + 7 * u;
  const yZc = yRow2 + 76 * u;
  const gifSz = 74 * u;
  const btn = 58 * u;

  const btnFill = armed ? HUD_COLORS.armed : hdr === 'on' ? HUD_COLORS.on : hdr === 'err' ? '#8A6400' : HUD_COLORS.off;
  const btnTxt = armed ? '确认?' : 'HDR';
  const btnTxtColor = armed ? '#FFFFFF' : hdr === 'on' ? HUD_COLORS.onTxt : HUD_COLORS.offTxt;
  const btnFont = armed ? 13 * u : 14.5 * u;

  const ring = (cx: number, cy: number, pct: number, color: string) => {
    const r = 17 * u;
    const sw = 3.5 * u;
    const C = 2 * Math.PI * r;
    return (
      <div key={cx} style={{ position: 'absolute', left: cx - r - sw / 2, top: cy - r - sw / 2, width: (r + sw / 2) * 2, height: (r + sw / 2) * 2 }}>
        <svg width={(r + sw / 2) * 2} height={(r + sw / 2) * 2} style={{ transform: 'rotate(-90deg)' }}>
          <circle cx={r + sw / 2} cy={r + sw / 2} r={r} fill="none" stroke="rgba(255,255,255,0.16)" strokeWidth={sw} />
          {pct > 0.003 && (
            <circle cx={r + sw / 2} cy={r + sw / 2} r={r} fill="none" stroke={color} strokeWidth={sw}
              strokeDasharray={`${C * Math.min(1, pct)} ${C}`} strokeLinecap="butt" />
          )}
        </svg>
        <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <span style={{ fontSize: 9.5 * u, fontWeight: 700, color: '#F2F5FA' }}>{Math.round(pct * 100)}</span>
        </div>
      </div>
    );
  };

  return (
    <div style={{ position: 'absolute', left: x, top: y, width: W, height: H, opacity: alpha, fontFamily: "'Microsoft YaHei'" }}>
      {/* 面板:半透明暗纱 r24,无投影无高光(与实现一致) */}
      <div style={{ position: 'absolute', inset: 0, borderRadius: 24 * u, background: HUD_COLORS.panel, border: `1px solid ${HUD_COLORS.border}` }} />
      {/* Home Indicator 把手 46x5 r2.5 */}
      <div style={{ position: 'absolute', left: W / 2 - 23 * u, top: yHandle + 8 * u - 2.5 * u, width: 46 * u, height: 5 * u, borderRadius: 2.5 * u, background: HUD_COLORS.pill }} />
      {/* HDR 钮 58x58 r16 平涂+1px 边,无光晕 */}
      <div style={{
        position: 'absolute', left: W / 2 - btn / 2, top: yHdr + 38 * u - btn / 2, width: btn, height: btn,
        borderRadius: 16 * u, background: btnFill, border: `1px solid ${HUD_COLORS.btnBorder}`,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
      }}>
        <span style={{ fontSize: btnFont, fontWeight: 700, color: btnTxtColor }}>{btnTxt}</span>
      </div>
      {/* 分隔线 margin 10/3 */}
      {[ySep1 + 3 * u, ySep2 + 3 * u].map((sy, i) => (
        <div key={i} style={{ position: 'absolute', left: 10 * u, top: sy, width: W - 20 * u, height: 1, background: HUD_COLORS.sep }} />
      ))}
      {/* 角色行:GIF 74 居中 */}
      <div style={{ position: 'absolute', left: W / 2 - gifSz / 2, top: yRow1 + (76 * u - gifSz) / 2, width: gifSz, height: gifSz }}>
        <Gif src={staticFile(`gifs/anim-${chDf}.gif`)} width={gifSz} height={gifSz} fit="fill" />
      </div>
      <div style={{ position: 'absolute', left: W / 2 - gifSz / 2, top: yRow2 + (76 * u - gifSz) / 2, width: gifSz, height: gifSz }}>
        <Gif src={staticFile(`gifs/anim-${chZc}.gif`)} width={gifSz} height={gifSz} fit="fill" />
      </div>
      {/* 状态点:DS 10px 右下(8,4);ZC 9px 双点(7,3,缝2) */}
      <Dot cx={W - 8 * u - 5 * u} cy={yRow1 + 76 * u - 4 * u - 5 * u} dia={10 * u} color={dotDs} />
      <Dot cx={W - 7 * u - 4.5 * u} cy={yRow2 + 76 * u - 3 * u - 4.5 * u - 5.5 * u} dia={9 * u} color={dotF} />
      <Dot cx={W - 7 * u - 4.5 * u} cy={yRow2 + 76 * u - 3 * u - 4.5 * u} dia={9 * u} color={dotW} />
      {/* DS 抽屉:面板内下展,余额 17px bold */}
      {openDs > 0.01 && (
        <div style={{ position: 'absolute', left: 0, top: yDs, width: W, height: dsH, overflow: 'hidden' }}>
          <div style={{
            transform: `translateY(${(1 - Math.min(1, openDs * 1.4)) * 12 * u}px)`,
            marginTop: 6 * u, textAlign: 'center',
            fontSize: 17 * u, fontWeight: 700, color: HUD_COLORS.balance,
            opacity: Math.min(1, openDs * 1.6),
          }}>{dsText}</div>
        </div>
      )}
      {/* ZC 抽屉:双圈 34 stroke3.5 + 标签 9.5 */}
      {openZc > 0.01 && (
        <div style={{ position: 'absolute', left: 0, top: yZc, width: W, height: zcH, overflow: 'hidden' }}>
          {[
            { lab: '5时', pct: five, cx: W / 2 - 21 * u },
            { lab: '本周', pct: week, cx: W / 2 + 21 * u },
          ].map(({ lab, pct, cx }) => {
            const kk = Math.min(1, Math.max(0, (openZc - 0.15) * 1.4)) * fillK;
            return (
              <div key={lab}>
                {ring(cx, 21 * u, pct * kk, pct > 0.5 ? HUD_COLORS.dotG : HUD_COLORS.dotY)}
                <div style={{ position: 'absolute', left: cx - 19 * u, top: 42 * u, width: 38 * u, textAlign: 'center', fontSize: 9.5 * u, color: HUD_COLORS.ringCap, opacity: Math.max(0.35, openZc) }}>{lab}</div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

// 半隐藏 sliver:贴右缘只露 12 DIP
export const TuckedHud: React.FC<{ frameRight?: number; alpha?: number }> = ({ frameRight = 1920, alpha = 1 }) => {
  const u = 2.4;
  const W = 104 * u;
  const x = frameRight - 12 * u;
  return (
    <div style={{ position: 'absolute', left: x, top: 0, width: W, height: closedH, opacity: alpha }}>
      <div style={{ position: 'absolute', inset: 0, borderRadius: 24 * u, background: 'rgba(30,34,44,0.95)', border: '1px solid rgba(255,255,255,0.31)' }} />
      <div style={{ position: 'absolute', left: 4 * u, top: closedH / 2, width: 5 * u, height: 2 * u, background: 'rgba(255,255,255,0.59)' }} />
    </div>
  );
};
