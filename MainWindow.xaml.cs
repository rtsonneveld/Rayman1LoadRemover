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
                LoadRemover.ProgressPhase.Phase_1_PreprocessVideo     => "Preprocessing Video",
                LoadRemover.ProgressPhase.Phase_2_StartingTime        => "Determining Start Time",
                LoadRemover.ProgressPhase.Phase_3_VideoScale          => "Determining Video Scale",
                LoadRemover.ProgressPhase.Phase_4_EndingTime          => "Determining End Time",
                LoadRemover.ProgressPhase.Phase_5_EndSignLoads        => "Checking End Sign Loads",
                LoadRemover.ProgressPhase.Phase_6_OverworldLoads      => "Checking Overworld Loads",
                LoadRemover.ProgressPhase.Phase_7_DeathLoads          => "Checking Death Loads",
                LoadRemover.ProgressPhase.Phase_8_BossLoads => "Checking Boss Loads",
                LoadRemover.ProgressPhase.Phase_9_GenerateReport      => "Generating Report",
                LoadRemover.ProgressPhase.Phase_10_Finished            => "Done",
                _ => ""
            };

            this.Dispatcher.Invoke(() => { ProgressBarText.Text = text; });

            bool enableStartButton = (phase == LoadRemover.ProgressPhase.Phase_10_Finished) ;
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
                    new LoadRemover.TrimSettings((float)TrimStart.Value.GetValueOrDefault(TimeSpan.Zero).TotalSeconds, (float)TrimEnd.Value.GetValueOrDefault(TimeSpan.Zero).TotalSeconds);
            }

            bool resize = resizeVideoCheckbox.IsChecked == true;

            var task = Task.Run(() =>
            {
                LoadType loadTypes = LoadType.None;

                Dispatcher.Invoke(() =>
                {
                    if (checkBoxLoadTypeDeath.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.Death;
                    if (checkBoxLoadTypeEndSign.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.EndSign;
                    if (checkBoxLoadTypeOverworld.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.Overworld;
                    if (checkBoxLoadTypeBackSign.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.BackSign;
                    if (checkBoxLoadTypeBoss.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.Boss;
                    if (checkBoxLoadTypeStart.IsChecked.GetValueOrDefault(false)) loadTypes |= LoadType.Start;
                });

                try {
                    LoadRemover.Start(file, partialRun, cropSettings, trimSettings, loadTypes, resize,
                        (phase, progress) =>
                            UpdateProgress(phase, ((int) phase / progressSteps) + (progress / progressSteps))
                    );
                } catch (Exception e) {
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine(e.ToString());
                        MessageBox.Show($"An error occurred: {e.Message}{Environment.NewLine}{e.StackTrace}");
                        UpdateProgress(LoadRemover.ProgressPhase.Phase_10_Finished, 1.0f);
                    });
                }

                UpdateProgress(LoadRemover.ProgressPhase.Phase_10_Finished, 1.0f);
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

        private bool CheckAnyLoadTypeCheckboxesNull()
        {
            return checkBoxLoadTypeAll == null ||
                   checkBoxLoadTypeDeath == null ||
                   checkBoxLoadTypeEndSign == null ||
                   checkBoxLoadTypeOverworld == null ||
                   checkBoxLoadTypeBackSign == null ||
                   checkBoxLoadTypeBoss == null ||
                   checkBoxLoadTypeStart == null;
        }

        private void checkBoxLoadTypeAll_Checked(object sender, RoutedEventArgs e)
        {
            if (CheckAnyLoadTypeCheckboxesNull()) {
                return;
            }
            bool c = checkBoxLoadTypeAll.IsChecked.GetValueOrDefault(false);

            checkBoxLoadTypeDeath.IsChecked = c;
            checkBoxLoadTypeEndSign.IsChecked = c;
            checkBoxLoadTypeOverworld.IsChecked = c;
            checkBoxLoadTypeBackSign.IsChecked = c;
            checkBoxLoadTypeBoss.IsChecked = c;
            checkBoxLoadTypeStart.IsChecked = c;
        }

        private void checkBoxLoadTypeAll_UpdateChecked(object sender, RoutedEventArgs e)
        {
            if (CheckAnyLoadTypeCheckboxesNull()) {
                return;
            }

            checkBoxLoadTypeAll.Checked -= checkBoxLoadTypeAll_Checked;
            checkBoxLoadTypeAll.Unchecked -= checkBoxLoadTypeAll_Checked;

            checkBoxLoadTypeAll.IsChecked =
                checkBoxLoadTypeDeath.IsChecked.GetValueOrDefault(false) &&
                checkBoxLoadTypeEndSign.IsChecked.GetValueOrDefault(false) &&
                checkBoxLoadTypeOverworld.IsChecked.GetValueOrDefault(false) &&
                checkBoxLoadTypeBackSign.IsChecked.GetValueOrDefault(false) &&
                checkBoxLoadTypeBoss.IsChecked.GetValueOrDefault(false) &&
                checkBoxLoadTypeStart.IsChecked.GetValueOrDefault(false);

            checkBoxLoadTypeAll.Checked += checkBoxLoadTypeAll_Checked;
            checkBoxLoadTypeAll.Unchecked += checkBoxLoadTypeAll_Checked;

        }
    }
}
