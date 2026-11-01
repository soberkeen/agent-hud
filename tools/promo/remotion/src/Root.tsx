// Root:4K60 Composition —— 设计空间 1920×1080,根节点 scale(2) 栅格化到 3840×2160
// 音轨 = NARR(GPT-SoVITS 女声) + SFX 钉帧表(相对帧号,Mixkit);无 BGM(用户硬约束)
import React from 'react';
import { Composition, AbsoluteFill, Sequence, Audio, staticFile, useCurrentFrame, interpolate } from 'remotion';
import { FPS, DUR, SHOTS, NARR, SFX } from './spec';
import { S1Spotlight, S2DockSnap } from './scenes/Scenes12';
import { S3Hdr, S4Drawers } from './scenes/Scenes34';
import { S5Bubbles, S6Moods } from './scenes/Scenes58';
import { S7Usage, S8Outro } from './scenes/Scenes78';

const GlobalFade: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const f = useCurrentFrame();
  const a = interpolate(f, [0, 42, DUR - 54, DUR], [0, 1, 1, 0], { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' });
  return <AbsoluteFill style={{ opacity: a }}>{children}</AbsoluteFill>;
};

const Scenes: React.FC = () => {
  return (
    <AbsoluteFill style={{ width: 1920, height: 1080, background: '#05060C' }}>
      {([
        [SHOTS.s1, S1Spotlight],
        [SHOTS.s2, S2DockSnap],
        [SHOTS.s3, S3Hdr],
        [SHOTS.s4, S4Drawers],
        [SHOTS.s5, S5Bubbles],
        [SHOTS.s6, S6Moods],
        [SHOTS.s7, S7Usage],
        [SHOTS.s8, S8Outro],
      ] as const).map(([sh, C]) => (
        <Sequence key={sh.from} from={sh.from} durationInFrames={sh.dur}>
          <C />
        </Sequence>
      ))}
    </AbsoluteFill>
  );
};

const AudioTracks: React.FC = () => {
  return (
    <>
      {/* 口播:GPT-SoVITS 女声(13 句,轴见 spec.NARR) */}
      {NARR.map((n) => (
        <Sequence key={n.key} from={n.from}>
          <Audio src={staticFile(`audio/nar/${n.key}.wav`)} volume={0.95} />
        </Sequence>
      ))}
      {/* SFX 钉帧表:长样本按 dur 截断(sound-design 4.1) */}
      {SFX.map((s, i) => (
        <Sequence key={i} from={s.from} durationInFrames={s.dur ?? 90}>
          <Audio src={staticFile(`audio/${s.src}`)} volume={s.volume} />
        </Sequence>
      ))}
    </>
  );
};

export const Promo: React.FC = () => (
  <AbsoluteFill style={{ background: '#05060C' }}>
    <div style={{ width: 3840, height: 2160, transform: 'scale(2)', transformOrigin: 'top left' }}>
      <GlobalFade>
        <Scenes />
      </GlobalFade>
    </div>
    <AudioTracks />
  </AbsoluteFill>
);

export const RemotionRoot: React.FC = () => (
  <>
    <Composition id="Promo" component={Promo} durationInFrames={DUR} fps={FPS} width={3840} height={2160} />
  </>
);
