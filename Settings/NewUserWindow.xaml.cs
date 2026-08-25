using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;

namespace DentalX
{
    // Admin-only modal for creating a new staff account (Assistant/Dentist/Admin).
    public partial class NewUserWindow : Window
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public NewUserWindow()
        {
            InitializeComponent();
            txtFullName.Focus();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            string fullName = txtFullName.Text.Trim();
            string username = txtUsername.Text.Trim();
            string role = (cmbRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Assistant";
            string password = pwdPassword.Password;
            string confirm = pwdConfirm.Password;

            if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(username))
            {
                ShowError("Full name and username are required.");
                return;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                ShowError("Password must be at least 4 characters.");
                return;
            }
            if (password != confirm)
            {
                ShowError("Password and confirmation don't match.");
                return;
            }

            try
            {
                db.CreateUser(username, password, fullName, role);
                DialogResult = true;
                Close();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (UNIQUE)
            {
                ShowError($"Username '{username}' is already taken — choose another.");
            }
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }
    }
}
