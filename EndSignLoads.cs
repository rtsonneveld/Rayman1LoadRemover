using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class EndSignLoads {

        /// <summary>
        /// Duration before the end sign check can detect the end of the loading screen, in seconds
        /// </summary>
        private const float EndSignLoadMinDuration = 1.0f;
        private const double EndSignLoadMaxDuration = 15.0f;
        private const float MaxBrightnessDeltaForCheck = 0.005f;

        public static List<Load> GetEndSignLoads(VideoCapture capture, float scale, int startingFrame, int endingFrame,
            Action<LoadRemover.ProgressPhase, float> updateProgress)
        {

            List<Load> loads = new List<Load>();
            List<int> potentialFrames = new List<int>();

            Mat lastMat = null;
            float lastBrightness = float.NaN;

            for (int f = startingFrame; f < endingFrame; f += (int)(capture.Fps* EndSignLoadMinDuration*0.5f)) {

                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_5_EndSignLoads, ((float)f/capture.FrameCount)*0.5f);

                capture.PosFrames = f;
                var m = new Mat();
                capture.Read(m);

                float brightness = Util.GetAverageBrightness(m);
                if (float.IsNaN(lastBrightness)) {
                    lastBrightness = brightness;
                }

                if (lastMat != null) {
                    float brightnessDiff = brightness - lastBrightness;

                    if (Math.Abs(brightnessDiff) < MaxBrightnessDeltaForCheck &&
                        Util.GetMaxDifference(m, lastMat) < Util.MaxDifferenceIdenticalFrames) {

                        // Either have a visible lifecount or the left part of the screen isn't dark (to prevent the musician cutscene from being detected)
                        if (LifeCounter.GetLifeCount(capture, f / capture.Fps, scale) >= 0 ||
                            Util.GetAverageBrightness(m[0,m.Height,0,(int)(m.Width*(3f/16))]) > Util.DarknessMaxBrightness) {

                            f += CheckPotentialFrames(capture, f, potentialFrames);
                        }
                    }

                    lastBrightness = brightness;
                }

                lastMat = m;
            }

            int pfProgress = 0;
            foreach (var pf in potentialFrames) {

                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_5_EndSignLoads, 0.5f+((float)pfProgress / potentialFrames.Count) * 0.5f);

                var load = Util.CountFrozenFrames(LoadType.EndSign, capture, pf/capture.Fps,
                    (int)(capture.Fps * EndSignLoadMinDuration), (int)(EndSignLoadMaxDuration*capture.Fps));

                if (load.HasValue) {

                    var loadEndMat = new Mat();
                    capture.PosFrames = (int)(load.Value.FrameEnd + capture.Fps*1.0f); // Check last frame + 1 second
                    capture.Read(loadEndMat);

                    // Not a binocular frame or life count found?
                    //if (!OverworldLoads.IsBinocularFrame(loadEndMat) ||
                        //LifeCounter.GetLifeCount(capture, capture.PosFrames / capture.Fps, scale) >= 0) {
                        loads.Add(load.Value);
                    //}
                }
            }

            return loads;
        }

        private static int CheckPotentialFrames(VideoCapture capture, int f, List<int> potentialFrames)
        {
            int sf;
            float lastBrightness = float.NaN;

            int stepSize = (int)(capture.Fps / 15); // 2 frames for 30 fps, 40 frames for 60 fps
            for (sf = 0; sf < (int) (capture.Fps * EndSignLoadMaxDuration); sf+=stepSize) {

                capture.PosFrames = f + sf;
                if (capture.PosFrames >= capture.FrameCount) {
                    break;
                }

                var sm = new Mat();
                capture.Read(sm);

                float brightness = Util.GetAverageBrightness(sm[sm.Height / 2, sm.Height, 0, sm.Width]);

                if (float.IsNaN(lastBrightness)) lastBrightness = brightness;
                float brightnessDiff = brightness - lastBrightness;

                // Brightness change
                if (Math.Abs(brightnessDiff) > 0.02f) {
                    // Only add as potential frame if the screen suddenly turned dark
                    if (brightness < Util.DarknessMaxBrightness) {
                        potentialFrames.Add(f);
                    }

                    break;
                }

                lastBrightness = brightness;
            }

            return sf;
        }
    }
}
