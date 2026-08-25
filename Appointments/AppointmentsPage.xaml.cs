using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    public partial class AppointmentsPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public IShellHost? Shell { get; set; }

        public AppointmentsPage()
        {
            InitializeComponent();
        }

        private void AppointmentsPage_Loaded(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            var items = db.GetAllAppointments();
            grid.ItemsSource = items;
            emptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int scheduled = items.Count(a => a.Status == "Scheduled");
            int today = items.Count(a => a.Status == "Scheduled" && a.AppointmentDate == System.DateTime.Today.ToString("yyyy-MM-dd"));
            txtSubtitle.Text = $"{items.Count} total · {scheduled} scheduled · {today} today";
        }

        private void NewAppointment_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewAppointmentWindow { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                Refresh();
        }

        private void Complete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int id)
            {
                db.UpdateAppointmentStatus(id, "Completed");
                Refresh();
            }
        }

        private void CancelAppt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int id)
            {
                db.UpdateAppointmentStatus(id, "Cancelled");
                Refresh();
            }
        }

        private void OpenPatient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is int patientId)
                Shell?.OpenPatientProfile(patientId);
        }
    }
}
