using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {

    public readonly struct LoadResults
    {
        public readonly List<Load> Loads;
        public readonly float FPS;
        public readonly int StartingFrame;
        public readonly int EndingFrame;

        public LoadResults(List<Load> loads, float fps, int startingFrame = -1, int endingFrame = -1)
        {
            Loads = loads;
            FPS = fps;
            StartingFrame = startingFrame;
            EndingFrame = endingFrame;
        }

        public void SaveDebugImages(VideoCapture capture, string path, string prefix)
        {
            foreach (var l in Loads) {

                // Export the two frames before and after the load
                for (int i = l.FrameStart-2; i <= l.FrameEnd+2; i++) {

                    if (i > l.FrameStart+1 && i < l.FrameEnd-1) continue;

                    capture.Set(VideoCaptureProperties.PosFrames, i);
                    Mat m = new Mat();
                    capture.Read(m);
                    string filename = $"{prefix}{l.Type}_{l.FrameStart:D8}_{l.FrameEnd:D8}_{i:D8}.png";
                    string combined = Path.Combine(path, filename);
                    Cv2.ImWrite(combined, m);
                }
            }
        }

        public int TotalLoadingFrames => Loads.Sum(l => l.Length);
        public int TotalFramesIncludingLoads => EndingFrame - StartingFrame;
        public int TotalFramesWithoutLoads => TotalFramesIncludingLoads - TotalLoadingFrames;
    }

    public enum LoadType {
        Death,
        EndSign,
        Overworld,
        BackSign,
        Boss,
        Start
    }

    public readonly struct Load
    {

        public readonly LoadType Type;
        public readonly int FrameStart;
        public readonly int FrameEnd;
        
        /// <summary>
        /// Length of the load in frames
        /// </summary>
        public int Length => FrameEnd - FrameStart;

        public Load(LoadType type, int frameStart, int frameEnd)
        {
            Type = type;

            if (frameStart > frameEnd) {
                throw new ArgumentException($"FrameStart cannot be bigger than FrameEnd: {frameStart} > {frameEnd}");
            }

            FrameStart = frameStart;
            FrameEnd = frameEnd;
        }

        public override string ToString()
        {
            return $"Load(Type={Type}, FrameStart={FrameStart}, FrameEnd={FrameEnd}, Length={Length})";
        }

        public bool Overlaps(Load load, int margin = 0)
        {
            bool overlap = load.FrameStart < this.FrameEnd+margin && load.FrameEnd > this.FrameStart-margin;

            if (overlap) {
                Debug.WriteLine($"Overlap found between {this} and {load} (with margin {margin})");
            }

            return overlap;
        }
    }
}
