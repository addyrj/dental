using System.Windows;
using System.Windows.Input;

namespace DentalX
{
    // Small reusable "type some text" modal — used for annotation labels etc.
    public partial class TextInputDialog : Window
    {
        public string ResultText { get; private set; } = "";

        public TextInputDialog(string title, string prompt)
        {
            InitializeComponent();
            Title = title;
            txtPrompt.Text = prompt;
            Loaded += (s, e) => txtInput.Focus();
        }

        private void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Ok_Click(sender, new RoutedEventArgs());
            else if (e.Key == Key.Escape) Cancel_Click(sender, new RoutedEventArgs());
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResultText = txtInput.Text;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
