using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using OpenCvSharp;

namespace Rayman1LoadRemover {
    class LoadRemoverReport
    {

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
                            $"<tr>{string.Join("", _columns.Values.Select(v => $"<td>{v.Invoke(load, _results)}</td>"))}</tr>";
                    }));
            }
        }

        private Dictionary<string,Func<Load,LoadResults,string>> _columns = new Dictionary<string, Func<Load, LoadResults, string>>()
        {
            {"Load Type", (load,lr)=>load.Type.ToString()},
            {"Start (seconds)", (load,lr)=>load.FrameStart.ToString()},
            {"End (seconds)", (load,lr)=>load.FrameEnd.ToString()},
            {"Length (seconds)", (load,lr)=>load.Length.ToString()},
            {"Start (frames)", (load,lr)=>FrameToTimeString(load.FrameStart, lr.FPS)},
            {"End (frames)", (load,lr)=>FrameToTimeString(load.FrameEnd, lr.FPS)},
            {"Length (frames)", (load,lr)=>FrameToTimeString(load.Length, lr.FPS)},
        };

        private Dictionary<string, Func<string>> _templateElements;
        private string _fileName;

        public LoadRemoverReport(string fileName, LoadResults results, VideoCapture capture)
        {
            this._fileName = fileName;
            this._results = results;
            _templateElements = new Dictionary<string, Func<string>>()
            {
                {"title", ()=>Title},
                {"tableHeaders", ()=>TableHeaders},
                {"tableRows", ()=>TableRows},
            };
        }

        private static string FrameToTimeString(int frames, float fps)
        {
            var timespan = TimeSpan.FromSeconds(frames / fps);
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
