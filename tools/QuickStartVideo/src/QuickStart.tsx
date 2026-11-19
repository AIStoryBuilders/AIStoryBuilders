import React from "react";
import {
  AbsoluteFill,
  Audio,
  Easing,
  Img,
  OffthreadVideo,
  Sequence,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { TransitionSeries, linearTiming } from "@remotion/transitions";
import { fade } from "@remotion/transitions/fade";
import { LEAD, OPENER_FRAMES, Scene, TRANSITION, audioFrames, sceneFrames, scenes } from "./timing";

export type QuickStartProps = {
  /** Show the narration as on-screen captions (off for sites that make their own). */
  captions: boolean;
};

const FONT = '"Segoe UI", Inter, system-ui, sans-serif';
const ACCENT = "#a5b4fc";

const Background: React.FC = () => (
  <AbsoluteFill
    style={{
      background:
        "radial-gradient(1200px 800px at 20% 10%, #3730a3 0%, transparent 60%), radial-gradient(1000px 700px at 90% 100%, #5b21b6 0%, transparent 55%), #12102e",
    }}
  />
);

/** Splits narration into sentences and shows each one for a share of the clip
 *  proportional to its length. */
const Caption: React.FC<{ text: string; frames: number }> = ({ text, frames }) => {
  const frame = useCurrentFrame() - LEAD;
  const sentences = text.match(/[^.!?]+[.!?]+(\s|$)/g)?.map((s) => s.trim()) ?? [text];
  const total = sentences.reduce((n, s) => n + s.length, 0);
  let start = 0;
  let index = sentences.length - 1;
  for (let i = 0; i < sentences.length; i++) {
    const len = (sentences[i].length / total) * frames;
    if (frame < start + len) {
      index = i;
      break;
    }
    start += len;
  }
  if (frame < 0) index = 0;
  const local = Math.max(0, frame - start);
  const opacity = index === 0 && frame < 0 ? 0 : interpolate(local, [0, 6], [0, 1], { extrapolateRight: "clamp" });
  return (
    <div
      style={{
        fontFamily: FONT,
        fontSize: 38,
        lineHeight: 1.3,
        color: "white",
        textAlign: "center",
        maxWidth: 1500,
        margin: "0 auto",
        opacity,
        textShadow: "0 2px 12px rgba(0,0,0,0.5)",
      }}
    >
      {sentences[index]}
    </div>
  );
};

const ScreenScene: React.FC<{ scene: Scene; step: number; steps: number; captions: boolean }> = ({
  scene,
  step,
  steps,
  captions,
}) => {
  const frame = useCurrentFrame();
  const { durationInFrames, fps } = useVideoConfig();
  const [fx, fy] = scene.focus ?? [640, 400];
  const zoom = interpolate(frame, [0, durationInFrames], [1, scene.zoom ?? 1.15], {
    easing: Easing.inOut(Easing.cubic),
    extrapolateRight: "clamp",
  });
  const enter = spring({ frame, fps, config: { damping: 200 }, durationInFrames: 20 });
  const cardW = 1344;
  const cardH = 840;
  return (
    <AbsoluteFill>
      <Background />
      <div
        style={{
          position: "absolute",
          left: (1920 - cardW) / 2,
          top: 36,
          width: cardW,
          height: cardH,
          borderRadius: 18,
          overflow: "hidden",
          boxShadow: "0 30px 80px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.08)",
          background: "#ecebf2",
          transform: `translateY(${(1 - enter) * 24}px)`,
        }}
      >
        <Img
          src={staticFile(`images/${scene.image}`)}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "cover",
            objectPosition: "top left",
            transformOrigin: `${(fx / 1280) * 100}% ${(fy / 800) * 100}%`,
            transform: `scale(${zoom})`,
          }}
        />
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: captions ? 896 : 930, textAlign: "center" }}>
        <div
          style={{
            fontFamily: FONT,
            fontSize: captions ? 24 : 34,
            fontWeight: 700,
            letterSpacing: 3,
            textTransform: "uppercase",
            color: ACCENT,
            marginBottom: 12,
            opacity: enter,
          }}
        >
          {step} / {steps} · {scene.label}
        </div>
        {captions ? <Caption text={scene.caption ?? scene.text} frames={audioFrames(scene.id)} /> : null}
      </div>
      <Sequence from={LEAD}>
        <Audio src={staticFile(`audio/${scene.id}.mp3`)} />
      </Sequence>
    </AbsoluteFill>
  );
};

const TitleScene: React.FC<{ scene: Scene; captions: boolean }> = ({ scene, captions }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const logo = spring({ frame, fps, config: { damping: 14 }, durationInFrames: 30 });
  const text = spring({ frame: frame - 8, fps, config: { damping: 200 }, durationInFrames: 25 });
  return (
    <AbsoluteFill>
      <Background />
      <AbsoluteFill style={{ alignItems: "center", justifyContent: "center", flexDirection: "column", gap: 28 }}>
        <Img
          src={staticFile("images/logo.png")}
          style={{ width: 240, height: 240, objectFit: "contain", transform: `scale(${0.6 + 0.4 * logo})`, opacity: logo }}
        />
        <div
          style={{
            fontFamily: FONT,
            fontSize: 104,
            fontWeight: 800,
            color: "white",
            letterSpacing: -1,
            opacity: text,
            transform: `translateY(${(1 - text) * 30}px)`,
          }}
        >
          {scene.title}
        </div>
        <div style={{ fontFamily: FONT, fontSize: 46, color: ACCENT, opacity: text }}>{scene.subtitle}</div>
      </AbsoluteFill>
      {captions ? (
        <div style={{ position: "absolute", left: 0, right: 0, top: 930 }}>
          <Caption text={scene.caption ?? scene.text} frames={audioFrames(scene.id)} />
        </div>
      ) : null}
      <Sequence from={LEAD}>
        <Audio src={staticFile(`audio/${scene.id}.mp3`)} />
      </Sequence>
    </AbsoluteFill>
  );
};

export const QuickStart: React.FC<QuickStartProps> = ({ captions }) => {
  const screens = scenes.filter((s) => s.kind !== "title");
  return (
    <TransitionSeries>
      <TransitionSeries.Sequence durationInFrames={OPENER_FRAMES}>
        <OffthreadVideo src={staticFile("opener.mp4")} />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={fade()} timing={linearTiming({ durationInFrames: TRANSITION })} />
      {scenes.flatMap((scene, i) => {
        const body =
          scene.kind === "title" ? (
            <TitleScene scene={scene} captions={captions} />
          ) : (
            <ScreenScene
              scene={scene}
              step={screens.indexOf(scene) + 1}
              steps={screens.length}
              captions={captions}
            />
          );
        const items = [
          <TransitionSeries.Sequence key={scene.id} durationInFrames={sceneFrames(scene)}>
            {body}
          </TransitionSeries.Sequence>,
        ];
        if (i < scenes.length - 1) {
          items.push(
            <TransitionSeries.Transition
              key={`${scene.id}-t`}
              presentation={fade()}
              timing={linearTiming({ durationInFrames: TRANSITION })}
            />,
          );
        }
        return items;
      })}
    </TransitionSeries>
  );
};
