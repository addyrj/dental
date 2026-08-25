using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DentalX
{
    // A Ctrl+P style "jump to patient" popup, usable from any page without leaving it.
    // Closes itself on Escape, on selection, or when it loses focus.
    public partial class QuickPatientSearchWindow : Window
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private List<Patient> _all = new();

        public IShellHost? Shell { get; set; }

        public QuickPatientSearchWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => { txtSearch.Focus(); LoadPatients(); };
        }

        private void LoadPatients()
        {
            _all = db.GetAllPatients();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = txtSearch.Text.Trim().ToLower();
            var filtered = string.IsNullOrEmpty(q)
                ? _all
                : _all.Where(p => p.Name.ToLower().Contains(q) || p.Phone.ToLower().Contains(q)).ToList();

            lstResults.ItemsSource = filtered.Take(30).Select(p => new ResultItem
            {
                Id = p.Id,
                Display = $"{p.Name}   ·   P-{p.Id:D5}   ·   {p.Phone}"
            }).ToList();
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Down && lstResults.Items.Count > 0)
            {
                lstResults.SelectedIndex = 0;
                lstResults.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && lstResults.Items.Count > 0)
            {
                OpenSelected(0);
            }
        }

        private void lstResults_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) OpenSelected(lstResults.SelectedIndex);
        }

        private void lstResults_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected(lstResults.SelectedIndex);

        private void OpenSelected(int index)
        {
            if (index < 0 || lstResults.Items[index] is not ResultItem item) return;
            Shell?.OpenPatientProfile(item.Id);
            Close();
        }

        private void Window_Deactivated(object sender, System.EventArgs e) => Close();

        private class ResultItem
        {
            public int Id { get; set; }
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }
    }
}
