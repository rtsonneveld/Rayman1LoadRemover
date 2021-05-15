using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using OpenCvSharp;
using SoundFingerprinting;
using SoundFingerprinting.Audio.NAudio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Configuration;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;

namespace Rayman1LoadRemover {

    /*
     * TODO:
     * Mr. Sax loading screen
     *
     */

    public static class LoadRemover {

        private static readonly IModelService modelService = new InMemoryModelService(); // store fingerprints in RAM
        private static readonly NAudioService audioService = new NAudioService(); // default audio library

        public static readonly string DataFolder = "data";
        public static readonly string SoundsFolder = Path.Combine(DataFolder, "sounds");
        private static readonly string ffmpegPath = Path.Combine(DataFolder, "FFmpeg", "bin", "x64", "ffmpeg.exe");
        private static readonly string tempAudioFilePath = "temp.mp3";

        public static readonly string ImageFolder = Path.Combine(DataFolder, "images");
        public static readonly string TemplateFile = Path.Combine(DataFolder, "template.html");

        /// <summary>
        /// Maximum duration of the "start" screen at the beginning of the video, in seconds
        /// </summary>
        private static float StartScreenMaxDuration = 30.0f;

        public enum SoundType
        {
            EndSign, Boss
        }

        public enum ProgressPhase
        {
            Phase_1_PreprocessVideo,
            Phase_2_StartingTime,
            Phase_3_VideoScale,
            Phase_4_EndingTime,
            Phase_5_EndSignLoads,
            Phase_6_OverworldLoads,
            Phase_7_DeathLoads,
            Phase_8_BossLoads,
            Phase_9_GenerateReport,
            Phase_10_Finished
        }

        public static void Init()
        {
            AddSample(SoundType.EndSign, Path.Combine(SoundsFolder, "endsign.wav"));
            AddSample(SoundType.Boss, Path.Combine(SoundsFolder, "bossdefeat.wav"));

            LifeCounter.Init();
            BossLoads.Init();
        }

        /// <summary>
        /// Query the audio file to detect all samples and at which time they occur
        /// </summary>
        /// <param name="file">The audio file (must be .mp3)</param>
        /// <returns></returns>
        public static async Task<QueryResult> QueryFile(string file)
        {
            TimeSpan duration;
            await using (Mp3FileReader reader = new Mp3FileReader(file)) {
                duration = reader.TotalTime;
            }

            int secondsToAnalyze = (int)Math.Floor(duration.TotalSeconds); // number of seconds to analyze from query file
            int startAtSecond = 0; // start at the begining

            var config = new LowLatencyQueryConfiguration()
            {
                AllowMultipleMatchesOfTheSameTrackInQuery = true,
                PermittedGap = 15.0f,
            };

            // query the underlying database for similar audio sub-fingerprints
            var queryResult = await QueryCommandBuilder.Instance.BuildQueryCommand()
                .From(file, secondsToAnalyze, startAtSecond).
                WithQueryConfig(config)
                .UsingServices(modelService, audioService)
                .Query();

            return queryResult;
        }

        private static void AddSample(SoundType soundType, string audioFilePath)
        {
            var track = new TrackInfo(soundType.ToString(), string.Empty, string.Empty);

            // create fingerprints
            var hashedFingerprints = FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(audioFilePath)
                .UsingServices(audioService)
                .Hash().GetAwaiter().GetResult();

            // store hashes in the database for later retrieval
            modelService.Insert(track, hashedFingerprints);
        }

