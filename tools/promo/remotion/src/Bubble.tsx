// NotifyBubble 复刻 —— 288 DIP 宽 r12 #2A2A2E@E8,标题13bold/正文12,
// 尾巴 11x11 菱形贴右缘指向角色头;顶边=角色行上方分割线(agent-hud.cs ShowEventBubble)
import React from 'react';

export const BUB = {
  bg: 'rgba(42,42,46,0.91)',
  border: 'rgba(255,255,255,0.3)',
  title: '#F4F6FA',
  body: '#CCCCCC',
  btnBg0: '#F3F3F3',
  btnBg1: '#EBEBEB',
  btnBorder: '#707070',
  btnTxt: '#1A1A1A',
  btnOkBg: 'linear-gradient(#D4ECD2, #A0CD9E)',
};

const BUB_W = 288 * 2.4;
const PAD = 12 * 2.4;

export const bubbleHeightD = (hasButtons: boolean) =>
  (12 + 17 + 5 + (hasButtons ? 16 + 10 + 30 : 16) + 12) * 2.4;

export const Bubble: React.FC<{
  xRight: number; top: number; tailY: number;
  title: string; body: string;
  buttons?: string[]; clicked?: string;
  alpha?: number; scale?: number;
}> = ({ xRight, top, tailY, title, body, buttons, clicked, alpha = 1, scale = 1 }) => {
  const H = bubbleHeightD(!!buttons);
  const u = 2.4;
  const w = BUB_W * scale;
  const h = H * scale;
  const px = xRight - w; // 气泡右缘
  const tailSize = 11 * u;
  const btnFont = 12.5 * u;
  return (
    <div style={{ position: 'absolute', left: px, top, opacity: alpha, fontFamily: "'Microsoft YaHei'" }}>
      <div style={{
        position: 'absolute', left: 0, top: 0, width: w, height: h,
        borderRadius: 12 * u * scale, background: BUB.bg, border: `1px solid ${BUB.border}`, padding: PAD * scale,
        boxSizing: 'border-box',
      }}>
        <div style={{ fontSize: 13 * u * scale, fontWeight: 700, color: BUB.title }}>{title}</div>
        <div style={{ fontSize: 12 * u * scale, color: BUB.body, marginTop: 5 * u * scale, lineHeight: 1.35 }}>{body}</div>
        {buttons && (
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 6 * u, marginTop: 10 * u * scale }}>
            {buttons.map((lab) => {
              const pressed = clicked === lab;
              return (
                <div key={lab} style={{
                  padding: `${3 * u * scale}px ${12 * u * scale}px ${4 * u * scale}px`,
                  borderRadius: 3 * u * scale,
                  background: pressed ? BUB.btnOkBg : `linear-gradient(${BUB.btnBg0}, ${BUB.btnBg1})`,
                  border: `1px solid ${pressed ? 'rgba(60,160,90,0.9)' : BUB.btnBorder}`,
                  fontSize: btnFont, color: pressed ? '#143C23' : BUB.btnTxt,
                }}>{lab}</div>
              );
            })}
          </div>
        )}
      </div>
      {/* 尾巴:菱形贴右缘,指向角色头 */}
      <div style={{
        position: 'absolute', left: w - tailSize / 2 - 3 * u, top: (tailY - top) * scale - tailSize / 2,
        width: tailSize, height: tailSize, borderRadius: 2 * u, background: BUB.bg,
        transform: 'rotate(45deg)', border: `1px solid ${BUB.border}`,
      }} />
    </div>
  );
};
