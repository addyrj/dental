using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class DicomPage : UserControl
    {
        private BitmapSource? _importedImage;
        private string _importedModality = "Intraoral";

        private string? _exportSourcePath;

        public DicomPage()
        {
            InitializeComponent();
        }

        // ═══ IMPORT ═══
        private void ImportChoose_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "DICOM Files|*.dcm;*.dicom|All Files|*.*", Title = "Choose DICOM File" };
            if (dlg.ShowDialog() != true) return;

            stackImportTags.Children.Clear();
            _importedImage = null;
            btnSaveImported.IsEnabled = false;
            HideStatus(txtImportStatus);

            try
            {
                var dicomFile = DicomFile.Open(dlg.FileName);
                var ds = dicomFile.Dataset;

                var syntax = ds.InternalTransferSyntax;
                bool uncompressed = syntax == DicomTransferSyntax.ExplicitVRLittleEndian
                                 || syntax == DicomTransferSyntax.ImplicitVRLittleEndian
                                 || syntax == DicomTransferSyntax.ExplicitVRBigEndian;
                if (!uncompressed)
                    throw new NotSupportedException(
                        $"This file uses a compressed transfer syntax ({syntax.UID.Name}) that isn't decoded in this build. " +
                        "Only uncompressed grayscale DICOM is supported currently.");

                string patientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "");
                string patientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, "");
                string studyDate = ds.GetSingleValueOrDefault(DicomTag.StudyDate, "");
                string modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");

                ushort width = ds.GetSingleValueOrDefault<ushort>(DicomTag.Columns, 0);
                ushort height = ds.GetSingleValueOrDefault<ushort>(DicomTag.Rows, 0);
                ushort bitsAllocated = ds.GetSingleValueOrDefault<ushort>(DicomTag.BitsAllocated, 8);
                ushort samplesPerPixel = ds.GetSingleValueOrDefault<ushort>(DicomTag.SamplesPerPixel, 1);

                var pixelData = DicomPixelData.Create(ds);
                byte[] raw = pixelData.GetFrame(0).Data;

                BitmapSource bmp;
                if (bitsAllocated == 16 && samplesPerPixel == 1)
                    bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, raw, width * 2);
                else if (bitsAllocated == 8 && samplesPerPixel == 1)
                    bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, raw, width);
                else
                    throw new NotSupportedException(
                        $"Unsupported pixel format (bits allocated={bitsAllocated}, samples/pixel={samplesPerPixel}). Only 8/16-bit grayscale is supported.");

                bmp.Freeze();
                _importedImage = bmp;
                _importedModality = string.IsNullOrWhiteSpace(modality) ? "Intraoral" : modality;
                imgImportPreview.Source = bmp;

                AddTagRow("Patient Name", string.IsNullOrEmpty(patientName) ? "—" : patientName);
                AddTagRow("Patient ID", string.IsNullOrEmpty(patientId) ? "—" : patientId);
                AddTagRow("Study Date", string.IsNullOrEmpty(studyDate) ? "—" : studyDate);
                AddTagRow("Modality", string.IsNullOrEmpty(modality) ? "—" : modality);
                AddTagRow("Dimensions", $"{width} × {height} px, {bitsAllocated}-bit");

                btnSaveImported.IsEnabled = true;
            }
            catch (Exception ex)
            {
                ShowStatus(txtImportStatus, $"Could not read this DICOM file: {ex.Message}", true);
            }
        }

        private void AddTagRow(string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = label + ":", Width = 100, FontSize = 11, Foreground = (Brush)FindResource("BrTextMuted") });
            row.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = (Brush)FindResource("BrText"), TextWrapping = TextWrapping.Wrap });
            stackImportTags.Children.Add(row);
        }

        private void SaveImported_Click(object sender, RoutedEventArgs e)
        {
            if (_importedImage == null) return;

            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
                Directory.CreateDirectory(dir);
                string prefix = _importedModality.ToLower() switch
                {
                    var m when m.Contains("pan") => "panoramic",
                    var m when m.Contains("ceph") => "cephalometric",
                    var m when m.Contains("cbct") || m.Contains("3d") => "cbct",
                    _ => "intraoral"
                };
                string file = $"{prefix}_dicomimport_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string path = Path.Combine(dir, file);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(_importedImage));
                using (var fs = new FileStream(path, FileMode.Create))
                    encoder.Save(fs);

                ShowStatus(txtImportStatus, $"Saved to gallery as {file} — visible on the Imaging page.", false);
            }
            catch (Exception ex)
            {
                ShowStatus(txtImportStatus, $"Could not save: {ex.Message}", true);
            }
        }

        // ═══ EXPORT ═══
        private void ExportChoose_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff",
                InitialDirectory = Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Title = "Choose Image to Export"
            };
            if (dlg.ShowDialog() != true) return;

            _exportSourcePath = dlg.FileName;
            txtExportSource.Text = "Source: " + Path.GetFileName(dlg.FileName);
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_exportSourcePath))
            {
                MessageBox.Show("Choose an image to export first.");
                return;
            }

            var saveDlg = new SaveFileDialog
            {
                Filter = "DICOM File|*.dcm",
                FileName = Path.GetFileNameWithoutExtension(_exportSourcePath) + ".dcm"
            };
            if (saveDlg.ShowDialog() != true) return;

            try
            {
                // Load the source image and force it to 8-bit grayscale raw pixels.
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.UriSource = new Uri(_exportSourcePath);
                source.EndInit();

                var gray8 = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
                int width = gray8.PixelWidth, height = gray8.PixelHeight;
                byte[] pixels = new byte[width * height];
                gray8.CopyPixels(pixels, width, 0);

                string modality = (cmbExportModality.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "IO";
                modality = modality.Split(' ')[0]; // "IO (Intraoral)" -> "IO"

                var dataset = new DicomDataset
                {
                    { DicomTag.PatientName, txtExportPatientName.Text.Trim() },
                    { DicomTag.PatientID, txtExportPatientId.Text.Trim() },
                    { DicomTag.Modality, modality },
                    { DicomTag.StudyDate, DateTime.Now.ToString("yyyyMMdd") },
                    { DicomTag.StudyTime, DateTime.Now.ToString("HHmmss") },
                    { DicomTag.StudyInstanceUID, DicomUID.Generate() },
                    { DicomTag.SeriesInstanceUID, DicomUID.Generate() },
                    { DicomTag.SOPInstanceUID, DicomUID.Generate() },
                    { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
                    { DicomTag.Manufacturer, "DentalX" },
                    { DicomTag.Rows, (ushort)height },
                    { DicomTag.Columns, (ushort)width },
                    { DicomTag.BitsAllocated, (ushort)8 },
                    { DicomTag.BitsStored, (ushort)8 },
                    { DicomTag.HighBit, (ushort)7 },
                    { DicomTag.PixelRepresentation, (ushort)0 },
                    { DicomTag.SamplesPerPixel, (ushort)1 },
                    { DicomTag.PhotometricInterpretation, "MONOCHROME2" }
                };

                var pixelData = DicomPixelData.Create(dataset, true);
                pixelData.AddFrame(new MemoryByteBuffer(pixels));

                var dicomFile = new DicomFile(dataset);
                dicomFile.Save(saveDlg.FileName);

                ShowStatus(txtExportStatus, $"Exported to {saveDlg.FileName}", false);
            }
            catch (Exception ex)
            {
                ShowStatus(txtExportStatus, $"Export failed: {ex.Message}", true);
            }
        }

        private void ShowStatus(TextBlock target, string msg, bool isError)
        {
            target.Text = msg;
            target.Foreground = isError ? (Brush)FindResource("BrDanger") : (Brush)FindResource("BrSuccess");
            target.Visibility = Visibility.Visible;
        }

        private void HideStatus(TextBlock target) => target.Visibility = Visibility.Collapsed;
    }
}
