using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DentalX
{
    public partial class SettingsPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadAll();
        }

        private void LoadAll()
        {
            txtClinicName.Text = db.GetSetting("ClinicName", "Main Clinic");
            txtPixelPitch.Text = db.GetSetting("SensorPixelPitchMicrons", "");
            LoadUsers();
        }

        private void SavePixelPitch_Click(object sender, RoutedEventArgs e)
        {
            string val = txtPixelPitch.Text.Trim();
            if (!string.IsNullOrEmpty(val) && !double.TryParse(val, out _))
            {
                MessageBox.Show("Pixel pitch must be a number (or left blank).");
                return;
            }
            db.SetSetting("SensorPixelPitchMicrons", val);
            MessageBox.Show("Calibration saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveGeneral_Click(object sender, RoutedEventArgs e)
        {
            db.SetSetting("ClinicName", txtClinicName.Text.Trim());
            MessageBox.Show("Settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            string current = pwdCurrent.Password;
            string next = pwdNew.Password;
            string confirm = pwdConfirm.Password;

            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
            {
                ShowPwdMessage("Fill in all password fields.", true);
                return;
            }
            if (next != confirm)
            {
                ShowPwdMessage("New password and confirmation don't match.", true);
                return;
            }
            if (next.Length < 4)
            {
                ShowPwdMessage("New password must be at least 4 characters.", true);
                return;
            }

            string username = CurrentSession.User?.Username ?? "";
            bool ok = db.ChangePassword(username, current, next);
            if (!ok)
            {
                ShowPwdMessage("Current password is incorrect.", true);
                return;
            }

            pwdCurrent.Password = "";
            pwdNew.Password = "";
            pwdConfirm.Password = "";
            ShowPwdMessage("Password updated.", false);
        }

        private void ShowPwdMessage(string msg, bool isError)
        {
            txtPwdMessage.Text = msg;
            txtPwdMessage.Foreground = isError ? (Brush)FindResource("BrDanger") : (Brush)FindResource("BrSuccess");
            txtPwdMessage.Visibility = Visibility.Visible;
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewUserWindow { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                LoadUsers();
        }

        private void LoadUsers()
        {
            stackUsers.Children.Clear();
            var users = db.GetAllUsers();
            if (users.Count == 0)
            {
                stackUsers.Children.Add(new TextBlock { Text = "No users.", FontSize = 12, Foreground = (Brush)FindResource("BrTextMuted") });
                return;
            }

            foreach (var u in users)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = string.IsNullOrEmpty(u.FullName) ? $"@{u.Username}" : $"{u.FullName}  (@{u.Username})",
                    FontSize = 12,
                    Foreground = (Brush)FindResource("BrText"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var role = new Border
                {
                    Background = (Brush)FindResource("BrBgCard"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                role.Child = new TextBlock { Text = u.Role, FontSize = 10, Foreground = (Brush)FindResource("BrAccent") };

                Grid.SetColumn(name, 0);
                Grid.SetColumn(role, 1);
                row.Children.Add(name);
                row.Children.Add(role);
                stackUsers.Children.Add(row);
            }
        }
    }
}
