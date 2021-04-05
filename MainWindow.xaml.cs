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

namespace Rayman1LoadRemover {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window {
        public MainWindow()
        {
            InitializeComponent();
            LoadRemover.Init();
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

        private async void startButton_Click(object sender, RoutedEventArgs e)
        {
            string file = browseFile.Text;
            if (!File.Exists(file)) {
                MessageBox.Show($"File {file} does not exist!");
                return;
            }

            float progressSteps = Enum.GetValues(typeof(LoadRemover.ProgressPhase)).Length;

            await LoadRemover.Start(file, partialRunCheckbox.IsChecked.Value, (phase, progress) =>
            {

                ProgressBar.Value = (int)phase / progressSteps;
            });
        }
    }
}
