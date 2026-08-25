using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DentalX
{
    public partial class DashboardPage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();

        public IShellHost? Shell { get; set; }

        public DashboardPage()
        {
            InitializeComponent();
        }

        private void DashboardPage_Loaded(object sender, RoutedEventArgs e) => Refresh();

        private void GoToImaging_Click(object sender, RoutedEventArgs e) => Shell?.GoToImaging();

        public void Refresh()
        {
            txtWelcome.Text = $"Welcome back, {CurrentSession.DisplayName} — {DateTime.Now:dddd, dd MMMM yyyy}";
            txtWhoAmI.Text = $"{CurrentSession.DisplayName}\n{CurrentSession.User?.Role ?? "Guest"}";

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png") : Array.Empty<string>();

            txtStatToday.Text = files.Count(f => File.GetCreationTime(f).Date == DateTime.Today).ToString();

            var patients = db.GetAllPatients();
            txtStatPatients.Text = patients.Count.ToString();

            int newPatients = patients.Count(p => DateTime.TryParse(p.Date, out var d) && d >= DateTime.Now.AddDays(-7));
            txtStatNewPatients.Text = newPatients.ToString();

            // txtStatAI intentionally stays "—" — no AI model is connected, so we never show a number here.

            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            double mb = totalBytes / (1024.0 * 1024.0);
            txtStorageDetail.Text = $"{files.Length} images\n{mb:F1} MB used\nDesktop\\DentalX_Captures";

            PopulateRecent(files);
        }

        private void PopulateRecent(string[] files)
        {
            stackRecent.Children.Clear();
            var recent = files.OrderByDescending(f => File.GetCreationTime(f)).Take(6).ToList();

            txtNoRecent.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var f in recent)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var modality = name.Contains('_') ? name[..name.IndexOf('_')] : "capture";
                if (modality.Length == 0) modality = "capture";
                var label = char.ToUpper(modality[0]) + modality[1..];

                var row = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(6),
                    Background = (Brush)FindResource("BrBgCard")
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = label,
                    Foreground = (Brush)FindResource("BrAccent"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Width = 120
                });
                sp.Children.Add(new TextBlock
                {
                    Text = File.GetCreationTime(f).ToString("dd MMM yyyy HH:mm"),
                    Foreground = (Brush)FindResource("BrTextMuted"),
                    FontSize = 11
                });
                row.Child = sp;
                stackRecent.Children.Add(row);
            }
        }
    }
}
