using System.Windows;
using System.Windows.Input;

namespace DentalX
{
    public partial class LoginWindow : Window
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private bool isPasswordVisible = false;

        public LoginWindow()
        {
            InitializeComponent();
            txtUsername.Focus();

            // First-run: a default admin account was just created. Tell the user plainly —
            // this is a real credential gating the real app, not something to bury.
            if (db.DidSeedDefaultAdmin)
            {
                MessageBox.Show(
                    "First run detected — a default admin account was created:\n\n" +
                    "Username: admin\nPassword: admin123\n\n" +
                    "Please log in and change this password once the user-management screen is available.",
                    "Default Admin Account Created",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                txtUsername.Text = "admin";
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) btnLogin_Click(sender, e);
        }

        // Eye toggle: PasswordBox and TextBox sit on top of each other; only one is visible
        // at a time, and their content is synced across the swap.
        private void btnTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;

            if (isPasswordVisible)
            {
                txtPasswordVisible.Text = txtPassword.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                txtPassword.Visibility = Visibility.Collapsed;
                txtPasswordVisible.Focus();
                txtPasswordVisible.CaretIndex = txtPasswordVisible.Text.Length;
                btnTogglePassword.Content = "🙈";
                btnTogglePassword.ToolTip = "Hide password";
            }
            else
            {
                txtPassword.Password = txtPasswordVisible.Text;
                txtPassword.Visibility = Visibility.Visible;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                txtPassword.Focus();
                btnTogglePassword.Content = "👁";
                btnTogglePassword.ToolTip = "Show password";
            }
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = isPasswordVisible ? txtPasswordVisible.Text : txtPassword.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Enter username and password.");
                return;
            }

            var user = db.ValidateLogin(username, password);
            if (user == null)
            {
                ShowError("Invalid username or password.");
                txtPassword.Password = "";
                txtPasswordVisible.Text = "";
                return;
            }

            CurrentSession.User = user;

            var shell = new MainWindow();
            Application.Current.MainWindow = shell;
            shell.Show();
            Close();
        }

        private void ShowError(string msg)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
        }
    }
}
