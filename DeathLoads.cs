using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {

    public static class DeathLoads {
        public static float DarknessBrightnessChangeThreshold = 0.002f;

        public static List<Load> GetDeathLoads(VideoCapture capture, float videoScale, int startingFrame, int endingFrame,
            Action<LoadRemover.ProgressPhase, float> updateProgress)
        {
            var deaths = FindDeaths(capture, videoScale, startingFrame, endingFrame);
            var deathLoads = new List<Load>();

            int progress = 0;

            foreach (var deathFrame in deaths) {
                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_7_DeathLoads, ((progress++) / (float)deaths.Count) * 1.0f);
                deathLoads.Add(Util.CountDarknessFrames(LoadType.Death, capture, deathFrame / capture.Fps, 300));
            }

            return deathLoads;

        }

        private static List<int> FindDeaths(VideoCapture capture, float videoScale, int startingFrame, int endingFrame)
        {
            List<int> deaths = new List<int>();
            FindDeathsRecursive(capture, deaths, videoScale, startingFrame, endingFrame);
            return deaths;
        }

        private static bool FindDeathsRecursive(VideoCapture capture, List<int> deaths, float videoScale, int startFrame, int endFrame, float stepSize = 4)
        {
            capture.Set(VideoCaptureProperties.PosMsec, 0);
            double fps = capture.Get(VideoCaptureProperties.Fps);

            int stepSizeFrames = (int)(stepSize * capture.Fps);

            int currentLives = -1;
            int lastFrame = 0;
            for (int frame = startFrame; frame < endFrame; frame += stepSizeFrames) {
                int lifeCount = LifeCounter.GetLifeCount(capture, frame / fps, videoScale);

                if (currentLives == -1) {
                    currentLives = lifeCount;
                }

                if (lifeCount > 0) {

                    if (lifeCount < currentLives) {

                        if (stepSizeFrames <= 1) {
                            if (!deaths.Contains(lastFrame)) {
                                deaths.Add(lastFrame);
                            }

                            Mat dbgMat = new Mat();
                            capture.Read(dbgMat);

                            return true;
                        } else {

                            // Try first half, then second half
                            if (!FindDeathsRecursive(capture, deaths, videoScale, lastFrame, frame + stepSizeFrames / 2, stepSize / 2)) {
                                FindDeathsRecursive(capture,deaths, videoScale, lastFrame + stepSizeFrames / 2, frame,
                                    stepSize / 2);
                            }
                        }
                    }

                    currentLives = lifeCount;
                }

                lastFrame = frame;
            }

            return false;
        }


    }
}
