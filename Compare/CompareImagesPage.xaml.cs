using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class CompareImagesPage : UserControl
    {
        private double zoomL = 100, zoomR = 100;
        private bool isPanningL = false, isPanningR = false;
        private Point panStartL, panStartR;
        private double panStartXL, panStartYL, panStartXR, panStartYR;

        public CompareImagesPage()
        {
            InitializeComponent();
        }

        private void ChooseLeft_Click(object sender, RoutedEventArgs e) => LoadImage(true);
        private void ChooseRight_Click(object sender, RoutedEventArgs e) => LoadImage(false);

        private void LoadImage(bool left)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
                InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Title = left ? "Choose Left Image" : "Choose Right Image"
            };
            if (dlg.ShowDialog() != true) return;

            var bmp = LoadBitmapUnlocked(dlg.FileName);
            if (left) { imgLeft.Source = bmp; txtLeftPlaceholder.Visibility = Visibility.Collapsed; }
            else { imgRight.Source = bmp; txtRightPlaceholder.Visibility = Visibility.Collapsed; }
        }

        // Loads fully into memory so the file isn't left locked (same reasoning as the
        // Imaging page's LoadBitmapUnlocked).
        private static BitmapImage LoadBitmapUnlocked(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ═══ Zoom (mouse wheel on either panel; mirrors to both when synced) ═══
        private void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isLeft = (sender as FrameworkElement)?.Tag as string == "L";
            double delta = e.Delta > 0 ? 15 : -15;

            if (isLeft) zoomL = Math.Clamp(zoomL + delta, 10, 500);
            else zoomR = Math.Clamp(zoomR + delta, 10, 500);

            if (chkSync.IsChecked == true)
            {
                if (isLeft) zoomR = zoomL; else zoomL = zoomR;
            }

            ApplyZoom();
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            zoomL = Math.Clamp(zoomL + 25, 10, 500);
            zoomR = Math.Clamp(zoomR + 25, 10, 500);
            ApplyZoom();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            zoomL = Math.Clamp(zoomL - 25, 10, 500);
            zoomR = Math.Clamp(zoomR - 25, 10, 500);
            ApplyZoom();
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            zoomL = zoomR = 100;
            panLeft.X = panLeft.Y = panRight.X = panRight.Y = 0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            scaleLeft.ScaleX = scaleLeft.ScaleY = zoomL / 100.0;
            scaleRight.ScaleX = scaleRight.ScaleY = zoomR / 100.0;
            txtZoom.Text = chkSync.IsChecked == true ? $"{zoomL:F0}%" : $"L:{zoomL:F0}% R:{zoomR:F0}%";
        }

        // ═══ Pan (drag on either panel; mirrors to both when synced) ═══
        private void Viewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            bool isLeft = (sender as Border)?.Tag as string == "L";
            if (isLeft)
            {
                isPanningL = true;
                panStartL = e.GetPosition(borderLeft);
                panStartXL = panLeft.X; panStartYL = panLeft.Y;
                borderLeft.CaptureMouse();
            }
            else
            {
                isPanningR = true;
                panStartR = e.GetPosition(borderRight);
                panStartXR = panRight.X; panStartYR = panRight.Y;
                borderRight.CaptureMouse();
            }
        }

        private void Viewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanningL)
            {
                var p = e.GetPosition(borderLeft);
                panLeft.X = panStartXL + (p.X - panStartL.X);
                panLeft.Y = panStartYL + (p.Y - panStartL.Y);
                if (chkSync.IsChecked == true) { panRight.X = panLeft.X; panRight.Y = panLeft.Y; }
            }
            else if (isPanningR)
            {
                var p = e.GetPosition(borderRight);
                panRight.X = panStartXR + (p.X - panStartR.X);
                panRight.Y = panStartYR + (p.Y - panStartR.Y);
                if (chkSync.IsChecked == true) { panLeft.X = panRight.X; panLeft.Y = panRight.Y; }
            }
        }

        private void Viewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isPanningL) { isPanningL = false; borderLeft.ReleaseMouseCapture(); }
            if (isPanningR) { isPanningR = false; borderRight.ReleaseMouseCapture(); }
        }
    }
}
