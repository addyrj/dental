using System.Windows.Controls;

namespace DentalX
{
    // Reusable placeholder for nav destinations that aren't built yet. Keeps the shell's
    // navigation fully wired (every click goes somewhere real) without faking content.
    public partial class ComingSoonPage : UserControl
    {
        public ComingSoonPage()
        {
            InitializeComponent();
        }

        public ComingSoonPage(string title, string message, string icon = "🚧") : this()
        {
            txtTitle.Text = title;
            txtMessage.Text = message;
            txtIcon.Text = icon;
        }
    }
}
