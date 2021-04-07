using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using OpenCvSharp;
using Window = System.Windows.Window;

namespace Rayman1LoadRemover {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public MainWindow()
        {
            InitializeComponent();
            LoadRemover.Init();

            cropContainer.Visibility = Visibility.Collapsed;
            trimContainer.Visibility = Visibility.Collapsed;
        }

        private void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.FileName = browseFile.Text;
            dialog.DefaultExt = ".mp4";
            dialog.Filter = "MP4 Files (.mp4)|*.mp4";

            // Show open file dialog box
            bool? result = dialog.ShowDialog();

            // Process open file dialog box results
            if (result == true) {
                // Open document
                browseFile.Text = dialog.FileName;
            }
        }

        private void UpdateProgress(LoadRemover.ProgressPhase phase, float progress)
        {
            this.Dispatcher.Invoke(() => { ProgressBar.Value = progress; });

            string text = phase switch
            {
                LoadRemover.ProgressPhase.Phase_0_PreprocessVideo     => "Preprocessing Video",
                LoadRemover.ProgressPhase.Phase_1_AnalyseAudio        => "Analysing Audio",
                LoadRemover.ProgressPhase.Phase_2_StartingTime        => "Determining Start Time",
                LoadRemover.ProgressPhase.Phase_3_VideoScale          => "Determining Video Scale",
                LoadRemover.ProgressPhase.Phase_4_EndingTime          => "Determining End Time",
                LoadRemover.ProgressPhase.Phase_5_OverworldLoads      => "Checking Overworld Loads",
                LoadRemover.ProgressPhase.Phase_6_DeathLoads          => "Checking Death Loads",
                LoadRemover.ProgressPhase.Phase_7_EndSignAndBossLoads => "Checking EndSign/Boss Loads",
                LoadRemover.ProgressPhase.Phase_8_GenerateReport      => "Generating Report",
                LoadRemover.ProgressPhase.Phase_9_Finished            => "Done",
                _ => ""
            };

            this.Dispatcher.Invoke(() => { ProgressBarText.Text = text; });

            bool enableStartButton = (phase == LoadRemover.ProgressPhase.Phase_9_Finished) ;
            this.Dispatcher.Invoke(() => { startButton.IsEnabled = enableStartButton; });
        }

        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            string file = browseFile.Text;
            if (!VerifyFileExists(file)) return;

            float progressSteps = Enum.GetValues(typeof(LoadRemover.ProgressPhase)).Length;

            bool partialRun = partialRunCheckbox.IsChecked.Value;

            LoadRemover.CropSettings? cropSettings = null;
            if (cropCheckbox.IsChecked == true) {
                cropSettings =
                    new LoadRemover.CropSettings(CropLeft.Value.Value, CropTop.Value.Value, CropRight.Value.Value, CropBot.Value.Value);
            }

            LoadRemover.TrimSettings? trimSettings = null;
            if (trimCheckbox.IsChecked == true) {
                trimSettings =
                    new LoadRemover.TrimSettings((int)Math.Ceiling(TrimStart.Value.Value.TotalSeconds), (int)Math.Ceiling(TrimEnd.Value.Value.TotalSeconds));
            }

            bool resize = resizeVideoCheckbox.IsChecked == true;

            var task = Task.Run(() =>
            {
                try {
                    LoadRemover.Start(file, partialRun, cropSettings, trimSettings, resize,
                        (phase, progress) =>
                            UpdateProgress(phase, ((int) phase / progressSteps) + (progress / progressSteps))
                    );
                } catch (Exception e) {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("An error occurred: " + e.Message);
                        UpdateProgress(LoadRemover.ProgressPhase.Phase_9_Finished, 1.0f);
                    });
                }

                UpdateProgress(LoadRemover.ProgressPhase.Phase_9_Finished, 1.0f);
            });
        }

        private static bool VerifyFileExists(string file)
        {
            if (!File.Exists(file)) {
                MessageBox.Show($"File {file} does not exist!");
                return false;
            }

            return true;
        }

        private void SelectCropSize_OnClick(object sender, RoutedEventArgs e)
        {
            string file = browseFile.Text;
            if (!VerifyFileExists(file)) return;

            var capture = VideoCapture.FromFile(file);
            capture.Set(VideoCaptureProperties.PosFrames, 0);
            var mat = new Mat();
            capture.Read(mat);
            
            var window = new CropWindow(mat);

            window.cropLeft.Value = CropLeft.Value;
            window.cropTop.Value = CropTop.Value;
            window.cropRight.Value = CropRight.Value;
            window.cropBot.Value = CropBot.Value;

            if (window.ShowDialog().HasValue) {
                CropLeft.Value = window.cropLeft.Value;
                CropTop.Value = window.cropTop.Value;
                CropRight.Value = window.cropRight.Value;
                CropBot.Value = window.cropBot.Value;
            }
        }

        private void cropCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            cropContainer.Visibility = cropCheckbox.IsChecked==true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void trimCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            trimContainer.Visibility = trimCheckbox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;


            string file = browseFile.Text;
            if (!VerifyFileExists(file)) return;

            var capture = VideoCapture.FromFile(file);

            // Only changing to 0 doesn't update the field...
            TrimStart.Value = TimeSpan.FromSeconds(1);
            TrimStart.Value = TimeSpan.FromSeconds(0);
            TrimEnd.Value = TimeSpan.FromSeconds(capture.FrameCount/capture.Fps);
        }

        private void browseFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            settingsPanel.IsEnabled = File.Exists(browseFile.Text);
        }

        private void FileTextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void FileTextBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) {
                    ((TextBox)sender).Text = files[0];
                }
            }
        }
    }
}
