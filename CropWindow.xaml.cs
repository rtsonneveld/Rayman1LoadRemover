using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenCvSharp;
using Point = System.Windows.Point;
using Window = System.Windows.Window;

namespace Rayman1LoadRemover {
    /// <summary>
    /// Interaction logic for CropWindow.xaml
    /// </summary>
    public partial class CropWindow : Window {

        private Point startPoint;
        private Point endPoint;
        private bool dragging = false;

        public CropWindow(Mat mat)
        {
            InitializeComponent();

            Cv2.ImEncode(".png", InputArray.Create(mat), out byte[] buf);

            canvas.Width = mat.Width;
            canvas.Height = mat.Height;
            canvas.LayoutTransform = new ScaleTransform(0.5f, 0.5f);

            ImageBrush ib = new ImageBrush();
            ib.ImageSource = LoadImage(buf);
            canvas.Background = ib;

            rect.Width = canvas.Width;
            rect.Height = canvas.Height;

            UpdateInputs();
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) {
                dragging = true;
                startPoint = Mouse.GetPosition(canvas);
                Point p = Mouse.GetPosition(canvas);
                rect.Width = 1;
                rect.Height = 1;
                Canvas.SetLeft(rect, p.X);
                Canvas.SetTop(rect, p.Y);
                canvas.CaptureMouse();
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (dragging) {

                startPoint = RestrictPointToCanvas(startPoint);
                endPoint = RestrictPointToCanvas(endPoint);

                UpdateRect();
                UpdateInputs();

                dragging = false;

                Mouse.Capture(null);

            }
        }

        private Point RestrictPointToCanvas(Point p)
        {
            return new Point(Math.Clamp(p.X, 0, canvas.Width), Math.Clamp(p.Y, 0, canvas.Height));
        }

        private void CropWindow_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (dragging) {
                endPoint = Mouse.GetPosition(canvas);

                UpdateRect();
            }
        }

        private void UpdateRect()
        {
            var xFrom = startPoint.X;
            var yFrom = startPoint.Y;

            Canvas.SetLeft(rect, startPoint.X);
            Canvas.SetTop(rect, startPoint.Y);

            if (endPoint.X < xFrom) {
                Canvas.SetLeft(rect, endPoint.X);
            }

            if (endPoint.Y < yFrom) {
                Canvas.SetTop(rect, endPoint.Y);
            }

            rect.Width = Math.Abs(endPoint.X - xFrom);
            rect.Height = Math.Abs(endPoint.Y - yFrom);
        }

        private void forceWidthToRatio_Click(object sender, RoutedEventArgs e)
        {
            rect.Width = rect.Height * 4f/3f;
            if (Canvas.GetLeft(rect) + rect.Width > canvas.Width) {
                Canvas.SetLeft(rect, canvas.Width-rect.Width);
            }

            UpdateInputs();
        }

        private void forceHeightToRatio_Click(object sender, RoutedEventArgs e)
        {
            rect.Height = Math.Min(rect.Width * 3f/4f, canvas.Height);
            if (Canvas.GetTop(rect) + rect.Height > canvas.Height) {
                Canvas.SetTop(rect, canvas.Height - rect.Height);
            }

            UpdateInputs();
        }

        private void UpdateInputs()
        {
            InitCropValues();

            cropLeft.Value = (int)Canvas.GetLeft(rect);
            cropTop.Value = (int)Canvas.GetTop(rect);
            cropRight.Value = (int)(canvas.Width - rect.Width - cropLeft.Value);
            cropBot.Value = (int)(canvas.Height - rect.Height - cropTop.Value);
        }

        private void UpdateRectFromInputs(object sender, RoutedPropertyChangedEventArgs<object> e)
        {

            if (!dragging) {
                InitCropValues();

                Canvas.SetLeft(rect, (double) cropLeft.Value.Value);
                Canvas.SetTop(rect, (double) cropTop.Value.Value);
                rect.Width = Math.Max(canvas.Width - cropRight.Value.Value - cropLeft.Value.Value, 0);
                rect.Height = Math.Max(canvas.Height - cropBot.Value.Value - cropTop.Value.Value, 0);
            }
        }

        private void InitCropValues()
        {
            if (!cropLeft.Value.HasValue) cropLeft.Value = 0;
            if (!cropTop.Value.HasValue) cropTop.Value = 0;
            if (!cropRight.Value.HasValue) cropRight.Value = 0;
            if (!cropBot.Value.HasValue) cropBot.Value = 0;
        }

        private static BitmapImage LoadImage(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;
            var image = new BitmapImage();
            using (var mem = new MemoryStream(imageData)) {
                mem.Position = 0;
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = null;
                image.StreamSource = mem;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
