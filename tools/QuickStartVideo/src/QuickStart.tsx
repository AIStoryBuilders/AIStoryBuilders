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
import { CLOSER_FRAMES, LEAD, OPENER_FRAMES, Scene, TRANSITION, audioFrames, sceneFrames, scenes } from "./timing";

export type QuickStartProps = {
  /** Show the narration as on-screen captions (off for sites that make their own). */
  captions: boolean;
  /** 1080x1920 layout for Shorts and Reels instead of 1920x1080. */
  vertical?: boolean;
};

const FONT = '"Segoe UI", Inter, system-ui, sans-serif';
const ACCENT = "#a5b4fc";
/** Screenshot size in pixels; focus points in scenes.json use these coordinates. */
const SHOT_W = 1280;
const SHOT_H = 800;

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
const Caption: React.FC<{ text: string; frames: number; vertical?: boolean }> = ({ text, frames, vertical }) => {
  const frame = useCurrentFrame() - LEAD;
  // Split only where punctuation is followed by a space, so "AIStoryBuilders.com" stays whole.
  const sentences = text.split(/(?<=[.!?])\s+/).filter((s) => s.length > 0);
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
        fontSize: vertical ? 54 : 38,
        fontWeight: vertical ? 600 : 400,
        lineHeight: 1.3,
        color: "white",
        textAlign: "center",
        maxWidth: vertical ? 940 : 1500,
        margin: "0 auto",
        opacity,
        textShadow: "0 2px 12px rgba(0,0,0,0.5)",
      }}
    >
      {sentences[index]}
    </div>
  );
};

/** Where the vertical layout puts the screenshot: the full 16:10 screenshot, full width. */
const VERTICAL_CARD = { left: 20, top: 560, width: 1040, height: 650 };

/** The whole screenshot in a card. The landscape layout slowly zooms toward the scene's
 *  focus point; the vertical layout passes zoom 1 so nothing is cut off. */
const ScreenshotCard: React.FC<{ scene: Scene; vertical: boolean; enter: number; zoom: number }> = ({
  scene,
  vertical,
  enter,
  zoom,
}) => {
  const [fx, fy] = scene.focus ?? [SHOT_W / 2, SHOT_H / 2];
  const card = vertical ? VERTICAL_CARD : { left: 288, top: 36, width: 1344, height: 840 };
  return (
    <div
      style={{
        position: "absolute",
        ...card,
        borderRadius: vertical ? 28 : 18,
        overflow: "hidden",
        boxShadow: "0 30px 80px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.08)",
        background: "#ecebf2",
        transform: `translateY(${(1 - enter) * 24}px)`,
      }}
    >
      <Img
        src={staticFile(`images/${scene.image}`)}
        style={{
          position: "absolute",
          left: 0,
          top: 0,
          width: card.width,
          height: card.height,
          objectFit: "cover",
          objectPosition: "top left",
          transformOrigin: `${(fx / SHOT_W) * 100}% ${(fy / SHOT_H) * 100}%`,
          transform: `scale(${zoom})`,
        }}
      />
    </div>
  );
};

