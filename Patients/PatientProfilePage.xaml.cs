using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class PatientProfilePage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private readonly int _patientId;

        public IShellHost? Shell { get; set; }

        public PatientProfilePage(int patientId)
        {
            InitializeComponent();
            _patientId = patientId;
            Load();
        }

        private void Load()
        {
            var p = db.GetPatientById(_patientId);
            if (p == null)
            {
                txtHeaderName.Text = "Patient not found";
                txtHeaderSub.Text = "";
                return;
            }

            txtAvatar.Text = p.Name.Length > 0 ? p.Name[0].ToString().ToUpper() : "?";
            txtHeaderName.Text = string.IsNullOrEmpty(p.Title) ? p.Name : $"{p.Title} {p.Name}";
            txtHeaderSub.Text = $"Patient ID: P-{p.Id:D5}  •  Registered {p.Date}";

            if (!string.IsNullOrEmpty(p.PhotoPath) && File.Exists(p.PhotoPath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p.PhotoPath);
                bmp.EndInit();
                imgPhoto.Source = bmp;
                imgPhoto.Visibility = Visibility.Visible;
                txtAvatar.Visibility = Visibility.Collapsed;
            }
            else
            {
                imgPhoto.Visibility = Visibility.Collapsed;
                txtAvatar.Visibility = Visibility.Visible;
            }

            txtTitle.Text = p.Title;
            txtName.Text = p.Name;
            txtAge.Text = p.Age;
            txtDob.Text = p.DOB;
            txtGender.Text = p.Gender;
            txtPhone.Text = p.Phone;
            txtAddress.Text = p.Address;
            txtCountry.Text = p.Country;
            txtNotes.Text = p.Notes;

            var exams = db.GetExaminationsForPatient(_patientId);
            txtStatExams.Text = exams.Count.ToString();
            int visitCount = db.GetAppointmentsForPatient(_patientId).Count(a => a.Status == "Completed");
            txtStatImages.Text = visitCount.ToString();

            stackExams.Children.Clear();
            txtNoExams.Visibility = exams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var ex in exams)
            {
                var row = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("BrBgCard")
                };
                var stack = new StackPanel();

                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var modalityText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(ex.Modality) ? "Exam" : ex.Modality,
                    Foreground = (Brush)FindResource("BrAccent"), FontSize = 12, FontWeight = FontWeights.SemiBold
                };
                var dateText = new TextBlock { Text = ex.Date, Foreground = (Brush)FindResource("BrTextMuted"), FontSize = 10 };
                Grid.SetColumn(modalityText, 0);
                Grid.SetColumn(dateText, 1);
                header.Children.Add(modalityText);
                header.Children.Add(dateText);
                stack.Children.Add(header);

                // Tooth linking — assign this image to an FDI tooth number (feature: image-to-tooth linking).
                var toothRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                toothRow.Children.Add(new TextBlock
                {
                    Text = "Tooth #:", FontSize = 10, Foreground = (Brush)FindResource("BrTextMuted"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0)
                });
                var toothBox = new TextBox
                {
                    Text = ex.ToothNumber > 0 ? ex.ToothNumber.ToString() : "",
                    Width = 40, Height = 22, FontSize = 10, Tag = ex.Id,
                    Background = (Brush)FindResource("BrBgDark"), Foreground = (Brush)FindResource("BrText"),
                    BorderBrush = (Brush)FindResource("BrBorder"), BorderThickness = new Thickness(1)
                };
                int examId = ex.Id;
                toothBox.LostFocus += (s, e) =>
                {
                    int tooth = int.TryParse(toothBox.Text.Trim(), out int t) ? t : 0;
                    db.SetExaminationTooth(examId, tooth);
                    toothChart.LoadForPatient(_patientId);
                };
                toothRow.Children.Add(toothBox);
                stack.Children.Add(toothRow);

                // Per-image comment/notes — auto-saves on losing focus.
                var notesBox = new TextBox
                {
                    Text = ex.Notes, FontSize = 10, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                    Height = 36, Margin = new Thickness(0, 4, 0, 0),
                    Background = (Brush)FindResource("BrBgDark"), Foreground = (Brush)FindResource("BrTextSec"),
                    BorderBrush = (Brush)FindResource("BrBorder"), BorderThickness = new Thickness(1),
                    ToolTip = "Comment on this image"
                };
                notesBox.LostFocus += (s, e) => db.UpdateExaminationNotes(examId, notesBox.Text);
                stack.Children.Add(notesBox);

                // Saved on-image measurements (Distance / Tooth Length / Angle) from Imaging.
                var measurements = db.GetMeasurementsForImage(ex.ImagePath);
                if (measurements.Count > 0)
                {
                    var measPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                    measPanel.Children.Add(new TextBlock
                    {
                        Text = "Measurements:", FontSize = 9, FontWeight = FontWeights.SemiBold,
                        Foreground = (Brush)FindResource("BrTextMuted")
                    });
                    foreach (var m in measurements)
                    {
                        measPanel.Children.Add(new TextBlock
                        {
                            Text = $"• {m.ValueText}", FontSize = 10, Foreground = (Brush)FindResource("BrAccent"),
                            Margin = new Thickness(4, 1, 0, 0), TextWrapping = TextWrapping.Wrap
                        });
                    }
                    stack.Children.Add(measPanel);
                }

                row.Child = stack;
                stackExams.Children.Add(row);
            }

            toothChart.LoadForPatient(_patientId);
            LoadTreatments();
        }

        private void Photo_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp", Title = "Choose Patient Photo" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures", "patient_photos");
                Directory.CreateDirectory(dir);
                string ext = Path.GetExtension(dlg.FileName);
                string destPath = Path.Combine(dir, $"patient_{_patientId}{ext}");
                File.Copy(dlg.FileName, destPath, true);

                var patient = db.GetPatientById(_patientId);
                if (patient != null)
                {
                    patient.PhotoPath = destPath;
                    db.UpdatePatient(patient);
                }
                Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not set photo: {ex.Message}");
            }
        }

        private void LoadTreatments()
        {
            var items = db.GetTreatmentItemsForPatient(_patientId);
            txtStatTreatments.Text = items.Count.ToString();

            stackTreatments.Children.Clear();
            txtNoTreatments.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var item in items)
            {
                var statusColor = item.Status switch
                {
                    "Completed" => (Brush)FindResource("BrSuccess"),
                    "Recommended" => (Brush)FindResource("BrWarning"),
                    _ => (Brush)FindResource("BrInfo")
                };
                var priorityColor = item.Priority switch
                {
                    "High" => (Brush)FindResource("BrDanger"),
                    "Low" => (Brush)FindResource("BrTextMuted"),
                    _ => (Brush)FindResource("BrWarning")
                };

                var row = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("BrBgCard")
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var toothText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(item.Tooth) ? "—" : item.Tooth,
                    Foreground = (Brush)FindResource("BrTextMuted"),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(toothText, 0);

                var desc = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                desc.Children.Add(new TextBlock { Text = item.Treatment, Foreground = (Brush)FindResource("BrText"), FontSize = 12 });
                desc.Children.Add(new TextBlock { Text = $"Priority: {item.Priority}", Foreground = priorityColor, FontSize = 10 });
                Grid.SetColumn(desc, 1);

                var statusPill = new Border
                {
                    Background = statusColor,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                statusPill.Child = new TextBlock { Text = item.Status, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
                Grid.SetColumn(statusPill, 2);

                var btnComplete = new Button
                {
                    Content = "✓", ToolTip = "Mark completed", Width = 26, Height = 26, Margin = new Thickness(0, 0, 4, 0),
                    Background = (Brush)FindResource("BrBgHover"), Foreground = (Brush)FindResource("BrSuccess"),
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Tag = item.Id,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnComplete.Click += (s, e) => { db.UpdateTreatmentStatus(item.Id, "Completed"); LoadTreatments(); };
                Grid.SetColumn(btnComplete, 3);

                var btnDelete = new Button
                {
                    Content = "✕", ToolTip = "Delete", Width = 26, Height = 26,
                    Background = (Brush)FindResource("BrBgHover"), Foreground = (Brush)FindResource("BrDanger"),
                    BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Tag = item.Id,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnDelete.Click += (s, e) => { db.DeleteTreatmentItem(item.Id); LoadTreatments(); };
                Grid.SetColumn(btnDelete, 4);

                grid.Children.Add(toothText);
                grid.Children.Add(desc);
                grid.Children.Add(statusPill);
                grid.Children.Add(btnComplete);
                grid.Children.Add(btnDelete);
                row.Child = grid;
                stackTreatments.Children.Add(row);
            }
        }

        private void AddTreatment_Click(object sender, RoutedEventArgs e)
        {
            string treatment = txtNewTreatment.Text.Trim();
            if (string.IsNullOrEmpty(treatment))
            {
                MessageBox.Show("Enter a treatment description first.");
                return;
            }

            string priority = (cmbNewPriority.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";
            string status = (cmbNewStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Planned";

            db.AddTreatmentItem(_patientId, txtNewTooth.Text.Trim(), treatment, priority, status, null);

            txtNewTooth.Text = "";
            txtNewTreatment.Text = "";
            LoadTreatments();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Name cannot be empty.");
                return;
            }

            var patient = new Patient
            {
                Id = _patientId,
                Name = txtName.Text.Trim(),
                Age = txtAge.Text.Trim(),
                Gender = txtGender.Text.Trim(),
                Phone = txtPhone.Text.Trim(),
                Notes = txtNotes.Text.Trim(),
                Title = txtTitle.Text.Trim(),
                DOB = txtDob.Text.Trim(),
                Address = txtAddress.Text.Trim(),
                Country = txtCountry.Text.Trim()
            };
            db.UpdatePatient(patient);
            Load();
            MessageBox.Show("Patient details updated.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Delete patient '{txtHeaderName.Text}' and all their examination/tooth-chart records?\n\nThis cannot be undone.",
                "Delete Patient", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            db.DeletePatient(_patientId);
            Shell?.OpenPatientsList();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            Shell?.OpenPatientsList();
        }
    }
}
