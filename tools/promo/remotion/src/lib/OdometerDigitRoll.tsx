// odometer-digit-roll 卡适配(demos/data/odometer-digit-roll 同源语法):
// 每位 overflow 盒 + 0-9×2 纵列,16f 减速 + 过冲半格 + 6f 弹回;滚动期 2 副本残影速度门控
import React from 'react';
import { interpolate, useCurrentFrame, Easing } from 'remotion';

const DIGITS = '0123456789';

const Strip: React.FC<{ pos: number; lineH: number; fontSize: number; color: string; opacity?: number }> =
  ({ pos, lineH, fontSize, color, opacity = 1 }) => (
    <span style={{ display: 'block', transform: `translateY(${-pos * lineH}px)`, opacity }}>
      {(DIGITS + DIGITS).split('').map((dg, j) => (
        <span key={j} style={{ display: 'block', fontSize, lineHeight: `${lineH}px`, color, fontWeight: 800, fontVariantNumeric: 'tabular-nums', textAlign: 'center' }}>
          {dg}
        </span>
      ))}
    </span>
  );

export const OdometerDigitRoll: React.FC<{
  value: string;
  delay?: number;
  fontSize?: number;
  color?: string;
  stagger?: number; // 每位错峰帧(卡:7f)
}> = ({ value, delay = 0, fontSize = 190, color = '#F2F5FA', stagger = 7 }) => {
  const frame = useCurrentFrame();
  const lineH = fontSize * 1.15;
  return (
    <div style={{ display: 'inline-flex', overflow: 'hidden', height: lineH, fontFamily: "'Microsoft YaHei'" }}>
      {value.split('').map((ch, i) => {
        const target = DIGITS.indexOf(ch);
        if (target < 0) {
          return <span key={i} style={{ fontSize, lineHeight: `${lineH}px`, color, fontWeight: 800 }}>{ch}</span>;
        }
        const start = delay + i * stagger;
        const rolling = frame >= start && frame < start + 22;
        if (frame < start) {
          // 未开始:静态显示终值(不显示 0)
          return (
            <span key={i} style={{ display: 'inline-block', height: lineH, overflow: 'hidden', width: fontSize * 0.62, position: 'relative' }}>
              <Strip pos={target} lineH={lineH} fontSize={fontSize} color={color} />
            </span>
          );
        }
        const over = interpolate(frame, [start, start + 16, start + 22], [0, 10 + target + 0.5, 10 + target], {
          extrapolateLeft: 'clamp', extrapolateRight: 'clamp',
          easing: Easing.bezier(0.25, 0.8, 0.25, 1),
        });
        const pos = rolling ? over : 10 + target;
        return (
          <span key={i} style={{ display: 'inline-block', height: lineH, overflow: 'hidden', width: fontSize * 0.62, position: 'relative' }}>
            <Strip pos={pos} lineH={lineH} fontSize={fontSize} color={color} />
            {rolling && (
              <>
                <span style={{ position: 'absolute', left: 0, top: 0, width: '100%' }}>
                  <Strip pos={pos + 0.5} lineH={lineH} fontSize={fontSize} color={color} opacity={0.25} />
                </span>
                <span style={{ position: 'absolute', left: 0, top: 0, width: '100%' }}>
                  <Strip pos={pos - 0.5} lineH={lineH} fontSize={fontSize} color={color} opacity={0.12} />
                </span>
              </>
            )}
          </span>
        );
      })}
    </div>
  );
};
