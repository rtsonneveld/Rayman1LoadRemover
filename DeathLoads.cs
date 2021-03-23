using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {

    public static class DeathLoads {
        public static float DarknessBrightnessChangeThreshold = 0.002f;

        public static List<Load> GetDeathLoads(VideoCapture capture, float videoScale)
        {
            var deaths = FindDeaths(capture, videoScale);
            var deathLoads = new List<Load>();

            foreach (var deathFrame in deaths) {
                deathLoads.Add(Util.CountDarknessFrames(LoadType.Death, capture, deathFrame / capture.Fps, 300));
            }

            return deathLoads;

        }

        private static List<int> FindDeaths(VideoCapture capture, float videoScale)
        {
            List<int> deaths = new List<int>();
            FindDeathsRecursive(capture, deaths, videoScale, 0, capture.FrameCount);
            return deaths;
        }

        private static bool FindDeathsRecursive(VideoCapture capture, List<int> deaths, float videoScale, int startFrame, int endFrame, int stepSize = 256)
        {
            capture.Set(VideoCaptureProperties.PosMsec, 0);
            double fps = capture.Get(VideoCaptureProperties.Fps);

            int currentLives = -1;
            int lastFrame = 0;
            for (int frame = startFrame; frame < endFrame; frame += stepSize) {
                int lifeCount = LifeCounter.GetLifeCount(capture, frame / fps, videoScale);

                if (currentLives == -1) {
                    currentLives = lifeCount;
                }

                //Debug.WriteLine($"Life Count @{frame} = {lifeCount}");
                if (lifeCount > 0) {

                    if (lifeCount < currentLives) {

                        //Debug.WriteLine($"Death between {lastFrame} and {frame}");

                        if (stepSize <= 1) {
                            deaths.Add(lastFrame);

                            //Debug.WriteLine($"Death at exactly {lastFrame}");

                            Mat dbgMat = new Mat();
                            capture.Read(dbgMat);

                            return true;
                        } else {

                            // Try first half, then second half
                            if (!FindDeathsRecursive(capture, deaths, videoScale, lastFrame, frame + stepSize/2, stepSize / 2)) {
                                FindDeathsRecursive(capture,deaths, videoScale, lastFrame + stepSize / 2, frame,
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
