using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    public static class Util {
        private const int maxDifferenceIdenticalFrames = 100; // 0-256
        private const float minMseDifferentFrames = 20f;
        private const float maxMseIdenticalFrames = 0.1f;
        private const float minSSIMIdenticalFrames = 0.995f;

        public static float DarknessMaxBrightness = 0.15f;
        public static float BrightMinBrightness = 0.43f;
        //private static float minScoreSameFrames = 0.99999f;

        /// <summary>
        /// Counts the amount of frames where the screen is frozen until it changes.
        /// </summary>
        /// <param name="capture">Capture</param>
        /// <param name="startTime">Starting time in the video in seconds</param>
        /// <param name="minimumDuration">Function will only return after this amount of frames</param>
        /// <param name="maxFramesToCount">Maximum amount of frames to count</param>
        /// <returns>The amount of frames for which the screen has been frozen, or -1 if the screen didn't freeze</returns>
        public static Load? CountFrozenFrames(LoadType type, VideoCapture capture, double startTime, int minimumDuration, int maxFramesToCount, Rect? cropRect = null)
        {
            capture.Set(VideoCaptureProperties.PosMsec, startTime * 1000.0f);
            int startingFrame = (int)capture.Get(VideoCaptureProperties.PosFrames);

            int sameFrames = 0;

            Mat[] mats = new Mat[maxFramesToCount];
            for (int i = 0; i < maxFramesToCount; i++) {

                if (capture.Get(VideoCaptureProperties.PosFrames) < capture.FrameCount) {

                    mats[i] = new Mat();
                    capture.Read(mats[i]);
                    if (cropRect.HasValue) {
                        var rect = cropRect.Value;
                        mats[i] = mats[i][rect.Y, rect.Y + rect.Height, rect.X, rect.X + rect.Width];
                    }
                }
            }

            /*Dictionary<int, double> scoresMSE = new Dictionary<int, double>();
            Dictionary<int, double> scoresSSIM = new Dictionary<int, double>();
            Dictionary<int, double> scoresMaxDiff = new Dictionary<int, double>();*/

            int firstFrame = -1;
            int loadingTime = -1;

            for (int i = 0; i < maxFramesToCount-2; i++) {

                if (mats[i + 1] == null) {
                    continue;
                }

                var matA = mats[i];
                var matB = mats[i+1];

                //matA = matA.Resize(new Size(matA.Width/2,matA.Height/2));
                //matA = matA.CvtColor(ColorConversionCodes.RGB2GRAY, 1);
                //matB = matB.Resize(new Size(matB.Width/2,matB.Height/2));
                //matB = matB.CvtColor(ColorConversionCodes.RGB2GRAY, 1);

                //var ssim = SSIM.GetMssim(matA, matB);
                //var mse = GetMSE(matA, matB);

                //scoresMSE.Add(i,mse);
                //scoresSSIM.Add(i, ssim.Score);
                //scoresMaxDiff.Add(i,GetMaxDifference(matA, matB));

                //Cv2.ImWrite(Path.Combine("debugExport", $"compare_{startingFrame+i}_{mse:F4}.png"), mats[i]);
                //Cv2.ImWrite(Path.Combine("debugExport", $"compare_{i}_{mse:F4}.png"), mats[i]);


                // If either the next frame or the frame after that is the same
                if (FramesIdentical(mats[i],mats[i+1])) {
                    if (firstFrame == -1) {
                        firstFrame = i;
                    }

                    sameFrames++;
                } else {

                    if (sameFrames > minimumDuration) {
                        loadingTime = sameFrames;

                        break;
                    }

                    firstFrame = -1;

                    sameFrames = 0;
                }
            }

            /*
            foreach (var kv in scoresMaxDiff) {
                Debug.WriteLine($"{kv.Key};{scoresMSE[kv.Key]};{scoresSSIM[kv.Key]};{scoresMaxDiff[kv.Key]}");
            }*/

            if (loadingTime >= 0) {
                return new Load(type, startingFrame + firstFrame, startingFrame + firstFrame + loadingTime);
            } else {

                Debug.WriteLine($"CountFrozenFrames: No frozen frames found, LoadType {type}, Start Time {startTime}, MinimumDuration {minimumDuration}, MaxFramesToCount = {maxFramesToCount}");
                return null;
            }
        }

        /*public static bool FramesIdentical(Mat a, Mat b)
        {
            var resultMat = new Mat();
            Cv2.MatchTemplate(InputArray.Create(a), InputArray.Create(b), resultMat, TemplateMatchModes.SqDiffNormed);
            resultMat.MinMaxLoc(out double minVal, out double maxVal);
            return minVal < minScoreSameFrames;
        }*/

        public static double GetMaxDifference(Mat a, Mat b)
        {
            var resultMat = new Mat();
            Cv2.Absdiff(InputArray.Create(a), InputArray.Create(b), resultMat);
            resultMat.MinMaxLoc(out double minVal, out double maxVal);

            return maxVal;
        }

        public static bool FramesIdentical(Mat a, Mat b)
        {
            /*var resultMat = new Mat();
            Cv2.Subtract(InputArray.Create(a), InputArray.Create(b), resultMat);
            //Cv2.MatchTemplate(InputArray.Create(a), InputArray.Create(b), resultMat, TemplateMatchModes.SqDiffNormed);
            resultMat.MinMaxLoc(out double minVal, out double maxVal);

            Debug.WriteLine("MaxVal: "+maxVal);

            return maxVal < maxDifferenceIdenticalFrames;*/

            //var ssim = SSIM.GetMssim(a, b);
            var maxDiff = GetMaxDifference(a, b);
            var mse = GetMSE(a, b);
            return (mse < maxMseIdenticalFrames || maxDiff < maxDifferenceIdenticalFrames) && mse < minMseDifferentFrames;
        }

        /// <summary>
        /// Calculate Mean Squared Error between two images
        /// </summary>
        /// <param name="a">First image</param>
        /// <param name="b">Second image</param>
        /// <returns>The MSE value, where 0 = no difference, 1 = a fully different image</returns>
        public static double GetMSE(Mat a, Mat b)
        {
            var s1 = new Mat();
            Cv2.Absdiff(a, b, s1);
            s1.ConvertTo(s1, MatType.CV_32F); // convert to float
            s1 = s1.Mul(s1); // square the matrix

            Scalar s = Cv2.Sum(s1);        // sum elements per channel

            double sse = s.Val0 + s.Val1 + s.Val2; // sum channels

            double mse = sse / (double)(a.Channels() * a.Total());

            return mse;
        }


        /// <summary>
        /// Counts the amount of frames where the bottom half of the screen is dark (with a threshold)
        /// </summary>
        /// <param name="capture">Capture</param>
        /// <param name="startTime">Starting time in the video in seconds</param>
        /// <param name="maxFramesToCount">Maximum amount of frames to count</param>
        /// <returns>The amount of frames for which the screen has been dark, or -1 if the screen never went dark</returns>
        public static Load CountDarknessFrames(LoadType type, VideoCapture capture, double startTime, int maxFramesToCount)
        {
            capture.Set(VideoCaptureProperties.PosMsec, startTime * 1000.0f);

            int startFrame = -1;
            int endFrame = -1;
            float minBrightness = 1;

            Mat[] mats = new Mat[maxFramesToCount];
            for (int i = 0; i < maxFramesToCount; i++) {
                mats[i] = new Mat();
                capture.Read(mats[i]);

                // Get bottom half
                var croppedMat = mats[i];
                croppedMat = croppedMat[croppedMat.Height / 2, croppedMat.Height, 0, croppedMat.Width];

                float brightness = GetAverageBrightness(croppedMat);

                // If the screen is dark enough
                if (brightness < DarknessMaxBrightness) {

                    // Check if the screen is turning darker or roughly as dark as last frame
                    if (brightness <= minBrightness + DeathLoads.DarknessBrightnessChangeThreshold) {

                        // If screen is not becoming darker anymore
                        if (brightness >= minBrightness - DeathLoads.DarknessBrightnessChangeThreshold) {
                            if (startFrame < 0) {
                                startFrame = i;
                            }
                        }

                        minBrightness = brightness;

                    } else { // Screen getting brighter, load over
                        endFrame = i;
                        break;
                    }
                } else if (startFrame >= 0) {
                    endFrame = i;
                    break;
                }
            }

            int frameOffset = (int)Math.Round(startTime * capture.Fps);
            return new Load(type, frameOffset+startFrame, frameOffset+endFrame);
        }

        /// <summary>
        /// Returns the average brightness of an image
        /// </summary>
        /// <param name="img">The image</param>
        /// <returns>Average brightness between 0 and 1</returns>
        public static float GetAverageBrightness(Mat img)
        {
            Mat imgHsv = new Mat();
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.RGB2HSV);
            Scalar average = Cv2.Mean(imgHsv);

            return (float)average.Val2/255.0f;
        }
    }
}
