using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DentalX
{
    // Shows the ONE real device (the Hamamatsu sensor, reflecting XRayViewerPage's live
    // connection state via the shell) plus honestly-labeled "not configured" placeholders
    // for the other device types the mockup shows — never a fake "Connected".
    public partial class DeviceManagementPage : UserControl
    {
        public IShellHost? Shell { get; set; }

        public DeviceManagementPage()
        {
            InitializeComponent();
            Loaded += (s, e) => BuildRows();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => BuildRows();

        private void BuildRows()
        {
            stackDevices.Children.Clear();

            bool connected = Shell?.IsSensorConnected ?? false;
            string details = Shell?.SensorDetails ?? "";

            stackDevices.Children.Add(BuildRow(
                "📡", "Hamamatsu Dental Sensor", "Intraoral CMOS X-ray sensor (USB)",
                connected,
                connected ? details : "Not connected — open Imaging to connect",
                real: true));

            stackDevices.Children.Add(BuildRow(
                "🦷", "Panoramic Unit", "Not configured in this installation",
                false, "No driver configured for this device type", real: false));

            stackDevices.Children.Add(BuildRow(
                "🌀", "CBCT Scanner", "Not configured in this installation",
                false, "No driver configured for this device type", real: false));

            stackDevices.Children.Add(BuildRow(
                "📷", "Intraoral Camera", "Not configured in this installation",
                false, "No driver configured for this device type", real: false));
        }

        private Border BuildRow(string icon, string name, string subtitle, bool connected, string statusText, bool real)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("BrBgPanel"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = (Brush)FindResource("BrBorder"),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconText = new TextBlock { Text = icon, FontSize = 26, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(iconText, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("BrText") });
            info.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = (Brush)FindResource("BrTextMuted"), Margin = new Thickness(0, 2, 0, 0) });
            info.Children.Add(new TextBlock
            {
                Text = statusText,
                FontSize = 11,
                Foreground = connected ? (Brush)FindResource("BrSuccess") : (Brush)FindResource("BrTextMuted"),
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(info, 1);

            var badge = new Border
            {
                Background = connected ? (Brush)FindResource("BrSuccess") : (Brush)FindResource("BrBgCard"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = connected ? "Connected" : (real ? "Disconnected" : "Not configured"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = connected ? Brushes.White : (Brush)FindResource("BrTextMuted")
            };
            Grid.SetColumn(badge, 2);

            grid.Children.Add(iconText);
            grid.Children.Add(info);
            grid.Children.Add(badge);
            row.Child = grid;
            return row;
        }
    }
}
