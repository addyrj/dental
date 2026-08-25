using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    public partial class PatientsListPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private List<PatientRow> _all = new();

        public IShellHost? Shell { get; set; }

        public PatientsListPage()
        {
            InitializeComponent();
        }

        private void PatientsListPage_Loaded(object sender, RoutedEventArgs e) => LoadPatients();

        public void LoadPatients()
        {
            _all = db.GetAllPatients().Select(p => new PatientRow
            {
                Id = p.Id,
                Name = p.Name,
                Age = p.Age,
                Gender = string.IsNullOrEmpty(p.Gender) ? "—" : p.Gender,
                Phone = p.Phone,
                Date = p.Date,
                ExamCount = db.GetExaminationCount(p.Id)
            }).ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = txtSearch.Text.Trim().ToLower();
            var filtered = string.IsNullOrEmpty(query)
                ? _all
                : _all.Where(p => p.Name.ToLower().Contains(query) || p.Phone.ToLower().Contains(query)).ToList();
            grid.ItemsSource = filtered;
            txtCount.Text = $"{filtered.Count} of {_all.Count} patients";
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void NewPatient_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewPatientWindow { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                LoadPatients();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = (grid.ItemsSource as IEnumerable<PatientRow>)?.ToList() ?? _all;
            if (rows.Count == 0)
            {
                MessageBox.Show("No patients to export.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV File|*.csv",
                FileName = $"DentalX_Patients_{System.DateTime.Now:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("ID,Name,Age,Gender,Phone,Registered,Examinations");
            foreach (var p in rows)
                sb.AppendLine($"{p.Id},{CsvField(p.Name)},{CsvField(p.Age)},{CsvField(p.Gender)},{CsvField(p.Phone)},{CsvField(p.Date)},{p.ExamCount}");

            File.WriteAllText(dlg.FileName, sb.ToString());
            MessageBox.Show($"Exported {rows.Count} patients to {Path.GetFileName(dlg.FileName)}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string CsvField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        // Imports patients from a CSV with a header row. Requires a "Name" column; recognizes
        // Age, Gender, Phone, Title, DOB, Address, Country, Notes if present (any order, case-insensitive).
        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV File|*.csv", Title = "Import Patients from CSV" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = File.ReadAllLines(dlg.FileName);
                if (lines.Length < 2)
                {
                    MessageBox.Show("The file needs a header row plus at least one patient row.");
                    return;
                }

                var headers = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLower()).ToList();
                int idxName = headers.IndexOf("name");
                if (idxName < 0)
                {
                    MessageBox.Show("The CSV must have a 'Name' column. Recognized headers: Name, Age, Gender, Phone, Title, DOB, Address, Country, Notes.");
                    return;
                }
                int idxAge = headers.IndexOf("age");
                int idxGender = headers.IndexOf("gender");
                int idxPhone = headers.IndexOf("phone");
                int idxTitle = headers.IndexOf("title");
                int idxDob = headers.IndexOf("dob");
                int idxAddress = headers.IndexOf("address");
                int idxCountry = headers.IndexOf("country");
                int idxNotes = headers.IndexOf("notes");

                int imported = 0, skipped = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var fields = ParseCsvLine(lines[i]);
                    string Get(int idx) => idx >= 0 && idx < fields.Count ? fields[idx] : "";

                    string name = Get(idxName).Trim();
                    if (string.IsNullOrEmpty(name)) { skipped++; continue; }

                    db.SavePatient(new Patient
                    {
                        Name = name,
                        Age = Get(idxAge).Trim(),
                        Gender = Get(idxGender).Trim(),
                        Phone = Get(idxPhone).Trim(),
                        Title = Get(idxTitle).Trim(),
                        DOB = Get(idxDob).Trim(),
                        Address = Get(idxAddress).Trim(),
                        Country = Get(idxCountry).Trim(),
                        Notes = Get(idxNotes).Trim()
                    });
                    imported++;
                }

                LoadPatients();
                MessageBox.Show(
                    $"Imported {imported} patient(s)." + (skipped > 0 ? $" Skipped {skipped} row(s) with no name." : ""),
                    "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}");
            }
        }

        // Minimal CSV line parser — handles quoted fields with embedded commas/quotes,
        // matching what CsvField() produces on export (so export → edit → import round-trips).
        private static List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(c);
                }
            }
            fields.Add(sb.ToString());
            return fields;
        }

        private void ViewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int id)
                Shell?.OpenPatientProfile(id);
        }

        // Simple row view-model for the grid — one query's worth of data, no live editing.
        private class PatientRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Age { get; set; } = "";
            public string Gender { get; set; } = "";
            public string Phone { get; set; } = "";
            public string Date { get; set; } = "";
            public int ExamCount { get; set; }
        }
    }
}
