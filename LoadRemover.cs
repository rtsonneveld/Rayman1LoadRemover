using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenCvSharp;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Audio.NAudio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Configuration;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;
using Point = OpenCvSharp.Point;

namespace Rayman1LoadRemover {

    /*
     * TODO:
     * Mr. Sax loading screen
     *
     */

    static class LoadRemover {

        private static readonly IModelService modelService = new InMemoryModelService(); // store fingerprints in RAM
        private static readonly NAudioService audioService = new NAudioService(); // default audio library

        public static readonly string DataFolder = "data";
        public static readonly string SoundsFolder = Path.Combine(DataFolder, "sounds");
        private static readonly string ffmpegPath = Path.Combine(DataFolder, "FFmpeg", "bin", "x64", "ffmpeg.exe");
        private static readonly string tempAudioFilePath = "temp.mp3";
        private static readonly float DesiredRatio = 4 / 3; // 4:3 Ratio

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
            Phase_0_ExtractMp3,
            Phase_1_CropVideo,
            Phase_2_StartingTime,
            Phase_3_VideoScale,
            Phase_4_EndingTime,
            Phase_5_OverworldLoads,
            Phase_6_DeathLoads,
            Phase_7_EndSignAndBossLoads,
            Phase_8_GenerateReport
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

        public static async Task<LoadResults> Start(string file, bool partialRun, Action<ProgressPhase, float> updateProgress)
        {
            List<Load> loads = new List<Load>();

            updateProgress.Invoke(ProgressPhase.Phase_0_ExtractMp3, 0);

            ExtractMp3(file, tempAudioFilePath);

            var queryResult = await QueryFile(tempAudioFilePath);
            var entries = queryResult.ResultEntries.ToList();

            VideoCapture capture = new VideoCapture(file);

            float ratio = (float)capture.FrameWidth / (float)capture.FrameHeight;
            //if (capture.fra)

            updateProgress.Invoke(ProgressPhase.Phase_1_CropVideo, 0);
            // crop video here

            updateProgress.Invoke(ProgressPhase.Phase_2_StartingTime, 0);

            int startingFrame = 0;
            int endingFrame = capture.FrameCount-1;

            if (!partialRun) {
                var startLoad = Util.CountDarknessFrames(LoadType.Start, capture, 0.0f,
                    (int) (capture.Fps * StartScreenMaxDuration));
                if (startLoad.FrameStart == -1) {
                    throw new Exception(
                        "Start screen not detected, make sure the video starts on the \"Start\"/\"Options\" screen");
                }

                loads.Add(startLoad);
                startingFrame = startLoad.FrameStart;
            }

            updateProgress.Invoke(ProgressPhase.Phase_3_VideoScale, 0);

            float videoScale = LifeCounter.GetLifeCountScale(capture);
            if (float.IsNaN(videoScale)) {
                throw new Exception("Video Scale couldn't be determined: " + videoScale);
            }

            updateProgress.Invoke(ProgressPhase.Phase_4_EndingTime, 0);

            if (!partialRun) {
                var _endingFrame = BossLoads.GetLastFinalBossFrame(capture, videoScale);
                if (!_endingFrame.HasValue) {
                    throw new Exception(
                        "Final hit not detected, make sure the video doesn't end more than 3 minutes after the final hit.");
                }

                endingFrame = _endingFrame.Value;
            }

            updateProgress.Invoke(ProgressPhase.Phase_5_OverworldLoads, 0);
            loads.AddRange(OverworldLoads.GetOverworldLoads(capture, videoScale));

            updateProgress.Invoke(ProgressPhase.Phase_6_DeathLoads, 0);
            loads.AddRange(DeathLoads.GetDeathLoads(capture, videoScale));

            updateProgress.Invoke(ProgressPhase.Phase_7_EndSignAndBossLoads, 0);

            foreach (var result in queryResult.ResultEntries) {
                string resultString = $"Track ID = {result.Track.Id}, Score = {result.Confidence}, Confidence = {result.Score}, Match at = {result.QueryMatchStartsAt}";
                Debug.WriteLine(resultString);

                if (Enum.TryParse(result.Track.Id, out SoundType type)) {
                    switch (type) {
                        case SoundType.EndSign:
                            var load = Util.CountFrozenFrames(LoadType.EndSign, capture, result.QueryMatchStartsAt,
                                (int)(capture.Fps*EndSignLoadingScreenMinDuration), 600);

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
            foreach (var load in loads.Where(l=>l.Type!=LoadType.BackSign).ToList()) {
                loads.RemoveAll(l => l.Type == LoadType.BackSign && l.Overlaps(load, (int)(capture.Fps * 0.5f)));
            }

            updateProgress.Invoke(ProgressPhase.Phase_8_GenerateReport, 0);

            LoadResults results = new LoadResults(loads, (float)capture.Fps, startingFrame, endingFrame);

            results.SaveDebugImages(capture, "debugExport", "file");
            var report = new LoadRemoverReport(file, results, capture);
            report.GenerateHtml(TemplateFile).Save("report.html");
            
            return results;
        }

        // Binary search


        private static void ExtractMp3(string sourceFile, string targetFile)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath);
            startInfo.Arguments = $"-y -i {sourceFile} \"{targetFile}\"";
            var process = Process.Start(startInfo);
            while (!process.HasExited) { }
        }

    }
}
