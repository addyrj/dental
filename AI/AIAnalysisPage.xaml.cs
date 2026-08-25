using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class AIAnalysisPage : UserControl
    {
        public AIAnalysisPage()
        {
            InitializeComponent();
        }

        private void ChooseImage_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
                InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Title = "Choose Image"
            };
            if (dlg.ShowDialog() != true) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            bmp.Freeze();

            imgTarget.Source = bmp;
            txtNoImage.Visibility = Visibility.Collapsed;
        }

        private void RunAnalysis_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "No AI model is connected, so there is nothing to run.\n\n" +
                "This screen is wired up and ready — once a trained ONNX model is added " +
                "(caries / bone loss / periapical lesion / calculus detection) and ONNX Runtime " +
                "is referenced, results will render here for real, in place of these placeholders.",
                "AI Analysis — No Model Connected",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
