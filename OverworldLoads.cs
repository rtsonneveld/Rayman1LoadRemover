using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class OverworldLoads
    {

        public static int backSignLoadMaxDuration = 10;

        public static List<Load> GetOverworldLoads(VideoCapture capture, float scale, float timeStart, float timeEnd,
            LoadType loadTypes,
            Action<LoadRemover.ProgressPhase, float> updateProgress)
        {
            List<Load> loads = new List<Load>();
            var binocularFrames = FindBinocularFrames(capture, timeStart, timeEnd);
            List<int> startingFrames = new List<int>();
            List<int> endingFrames = new List<int>();
            int lastFrame = -2;
            foreach (var frame in binocularFrames) {
                if (frame > lastFrame + 1) {
                    startingFrames.Add(frame);
                    endingFrames.Add(lastFrame);
                }

                lastFrame = frame;
            }

            float fps = (float)capture.Fps;
            int progress = 0;

            if (loadTypes.HasFlag(LoadType.Overworld)) {
                foreach (var f in startingFrames) {

                    updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_6_OverworldLoads,
                        0.5f + ((progress++) / (float) startingFrames.Count) * 0.25f);

                    // 30 fps -> 10 frozen frames
                    // 60 fps -> 20 frozen frames
                    Rect cropRect = new Rect(0, 0, (int) (capture.FrameWidth * 0.5f), capture.FrameHeight);
                    var _loadStart = Util.CountFrozenFrames(LoadType.Overworld, capture, f / fps, (int) (fps / 3),
                        (int) fps * 25, cropRect);

                    if (!_loadStart.HasValue) {
                        continue;
                    }

                    var loadStart = _loadStart.Value;

                    // Check when the life counter first appears, skip three seconds
                    for (int i = (int) fps * 3; i < fps * 20; i++) {
                        var lifeCount = LifeCounter.GetLifeCount(capture, (f + i) / fps, scale);
                        if (lifeCount >= 0) {

                            loads.Add(new Load(LoadType.Overworld, loadStart.FrameStart, f + i));
                            break;
                        }
                    }

                }
            }

            updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_6_OverworldLoads, 0.66f);

            progress = 0;


            if (loadTypes.HasFlag(LoadType.BackSign)) {

                foreach (var f in startingFrames) {

                    updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_6_OverworldLoads,
                        0.75f + ((progress++) / (float) startingFrames.Count) * 0.25f);

                    // Check back sign loads
                    var _backSignLoad = Util.CountFrozenFrames(LoadType.BackSign, capture,
                        (f / fps) - backSignLoadMaxDuration, (int) fps * 1, (int) fps * backSignLoadMaxDuration);

                    if (!_backSignLoad.HasValue) {
                        continue;
                    }

                    loads.Add(_backSignLoad.Value);
                }
            }

            return loads;

        }

        public static List<int> FindBinocularFrames(VideoCapture capture, float timeStart, float timeEnd)
        {
            int fps = (int)capture.Fps;

            List<int> firstPassFrames = new List<int>();
            List<int> frames = new List<int>();

            for (float time = timeStart; time < timeEnd; time += 0.5f) { // 1 second is too coarse

                capture.Set(VideoCaptureProperties.PosMsec, time*1000.0f);
                int frame = (int)capture.Get(VideoCaptureProperties.PosFrames);

                Mat mat = new Mat();
                capture.Read(mat);

                bool isBinocular = IsBinocularFrame(mat);
                if (isBinocular) {
                    firstPassFrames.Add(frame);
                }
            }

            foreach (var f in firstPassFrames) {
                for (int i = f-fps + 1; i < f+fps; i++) {

                    capture.Set(VideoCaptureProperties.PosFrames, i);

                    Mat mat = new Mat();
                    capture.Read(mat);

                    bool isBinocular = IsBinocularFrame(mat);
                    if (isBinocular) {
                        frames.Add(i);
                    }
                }
            }

            frames.Sort();
            return frames;
        }

        public static bool IsBinocularFrame(Mat mat)
        {
            if (Util.GetAverageBrightness(mat[0, 5, 0, 5]) > Util.DarknessMaxBrightness) {
                return false;
            }

            float height = 0.4583f;
            int rowFrom = (int) (0.5 * mat.Height - (height*0.5f)*mat.Height);
            int rowTo = (int) (0.5 * mat.Height + (height*0.5f)*mat.Height);

            float columnWidth = 0.145f;

            var centerTopPart = mat[(int)(0.21558 * mat.Height), (int)(0.27399 * mat.Height),
                (int)(0.4729 * mat.Width), (int)(0.52917 * mat.Width)];

            var brightnessCenterTop = Util.GetAverageBrightness(centerTopPart);

            if (brightnessCenterTop > Util.DarknessMaxBrightness) return false;

            var leftColumn = mat[rowFrom, rowTo, 0, (int)(columnWidth * mat.Width)];

            var brightnessLeft = Util.GetAverageBrightness(leftColumn);

            if (brightnessLeft > Util.DarknessMaxBrightness) return false;

            var rightColumn = mat[rowFrom, rowTo, (int)((1 - columnWidth) * mat.Width), mat.Width];

            var brightnessRight = Util.GetAverageBrightness(rightColumn);

            if (brightnessRight > Util.DarknessMaxBrightness) return false;

            var centerPart = mat[rowFrom, rowTo,
                (int)(0.236f * mat.Width), (int)((1 - 0.236f) * mat.Width)];

            var brightnessCenter = Util.GetAverageBrightness(centerPart);

            if (brightnessCenter < Util.BrightMinBrightness) return false;

            return true;
        }
    }
}
