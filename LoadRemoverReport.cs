using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    class LoadRemoverReport
    {

        private VideoCapture _capture;
        private LoadResults _results;

        private string Title => $"Load Remover Report for {_fileName}";
        private string TableHeaders => string.Join(Environment.NewLine, _columns.Keys.Select(k=>$"<td>{k}</td>"));
        private string TableRows
        {
            get
            {
                return string.Join(Environment.NewLine,
                    _results.Loads.Select(load =>
                    {
                        return
                            $"<tr>{string.Join("", _columns.Values.Select(v => $"<td>{v.Invoke(load, _results, _capture)}</td>"))}</tr>";
                    }));
            }
        }

        private Dictionary<string,Func<Load,LoadResults,VideoCapture,string>> _columns = new Dictionary<string, Func<Load, LoadResults, VideoCapture, string>>()
        {
            {"Load Type", (load,lr,c)=>load.Type.ToString()},
            {"Start (frames)", (load,lr,c)=>load.FrameStart.ToString()},
            {"End (frames)", (load,lr,c)=>load.FrameEnd.ToString()},
            {"Length (frames)", (load,lr,c)=>load.Length.ToString()},
            {"Start (seconds)", (load,lr,c)=>FrameToTimeString(load.FrameStart, lr.FPS)},
            {"End (seconds)", (load,lr,c)=>FrameToTimeString(load.FrameEnd, lr.FPS)},
            {"Length (seconds)", (load,lr,c)=>FrameToTimeString(load.Length, lr.FPS)},
            {"Frames Before Load", (load,lr,c)=>CreateFrameImages(load,lr,c, load.FrameStart-2,load.FrameStart)},
            {"Frames After Load", (load,lr,c)=>CreateFrameImages(load,lr,c, load.FrameEnd,load.FrameEnd+2)},
        };

        private static string CreateFrameImages(Load load, LoadResults lr, VideoCapture capture, int offsetStart, int offsetEnd)
        {
            string result = "";
            for (int i = offsetStart; i <= offsetEnd; i++) {
                capture.Set(VideoCaptureProperties.PosFrames, i);
                var mat = new Mat();
                capture.Read(mat);
                result += CreateBase64HtmlImage(mat);
            }

            return result;
        }

        private static string CreateBase64HtmlImage(Mat mat)
        {
            byte[] buffer;

            Mat mediumMat = new Mat();
            Cv2.Resize(InputArray.Create(mat), OutputArray.Create(mediumMat), new Size(mat.Width / 2, mat.Height / 2));
            Cv2.ImEncode(".jpeg", InputArray.Create(mediumMat), out buffer);
            string b64 = Convert.ToBase64String(buffer);

            // Create thumbnail
            Mat smallMat = new Mat();
            Cv2.Resize(InputArray.Create(mat), OutputArray.Create(smallMat), new Size(mat.Width/6, mat.Height/6));

            Cv2.ImEncode(".jpeg", InputArray.Create(smallMat), out buffer);
            string b64Small = Convert.ToBase64String(buffer);

            return $"<a class=\"thumbnail\" href=\"#thumb\"><img src=\"data:image/jpeg;base64, {b64Small}\" class=\"thumbnail-image\" border=\"0\" /><span><img src=\"data:image/jpeg;base64, {b64}\" /><br></span></a>";
        }

        private Dictionary<string, Func<string>> _templateElements;
        private string _fileName;

        public LoadRemoverReport(string fileName, LoadResults results, VideoCapture capture)
        {
            this._fileName = fileName;
            this._results = results;
            this._capture = capture;

            _templateElements = new Dictionary<string, Func<string>>()
            {
                {"title", ()=>Title},
                {"tableHeaders", ()=>TableHeaders},
                {"tableRows", ()=>TableRows},
                {"videoFPS", ()=>results.FPS.ToString()},
                {"startTimeSeconds", ()=>FrameToTimeString(results.StartingFrame, results.FPS)},
                {"startTimeFrames", ()=>results.StartingFrame.ToString()},
                {"endTimeSeconds", ()=>FrameToTimeString(results.EndingFrame, results.FPS)},
                {"endTimeFrames", ()=>results.EndingFrame.ToString()},
                {"totalTimeWithLoadsSeconds", ()=>FrameToTimeString(results.TotalFramesIncludingLoads,results.FPS)},
                {"totalTimeWithLoadsFrames", ()=>results.TotalFramesIncludingLoads.ToString()},
                {"totalLoadTimeSeconds", ()=>FrameToTimeString(results.TotalLoadingFrames, results.FPS)},
                {"totalLoadTimeFrames", ()=>results.TotalLoadingFrames.ToString() },
                {"totalTimeWithoutLoadsSeconds", ()=>FrameToTimeString(results.TotalFramesWithoutLoads, results.FPS)},
                {"totalTimeWithoutLoadsFrames", ()=>results.TotalFramesWithoutLoads.ToString()},
                {"loadCountBackSign", ()=>results.Loads.Count(l=>l.Type==LoadType.BackSign).ToString()},
                {"loadCountBoss", ()=>results.Loads.Count(l=>l.Type==LoadType.Boss).ToString()},
                {"loadCountDeath", ()=>results.Loads.Count(l=>l.Type==LoadType.Death).ToString()},
                {"loadCountEndSign", ()=>results.Loads.Count(l=>l.Type==LoadType.EndSign).ToString()},
                {"loadCountOverworld", ()=>results.Loads.Count(l=>l.Type==LoadType.Overworld).ToString()},
            };
        }

        private static string FrameToTimeString(int frames, float fps)
        {
            var timespan = TimeSpan.FromSeconds(Math.Round(frames / fps, 3));
            return timespan.ToString("g");
        }

        public GeneratedReport GenerateHtml(string templateFilePath)
        {
            string html = File.ReadAllText(templateFilePath);

            foreach (var field in _templateElements) {
                html = html.Replace($"{{{field.Key}}}", field.Value.Invoke());
            }

            return new GeneratedReport(html);
        }

        public readonly struct GeneratedReport
        {
            public string Html { get; }

            public GeneratedReport(string html)
            {
                this.Html = html;
            }

            /// <summary>
            /// Saves the report to the specified file. Report needs to be generated first with GenerateHTML()
            /// </summary>
            /// <param name="file"></param>
            public void Save(string file)
            {
                File.WriteAllText(file, Html);
            }

        }
    }
}
