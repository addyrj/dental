using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DentalX
{
    // Interactive 32-tooth FDI chart. Self-contained: loads/saves per-patient tooth
    // conditions directly against the DB, so any page can just drop it in and call
    // LoadForPatient(id). Left-click cycles condition; right-click shows images linked
    // to that tooth (assigned from the Examination History list on Patient Profile).
    public partial class ToothChartControl : UserControl
    {
        private static readonly int[] UpperTeeth = { 18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28 };
        private static readonly int[] LowerTeeth = { 48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38 };
        private static readonly string[] ConditionCycle =
            { "Healthy", "Caries", "Restoration", "RootCanal", "Missing", "Extraction", "Implant" };

        private readonly DatabaseHelper db = new DatabaseHelper();
        private int _patientId;
        private Dictionary<int, ToothCondition> _conditions = new();
        private Dictionary<int, List<Examination>> _examsByTooth = new();

        public ToothChartControl()
        {
            InitializeComponent();
            BuildLegend();
        }

        public void LoadForPatient(int patientId)
        {
            _patientId = patientId;
            _conditions = db.GetToothConditions(patientId).ToDictionary(t => t.ToothNumber);
            _examsByTooth = db.GetExaminationsForPatient(patientId)
                .Where(e => e.ToothNumber > 0)
                .GroupBy(e => e.ToothNumber)
                .ToDictionary(g => g.Key, g => g.ToList());
            BuildRow(upperRow, UpperTeeth);
            BuildRow(lowerRow, LowerTeeth);
        }

        private void BuildRow(StackPanel row, int[] teeth)
        {
            row.Children.Clear();
            foreach (var t in teeth)
                row.Children.Add(BuildTooth(t));
        }

        // Anatomical-ish tooth silhouette: rounded-top "crown" + tapered "root" triangle,
        // stacked vertically. Not real dental art, but reads as a tooth rather than a box.
        private Grid BuildTooth(int number)
        {
            string condition = _conditions.TryGetValue(number, out var tc) ? tc.Condition : "Healthy";
            var color = ColorForCondition(condition);
            bool isMissing = condition == "Missing";
            int imageCount = _examsByTooth.TryGetValue(number, out var linked) ? linked.Count : 0;

            string tooltip = $"Tooth {number} — {condition} (click to change)";
            if (imageCount > 0) tooltip += $"\n{imageCount} linked image(s) — right-click to view";

            var container = new Grid
            {
                Width = 40,
                Height = 58,
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = number,
                Opacity = isMissing ? 0.4 : 1.0,
                ToolTip = tooltip
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var crown = new Border
            {
                Width = 34,
                Height = 30,
                CornerRadius = new CornerRadius(14, 14, 5, 5),
                Background = new SolidColorBrush(Color.FromArgb(condition == "Healthy" ? (byte)0x00 : (byte)0x3A, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(condition == "Healthy" ? 1.5 : 2.5),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            crown.Child = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            stack.Children.Add(crown);

            var root = new Polygon
            {
                Points = new PointCollection(new[] { new Point(17, 0), new Point(9, 16), new Point(25, 16) }),
                Fill = new SolidColorBrush(color),
                Opacity = 0.55,
                Width = 34,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            };
            stack.Children.Add(root);

            container.Children.Add(stack);

            if (imageCount > 0)
            {
                var badge = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = (Brush)FindResource("BrAccent"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                };
                badge.Child = new TextBlock
                {
                    Text = imageCount.ToString(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                container.Children.Add(badge);
            }

            container.MouseLeftButtonDown += Tooth_Click;
            container.MouseRightButtonDown += Tooth_RightClick;
            return container;
        }

        private void Tooth_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid g || g.Tag is not int number) return;

            string current = _conditions.TryGetValue(number, out var tc) ? tc.Condition : "Healthy";
            int idx = Array.IndexOf(ConditionCycle, current);
            string next = ConditionCycle[(idx + 1) % ConditionCycle.Length];

            db.SetToothCondition(_patientId, number, next, null);
            _conditions[number] = new ToothCondition { PatientId = _patientId, ToothNumber = number, Condition = next };

            // Rebuild both rows so the change (and its color) is reflected immediately.
            BuildRow(upperRow, UpperTeeth);
            BuildRow(lowerRow, LowerTeeth);
        }

        // Image-to-tooth linking: shows which captures are assigned to this tooth
        // (assigned via the "Tooth #" field on each Examination History entry).
        private void Tooth_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Grid g || g.Tag is not int number) return;
            e.Handled = true;

            if (!_examsByTooth.TryGetValue(number, out var exams) || exams.Count == 0)
            {
                MessageBox.Show(
                    $"No images linked to tooth {number} yet.\n\nAssign one from the \"Tooth #\" field on an entry in Examination History, above.",
                    $"Tooth {number}", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lines = exams.Select(ex =>
                $"• {ex.Modality} — {ex.Date}" + (string.IsNullOrEmpty(ex.Notes) ? "" : $"\n   \"{ex.Notes}\""));
            MessageBox.Show(
                $"Tooth {number} — {exams.Count} linked image(s):\n\n{string.Join("\n", lines)}",
                $"Tooth {number} — Linked Images", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BuildLegend()
        {
            legendPanel.Children.Clear();
            foreach (var cond in ConditionCycle)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 4) };
                item.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(ColorForCondition(cond)),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                item.Children.Add(new TextBlock
                {
                    Text = cond,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("BrTextSec"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                legendPanel.Children.Add(item);
            }
        }

        private static Color ColorForCondition(string condition) => condition switch
        {
            "Caries" => Color.FromRgb(0xEF, 0x44, 0x44),
            "Restoration" => Color.FromRgb(0x3B, 0x82, 0xF6),
            "RootCanal" => Color.FromRgb(0xA8, 0x55, 0xF7),
            "Missing" => Color.FromRgb(0x55, 0x62, 0x75),
            "Extraction" => Color.FromRgb(0xF9, 0x73, 0x16),
            "Implant" => Color.FromRgb(0x14, 0xB8, 0xA6),
            _ => Color.FromRgb(0x2F, 0xD9, 0xC4) // Healthy
        };
    }
}
