using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class BossLoads {

        private static float BossLoadingScreenMinDuration = 1.0f;
        private static float BossLoadingScreenMaxDuration = 20.0f;

        private static Mat imgBossCutscene1;
        private static Mat imgBossCutscene2;

        public static void Init()
        {
            //imgBossCutscene1 = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "boss_cutscene_1.png"));
            //imgBossCutscene2 = Cv2.ImRead(Path.Combine(LoadRemover.ImageFolder, "boss_cutscene_2.png"));
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

                Cv2.ImWrite("debug_top.png", topPart);
                Cv2.ImWrite("debug_bot.png", botPart);

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
