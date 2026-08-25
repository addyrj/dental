using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    public partial class NewAppointmentWindow : Window
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private List<Patient> _all = new();
        private int? _selectedPatientId = null;

        public NewAppointmentWindow()
        {
            InitializeComponent();
            dpDate.SelectedDate = DateTime.Today;
            Loaded += (s, e) => LoadPatients();
        }

        private void LoadPatients()
        {
            _all = db.GetAllPatients();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = txtPatientSearch.Text.Trim().ToLower();
            var filtered = string.IsNullOrEmpty(q) ? _all : _all.Where(p => p.Name.ToLower().Contains(q)).ToList();
            lstPatients.ItemsSource = filtered.Select(p => new PatientItem { Id = p.Id, Display = $"{p.Name}  ·  P-{p.Id:D5}" }).ToList();
        }

        private void txtPatientSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void lstPatients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPatients.SelectedItem is not PatientItem item) return;
            _selectedPatientId = item.Id;
            txtSelectedPatient.Text = $"Selected: {item.Display}";
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientId == null)
            {
                ShowError("Select a patient from the list first.");
                return;
            }
            if (dpDate.SelectedDate == null)
            {
                ShowError("Pick a date.");
                return;
            }
            string time = txtTime.Text.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(time, @"^([01]?\d|2[0-3]):[0-5]\d$"))
            {
                ShowError("Time must be in HH:mm format (e.g. 09:30).");
                return;
            }

            string date = dpDate.SelectedDate.Value.ToString("yyyy-MM-dd");
            db.AddAppointment(_selectedPatientId.Value, date, time, txtReason.Text.Trim());
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }

        private class PatientItem
        {
            public int Id { get; set; }
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }
    }
}
