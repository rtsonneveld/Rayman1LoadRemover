using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class LifeCounter
    {

        private static Mat LifeCounterIcon;
        private static Mat LifeCounterIconMask;
        private static List<Mat> LifeCounterNumbers;
        private static List<Mat> LifeCounterNumberMasks;

        public static void Init()
        {
            InitIcon();

            LifeCounterNumbers = new List<Mat>();
            LifeCounterNumberMasks = new List<Mat>();
            for (int i = 0; i < 10; i++) {
                string filename = Path.Combine(LoadRemover.ImageFolder, $"{i}.png");
                var img = Cv2.ImRead(filename);
                Cv2.CvtColor(img, img, ColorConversionCodes.BGR2GRAY, 1);
                //Cv2.ImWrite("grayscale_life_" + i + ".png", img);
                LifeCounterNumbers.Insert(i, img);
                Mat mask = new Mat();
                Mat invMask = new Mat();
                Cv2.InRange(img, 0.01f, 1.0f, mask);
                Cv2.BitwiseNot(mask, invMask);
                //Cv2.ImWrite("temp_mask_" + i + ".png", invMask);
                LifeCounterNumberMasks.Insert(i, invMask);
            }
        }

        private static void InitIcon()
        {
            LifeCounterIcon = new Mat();
            string filenameIcon = Path.Combine(LoadRemover.ImageFolder, $"life.png");
            LifeCounterIcon = Cv2.ImRead(filenameIcon);
            LifeCounterIconMask = new Mat();
            Cv2.InRange(LifeCounterIcon, 0.01f, 1.0f, LifeCounterIconMask);
            Cv2.BitwiseNot(LifeCounterIconMask, LifeCounterIconMask);
        }

        private static float LifeCountMatchThreshold = 0.1f;

        public readonly struct MinValAndLocation
        {
            public readonly float MinVal;
            public readonly Point Location;

            public MinValAndLocation(float minVal, Point location)
            {
                MinVal = minVal;
                Location = location;
            }
        }

        public static int GetLifeCount(VideoCapture capture, double time, float scale)
        {
            capture.Set(VideoCaptureProperties.PosMsec, time * 1000.0f);

            var mat = new Mat();
            var resultMat = new Mat();

            if (capture.PosFrames >= capture.FrameCount) {
                return -1;
            }

            capture.Read(mat);

            Cv2.Resize(mat, mat, new Size(mat.Width * scale, mat.Height * scale));
            // Crop to the top-left corner, since that's where the lives are
            mat = mat[0, mat.Height / 4, 0, mat.Width / 3];
            // Use grayscale
            Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY,1);

            Dictionary<int, MinValAndLocation> minVals = new Dictionary<int, MinValAndLocation>();

            for (int i = 0; i < 10; i++) {
                Cv2.MatchTemplate(InputArray.Create(mat), InputArray.Create(LifeCounterNumbers[i]), resultMat,
                    TemplateMatchModes.SqDiffNormed, InputArray.Create(LifeCounterNumberMasks[i]));
                resultMat.MinMaxLoc(out double minVal, out double maxVal, out Point minLoc, out Point maxLoc);
                minVals.Add(i, new MinValAndLocation((float)minVal, minLoc));
            }

            var mostLikely1 = minVals.OrderBy(x => x.Value.MinVal).First();
            var mostLikely2 = minVals.OrderBy(x => x.Value.MinVal).Skip(1).First();
            
            if (mostLikely1.Value.MinVal < LifeCountMatchThreshold && mostLikely2.Value.MinVal < LifeCountMatchThreshold) {

                // Value 1 left of value 2
                if (mostLikely1.Value.Location.X < mostLikely2.Value.Location.X)
                    return mostLikely1.Key * 10 + mostLikely2.Key;
                else
                    return mostLikely2.Key * 10 + mostLikely1.Key;
            }

            return -1;
        }

        public readonly struct ScaleAndMinValue
        {
            public readonly float Scale;
            public readonly float MinValue;

            public ScaleAndMinValue(float scale, float minValue)
            {
                Scale = scale;
                MinValue = minValue;
            }
        }

        public static float GetLifeCountScale(VideoCapture capture, int startFrame, Action<LoadRemover.ProgressPhase, float> updateProgress)
        {
            const int attempts = 20;
            int currentAttempt = 0;
            for (int i = startFrame; i < capture.FrameCount; i += capture.FrameCount/attempts) {
                capture.Set(VideoCaptureProperties.PosFrames, i);
                var mat = new Mat();
                capture.Read(mat);
                // Use grayscale
                //Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY,1);

                Dictionary<int, double> minVals = new Dictionary<int, double>();

                ScaleAndMinValue bestScale = BestScale(mat);
                bestScale = BestScale(mat, bestScale.Scale, bestScale.MinValue, bestScale.Scale - 0.05f, bestScale.Scale + 0.05f, 0.01f);

                if (bestScale.Scale > 0 && bestScale.MinValue < LifeCountMatchThreshold) {
                    return bestScale.Scale;
                }

                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_3_VideoScale, (float)currentAttempt/ attempts);
            }
            return float.NaN;
        }

        private static ScaleAndMinValue BestScale(Mat mat, float bestScale = -1, float bestResult = float.PositiveInfinity, float minScale=0.2f, float maxScale=1.0f, float stepSize = 0.05f)
        {

            for (float scale = minScale; scale <= maxScale; scale += stepSize) {
                var resultMat = new Mat();
                Mat scaledMat = new Mat();
                Cv2.Resize(mat, scaledMat, new Size(mat.Width * scale, mat.Height * scale));
                Cv2.MatchTemplate(InputArray.Create(scaledMat), InputArray.Create(LifeCounterIcon), resultMat,
                    TemplateMatchModes.SqDiffNormed, InputArray.Create(LifeCounterIconMask));
                resultMat.MinMaxLoc(out double minVal, out double maxVal);

                if (minVal <= bestResult) {
                    bestScale = scale;
                    bestResult = (float) minVal;
                    //Cv2.ImWrite($"lifecounter_debug_{scale:F2}.png", resultMat);
                    //Cv2.ImWrite($"lifecounter_debug_{scale:F2}_org.png", scaledMat);
                }
            }

            return new ScaleAndMinValue(bestScale, bestResult);
        }
    }
}
