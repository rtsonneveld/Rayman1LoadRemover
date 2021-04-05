using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class BossLoads {

        private static float BossLoadingScreenMinDuration = 1.0f;
        private static float BossLoadingScreenMaxDuration = 20.0f;
        private static float MaxTimeAfterFinalHit = 180.0f; // TODO change to 120
        private static float EndBossIconMatchThreshold = 0.2f;

        private static Mat ImgEndBoss;
        private static Mat ImgEndBossMask;

        public static void Init()
        {
            ImgEndBoss = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "endboss.png"));
            ImgEndBossMask = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "endbossmask.png"));
        }

        public static int? GetLastFinalBossFrame(VideoCapture capture, float scale, int start=1, int stepSize=-1)
        {
            if (stepSize <= 0) {
                stepSize = (int)capture.Fps;
            }

            capture.Set(VideoCaptureProperties.PosFrames, capture.FrameCount-1);

            int maxFrames = (int)(MaxTimeAfterFinalHit * capture.Fps);
            for (int i = start; i < maxFrames; i+=stepSize) {

                // Start from the end of the video and move backwards to find the last occurrence of the boss icon
                capture.Set(VideoCaptureProperties.PosFrames, capture.FrameCount - i);

                Mat mat = new Mat();
                capture.Read(mat);

                Cv2.Resize(mat, mat, new Size(mat.Width * scale, mat.Height * scale));
                // Crop to the bottom-left corner, since that's where the lives are
                mat = mat[(int)(mat.Height * 0.83f), mat.Height, 0, (int)(mat.Width * 0.085f)];

                Mat result = new Mat();
                Cv2.MatchTemplate(InputArray.Create(mat), InputArray.Create(ImgEndBoss), result,
                    TemplateMatchModes.SqDiffNormed, InputArray.Create(ImgEndBossMask));

                result.MinMaxLoc(out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);
                var timespan = TimeSpan.FromSeconds((capture.FrameCount - i) / (float)capture.Fps);
                Debug.WriteLine($"MinVal @{timespan:g}: {minVal}");

                if (minVal < EndBossIconMatchThreshold) {
                    if (stepSize == 1) {
                        return (capture.FrameCount - i) + 1;
                    } else {
                        return GetLastFinalBossFrame(capture, scale, i-stepSize, 1);
                    }
                }

            }

            return null;
        }

        public static Load GetBossLoad(VideoCapture capture, double startTime)
        {

            var loadBoss = Util.CountFrozenFrames(LoadType.Boss, capture, startTime,
                (int)(capture.Fps * BossLoadingScreenMinDuration), (int)(capture.Fps*BossLoadingScreenMaxDuration));

            if (loadBoss.HasValue) {

                Mat frame = new Mat();
                capture.Set(VideoCaptureProperties.PosFrames, loadBoss.Value.FrameStart);
                capture.Read(frame);
                //Cv2.MatchTemplate(new InputArray(frame), new InputArray(imgBossCutscene1),)

                var topPart = frame[0, (int) (0.16f * frame.Height), 0, frame.Width];
                var botPart = frame[(int) ((1.0f - 0.13f) * frame.Height), frame.Height, 0, frame.Width];

                // top 0.16, bottom 0.13
                float topBrightness = Util.GetAverageBrightness(topPart);
                float botBrightness = Util.GetAverageBrightness(botPart);

                // Cutscene image? Count darkness frames instead
                if (topBrightness < Util.DarknessMaxBrightness && botBrightness < Util.DarknessMaxBrightness) {

                    return Util.CountDarknessFrames(LoadType.Boss, capture, loadBoss.Value.FrameStart / capture.Fps, (int)(capture.Fps * BossLoadingScreenMaxDuration));
                } else {

                    return loadBoss.Value;
                }
            }

            throw new Exception($"Failed to determine Boss Load at {startTime} seconds");
        }
    }
}