const ScreenScene: React.FC<{ scene: Scene; step: number; steps: number; captions: boolean; vertical: boolean }> = ({
  scene,
  step,
  steps,
  captions,
  vertical,
}) => {
  const frame = useCurrentFrame();
  const { durationInFrames, fps } = useVideoConfig();
  const zoom = vertical
    ? 1
    : interpolate(frame, [0, durationInFrames], [1, scene.zoom ?? 1.15], {
        easing: Easing.inOut(Easing.cubic),
        extrapolateRight: "clamp",
      });
  const enter = spring({ frame, fps, config: { damping: 200 }, durationInFrames: 20 });
  const label = (
    <div
      style={{
        fontFamily: FONT,
        fontSize: vertical ? 34 : captions ? 24 : 34,
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
  );
  const caption = captions ? (
    <Caption text={scene.caption ?? scene.text} frames={audioFrames(scene.id)} vertical={vertical} />
  ) : null;
  return (
    <AbsoluteFill>
      <Background />
      {vertical ? (
        <>
          <div style={{ position: "absolute", left: 0, right: 0, top: 360, textAlign: "center" }}>
            <BrandLine />
            <div style={{ height: 40 }} />
            {label}
          </div>
          <ScreenshotCard scene={scene} vertical enter={enter} zoom={zoom} />
          {/* Kept above the bottom ~400 px, where Shorts and Reels draw their own buttons and text. */}
          <div
            style={{
              position: "absolute",
              left: 0,
              right: 0,
              top: VERTICAL_CARD.top + VERTICAL_CARD.height + 60,
              textAlign: "center",
            }}
          >
            {caption}
          </div>
        </>
      ) : (
        <>
          <ScreenshotCard scene={scene} vertical={false} enter={enter} zoom={zoom} />
          <div style={{ position: "absolute", left: 0, right: 0, top: captions ? 896 : 930, textAlign: "center" }}>
            {label}
            {caption}
          </div>
        </>
      )}
      <Sequence from={LEAD}>
        <Audio src={staticFile(`audio/${scene.id}.mp3`)} />
      </Sequence>
    </AbsoluteFill>
  );
};

/** Small logo and name shown above each screen in the vertical layout. */
const BrandLine: React.FC = () => (
  <div style={{ display: "flex", alignItems: "center", justifyContent: "center", gap: 18 }}>
    <Img src={staticFile("images/logo.png")} style={{ width: 72, height: 72, objectFit: "contain" }} />
    <div style={{ fontFamily: FONT, fontSize: 44, fontWeight: 800, color: "white" }}>AI StoryBuilders 4.0</div>
  </div>
);

const TitleScene: React.FC<{ scene: Scene; captions: boolean; vertical: boolean }> = ({ scene, captions, vertical }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const logo = spring({ frame, fps, config: { damping: 14 }, durationInFrames: 30 });
  const text = spring({ frame: frame - 8, fps, config: { damping: 200 }, durationInFrames: 25 });
  return (
    <AbsoluteFill>
      <Background />
      <AbsoluteFill
        style={{
          alignItems: "center",
          justifyContent: "center",
          flexDirection: "column",
          gap: 28,
          paddingBottom: vertical ? 300 : 0,
        }}
      >
        <Img
          src={staticFile("images/logo.png")}
          style={{
            width: vertical ? 360 : 240,
            height: vertical ? 360 : 240,
            objectFit: "contain",
            transform: `scale(${0.6 + 0.4 * logo})`,
            opacity: logo,
          }}
        />
        <div
          style={{
            fontFamily: FONT,
            fontSize: vertical ? 90 : 104,
            fontWeight: 800,
            color: "white",
            letterSpacing: -1,
            textAlign: "center",
            maxWidth: vertical ? 980 : undefined,
            lineHeight: 1.1,
            opacity: text,
            transform: `translateY(${(1 - text) * 30}px)`,
          }}
        >
          {scene.title}
        </div>
        <div
          style={{
            fontFamily: FONT,
            fontSize: vertical ? 50 : 46,
            color: ACCENT,
            textAlign: "center",
            maxWidth: vertical ? 940 : undefined,
            opacity: text,
          }}
        >
          {scene.subtitle}
        </div>
      </AbsoluteFill>
      {captions ? (
        <div style={{ position: "absolute", left: 0, right: 0, top: vertical ? 1420 : 930 }}>
          <Caption text={scene.caption ?? scene.text} frames={audioFrames(scene.id)} vertical={vertical} />
        </div>
      ) : null}
      <Sequence from={LEAD}>
        <Audio src={staticFile(`audio/${scene.id}.mp3`)} />
      </Sequence>
    </AbsoluteFill>
  );
};

/** A 16:9 clip (opener or closer). In the vertical layout it is centre-cropped to fill the frame. */
const Clip: React.FC<{ src: string }> = ({ src }) => (
  <AbsoluteFill style={{ backgroundColor: "black" }}>
    <OffthreadVideo src={staticFile(src)} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
  </AbsoluteFill>
);

export const QuickStart: React.FC<QuickStartProps> = ({ captions, vertical = false }) => {
  const screens = scenes.filter((s) => s.kind !== "title");
  return (
    <TransitionSeries>
      <TransitionSeries.Sequence durationInFrames={OPENER_FRAMES}>
        <Clip src="opener.mp4" />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={fade()} timing={linearTiming({ durationInFrames: TRANSITION })} />
      {scenes.flatMap((scene) => {
        const body =
          scene.kind === "title" ? (
            <TitleScene scene={scene} captions={captions} vertical={vertical} />
          ) : (
            <ScreenScene
              scene={scene}
              step={screens.indexOf(scene) + 1}
              steps={screens.length}
              captions={captions}
              vertical={vertical}
            />
          );
        return [
          <TransitionSeries.Sequence key={scene.id} durationInFrames={sceneFrames(scene)}>
            {body}
          </TransitionSeries.Sequence>,
          // A fade follows every scene, the last one leading into the closer.
          <TransitionSeries.Transition
            key={`${scene.id}-t`}
            presentation={fade()}
            timing={linearTiming({ durationInFrames: TRANSITION })}
          />,
        ];
      })}
      <TransitionSeries.Sequence durationInFrames={CLOSER_FRAMES}>
        <Clip src="logo-reveal.mp4" />
      </TransitionSeries.Sequence>
    </TransitionSeries>
  );
};
