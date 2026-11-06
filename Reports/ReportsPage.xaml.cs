using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class ReportsPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private List<Patient> _patients = new();
        private Patient? _selectedPatient;
        private List<Examination> _exams = new();
        private Examination? _selectedExam;

        public ReportsPage()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadPatients();
        }

        private void LoadPatients()
        {
            _patients = db.GetAllPatients();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = txtSearch.Text.Trim().ToLower();
            var filtered = string.IsNullOrEmpty(q) ? _patients : _patients.Where(p => p.Name.ToLower().Contains(q)).ToList();
            lstPatients.ItemsSource = filtered.Select(p => $"{p.Id} | {p.Name}").ToList();
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void lstPatients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPatients.SelectedItem is not string sel) return;
            var idStr = sel.Split('|')[0].Trim();
            if (!int.TryParse(idStr, out int id)) return;

            _selectedPatient = db.GetPatientById(id);
            if (_selectedPatient == null) return;

            txtSelectedPatient.Text = $"{_selectedPatient.Name}  (P-{_selectedPatient.Id:D5})";
            _exams = db.GetExaminationsForPatient(id);
            lstExams.ItemsSource = _exams.Select(ex => $"{ex.Modality} — {ex.Date}").ToList();
            txtStatus.Text = "";

            // Auto-select when there's exactly one exam, so a patient with a single X-ray doesn't
            // require an extra, easy-to-miss click before Generate & Print does anything.
            if (_exams.Count == 1) lstExams.SelectedIndex = 0;
            else _selectedExam = null;
        }

        private void lstExams_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = lstExams.SelectedIndex;
            _selectedExam = (idx >= 0 && idx < _exams.Count) ? _exams[idx] : null;
        }

        private void GenerateAndPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatient == null) { MessageBox.Show("Select a patient first."); return; }
            if (_selectedExam == null) { MessageBox.Show("Select an examination first."); return; }

            var dlg = new System.Windows.Controls.PrintDialog();
            if (dlg.ShowDialog() != true) return;

            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                double pageW = dlg.PrintableAreaWidth;
                double pageH = dlg.PrintableAreaHeight;
                ctx.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

                double y = 40;
                string clinic = db.GetSetting("ClinicName", "DentalX Clinic");
                DrawText(ctx, clinic, 22, FontWeights.Bold, Brushes.Black, 40, ref y);
                DrawText(ctx, "Dental Radiology Report", 15, FontWeights.SemiBold, Brushes.DarkSlateGray, 40, ref y);
                y += 8;

                DrawText(ctx, $"Patient: {_selectedPatient.Name}   (ID: P-{_selectedPatient.Id:D5})", 12, FontWeights.Normal, Brushes.Black, 40, ref y);
                DrawText(ctx, $"Age: {_selectedPatient.Age}   Gender: {_selectedPatient.Gender}   Phone: {_selectedPatient.Phone}", 11, FontWeights.Normal, Brushes.Black, 40, ref y);
                DrawText(ctx, $"Examination: {_selectedExam.Modality}   Date: {_selectedExam.Date}", 11, FontWeights.Normal, Brushes.Black, 40, ref y);
                y += 10;

                if (!string.IsNullOrEmpty(_selectedExam.ImagePath) && File.Exists(_selectedExam.ImagePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(_selectedExam.ImagePath);
                        bmp.EndInit();

                        double imgW = Math.Min(pageW - 80, 380);
                        double imgH = imgW * bmp.PixelHeight / bmp.PixelWidth;
                        ctx.DrawImage(bmp, new Rect(40, y, imgW, imgH));
                        y += imgH + 20;
                    }
                    catch { /* if the image can't load, skip it rather than fail the whole report */ }
                }

                DrawText(ctx, "Findings:", 12, FontWeights.SemiBold, Brushes.Black, 40, ref y);
                DrawText(ctx, "No automated AI findings — manual clinical review required. (AI detection is not yet connected in this build.)",
                    11, FontWeights.Normal, Brushes.DimGray, 40, ref y);
                y += 16;

                DrawText(ctx, $"Reviewing Doctor: {CurrentSession.DisplayName}", 11, FontWeights.Normal, Brushes.Black, 40, ref y);
                DrawText(ctx, $"Report Generated: {DateTime.Now:dd MMM yyyy HH:mm}", 11, FontWeights.Normal, Brushes.Black, 40, ref y);
                y += 40;
                ctx.DrawLine(new Pen(Brushes.Black, 1), new Point(40, y), new Point(260, y));
                y += 4;
                DrawText(ctx, "Doctor's Signature", 9, FontWeights.Normal, Brushes.DimGray, 40, ref y);
            }

            dlg.PrintVisual(visual, $"DentalX Report — {_selectedPatient.Name}");
            txtStatus.Text = "Sent to printer. Pick \"Microsoft Print to PDF\" in the dialog to save it as a PDF file instead of printing on paper.";
        }

        private static void DrawText(DrawingContext ctx, string text, double size, FontWeight weight, Brush brush, double x, ref double y)
        {
            var ft = new FormattedText(
                text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
                size, brush, 1.0);
            ctx.DrawText(ft, new Point(x, y));
            y += ft.Height + 6;
        }
    }
}
