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
        
        private static readonly string SoundsFolder = "sounds";
        private static readonly string ffmpegPath = Path.Combine("FFmpeg", "bin", "x64", "ffmpeg.exe");
        private static readonly string tempAudioFilePath = "temp.mp3";

        public static readonly string ImageFolder = "images";

        /// <summary>
        /// Duration before the end sign check can detect the end of the loading screen, in seconds
        /// </summary>
        private static float EndSignLoadingScreenMinDuration = 1.0f;

        public enum SoundType
        {
            EndSign, Boss
        }

        public static void Init()
        {
            AddSample(SoundType.EndSign, Path.Combine(SoundsFolder, "endsign.wav"));
            AddSample(SoundType.Boss, Path.Combine(SoundsFolder, "bossdefeat.wav"));

            LifeCounter.Init();
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

        public static async Task<LoadResults> Start(string file)
        {
            List<Load> loads = new List<Load>();

            ExtractMp3(file, tempAudioFilePath);
            var queryResult = await QueryFile(tempAudioFilePath);
            var entries = queryResult.ResultEntries.ToList();

            VideoCapture capture = new VideoCapture(file);

            float videoScale = LifeCounter.GetLifeCountScale(capture);
            if (float.IsNaN(videoScale)) {
                throw new Exception("Video Scale couldn't be determined: " + videoScale);
            }

            loads.AddRange(OverworldLoads.GetOverworldLoads(capture, videoScale));
            loads.AddRange(DeathLoads.GetDeathLoads(capture, videoScale));

            foreach (var result in queryResult.ResultEntries) {
                string resultString = $"Track ID = {result.Track.Id}, Score = {result.Confidence}, Confidence = {result.Score}, Match at = {result.QueryMatchStartsAt}";
                Debug.WriteLine(resultString);

                if (Enum.TryParse(result.Track.Id, out SoundType type)) {
                    switch (type) {
                        case SoundType.EndSign:
                            var load = Util.CountFrozenFrames(LoadType.EndSign, capture, result.QueryMatchStartsAt,
                                (int)(capture.Fps*EndSignLoadingScreenMinDuration), 600);

                            if (load.HasValue) {

                                loads.RemoveAll(l => l.Type == LoadType.BackSign && l.Overlaps(load.Value));

                                loads.Add(load.Value);
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            LoadResults results = new LoadResults(loads, (float)capture.Fps);

            results.SaveDebugImages(capture, "debugExport", "file");

            
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