        public readonly struct CropSettings
        {
            public readonly float Left;
            public readonly float Top;
            public readonly float Right;
            public readonly float Bottom;

            public CropSettings(float left, float top, float right, float bottom) : this()
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        public readonly struct TrimSettings {
            
            public readonly float Start;
            public readonly float End;

            public TrimSettings(float start, float end)
            {
                Start = start;
                End = end;
            }
        }

        public static LoadResults Start(string file, bool partialRun, CropSettings? crop, TrimSettings? trim, LoadType loadTypes, bool resize, Action<ProgressPhase, float> updateProgress)
        {
            List<Load> loads = new List<Load>();

            updateProgress.Invoke(ProgressPhase.Phase_1_PreprocessVideo, 0);

            VideoCapture capture = new VideoCapture(file);
            string processedFile = CropTrimAndResizeVideo(capture, file, crop, /*trim,*/ resize);
            capture = VideoCapture.FromFile(processedFile);

            updateProgress.Invoke(ProgressPhase.Phase_2_StartingTime, 0);

            int startingFrame = 0;
            int endingFrame = trim.HasValue ? (int)Math.Min((trim.Value.End * capture.Fps) - 1, capture.FrameCount - 1) : capture.FrameCount - 1;

            if (!partialRun && loadTypes.HasFlag(LoadType.Start)) {

                var startLoad = Util.CountDarknessFrames(LoadType.Start, capture, trim?.Start ?? 0,
                    (int) (capture.Fps * StartScreenMaxDuration));
                if (startLoad.FrameStart == -1) {
                    throw new Exception(
                        "Start screen not detected, make sure the video starts on the \"Start\"/\"Options\" screen");
                }

                loads.Add(startLoad);
                startingFrame = startLoad.FrameStart;

                if (loadTypes.HasFlag(LoadType.Overworld)) {
                    var startOverworldLoad = Util.CountFrozenFrames(LoadType.Overworld, capture,
                        startLoad.FrameEnd / capture.Fps, (int) capture.Fps / 5, (int) capture.Fps * 20);

                    if (startOverworldLoad.HasValue)
                        loads.Add(startOverworldLoad.Value);
                }
            }

            updateProgress.Invoke(ProgressPhase.Phase_3_VideoScale, 0);

            float videoScale = LifeCounter.GetLifeCountScale(capture, startingFrame, updateProgress);
            if (float.IsNaN(videoScale)) {
                throw new Exception("Video Scale couldn't be determined: " + videoScale);
            }

            updateProgress.Invoke(ProgressPhase.Phase_4_EndingTime, 0);

            if (!partialRun) {
                var _endingFrame = BossLoads.GetLastFinalBossFrame(capture, videoScale, endingFrame, updateProgress);
                if (!_endingFrame.HasValue) {
                    throw new Exception(
                        "Final hit not detected, make sure the video doesn't end more than 3 minutes after the final hit.");
                }

                endingFrame = _endingFrame.Value;
            }
            
            updateProgress.Invoke(ProgressPhase.Phase_5_EndSignLoads, 0);
            
            if (loadTypes.HasFlag(LoadType.EndSign))
                loads.AddRange(EndSignLoads.GetEndSignLoads(capture, videoScale, startingFrame, endingFrame, updateProgress));

            updateProgress.Invoke(ProgressPhase.Phase_6_OverworldLoads, 0);
            
            if (loadTypes.HasFlag(LoadType.Overworld) || loadTypes.HasFlag(LoadType.BackSign))
                loads.AddRange(OverworldLoads.GetOverworldLoads(capture, videoScale, startingFrame /(float)capture.Fps, endingFrame / (float)capture.Fps, loadTypes, updateProgress));

            updateProgress.Invoke(ProgressPhase.Phase_7_DeathLoads, 0);

            if (loadTypes.HasFlag(LoadType.Death))
                loads.AddRange(DeathLoads.GetDeathLoads(capture, videoScale, startingFrame, endingFrame, updateProgress));

            updateProgress.Invoke(ProgressPhase.Phase_8_BossLoads, 0);

            if (loadTypes.HasFlag(LoadType.Boss))
                loads.AddRange(BossLoads.GetBossLoads(capture, videoScale, startingFrame, endingFrame, updateProgress));

            int phase8Progress = 0;

            // Remove backsign loads that aren't preceded by an overworld load (ignore death loads for this)
            var sortedLoads = loads.OrderBy(l=>l.FrameStart).ToList();
            List<Load> backsignLoadsToRemove = new List<Load>();
            for (int i = 0; i < sortedLoads.Count; i++) {
                if (sortedLoads[i].Type == LoadType.BackSign) {

                    var bsLoad = sortedLoads[i];

                    for (int j = i-1; j >= 0; j--) {

                        var checkLoad = sortedLoads[j];

                        // only consider loads more than 3 seconds before the backsign load
                        if (checkLoad.FrameStart > bsLoad.FrameStart - capture.Fps * 3.0) {
                            continue;
                        }

                        if (checkLoad.Type == LoadType.Death) {
                            continue;
                        }
                        if (checkLoad.Type == LoadType.Overworld) {
                            break;
                        } else {
                            backsignLoadsToRemove.Add(bsLoad);
                        }
                    }
                }
            }

            foreach (var l in backsignLoadsToRemove) {
                loads.Remove(l);
            }

            // Remove unnecessary endsign loads (when they overlap with other loads)
            foreach (var load in loads.Where(l => l.Type != LoadType.EndSign).ToList()) {
                loads.RemoveAll(l => l.Type == LoadType.EndSign && l.Overlaps(load, (int)(capture.Fps * 0.5f)));
            }

            // Remove unnecessary backsign loads (when they overlap with other loads)
            foreach (var load in loads.Where(l => l.Type != LoadType.BackSign).ToList()) {
                loads.RemoveAll(l => l.Type == LoadType.BackSign && l.Overlaps(load, (int)(capture.Fps * 0.5f)));
            }

            // Remove all loads that start after the last frame
            loads.RemoveAll(l => l.FrameStart > endingFrame);

            updateProgress.Invoke(ProgressPhase.Phase_9_GenerateReport, 0);

            LoadResults results = new LoadResults(loads, (float)capture.Fps, startingFrame, endingFrame);

            results.SaveDebugImages(capture, "debugExport", "file");
            var report = new LoadRemoverReport(Path.GetFileName(file), results, capture);
            var reportPath = Path.ChangeExtension(file,null) + "_report.html";
            report.GenerateHtml(TemplateFile).Save(reportPath);

            updateProgress.Invoke(ProgressPhase.Phase_9_GenerateReport, 1);

            var openReport = MessageBox.Show($"Done! The report file can be found at {Environment.NewLine}{reportPath}{Environment.NewLine}" +
                                         $"Do you wish to open the report now?", "Report", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (openReport == MessageBoxResult.Yes) {

                // Open report in default application (hopefully the browser)
                var psi = new ProcessStartInfo
                {
                    FileName = reportPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }


            return results;
        }

        private static string CropTrimAndResizeVideo(VideoCapture capture, string sourceFile, CropSettings? cropSettings, /*TrimSettings? trimSettings,*/ bool resize)
        {
            if (cropSettings == null && /*trimSettings == null &&*/ !resize) {
                return sourceFile;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath);

            List<string> filters = new List<string>();

            if (cropSettings != null) {
                var crop = cropSettings.Value;
                int x = (int)crop.Left;
                int y = (int)crop.Top;
                int w = (int)(capture.FrameWidth-crop.Right-x);
                int h = (int)(capture.FrameHeight-crop.Bottom-y);
                filters.Add($"crop={w}:{h}:{x}:{y}");
            }

            // Resize to 4:3
            if (resize) {
                int sw = (int)capture.FrameWidth;
                int sh = (int)(capture.FrameWidth * 3/4);
                filters.Add($"scale={sw}:{sh}");
            }

            string targetFile = Path.GetTempFileName()+".mp4";

            Process process = null;

            startInfo.RedirectStandardOutput = false;//true;
            startInfo.RedirectStandardError = false;// true;

            /*if (trimSettings != null) {

                var trim = trimSettings.Value;
                startInfo.Arguments = $"-y -ss {trim.Start} -i \"{sourceFile}\" -to {trim.End} -c copy -copyts \"{targetFile}\"";
                process = Process.Start(startInfo);
                while (!process.HasExited) { }

                sourceFile = targetFile;
            }*/

            if (filters.Any()) {

                if (sourceFile == targetFile) {
                    targetFile = Path.GetTempFileName() + ".mp4";
                }

                startInfo.Arguments = $"-y -i \"{sourceFile}\" -filter:v \"{string.Join(",", filters)}\" -c:a copy \"{targetFile}\"";
                process = Process.Start(startInfo);
                while (!process.HasExited) {
                }
            }

            return targetFile;
        }

        private static void ExtractMp3(string sourceFile, string targetFile)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath);
            startInfo.Arguments = $"-y -i {sourceFile} \"{targetFile}\"";
            var process = Process.Start(startInfo);
            while (!process.HasExited) { }
        }

    }
}
