// 缓动/几何工具 —— 动效 tokens:活泼愉悦(过冲 1.08,squash 0.18),落地弹 y1>1
export const clamp01 = (x: number) => (x < 0 ? 0 : x > 1 ? 1 : x);
export const seg = (f: number, a: number, b: number) => (b > a ? clamp01((f - a) / (b - a)) : 1);
export const lerp = (a: number, b: number, k: number) => a + (b - a) * k;

// 入场过冲统一用 outBack(s≈1.56,≈bezier(0.34,1.56,0.64,1) 的弹簧读感)
export const outBack = (x: number, s = 1.56): number => {
  const t = clamp01(x) - 1;
  return 1 + t * t * ((s + 1) * t + s);
};
export const outCubic = (x: number): number => {
  const t = clamp01(x);
  return 1 - Math.pow(1 - t, 3);
};
export const outQuint = (x: number): number => {
  const t = clamp01(x);
  return 1 - Math.pow(1 - t, 5);
};
export const inOutSine = (x: number): number => {
  const t = clamp01(x);
  return 0.5 - 0.5 * Math.cos(t * Math.PI);
};
export const inCubic = (x: number): number => {
  const t = clamp01(x);
  return t * t * t;
};
// dampened settle(assets/lib helpers/motion dampedSettle 同源)
export const dampedSettle = (t: number, freq = 0.1, damping = 0.15): number =>
  t <= 0 ? 0 : Math.exp(-damping * t) * Math.sin(2 * Math.PI * freq * t);
