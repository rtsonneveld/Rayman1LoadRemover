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
        private static float EndBossIconMatchThreshold = 0.3f;
        private static float DelayAfterLastMosquitoHealthBar = 15.0f; // Don't check for frozen frames immediately after bzzit/mosquito is finished but wait 15 seconds
        private static float DelayAfterLastBzzitHealthBar = 10.0f;

        private static Mat ImgEndBoss;
        private static Mat ImgEndBossMask;

        public enum BossType
        {
            Bzzit,
            Mosquito,
            Sax,
            Stone,
            VikingMama,
            SpaceMama,
            Skops,
            MrDark,
            _NumTypes,
        }

        private static Dictionary<BossType,Mat> ImgBoss;
        private static Dictionary<BossType,Mat> ImgBossMask;

        public static void Init()
        {
            ImgEndBoss = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "boss_mrdark.png"));
            ImgEndBossMask = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "boss_mrdark_mask.png"));

            ImgBoss = new Dictionary<BossType, Mat>();
            ImgBossMask = new Dictionary<BossType, Mat>();

            AddBossIcon(BossType.Bzzit,"bzzit");
            AddBossIcon(BossType.Mosquito,"bzzit");
            AddBossIcon(BossType.Sax,"sax");
            AddBossIcon(BossType.Stone,"stone");
            AddBossIcon(BossType.VikingMama,"vikingmama");
            AddBossIcon(BossType.SpaceMama,"spacemama");
            AddBossIcon(BossType.Skops,"skops");
            AddBossIcon(BossType.MrDark,"mrdark");
        }

        private static void AddBossIcon(BossType type, string filename)
        {
            ImgBoss.Add(type, Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, $"boss_{filename}.png")));
            ImgBossMask.Add(type, Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, $"boss_{filename}_mask.png")));
        }

        public static List<(BossType,int)> GetBossEndFrames(VideoCapture capture, float scale, int startingFrame, int endingFrame)
        {
            var bossEndFrames = new List<(BossType,int)>();

            float duration = (float)(endingFrame / capture.Fps);
            float start = (float)(startingFrame / capture.Fps);

            for (float i = start; i < duration; i += 10) {
                
                var iBossFrame = IsBossFrame(capture, scale, i);
                if (iBossFrame.HasValue) {

                    int nonBossFrames = 0;

                    for (float j = i; j < duration; j += 1) {

                        if (!IsBossFrame(capture, scale, j).HasValue) {

                            if (++nonBossFrames >= 2) {

                                for (int k = (int) (j * capture.Fps); k >= (j - 5) * capture.Fps; k--) {
                                    var lastBossFrame = IsBossFrame(capture, scale, k / (float) capture.Fps);
                                    if (lastBossFrame.HasValue) {
                                        var lastFrameSeconds = (k / (float) capture.Fps);
                                        

                                        // At least 10 seconds
                                        if (lastFrameSeconds > i + 10.0f) {
                                            bossEndFrames.Add((lastBossFrame.Value, k));
                                        }

                                        i = lastFrameSeconds;
                                        break;
                                    }
                                }

                                break;
                            }
                        } else {
                            nonBossFrames = 0;
                        }
                    }
                }
            }

            return bossEndFrames;

        }

        private static BossType? IsBossFrame(VideoCapture capture, float scale, float posSeconds)
        {
            int frameNum = (int)(posSeconds * capture.Fps);
            capture.Set(VideoCaptureProperties.PosFrames, frameNum);

            Mat mat = new Mat();
            capture.Read(mat);

            Cv2.Resize(mat, mat, new Size(mat.Width * scale, mat.Height * scale));
            // Crop to the bottom-left corner, since that's where the lives are
            mat = mat[(int)(mat.Height * 0.83f), mat.Height, 0, (int)(mat.Width * 0.085f)];

            float lowestThreshold = float.MaxValue;

            BossType? type = null;

            for (int i = 0; i < (int)BossType._NumTypes; i++) {

                Mat result = new Mat();
                Cv2.MatchTemplate(InputArray.Create(mat), InputArray.Create(ImgBoss[(BossType)i]), result,
                    TemplateMatchModes.SqDiffNormed, InputArray.Create(ImgBossMask[(BossType)i]));

                result.MinMaxLoc(out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);

                if (minVal < EndBossIconMatchThreshold) {
                    if (minVal < lowestThreshold) { // pick the match with the lowest threshold
                        type = (BossType)i;
                        lowestThreshold = (float)minVal;
                    }
                }
            }

            if (type == BossType.Bzzit || type == BossType.Mosquito) {

                capture.Read(mat);
                // check bottom line, for mosquito fight it's black
                mat = mat[(int)(mat.Height * 0.983f), mat.Height, 0, mat.Width];

                if (Util.GetAverageBrightness(mat) < Util.DarknessMaxBrightness) {
                    type = BossType.Mosquito;
                }
            }

            return type;
        }

        public static int? GetLastFinalBossFrame(VideoCapture capture, float scale, int videoEndFrame,
            Action<LoadRemover.ProgressPhase, float> updateProgress, int start = 1, int stepSize = -1)
        {
            if (stepSize <= 0) {
                stepSize = (int)capture.Fps;
            }


            capture.Set(VideoCaptureProperties.PosFrames, videoEndFrame);

            int maxFrames = (int)(MaxTimeAfterFinalHit * capture.Fps);
            for (int i = start; i < maxFrames; i+=stepSize) {

                // Start from the end of the video and move backwards to find the last occurrence of the boss icon
                capture.Set(VideoCaptureProperties.PosFrames, videoEndFrame - i);

                Mat mat = new Mat();
                capture.Read(mat);

                Cv2.Resize(mat, mat, new Size(mat.Width * scale, mat.Height * scale));
                // Crop to the bottom-left corner, since that's where the lives are
                mat = mat[(int)(mat.Height * 0.83f), mat.Height, 0, (int)(mat.Width * 0.085f)];

                Mat result = new Mat();
                Cv2.MatchTemplate(InputArray.Create(mat), InputArray.Create(ImgEndBoss), result,
                    TemplateMatchModes.SqDiffNormed, InputArray.Create(ImgEndBossMask));

                result.MinMaxLoc(out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);
                var timespan = TimeSpan.FromSeconds((videoEndFrame - i) / (float)capture.Fps);

                if (start == 1) {
                    updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_4_EndingTime, (float)i/maxFrames);
                }

                if (minVal < EndBossIconMatchThreshold) {
                    if (stepSize == 1) {
                        return (videoEndFrame - i) + 1;
                    } else {
                        return GetLastFinalBossFrame(capture, scale, videoEndFrame, updateProgress, i-stepSize, stepSize: 1);
                    }
                }

            }

            return null;
        }

        public static Load? GetBossLoad(VideoCapture capture, double startTime)
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

            return null;
            //throw new Exception($"Failed to determine Boss Load at {startTime} seconds");
        }

        public static List<Load> GetBossLoads(VideoCapture capture, float videoScale, int startingFrame, int endingFrame,
            Action<LoadRemover.ProgressPhase, float> updateProgress)
        {
            List<Load> bossLoads = new List<Load>();

            var lastFrames = GetBossEndFrames(capture, videoScale, startingFrame, endingFrame);
            int count = 0;
            foreach (var f in lastFrames) {

                if (f.Item1 == BossType.MrDark) {
                    continue;
                }

                float extraDelay = (f.Item1 == BossType.Mosquito) ? DelayAfterLastMosquitoHealthBar : (f.Item1 == BossType.Bzzit) ? DelayAfterLastBzzitHealthBar : 0;
                var bossLoad = GetBossLoad(capture, (f.Item2 / capture.Fps) + extraDelay);

                if (bossLoad.HasValue)
                    bossLoads.Add(bossLoad.Value);

                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_8_BossLoads, (++count)/(float)lastFrames.Count);
            }

            return bossLoads;
        }
    }
}
