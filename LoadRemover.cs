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
        /// Duration before the end sign check can detect the end of the loading screen, in seconds
        /// </summary>
        private static float EndSignLoadingScreenMinDuration = 1.0f;

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
            Phase_0_PreprocessVideo,
            Phase_1_AnalyseAudio,
            Phase_2_StartingTime,
            Phase_3_VideoScale,
            Phase_4_EndingTime,
            Phase_5_OverworldLoads,
            Phase_6_DeathLoads,
            Phase_7_EndSignAndBossLoads,
            Phase_8_GenerateReport,
            Phase_9_Finished
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

            var config = new DefaultQueryConfiguration()
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
            
            public readonly int Start;
            public readonly int End;

            public TrimSettings(int start, int end)
            {
                Start = start;
                End = end;
            }
        }

        public static LoadResults Start(string file, bool partialRun, CropSettings? crop, TrimSettings? trim, bool resize, Action<ProgressPhase, float> updateProgress)
        {
            List<Load> loads = new List<Load>();

            updateProgress.Invoke(ProgressPhase.Phase_0_PreprocessVideo, 0);

            VideoCapture capture = new VideoCapture(file);
            string processedFile = CropTrimAndResizeVideo(capture, file, crop, trim, resize);
            capture = VideoCapture.FromFile(processedFile);

            updateProgress.Invoke(ProgressPhase.Phase_1_AnalyseAudio, 0);

            ExtractMp3(processedFile, tempAudioFilePath);

            var queryResult = QueryFile(tempAudioFilePath).Result;
            var entries = queryResult.ResultEntries.ToList();

            // crop video here

            updateProgress.Invoke(ProgressPhase.Phase_2_StartingTime, 0);

            int startingFrame = 0;
            int endingFrame = capture.FrameCount - 1;

            if (!partialRun) {
                var startLoad = Util.CountDarknessFrames(LoadType.Start, capture, 0.0f,
                    (int)(capture.Fps * StartScreenMaxDuration));
                if (startLoad.FrameStart == -1) {
                    throw new Exception(
                        "Start screen not detected, make sure the video starts on the \"Start\"/\"Options\" screen");
                }

                loads.Add(startLoad);
                startingFrame = startLoad.FrameStart;

                var startOverworldLoad = Util.CountFrozenFrames(LoadType.Overworld, capture, startLoad.FrameEnd / capture.Fps, (int)capture.Fps / 5, (int)capture.Fps * 20);

                if (startOverworldLoad.HasValue)
                    loads.Add(startOverworldLoad.Value);
            }

            updateProgress.Invoke(ProgressPhase.Phase_3_VideoScale, 0);

            float videoScale = LifeCounter.GetLifeCountScale(capture, updateProgress);
            if (float.IsNaN(videoScale)) {
                throw new Exception("Video Scale couldn't be determined: " + videoScale);
            }

            updateProgress.Invoke(ProgressPhase.Phase_4_EndingTime, 0);

            if (!partialRun) {
                var _endingFrame = BossLoads.GetLastFinalBossFrame(capture, videoScale, updateProgress);
                if (!_endingFrame.HasValue) {
                    throw new Exception(
                        "Final hit not detected, make sure the video doesn't end more than 3 minutes after the final hit.");
                }

                endingFrame = _endingFrame.Value;
            }

            updateProgress.Invoke(ProgressPhase.Phase_5_OverworldLoads, 0);
            loads.AddRange(OverworldLoads.GetOverworldLoads(capture, videoScale, updateProgress));

            updateProgress.Invoke(ProgressPhase.Phase_6_DeathLoads, 0);
            loads.AddRange(DeathLoads.GetDeathLoads(capture, videoScale, updateProgress));

            updateProgress.Invoke(ProgressPhase.Phase_7_EndSignAndBossLoads, 0);

            int phase7Progress = 0;

            foreach (var result in queryResult.ResultEntries) {

                updateProgress.Invoke(LoadRemover.ProgressPhase.Phase_7_EndSignAndBossLoads, (phase7Progress++) / (float)queryResult.ResultEntries.Count());

                string resultString = $"Track ID = {result.Track.Id}, Score = {result.Confidence}, Confidence = {result.Score}, Match at = {result.QueryMatchStartsAt}";
                Debug.WriteLine(resultString);

                if (Enum.TryParse(result.Track.Id, out SoundType type)) {
                    switch (type) {
                        case SoundType.EndSign:
                            var load = Util.CountFrozenFrames(LoadType.EndSign, capture, result.QueryMatchStartsAt,
                                (int)(capture.Fps * EndSignLoadingScreenMinDuration), 600);

                            if (load.HasValue) {
                                loads.Add(load.Value);
                            }

                            break;
                        case SoundType.Boss:

                            var bossLoad = BossLoads.GetBossLoad(capture, result.QueryMatchStartsAt);
                            loads.Add(bossLoad);

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            // Remove unnecessary backsign loads (when they overlap with other loads)
            foreach (var load in loads.Where(l => l.Type != LoadType.BackSign).ToList()) {
                loads.RemoveAll(l => l.Type == LoadType.BackSign && l.Overlaps(load, (int)(capture.Fps * 0.5f)));
            }

            updateProgress.Invoke(ProgressPhase.Phase_8_GenerateReport, 0);

            LoadResults results = new LoadResults(loads, (float)capture.Fps, startingFrame, endingFrame);

            results.SaveDebugImages(capture, "debugExport", "file");
            var report = new LoadRemoverReport(Path.GetFileName(file), results, capture);
            var reportPath = Path.ChangeExtension(file,null) + "_report.html";
            report.GenerateHtml(TemplateFile).Save(reportPath);

            updateProgress.Invoke(ProgressPhase.Phase_8_GenerateReport, 1);

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

        private static string CropTrimAndResizeVideo(VideoCapture capture, string sourceFile, CropSettings? cropSettings, TrimSettings? trimSettings, bool resize)
        {
            if (cropSettings == null && trimSettings == null && !resize) {
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

            if (trimSettings != null) {

                var trim = trimSettings.Value;
                startInfo.Arguments = $"-y -ss {trim.Start} -i \"{sourceFile}\" -to {trim.End} -c copy -copyts \"{targetFile}\"";
                process = Process.Start(startInfo);
                while (!process.HasExited) { }

                sourceFile = targetFile;
                targetFile = Path.GetTempFileName() + ".mp4";
            }

            //startInfo.RedirectStandardOutput = true;
            //startInfo.RedirectStandardError = true;

            if (filters.Any()) {
                startInfo.Arguments = $"-y -i \"{sourceFile}\" -filter:v \"{string.Join(",", filters)}\" -c:a copy \"{targetFile}\"";
                process = Process.Start(startInfo);
                while (!process.HasExited) {
                }
            }

            //string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            //Debug.WriteLine(output);

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
