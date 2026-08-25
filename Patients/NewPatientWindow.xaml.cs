using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    // Modal "add patient" dialog. Caller checks ShowDialog() == true and reads NewPatientId.
    public partial class NewPatientWindow : Window
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public int NewPatientId { get; private set; } = -1;

        public NewPatientWindow()
        {
            InitializeComponent();
            txtFirstName.Focus();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string first = txtFirstName.Text.Trim();
            string last = txtLastName.Text.Trim();

            if (string.IsNullOrEmpty(first))
            {
                ShowError("First name is required.");
                return;
            }

            string name = string.IsNullOrEmpty(last) ? first : $"{first} {last}";
            string gender = (cmbGender.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (gender == "Not specified") gender = "";
            string title = (cmbTitle.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (title == "—") title = "";

            var patient = new Patient
            {
                Name = name,
                Age = txtAge.Text.Trim(),
                Phone = txtPhone.Text.Trim(),
                Notes = txtNotes.Text.Trim(),
                Gender = gender,
                Title = title,
                DOB = txtDob.Text.Trim(),
                Address = txtAddress.Text.Trim(),
                Country = txtCountry.Text.Trim(),
                ImagePath = ""
            };

            NewPatientId = db.SavePatient(patient);
            DialogResult = true;
            Close();
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }
    }
}
