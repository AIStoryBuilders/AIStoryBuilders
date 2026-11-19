import React from "react";
import { Composition } from "remotion";
import { QuickStart, QuickStartProps } from "./QuickStart";
import { FPS, HEIGHT, WIDTH, totalFrames } from "./timing";

export const RemotionRoot: React.FC = () => (
  <>
    <Composition
      id="QuickStart"
      component={QuickStart}
      durationInFrames={totalFrames()}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
      defaultProps={{ captions: true } satisfies QuickStartProps}
    />
    <Composition
      id="QuickStartNoCaptions"
      component={QuickStart}
      durationInFrames={totalFrames()}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
      defaultProps={{ captions: false } satisfies QuickStartProps}
    />
    <Composition
      id="QuickStartVertical"
      component={QuickStart}
      durationInFrames={totalFrames()}
      fps={FPS}
      width={HEIGHT}
      height={WIDTH}
      defaultProps={{ captions: true, vertical: true } satisfies QuickStartProps}
    />
  </>
);
