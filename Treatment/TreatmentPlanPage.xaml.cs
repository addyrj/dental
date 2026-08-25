using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    // Master worklist of every treatment item across every patient. Per-patient add/edit
    // happens on the Patient Profile page — this is the overview/triage view.
    public partial class TreatmentPlanPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public IShellHost? Shell { get; set; }

        public TreatmentPlanPage()
        {
            InitializeComponent();
        }

        private void TreatmentPlanPage_Loaded(object sender, RoutedEventArgs e)
        {
            var items = db.GetAllTreatmentItems();
            grid.ItemsSource = items;
            emptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int planned = items.Count(i => i.Status == "Planned");
            int completed = items.Count(i => i.Status == "Completed");
            txtSubtitle.Text = $"{items.Count} items total · {planned} planned · {completed} completed";
        }

        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int patientId)
                Shell?.OpenPatientProfile(patientId);
        }
    }
}
