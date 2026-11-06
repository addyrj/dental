using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DentalX
{
    public partial class XRayViewerPage : UserControl
    {
        private string currentImagePath = "";
        private string currentModality = "All"; // active gallery filter: All / Intraoral / Panoramic / Cephalometric / CBCT
        private System.Windows.Threading.DispatcherTimer? _sensorTimer;
        private DatabaseHelper db = new DatabaseHelper();
        private SensorManager sensor = new SensorManager();
        private BitmapSource? originalImage = null;
        private BitmapSource? enhancedImage = null;
        private BitmapSource? _lastAttachedEnhanced = null; // so re-clicking Attach doesn't save the same enhanced copy twice
        private double currentZoom = 100;
        private double currentRotation = 0;
        private bool isProcessing = false;
        private bool isPanning = false;
        private System.Windows.Point panStart;
        private double panStartX, panStartY;
        private bool isWindowLevel = false;
        private System.Windows.Point wlStart;
        private int? _selectedPatientId = null; // picked from the attach-list, not free-form entered

        // ─── Measurement tool state ───
        private enum MeasureMode { Off, Distance, ToothLength, Angle, Calibrate }
        private MeasureMode _measureSubMode = MeasureMode.Off;
        private readonly List<System.Windows.Point> _pendingMeasurePoints = new(); // pixel-space clicks collected so far
        private readonly List<MeasureVisual> _measureVisuals = new();
        private MeasureVisual? _draggingVisual;
        private int _draggingHandleIndex;
        private bool _galleryGridMode = false;

        // ─── Tooth Length tool (crown tip + apex clicks → length + automatic crown breadth) ───
        private bool _toothMode = false;
        private readonly List<System.Windows.Point> _pendingToothPoints = new();
        private System.Windows.Shapes.Ellipse? _pendingToothMarker;
        private static readonly Brush ToothWidthBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0xC8)); // pink

        // One placed measurement overlay (Distance / Tooth Length / Angle). Coordinates are
        // stored in image-pixel space so they stay correct across zoom/pan/rotate and round-trip
        // cleanly to/from the database regardless of the viewer's current transform.
        private class MeasureVisual
        {
            public int DbId = -1;
            public string Kind = "Distance"; // Distance | ToothLength | Angle | Calibrate | AutoLength | AutoWidth
            public int ToothNumber = 0;
            public bool Estimated;           // auto-computed and not yet adjusted by hand → label shows "~"
            public System.Windows.Point P1, P2, P3;
            public bool HasP3 = false;
            public System.Windows.Shapes.Line Line1 = null!;
            public System.Windows.Shapes.Line? Line2;
            public TextBlock Label = null!;
            public System.Windows.Shapes.Ellipse Handle1 = null!;
            public System.Windows.Shapes.Ellipse Handle2 = null!;
            public System.Windows.Shapes.Ellipse? Handle3;
        }

        // ─── Crop tool state ───
        private bool _cropMode = false;
        private bool _isCropping = false;
        private System.Windows.Point _cropStartCanvas;
        private System.Windows.Point _cropStartImage;
        private System.Windows.Point _cropEndImage;
        private System.Windows.Shapes.Rectangle? _cropRectVisual;
        private StackPanel? _cropButtonsPanel;

        // ─── Annotate tool state ───
        private bool _annotateMode = false;
        private readonly List<UIElement> _annotationVisuals = new();

        // ─── Magnifier tool state ───
        private bool _magnifierMode = false;
        private double _magnifierZoom = 2.0; // 1x/2x/4x — image pixels shown per screen pixel
        private const double MagnifierRadius = 100;
        private const double AnchorRadius = 10;

        // Set by the Shell right after this page is created, so the page can update the
        // header sensor pill without owning the shell chrome itself.
        public IShellHost? Shell { get; set; }

        public XRayViewerPage()
        {
            InitializeComponent();
            LoadPatientList();
            LoadGalleryThumbnails();
            UpdateStorageInfo();
            // Auto-detect sensor every 3 seconds
            _sensorTimer = new System.Windows.Threading.DispatcherTimer();
            _sensorTimer.Interval = TimeSpan.FromSeconds(3);
            _sensorTimer.Tick += SensorTimer_Tick;
            _sensorTimer.Start();

            // Makes sure the sensor's USB driver is installed on THIS PC — a fresh install only
            // needs this once (it triggers a single Windows permission prompt); every launch after
            // that, the check is instant and silent. Runs off the UI thread since it may briefly
            // block on that prompt.
            Task.Run(() => DriverInstaller.EnsureInstalled());
        }

        // Hook keyboard shortcuts onto the host Window (a UserControl doesn't get top-level
        // key events on its own). Guarded so shortcuts only fire while this page is showing.
        private void XRayViewerPage_Loaded(object sender, RoutedEventArgs e)
        {
            var win = System.Windows.Window.GetWindow(this);
            if (win != null)
            {
                win.PreviewKeyDown -= Window_KeyDown;
                win.PreviewKeyDown += Window_KeyDown;
            }
        }

        // Called by the Shell when the app is really closing or the user logs out.
        public void Cleanup()
        {
            _sensorTimer?.Stop();
            if (sensor.IsConnected) sensor.Disconnect();
        }

        // Wrappers so the Shell's global "Quick Actions" can drive this page after navigating to it.
        public void TriggerExport() => btnSaveEnhanced_Click(this, new RoutedEventArgs());
        public void TriggerPrint() => btnPrint_Click(this, new RoutedEventArgs());
        public void RefreshPatients() => LoadPatientList();

        // =================================================================
        //  KEYBOARD SHORTCUTS
        //  Space=toggle  I=invert  R=rotate  +/-=zoom  0=fit
        //  Ctrl+S=save  Ctrl+Z=reset  T=test capture
        // =================================================================
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (!IsVisible) return; // only handle shortcuts while this page is the active one
            if (e.Key == Key.Escape && fullZoomOverlay.Visibility == Visibility.Visible)
            {
                CloseFullZoom();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && _toothMode)
            {
                ExitToothMode();
                txtStatus.Text = "Tooth Length off";
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Space) { ToggleOriginalEnhanced(); e.Handled = true; }
            else if (e.Key == Key.I && !IsTextBoxFocused()) { btnInvert_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.R && !IsTextBoxFocused()) { btnRotate_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add) { btnZoomIn_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { btnZoomOut_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { btnFit_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { AttachImage_Click(sender, e); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { btnResetSliders_Click(sender, e); e.Handled = true; }
        }

        private bool IsTextBoxFocused() => Keyboard.FocusedElement is TextBox;

        private void ToggleOriginalEnhanced()
        {
            if (originalImage == null) return;
            if (enhancedImage != null && imgDisplay.Source == enhancedImage)
            { imgDisplay.Source = originalImage; txtStatus.Text = "Original (Space to toggle)"; }
            else if (enhancedImage != null)
            { imgDisplay.Source = enhancedImage; txtStatus.Text = "Enhanced (Space to toggle)"; }
        }

        // =================================================================
        //  MOUSE — Zoom / Pan / Window-Level
        // =================================================================
        private void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
        { SetZoom(currentZoom + (e.Delta > 0 ? 15 : -15)); e.Handled = true; }

        private void Viewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_toothMode) // before the double-click check: two quick clicks are crown + apex here
            {
                HandleToothClick(e);
                return;
            }
            if (e.ClickCount == 2)
            {
                OpenFullZoom();
                return;
            }
            if (_measureSubMode != MeasureMode.Off)
            {
                HandleMeasureClick(e);
                return;
            }
            if (_cropMode)
            {
                StartCrop(e);
                return;
            }
            if (_annotateMode)
            {
                HandleAnnotateClick(e);
                return;
            }

            isPanning = true;
            panStart = e.GetPosition(viewerBorder);
            panStartX = imgTranslate.X;
            panStartY = imgTranslate.Y;
            viewerBorder.CaptureMouse();
            viewerBorder.Cursor = Cursors.Hand;
        }

        // ═══ MEASUREMENT TOOL — Distance / Tooth Length / Angle / Calibration ═══
        // Every point is stored/compared in image-pixel space (same "local / ActualWidth *
        // PixelWidth" technique the Crop tool already uses), so measurements stay correct and
        // redraw accurately no matter the viewer's current zoom/pan/rotation, and round-trip
        // cleanly to the database.
        private void btnMeasure_Click(object sender, RoutedEventArgs e)
        {
            ExitToothMode(); // one click-tool at a time
            bool turningOn = measureControls.Visibility != Visibility.Visible;
            measureControls.Visibility = turningOn ? Visibility.Visible : Visibility.Collapsed;
            btnMeasure.Background = turningOn ? (Brush)FindResource("BrAccentDim") : Brushes.Transparent;
            if (!turningOn)
            {
                _measureSubMode = MeasureMode.Off;
                _pendingMeasurePoints.Clear();
                UpdateMeasureModeButtons();
                txtStatus.Text = "Measure off";
            }
            else
            {
                UpdateCalibrationStatus();
            }
        }

        private void MeasureModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            _measureSubMode = tag switch
            {
                "Distance" => MeasureMode.Distance,
                "ToothLength" => MeasureMode.ToothLength,
                "Angle" => MeasureMode.Angle,
                "Calibrate" => MeasureMode.Calibrate,
                _ => MeasureMode.Off
            };
            _pendingMeasurePoints.Clear();
            UpdateMeasureModeButtons();
            txtStatus.Text = _measureSubMode switch
            {
                MeasureMode.Distance => "Distance — click two points on the image",
                MeasureMode.ToothLength => "Tooth Length — click the crown reference point, then the root apex",
                MeasureMode.Angle => "Angle — click the first point, then the vertex, then the second point",
                MeasureMode.Calibrate => "Calibration — click two points spanning a KNOWN real-world distance",
                _ => "Measure off"
            };
        }

        private void UpdateMeasureModeButtons()
        {
            Brush on = (Brush)FindResource("BrAccentDim");
            Brush off = (Brush)FindResource("BrBgCard");
            measureBtnDistance.Background = _measureSubMode == MeasureMode.Distance ? on : off;
            measureBtnTooth.Background = _measureSubMode == MeasureMode.ToothLength ? on : off;
            measureBtnAngle.Background = _measureSubMode == MeasureMode.Angle ? on : off;
            measureBtnCalibrate.Background = _measureSubMode == MeasureMode.Calibrate ? on : off;
        }

        private void UpdateCalibrationStatus()
        {
            string pitchStr = db.GetSetting("SensorPixelPitchMicrons", "");
            txtCalibrationStatus.Text = double.TryParse(pitchStr, out double pitchMicrons) && pitchMicrons > 0
                ? $"Calibrated: {pitchMicrons / 1000.0:F3} mm/px"
                : "Not calibrated — showing pixels only";
        }

        private void HandleMeasureClick(MouseButtonEventArgs e)
        {
            if (imgDisplay.Source is not BitmapSource src)
            {
                MessageBox.Show("Load or capture an image first.");
                return;
            }

            // ActualWidth reflects the Stretch=Uniform layout size (pre-zoom), so this conversion
            // gives true image-pixel coordinates regardless of current zoom.
            double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);
            var imagePt = e.GetPosition(imgDisplay);
            var pixelPt = new System.Windows.Point(imagePt.X * scaleToPixels, imagePt.Y * scaleToPixels);

            int needed = _measureSubMode == MeasureMode.Angle ? 3 : 2;
            _pendingMeasurePoints.Add(pixelPt);

            if (_pendingMeasurePoints.Count < needed)
            {
                txtStatus.Text = _measureSubMode == MeasureMode.Angle
                    ? (_pendingMeasurePoints.Count == 1 ? "Angle — now click the vertex" : "Angle — now click the second point")
                    : "Click the second point";
                return;
            }

            var pts = _pendingMeasurePoints.ToList();
            _pendingMeasurePoints.Clear();

            switch (_measureSubMode)
            {
                case MeasureMode.Distance:
                    FinalizeLineMeasurement(pts[0], pts[1], "Distance", 0);
                    break;
                case MeasureMode.ToothLength:
                    var dlg = new TextInputDialog("Tooth Length", "Tooth number (FDI, optional):") { Owner = System.Windows.Window.GetWindow(this) };
                    int tooth = 0;
                    if (dlg.ShowDialog() == true) int.TryParse(dlg.ResultText.Trim(), out tooth);
                    FinalizeLineMeasurement(pts[0], pts[1], "ToothLength", tooth);
                    break;
                case MeasureMode.Angle:
                    FinalizeAngleMeasurement(pts[0], pts[1], pts[2]);
                    break;
                case MeasureMode.Calibrate:
                    FinalizeCalibration(pts[0], pts[1]);
                    break;
            }
        }

        // Shows real mm only if a sensor pixel pitch has been configured (Settings, or via the
        // Calibrate tool below); otherwise honestly reports pixels rather than fabricating a scale.
        private string FormatMeasurement(double pixelDist)
        {
            string pitchStr = db.GetSetting("SensorPixelPitchMicrons", "");
            if (double.TryParse(pitchStr, out double pitchMicrons) && pitchMicrons > 0)
            {
                double mm = pixelDist * pitchMicrons / 1000.0;
                return $"{mm:F2} mm";
            }
            return $"{pixelDist:F0} px";
        }

        private void FinalizeLineMeasurement(System.Windows.Point p1, System.Windows.Point p2, string kind, int tooth)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double pixelDist = Math.Sqrt(dx * dx + dy * dy);
            string value = FormatMeasurement(pixelDist);
            string label = kind == "ToothLength"
                ? (tooth > 0 ? $"Tooth {tooth}: {value} (radiographic)" : $"Tooth Length: {value} (radiographic)")
                : value;

            var visual = CreateMeasureVisual(kind, tooth, p1, p2, default, false, label);
            visual.DbId = db.SaveMeasurement(new Measurement
            {
                ImagePath = currentImagePath,
                Type = kind,
                ToothNumber = tooth,
                X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y,
                HasThirdPoint = false,
                ValueText = label
            });

            txtStatus.Text = $"{(kind == "ToothLength" ? "Tooth length" : "Distance")} measured: {value}";
        }

        private void FinalizeAngleMeasurement(System.Windows.Point p1, System.Windows.Point vertex, System.Windows.Point p3)
        {
            string label = $"{ComputeAngleDegrees(p1, vertex, p3):F1}°";

            var visual = CreateMeasureVisual("Angle", 0, p1, vertex, p3, true, label);
            visual.DbId = db.SaveMeasurement(new Measurement
            {
                ImagePath = currentImagePath,
                Type = "Angle",
                ToothNumber = 0,
                X1 = p1.X, Y1 = p1.Y, X2 = vertex.X, Y2 = vertex.Y, X3 = p3.X, Y3 = p3.Y,
                HasThirdPoint = true,
                ValueText = label
            });

            txtStatus.Text = $"Angle measured: {label}";
        }

        private static double ComputeAngleDegrees(System.Windows.Point p1, System.Windows.Point vertex, System.Windows.Point p3)
        {
            var v1 = new Vector(p1.X - vertex.X, p1.Y - vertex.Y);
            var v2 = new Vector(p3.X - vertex.X, p3.Y - vertex.Y);
            double mag = v1.Length * v2.Length;
            if (mag <= 0) return 0;
            double dot = v1.X * v2.X + v1.Y * v2.Y;
            return Math.Acos(Math.Clamp(dot / mag, -1, 1)) * 180.0 / Math.PI;
        }

        private void FinalizeCalibration(System.Windows.Point p1, System.Windows.Point p2)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double pixelDist = Math.Sqrt(dx * dx + dy * dy);
            if (pixelDist < 1)
            {
                txtStatus.Text = "Calibration line too short";
                return;
            }

            var dlg = new TextInputDialog("Calibration", "Known real-world length of this line (mm):") { Owner = System.Windows.Window.GetWindow(this) };
            if (dlg.ShowDialog() != true || !double.TryParse(dlg.ResultText.Trim(), out double knownMm) || knownMm <= 0)
            {
                txtStatus.Text = "Calibration cancelled";
                return;
            }

            double pitchMicrons = (knownMm / pixelDist) * 1000.0;
            db.SetSetting("SensorPixelPitchMicrons", pitchMicrons.ToString(CultureInfo.InvariantCulture));
            UpdateCalibrationStatus();

            // Calibration marks the sensor's scale, not a clinical finding — shown as a visual
            // confirmation on the image but never saved to the Measurements table.
            var visual = CreateMeasureVisual("Calibrate", 0, p1, p2, default, false, $"Calibration: {knownMm:F1} mm ✓");
            visual.Line1.Stroke = Brushes.LimeGreen;
            visual.Line1.StrokeDashArray = new DoubleCollection { 4, 2 };
            visual.Label.Foreground = Brushes.LimeGreen;

            txtStatus.Text = $"Calibrated: {pitchMicrons / 1000.0:F3} mm/pixel";
        }

        private MeasureVisual CreateMeasureVisual(string kind, int tooth, System.Windows.Point p1, System.Windows.Point p2,
            System.Windows.Point p3, bool hasP3, string label)
        {
            var v = new MeasureVisual { Kind = kind, ToothNumber = tooth, P1 = p1, P2 = p2, P3 = p3, HasP3 = hasP3 };
            Brush stroke = kind == "Calibrate" ? Brushes.LimeGreen : kind == "AutoWidth" ? ToothWidthBrush : Brushes.Yellow;

            v.Line1 = new System.Windows.Shapes.Line
            {
                Stroke = stroke, StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
            };
            viewerCanvas.Children.Add(v.Line1);

            if (hasP3)
            {
                v.Line2 = new System.Windows.Shapes.Line
                {
                    Stroke = stroke, StrokeThickness = 2,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
                };
                viewerCanvas.Children.Add(v.Line2);
            }

            v.Label = new TextBlock
            {
                Text = label, Foreground = stroke, FontWeight = FontWeights.Bold, FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), Padding = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Cursor = Cursors.Hand, ToolTip = "Right-click to remove"
            };
            viewerCanvas.Children.Add(v.Label);

            v.Handle1 = CreateMeasureHandle(v, 1, stroke);
            v.Handle2 = CreateMeasureHandle(v, 2, stroke);
            if (hasP3) v.Handle3 = CreateMeasureHandle(v, 3, stroke);

            MouseButtonEventHandler removeHandler = (s, ev) => { RemoveMeasureVisual(v); ev.Handled = true; };
            v.Line1.MouseRightButtonDown += removeHandler;
            if (v.Line2 != null) v.Line2.MouseRightButtonDown += removeHandler;
            v.Label.MouseRightButtonDown += removeHandler;

            _measureVisuals.Add(v);
            RepositionVisual(v);
            return v;
        }

        private System.Windows.Shapes.Ellipse CreateMeasureHandle(MeasureVisual v, int index, Brush stroke)
        {
            var handle = new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10, Fill = Brushes.Black, Stroke = stroke, StrokeThickness = 2,
                Cursor = Cursors.SizeAll, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Drag to correct — right-click to remove"
            };
            handle.MouseLeftButtonDown += (s, e) =>
            {
                _draggingVisual = v;
                _draggingHandleIndex = index;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (s, e) =>
            {
                if (_draggingVisual != v || !handle.IsMouseCaptured) return;
                if (imgDisplay.Source is not BitmapSource src) return;
                double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);
                var imagePt = e.GetPosition(imgDisplay);
                var pixelPt = new System.Windows.Point(imagePt.X * scaleToPixels, imagePt.Y * scaleToPixels);
                if (index == 1) v.P1 = pixelPt;
                else if (index == 2) v.P2 = pixelPt;
                else v.P3 = pixelPt;
                v.Estimated = false; // hand-adjusted → no longer an estimate
                RecomputeMeasureLabel(v);
                RepositionVisual(v);
                e.Handled = true;
            };
            handle.MouseLeftButtonUp += (s, e) =>
            {
                if (_draggingVisual == v)
                {
                    handle.ReleaseMouseCapture();
                    _draggingVisual = null;
                    if (v.DbId > 0) db.UpdateMeasurement(v.DbId, v.P1.X, v.P1.Y, v.P2.X, v.P2.Y, v.P3.X, v.P3.Y, v.Label.Text);
                }
                e.Handled = true;
            };
            handle.MouseRightButtonDown += (s, e) => { RemoveMeasureVisual(v); e.Handled = true; };
            viewerCanvas.Children.Add(handle);
            return handle;
        }

        private void RecomputeMeasureLabel(MeasureVisual v)
        {
            if (v.Kind == "Angle")
            {
                v.Label.Text = $"{ComputeAngleDegrees(v.P1, v.P2, v.P3):F1}°";
                return;
            }
            if (v.Kind == "Calibrate") return; // confirmation text is fixed at the calibrated value

            double dx = v.P2.X - v.P1.X, dy = v.P2.Y - v.P1.Y;
            if (v.Kind is "AutoLength" or "AutoWidth")
            {
                v.Label.Text = ToothLabel(v.Kind, Math.Sqrt(dx * dx + dy * dy), v.Estimated);
                return;
            }
            string value = FormatMeasurement(Math.Sqrt(dx * dx + dy * dy));
            v.Label.Text = v.Kind == "ToothLength"
                ? (v.ToothNumber > 0 ? $"Tooth {v.ToothNumber}: {value} (radiographic)" : $"Tooth Length: {value} (radiographic)")
                : value;
        }

        private void RemoveMeasureVisual(MeasureVisual v)
        {
            viewerCanvas.Children.Remove(v.Line1);
            if (v.Line2 != null) viewerCanvas.Children.Remove(v.Line2);
            viewerCanvas.Children.Remove(v.Label);
            viewerCanvas.Children.Remove(v.Handle1);
            viewerCanvas.Children.Remove(v.Handle2);
            if (v.Handle3 != null) viewerCanvas.Children.Remove(v.Handle3);
            _measureVisuals.Remove(v);
            if (v.DbId > 0) db.DeleteMeasurement(v.DbId);
            txtStatus.Text = "Measurement removed";
        }

        // Converts this visual's stored pixel-space points into the viewer's CURRENT on-screen
        // position via TranslatePoint, which accounts for whatever zoom/pan/rotation is active —
        // so measurements stay correctly placed even after the user changes the view.
        private void RepositionVisual(MeasureVisual v)
        {
            if (imgDisplay.Source is not BitmapSource src) return;
            double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);

            System.Windows.Point ToCanvas(System.Windows.Point pixelPt)
            {
                var local = new System.Windows.Point(pixelPt.X / scaleToPixels, pixelPt.Y / scaleToPixels);
                return imgDisplay.TranslatePoint(local, viewerCanvas);
            }

            var c1 = ToCanvas(v.P1);
            var c2 = ToCanvas(v.P2);
            v.Line1.X1 = c1.X; v.Line1.Y1 = c1.Y; v.Line1.X2 = c2.X; v.Line1.Y2 = c2.Y;
            PositionMeasureHandle(v.Handle1, c1);
            PositionMeasureHandle(v.Handle2, c2);

            var mid = new System.Windows.Point((c1.X + c2.X) / 2, (c1.Y + c2.Y) / 2 - 18);

            if (v.HasP3 && v.Line2 != null)
            {
                var c3 = ToCanvas(v.P3);
                v.Line2.X1 = c2.X; v.Line2.Y1 = c2.Y; v.Line2.X2 = c3.X; v.Line2.Y2 = c3.Y;
                if (v.Handle3 != null) PositionMeasureHandle(v.Handle3, c3);
                mid = new System.Windows.Point(c2.X + 14, c2.Y - 14);
            }

            v.Label.Margin = new Thickness(mid.X, mid.Y, 0, 0);
        }

        private static void PositionMeasureHandle(System.Windows.Shapes.Ellipse handle, System.Windows.Point c)
        {
            handle.Margin = new Thickness(c.X - 5, c.Y - 5, 0, 0);
        }

        private void RedrawAllMeasurements()
        {
            foreach (var v in _measureVisuals) RepositionVisual(v);
        }

        private void ClearAllMeasureVisuals()
        {
            foreach (var v in _measureVisuals.ToList())
            {
                viewerCanvas.Children.Remove(v.Line1);
                if (v.Line2 != null) viewerCanvas.Children.Remove(v.Line2);
                viewerCanvas.Children.Remove(v.Label);
                viewerCanvas.Children.Remove(v.Handle1);
                viewerCanvas.Children.Remove(v.Handle2);
                if (v.Handle3 != null) viewerCanvas.Children.Remove(v.Handle3);
            }
            _measureVisuals.Clear();
        }

        // Clears whatever's on screen and reloads the saved measurements (if any) for whichever
        // image is now current — called every time a different image is loaded/captured/selected.
        private void LoadMeasurementsForCurrentImage()
        {
            ClearAllMeasureVisuals();
            ClearPendingTooth(); // a half-placed tooth belongs to the previous image
            if (string.IsNullOrEmpty(currentImagePath)) return;
            foreach (var m in db.GetMeasurementsForImage(currentImagePath))
            {
                var visual = CreateMeasureVisual(m.Type, m.ToothNumber,
                    new System.Windows.Point(m.X1, m.Y1), new System.Windows.Point(m.X2, m.Y2),
                    new System.Windows.Point(m.X3, m.Y3), m.HasThirdPoint, m.ValueText);
                visual.DbId = m.Id;
                visual.Estimated = m.ValueText.Contains('~');
            }
        }

        // ═══ TOOTH LENGTH TOOL ═══
        // Clicking 🦷 Tooth Length auto-detects the teeth (ToothDetector) and draws, for each,
        // a length line (yellow, crown tip → apex) and a crown breadth line (pink), all marked
        // "~" until the dentist drags them. Auto-detection is an estimate and misses teeth, so
        // while the tool is on the dentist can also click a crown tip, then its apex, to add one:
        // that length is exactly what was clicked, and its breadth is found automatically.
        // All lines are normal measurements — draggable, right-click to remove, saved with the image.
        private void btnToothLength_Click(object sender, RoutedEventArgs e)
        {
            if (_toothMode) { ExitToothMode(); txtStatus.Text = "Tooth Length off"; return; }
            if (imgDisplay.Source == null) { MessageBox.Show("Load or capture an image first."); return; }

            // one click-tool at a time
            if (measureControls.Visibility == Visibility.Visible) btnMeasure_Click(sender, e);
            if (_cropMode) btnCrop_Click(sender, e);
            if (_annotateMode) btnAnnotate_Click(sender, e);

            _toothMode = true;
            btnToothLength.Content = "🦷 Done";
            viewerBorder.Cursor = Cursors.Cross;
            txtStatus.Text = "Tooth Length — click a CROWN TIP, then its ROOT APEX to add a tooth  (Esc to finish)";

            // auto-detect only on an image that has no tooth lines yet, so reopening the tool
            // doesn't stack duplicates on top of lines already checked
            if (!_measureVisuals.Any(v => v.Kind is "AutoLength" or "AutoWidth")) RunToothAutoDetect();
        }

        private void ExitToothMode()
        {
            if (!_toothMode) return;
            _toothMode = false;
            ClearPendingTooth();
            btnToothLength.Content = "🦷 Tooth Length";
            viewerBorder.Cursor = Cursors.Arrow;
        }

        private void ClearPendingTooth()
        {
            _pendingToothPoints.Clear();
            if (_pendingToothMarker != null) { viewerCanvas.Children.Remove(_pendingToothMarker); _pendingToothMarker = null; }
        }

        private void HandleToothClick(MouseButtonEventArgs e)
        {
            if (imgDisplay.Source is not BitmapSource src) return;
            double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);
            var imagePt = e.GetPosition(imgDisplay);
            var pixelPt = new System.Windows.Point(imagePt.X * scaleToPixels, imagePt.Y * scaleToPixels);
            if (pixelPt.X < 0 || pixelPt.Y < 0 || pixelPt.X >= src.PixelWidth || pixelPt.Y >= src.PixelHeight) return;

            _pendingToothPoints.Add(pixelPt);
            if (_pendingToothPoints.Count == 1)
            {
                // mark the crown tip until the apex is clicked
                var c = e.GetPosition(viewerCanvas);
                _pendingToothMarker = new System.Windows.Shapes.Ellipse
                {
                    Width = 10, Height = 10, Fill = Brushes.Yellow, IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(c.X - 5, c.Y - 5, 0, 0)
                };
                viewerCanvas.Children.Add(_pendingToothMarker);
                txtStatus.Text = "Tooth Length — now click the ROOT APEX";
                return;
            }

            var crown = _pendingToothPoints[0];
            var apex = _pendingToothPoints[1];
            ClearPendingTooth();
            FinalizeTooth(src, crown, apex);
        }

        private void FinalizeTooth(BitmapSource src, System.Windows.Point crown, System.Windows.Point apex)
        {
            double len = (apex - crown).Length;
            if (len < 10) { txtStatus.Text = "Crown tip and apex are too close — click again"; return; }

            var length = CreateMeasureVisual("AutoLength", 0, crown, apex, default, false, ToothLabel("AutoLength", len, false));
            length.DbId = SaveLineMeasurement(length);

            // breadth on the image as shown (the preset the dentist is looking at)
            (OpenCvSharp.Point2d A, OpenCvSharp.Point2d B)? breadth;
            using (var mat = ImageEnhancer.BitmapSourceToMat(src))
                breadth = ToothMeasure.FindCrownBreadth(mat, new OpenCvSharp.Point2d(crown.X, crown.Y), new OpenCvSharp.Point2d(apex.X, apex.Y));

            string lenText = length.Label.Text;
            if (breadth == null)
            {
                txtStatus.Text = $"Tooth: {lenText} — crown breadth not found (edges unclear); use Measure → Distance. Next crown tip, or Esc.";
                return;
            }
            var a = new System.Windows.Point(breadth.Value.A.X, breadth.Value.A.Y);
            var b = new System.Windows.Point(breadth.Value.B.X, breadth.Value.B.Y);
            var width = CreateMeasureVisual("AutoWidth", 0, a, b, default, false, ToothLabel("AutoWidth", (b - a).Length, true));
            width.Estimated = true;
            width.DbId = SaveLineMeasurement(width);

            txtStatus.Text = $"Tooth: {lenText}, {width.Label.Text} — drag any end to adjust. Next crown tip, or Esc to finish.";
        }

        // Auto-detect teeth (ToothDetector: finds, measures and validates them) and draw
        // length + breadth for each, marked "~" (estimate) for the dentist to check.
        private async void RunToothAutoDetect()
        {
            if (imgDisplay.Source is not BitmapSource src) return;
            txtStatus.Text = "Detecting teeth...";
            try
            {
                src.Freeze();
                var found = await Task.Run(() =>
                {
                    using var mat = ImageEnhancer.BitmapSourceToMat(src);
                    return ToothDetector.Detect(mat);
                });
                if (!_toothMode || !ReferenceEquals(src, imgDisplay.Source)) return; // tool closed / image changed

                foreach (var t in found)
                {
                    var crown = new System.Windows.Point(t.Crown.X, t.Crown.Y);
                    var apex = new System.Windows.Point(t.Apex.X, t.Apex.Y);
                    var len = CreateMeasureVisual("AutoLength", 0, crown, apex, default, false, ToothLabel("AutoLength", (apex - crown).Length, true));
                    len.Estimated = true;
                    len.DbId = SaveLineMeasurement(len);

                    var a = new System.Windows.Point(t.WidthA.X, t.WidthA.Y);
                    var b = new System.Windows.Point(t.WidthB.X, t.WidthB.Y);
                    var wid = CreateMeasureVisual("AutoWidth", 0, a, b, default, false, ToothLabel("AutoWidth", (b - a).Length, true));
                    wid.Estimated = true;
                    wid.DbId = SaveLineMeasurement(wid);
                }
                txtStatus.Text = found.Count == 0
                    ? "No teeth detected automatically — click a CROWN TIP, then its ROOT APEX to measure  (Esc to finish)"
                    : $"Detected {found.Count} tooth/teeth — check each: drag ends to adjust, right-click wrong lines. Click crown tip → apex to add a missed tooth.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Tooth detection error: {ex.Message} — click crown tip → apex to measure";
            }
        }

        // 🧹 Clear Lengths: removes every tooth length/breadth line on this image (and its DB rows)
        private void btnToothClear_Click(object sender, RoutedEventArgs e)
        {
            var toothLines = _measureVisuals.Where(v => v.Kind is "AutoLength" or "AutoWidth").ToList();
            if (toothLines.Count == 0) { txtStatus.Text = "No tooth lines to clear"; return; }
            if (MessageBox.Show($"Remove all {toothLines.Count} tooth length/breadth lines from this image?", "Clear Lengths",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            foreach (var v in toothLines) RemoveMeasureVisual(v);
            ClearPendingTooth();
            txtStatus.Text = $"Cleared {toothLines.Count} tooth lines";
        }

        private int SaveLineMeasurement(MeasureVisual v) => db.SaveMeasurement(new Measurement
        {
            ImagePath = currentImagePath,
            Type = v.Kind,
            ToothNumber = v.ToothNumber,
            X1 = v.P1.X, Y1 = v.P1.Y, X2 = v.P2.X, Y2 = v.P2.Y,
            HasThirdPoint = false,
            ValueText = v.Label.Text
        });

        // "L 17.30 mm" / "W ~6.80 mm" — "~" while it's an automatic estimate
        private string ToothLabel(string kind, double pixelDist, bool estimated)
            => $"{(kind == "AutoWidth" ? "W" : "L")} {(estimated ? "~" : "")}{FormatMeasurement(pixelDist)}";

        // ═══ CROP TOOL — genuinely crops the pixel data via CroppedBitmap, not just visual ═══
        private void btnCrop_Click(object sender, RoutedEventArgs e)
        {
            ExitToothMode(); // one click-tool at a time
            _cropMode = !_cropMode;
            ClearCropOverlay();
            btnCrop.Background = _cropMode ? (Brush)FindResource("BrAccentDim") : Brushes.Transparent;
            txtStatus.Text = _cropMode ? "Crop mode ON — drag a rectangle on the image" : "Crop mode off";
        }

        private void StartCrop(MouseButtonEventArgs e)
        {
            if (imgDisplay.Source == null) { MessageBox.Show("Load or capture an image first."); return; }
            ClearCropOverlay();
            _isCropping = true;
            _cropStartCanvas = e.GetPosition(viewerCanvas);
            _cropStartImage = e.GetPosition(imgDisplay);

            _cropRectVisual = new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 0)),
                Width = 1,
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(_cropStartCanvas.X, _cropStartCanvas.Y, 0, 0)
            };
            viewerCanvas.Children.Add(_cropRectVisual);
            viewerBorder.CaptureMouse();
        }

        private void UpdateCropRect(MouseEventArgs e)
        {
            if (_cropRectVisual == null) return;
            var p = e.GetPosition(viewerCanvas);
            double x = Math.Min(p.X, _cropStartCanvas.X), y = Math.Min(p.Y, _cropStartCanvas.Y);
            double w = Math.Abs(p.X - _cropStartCanvas.X), h = Math.Abs(p.Y - _cropStartCanvas.Y);
            _cropRectVisual.Margin = new Thickness(x, y, 0, 0);
            _cropRectVisual.Width = w;
            _cropRectVisual.Height = h;
        }

        private void FinishCropDrag(MouseButtonEventArgs e)
        {
            _isCropping = false;
            viewerBorder.ReleaseMouseCapture();
            _cropEndImage = e.GetPosition(imgDisplay);

            if (_cropRectVisual == null || _cropRectVisual.Width < 10 || _cropRectVisual.Height < 10)
            {
                ClearCropOverlay();
                txtStatus.Text = "Crop selection too small — drag a bigger rectangle";
                return;
            }
            ShowCropButtons();
        }

        private void ShowCropButtons()
        {
            if (_cropRectVisual == null) return;

            _cropButtonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(_cropRectVisual.Margin.Left, _cropRectVisual.Margin.Top + _cropRectVisual.Height + 4, 0, 0)
            };
            var apply = new Button
            {
                Content = "✓ Apply Crop", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 0),
                Background = (Brush)FindResource("BrSuccess"), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            apply.Click += (s, e) => ApplyCrop();
            var cancel = new Button
            {
                Content = "✕ Cancel", Padding = new Thickness(8, 4, 8, 4),
                Background = (Brush)FindResource("BrBgCard"), Foreground = (Brush)FindResource("BrText"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            cancel.Click += (s, e) => { ClearCropOverlay(); txtStatus.Text = "Crop cancelled"; };
            _cropButtonsPanel.Children.Add(apply);
            _cropButtonsPanel.Children.Add(cancel);
            viewerCanvas.Children.Add(_cropButtonsPanel);
        }

        private void ApplyCrop()
        {
            if (imgDisplay.Source is not BitmapSource src) { ClearCropOverlay(); return; }

            // _cropStartImage/_cropEndImage are already in imgDisplay's local (pre-zoom) space
            // — same technique as the measurement tool — so this is correct at any zoom level.
            double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);

            double x0 = Math.Min(_cropStartImage.X, _cropEndImage.X) * scaleToPixels;
            double y0 = Math.Min(_cropStartImage.Y, _cropEndImage.Y) * scaleToPixels;
            double x1 = Math.Max(_cropStartImage.X, _cropEndImage.X) * scaleToPixels;
            double y1 = Math.Max(_cropStartImage.Y, _cropEndImage.Y) * scaleToPixels;

            int px = (int)Math.Clamp(x0, 0, src.PixelWidth - 1);
            int py = (int)Math.Clamp(y0, 0, src.PixelHeight - 1);
            int pw = (int)Math.Clamp(x1 - x0, 1, src.PixelWidth - px);
            int ph = (int)Math.Clamp(y1 - y0, 1, src.PixelHeight - py);

            try
            {
                var cropped = new CroppedBitmap(src, new Int32Rect(px, py, pw, ph));
                cropped.Freeze();
                imgDisplay.Source = cropped;
                originalImage = cropped;
                ResetEnhancementState(); // new original → old preset/slider settings don't carry over
                enhancedImage = null;
                ClearAllMeasureVisuals(); // old measurements were in the pre-crop pixel space
                ResetViewTransforms();
                txtStatus.Text = $"Cropped to {pw}×{ph}px — use Save/Attach to keep this version";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Crop failed: {ex.Message}");
            }
            finally
            {
                ClearCropOverlay();
                _cropMode = false;
                btnCrop.Background = Brushes.Transparent;
            }
        }

        private void ClearCropOverlay()
        {
            if (_cropRectVisual != null) { viewerCanvas.Children.Remove(_cropRectVisual); _cropRectVisual = null; }
            if (_cropButtonsPanel != null) { viewerCanvas.Children.Remove(_cropButtonsPanel); _cropButtonsPanel = null; }
        }

        // ═══ ANNOTATE TOOL — real text labels placed on the viewer, right-click to remove.
        // Overlay only (like Measure) — not baked into exported/saved image files. ═══
        private void btnAnnotate_Click(object sender, RoutedEventArgs e)
        {
            ExitToothMode(); // one click-tool at a time
            _annotateMode = !_annotateMode;
            btnAnnotate.Background = _annotateMode ? (Brush)FindResource("BrAccentDim") : Brushes.Transparent;
            txtStatus.Text = _annotateMode
                ? "Annotate mode ON — click on the image to add a label (right-click a label to remove it)"
                : "Annotate mode off";
        }

        private void HandleAnnotateClick(MouseButtonEventArgs e)
        {
            if (imgDisplay.Source == null) { MessageBox.Show("Load or capture an image first."); return; }

            var canvasPt = e.GetPosition(viewerCanvas);
            var dlg = new TextInputDialog("Add Annotation", "Label text:") { Owner = System.Windows.Window.GetWindow(this) };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ResultText)) return;

            var label = new TextBlock
            {
                Text = dlg.ResultText,
                Foreground = Brushes.Cyan,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
                Padding = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand,
                ToolTip = "Right-click to remove",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(canvasPt.X, canvasPt.Y, 0, 0)
            };
            label.MouseRightButtonDown += (s, ev) =>
            {
                viewerCanvas.Children.Remove(label);
                _annotationVisuals.Remove(label);
                ev.Handled = true;
            };
            viewerCanvas.Children.Add(label);
            _annotationVisuals.Add(label);
        }

        private void ClearAnnotations()
        {
            foreach (var v in _annotationVisuals)
                viewerCanvas.Children.Remove(v);
            _annotationVisuals.Clear();
        }

        private void Viewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isCropping)
            {
                FinishCropDrag(e);
                return;
            }
            isPanning = false;
            viewerBorder.ReleaseMouseCapture();
            viewerBorder.Cursor = Cursors.Arrow;
        }

        private void SensorTimer_Tick(object? sender, EventArgs e)
        {
            // Don't check if already connected or currently connecting
            if (sensor.IsConnected || !btnConnect.IsEnabled) return;

            // Try to detect sensor (quick check — just open/close)
            try
            {
                IntPtr handle = CMOS_USB.USB_OpenDevice(0x4400);
                if (handle != new IntPtr(-1))
                {
                    // Sensor found! Close the test handle and do full connect
                    CMOS_USB.USB_CloseDevice(handle);
                    _sensorTimer?.Stop();
                    txtStatus.Text = "Sensor detected — connecting...";
                    btnConnect_Click(this, new RoutedEventArgs());
                }
            }
            catch { }
        }

        private void Viewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_magnifierMode) UpdateMagnifier();
            if (_isCropping)
            {
                UpdateCropRect(e);
                return;
            }
            if (isPanning)
            {
                var p = e.GetPosition(viewerBorder);
                imgTranslate.X = panStartX + (p.X - panStart.X);
                imgTranslate.Y = panStartY + (p.Y - panStart.Y);
                RedrawAllMeasurements();
            }
            else if (isWindowLevel && originalImage != null)
            {
                var p = e.GetPosition(viewerBorder);
                sliderBrightness.Value = Math.Clamp((p.X - wlStart.X) * 0.5, -100, 100);
                sliderContrast.Value = Math.Clamp((wlStart.Y - p.Y) * 0.03, 0, 8);
                UpdateSliderLabels();
            }
        }

        private void Viewer_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            isWindowLevel = true;
            wlStart = e.GetPosition(viewerBorder);
            viewerBorder.CaptureMouse();
            viewerBorder.Cursor = Cursors.SizeAll;
            txtStatus.Text = "Window/Level — drag right=brightness, up=contrast";
        }

        private void Viewer_RightMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isWindowLevel)
            {
                isWindowLevel = false;
                viewerBorder.ReleaseMouseCapture();
                viewerBorder.Cursor = Cursors.Arrow; // sliders already rendered live during the drag
            }
        }

        private void Viewer_MouseLeave(object sender, MouseEventArgs e)
        {
            HideMagnifierVisuals();
        }

        // ═══ MAGNIFIER — circular loupe that follows the cursor, showing the image at
        // 1x/2x/4x native pixel scale regardless of the main viewer's current zoom/pan/rotation.
        // A passive hover overlay (doesn't intercept clicks), so it works alongside pan/other tools. ═══
        private void btnMagnifier_Click(object sender, RoutedEventArgs e)
        {
            _magnifierMode = !_magnifierMode;
            toolMagnifier.Background = _magnifierMode ? (Brush)FindResource("BrAccentDim") : Brushes.Transparent;
            magnifierControls.Visibility = _magnifierMode ? Visibility.Visible : Visibility.Collapsed;
            if (!_magnifierMode) HideMagnifierVisuals();
            txtStatus.Text = _magnifierMode
                ? "Magnifier ON — hover over the image to inspect (click 🔍 again to stop)"
                : "Magnifier off";
        }

        private void MagZoomLevel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tagStr || !double.TryParse(tagStr, out double level)) return;
            _magnifierZoom = level;
            magBtn1x.Background = (Brush)FindResource(level == 1 ? "BrAccentDim" : "BrBgCard");
            magBtn2x.Background = (Brush)FindResource(level == 2 ? "BrAccentDim" : "BrBgCard");
            magBtn4x.Background = (Brush)FindResource(level == 4 ? "BrAccentDim" : "BrBgCard");
            // Refresh immediately at the cursor's current position — otherwise the new zoom
            // level wouldn't show until the next mouse move, which looks like the button did nothing.
            UpdateMagnifier();
        }

        private void HideMagnifierVisuals()
        {
            magnifierLoupe.Visibility = Visibility.Collapsed;
            magAnchor.Visibility = Visibility.Collapsed;
            magTangent1.Visibility = Visibility.Collapsed;
            magTangent2.Visibility = Visibility.Collapsed;
        }

        // External tangent lines between two circles — used to draw the Nike-style "callout"
        // connecting the small anchor circle (exact cursor point) to the offset zoomed circle.
        // Derived via the classic shrink-to-point construction: tangents from c1 to a circle at
        // c2 with radius (r2-r1) are found first, then both tangent points are pushed outward
        // along the same normal by r1/r2 respectively to get the true external tangent points.
        private static void ExternalTangentPoints(
            System.Windows.Point c1, double r1, System.Windows.Point c2, double r2,
            out System.Windows.Point t1a, out System.Windows.Point t2a,
            out System.Windows.Point t1b, out System.Windows.Point t2b)
        {
            double dx = c2.X - c1.X, dy = c2.Y - c1.Y;
            double d = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
            double diff = r2 - r1;
            if (d <= Math.Abs(diff)) d = Math.Abs(diff) + 0.01;
            double theta = Math.Atan2(dy, dx);
            double beta = Math.Asin(Math.Clamp(diff / d, -1, 1));
            double L = Math.Sqrt(Math.Max(d * d - diff * diff, 0));

            double angleA = theta + beta;
            var auxA = new System.Windows.Point(c1.X + L * Math.Cos(angleA), c1.Y + L * Math.Sin(angleA));
            var normalA = new System.Windows.Vector((auxA.X - c2.X) / diff, (auxA.Y - c2.Y) / diff);
            t1a = new System.Windows.Point(c1.X + r1 * normalA.X, c1.Y + r1 * normalA.Y);
            t2a = new System.Windows.Point(c2.X + r2 * normalA.X, c2.Y + r2 * normalA.Y);

            double angleB = theta - beta;
            var auxB = new System.Windows.Point(c1.X + L * Math.Cos(angleB), c1.Y + L * Math.Sin(angleB));
            var normalB = new System.Windows.Vector((auxB.X - c2.X) / diff, (auxB.Y - c2.Y) / diff);
            t1b = new System.Windows.Point(c1.X + r1 * normalB.X, c1.Y + r1 * normalB.Y);
            t2b = new System.Windows.Point(c2.X + r2 * normalB.X, c2.Y + r2 * normalB.Y);
        }

        private void UpdateMagnifier()
        {
            if (imgDisplay.Source is not BitmapSource src) { HideMagnifierVisuals(); return; }

            // Pre-zoom, image-local coordinates — same technique as Measure/Crop, so this
            // stays correct regardless of the main viewer's current zoom/pan/rotation.
            var localPt = Mouse.GetPosition(imgDisplay);
            if (localPt.X < 0 || localPt.Y < 0 || localPt.X > imgDisplay.ActualWidth || localPt.Y > imgDisplay.ActualHeight)
            {
                HideMagnifierVisuals();
                return;
            }

            double scaleToPixels = src.PixelWidth / Math.Max(imgDisplay.ActualWidth, 1);
            double pixelX = localPt.X * scaleToPixels;
            double pixelY = localPt.Y * scaleToPixels;

            // Crop just the small native-pixel region the loupe needs, instead of transforming
            // the (possibly huge) full-resolution Image with a large Scale/Translate — WPF was
            // silently failing to render the Image whenever those transform values got large
            // (confirmed: identical math stayed correct arithmetically but rendered nothing for
            // most hover positions, only "working" near the top-left corner by coincidence).
            // Cropping keeps every number bounded to the loupe's own 200x200 size, so the bug
            // can't recur anywhere on the image. Int32Rect-based CroppedBitmap is the same
            // technique the Crop tool already uses successfully elsewhere in this file.
            double loupeSize = MagnifierRadius * 2;
            double cropSizeSrc = loupeSize / _magnifierZoom;
            int cropW = (int)Math.Clamp(cropSizeSrc, 2, src.PixelWidth);
            int cropH = (int)Math.Clamp(cropSizeSrc, 2, src.PixelHeight);
            int cropX = (int)Math.Clamp(pixelX - cropW / 2.0, 0, Math.Max(src.PixelWidth - cropW, 0));
            int cropY = (int)Math.Clamp(pixelY - cropH / 2.0, 0, Math.Max(src.PixelHeight - cropH, 0));

            try
            {
                imgMagnifier.Source = new CroppedBitmap(src, new Int32Rect(cropX, cropY, cropW, cropH));
            }
            catch
            {
                HideMagnifierVisuals();
                return;
            }
            magRotate.Angle = currentRotation;

            var canvasPt = Mouse.GetPosition(viewerCanvas);

            // Offset the big zoomed circle away from the cursor (default: up-and-right, like the
            // Nike reference) so it never sits on top of the exact spot being inspected. Flip to
            // whichever side still fits if the default placement would run off the viewer edges.
            double offset = MagnifierRadius * 1.4;
            double bigX = canvasPt.X + offset, bigY = canvasPt.Y - offset;
            if (bigX + MagnifierRadius > viewerCanvas.ActualWidth) bigX = canvasPt.X - offset;
            if (bigY - MagnifierRadius < 0) bigY = canvasPt.Y + offset;
            bigX = Math.Clamp(bigX, MagnifierRadius, Math.Max(viewerCanvas.ActualWidth - MagnifierRadius, MagnifierRadius));
            bigY = Math.Clamp(bigY, MagnifierRadius, Math.Max(viewerCanvas.ActualHeight - MagnifierRadius, MagnifierRadius));
            var bigCenter = new System.Windows.Point(bigX, bigY);

            magnifierLoupe.Margin = new Thickness(bigX - MagnifierRadius, bigY - MagnifierRadius, 0, 0);
            magnifierLoupe.Visibility = Visibility.Visible;

            magAnchor.Margin = new Thickness(canvasPt.X - AnchorRadius, canvasPt.Y - AnchorRadius, 0, 0);
            magAnchor.Visibility = Visibility.Visible;

            ExternalTangentPoints(canvasPt, AnchorRadius, bigCenter, MagnifierRadius,
                out var t1a, out var t2a, out var t1b, out var t2b);
            magTangent1.X1 = t1a.X; magTangent1.Y1 = t1a.Y; magTangent1.X2 = t2a.X; magTangent1.Y2 = t2a.Y;
            magTangent2.X1 = t1b.X; magTangent2.Y1 = t1b.Y; magTangent2.X2 = t2b.X; magTangent2.Y2 = t2b.Y;
            magTangent1.Visibility = Visibility.Visible;
            magTangent2.Visibility = Visibility.Visible;
        }
        // =================================================================
        //  SENSOR — Connect
        // =================================================================
        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (sensor.IsConnected)
            {
                sensor.Abort();
                sensor.Disconnect();
                SetSensorUI(false);
                txtStatus.Text = "Sensor disconnected";
                return;
            }

            txtStatus.Text = "Connecting... (acquiring dark image)";
            btnConnect.IsEnabled = false;

            var w = new BackgroundWorker();
            w.DoWork += (s, a) => a.Result = sensor.Connect();
            w.RunWorkerCompleted += (s, a) =>
            {
                btnConnect.IsEnabled = true;
                if (a.Error != null)
                {
                    SetSensorUI(false);
                    MessageBox.Show($"Connection error:\n{a.Error.Message}", "Error");
                    txtStatus.Text = "Connection error";
                    return;
                }
                var r = (ConnectResult)a.Result!;
                if (r.Success)
                {
                    SetSensorUI(true);
                    txtStatus.Text = $"Connected — SN:{r.Serial} {r.Width}×{r.Height} FW:{r.Firmware}";
                }
                else
                {
                    SetSensorUI(false);
                    MessageBox.Show(r.Error, "Connection Failed");
                    txtStatus.Text = "Connection failed";
                }
            };
            w.RunWorkerAsync();
        }

        // =================================================================
        //  SENSOR — Capture X-Ray (with timeout)
        // =================================================================
        private void btnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (!sensor.IsConnected)
            {
                MessageBox.Show("Connect sensor first.");
                return;
            }

            txtStatus.Text = "Waiting for X-ray exposure... (30s timeout)";
            btnCapture.IsEnabled = false;
            btnConnect.IsEnabled = false;

            var w = new BackgroundWorker();
            w.DoWork += (s, a) =>
            {
                try
                {
                    a.Result = sensor.Capture();
                }
                catch (Exception ex)
                {
                    a.Result = CaptureResult.Fail($"Capture exception: {ex.Message}");
                }
            };
            w.RunWorkerCompleted += (s, a) =>
            {
                btnConnect.IsEnabled = true;

                if (a.Error != null)
                {
                    MessageBox.Show($"Capture error:\n{a.Error.Message}", "Error");
                    txtStatus.Text = "Capture error";
                    btnCapture.IsEnabled = sensor.IsConnected;
                    return;
                }

                var r = (CaptureResult)a.Result!;
                if (r.Success)
                {
                    originalImage = r.Image;
                    ResetEnhancementState(); // new original → old preset/slider settings don't carry over
                    enhancedImage = null;
                    imgDisplay.Source = r.Image;
                    ResetViewTransforms();

                    // Auto-save capture
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
                    Directory.CreateDirectory(dir);
                    // Tag capture with the active modality so gallery filtering works
                    string modPrefix = currentModality == "All" ? "intraoral" : currentModality.ToLower();
                    var file = $"{modPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var path = Path.Combine(dir, file);
                    SaveBitmapSource(r.Image!, path);
                    currentImagePath = path;
                    LoadMeasurementsForCurrentImage();
                    LoadGalleryThumbnails();
                    UpdateStorageInfo();
                    if (r.IsNoiseOnly)
                        txtStatus.Text = $"Captured {r.Width}×{r.Height} — No X-ray detected (noise only). Fire X-ray source during capture.";
                    else
                        txtStatus.Text = $"Captured {r.Width}×{r.Height} — saved as {file}";
                }
                else
                {
                    MessageBox.Show($"Capture failed:\n{r.Error}", "Capture Failed");
                    txtStatus.Text = $"Capture failed: {r.Error}";
                }
                btnCapture.IsEnabled = sensor.IsConnected;
            };
            w.RunWorkerAsync();
        }

        // Sensor status now lives entirely in the Shell header pill + Devices page —
        // this page just feeds those via Shell, no local status UI to update anymore.
        private void SetSensorUI(bool c)
        {
            Shell?.SetSensorIndicator(c);

            if (c)
            {
                Shell?.SetSensorDetails($"SN {sensor.Serial} · Type 0x{sensor.SensorType:X2} · FW {sensor.Firmware} · {sensor.Width}×{sensor.Height}px");
                btnConnect.Content = "⬤ Disconnect";
                btnCapture.IsEnabled = true;
            }
            else
            {
                _sensorTimer?.Start(); // Resume auto-detect
                Shell?.SetSensorDetails("");
                btnConnect.Content = "⬤ Connect Sensor";
                btnCapture.IsEnabled = false;
            }
        }

        // =================================================================
        //  ZOOM / ROTATE / FIT
        // =================================================================
        private void btnZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(currentZoom + 25);
        private void btnZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(currentZoom - 25);

        private void btnFit_Click(object sender, RoutedEventArgs e)
        {
            ResetViewTransforms();
            txtStatus.Text = "View reset";
        }

        private void btnRotate_Click(object sender, RoutedEventArgs e)
        {
            currentRotation = (currentRotation + 90) % 360;
            imgRotate.Angle = currentRotation;
            RedrawAllMeasurements();
            txtStatus.Text = $"Rotated {currentRotation}°";
        }

        private void sliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (imgScale == null || txtZoom == null) return;
            SetZoom(sliderZoom.Value, false);
        }

        private void SetZoom(double pct, bool updateSlider = true)
        {
            currentZoom = Math.Clamp(pct, 10, 500);
            var s = currentZoom / 100.0;
            imgScale.ScaleX = s;
            imgScale.ScaleY = s;
            if (txtZoom != null) txtZoom.Text = $"{currentZoom:F0}%";
            if (updateSlider && sliderZoom != null) sliderZoom.Value = currentZoom;
            RedrawAllMeasurements();
        }

        private void ResetViewTransforms()
        {
            currentZoom = 100;
            currentRotation = 0;
            imgScale.ScaleX = 1;
            imgScale.ScaleY = 1;
            imgRotate.Angle = 0;
            imgTranslate.X = 0;
            imgTranslate.Y = 0;
            if (sliderZoom != null) sliderZoom.Value = 100;
            if (txtZoom != null) txtZoom.Text = "100%";
            _pendingMeasurePoints.Clear();
            RedrawAllMeasurements();
            ClearCropOverlay();
            ClearAnnotations();
        }

        // ═══ FULL ZOOM — large overlay filling the viewer column, triggered by button or
        // double-click; leaves the sidebar and right panel visible. ═══
        private void FullZoom_Click(object sender, RoutedEventArgs e) => OpenFullZoom();

        private void OpenFullZoom()
        {
            if (imgDisplay.Source == null)
            {
                MessageBox.Show("Load or capture an image first.");
                return;
            }
            imgFullZoom.Source = imgDisplay.Source;
            fullZoomOverlay.Visibility = Visibility.Visible;
        }

        private void CloseFullZoom_Click(object sender, MouseButtonEventArgs e)
        {
            CloseFullZoom();
            e.Handled = true;
        }

        private void CloseFullZoom()
        {
            fullZoomOverlay.Visibility = Visibility.Collapsed;
            imgFullZoom.Source = null;
        }

        // =================================================================
        //  ENHANCEMENT PRESETS — ALL ASYNC + try/finally
        // =================================================================
        private void btnPresetNormal_Click(object sender, RoutedEventArgs e)
            => _ = RunAsync("Auto Enhance", m => ImageEnhancer.PresetNormal(m));

        private void btnPresetHighContrast_Click(object sender, RoutedEventArgs e)
            => _ = RunAsync("High Contrast", m => ImageEnhancer.PresetHighContrast(m));

        private void btnPresetCaries_Click(object sender, RoutedEventArgs e)
            => _ = RunAsync("Caries", m => ImageEnhancer.FullEnhance(m, 4.5, 1.3, 0.55, true));

        private void btnPresetEndo_Click(object sender, RoutedEventArgs e)
            => _ = RunAsync("Endodontic", m => ImageEnhancer.PresetEndodontic(m));

        private void btnPresetPerio_Click(object sender, RoutedEventArgs e)
            => _ = RunAsync("Periodontal", m => ImageEnhancer.PresetPeriodontal(m));

        private async Task RunAsync(string name, Func<Mat, Mat> proc)
        {
            if (!EnsureImage() || isProcessing) return;
            isProcessing = true;
            txtStatus.Text = $"Applying {name}...";
            try
            {
                var src = originalImage!;
                src.Freeze();
                BitmapSource? res = null;

                await Task.Run(() =>
                {
                    using var m = ImageEnhancer.BitmapSourceToMat(src);
                    using var p = proc(m);
                    res = ImageEnhancer.MatToBitmapSource(p);
                    res.Freeze();
                });

                if (res != null) SetPresetBase(res, null, name); // sliders now fine-tune this preset
                txtStatus.Text = $"{name} applied — Space to compare";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error in {name}: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        // =================================================================
        //  INVERT — ASYNC + try/finally
        // =================================================================
        private async void btnInvert_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureImage() || isProcessing) return;
            isProcessing = true;
            txtStatus.Text = "Inverting...";
            try
            {
                // Inverts the slider base (preset result, or the original), so slider
                // adjustments and any colour map stay on top; pressing it twice undoes it.
                var src = _presetBase ?? originalImage!;
                src.Freeze();
                BitmapSource? res = null;

                await Task.Run(() =>
                {
                    using var m = ImageEnhancer.BitmapSourceToMat(src);
                    using var r = ImageEnhancer.ApplyInvert(m);
                    res = ImageEnhancer.MatToBitmapSource(r);
                    res.Freeze();
                });

                if (res != null) SetPresetBase(res, _presetPost, _presetName == "" ? "Inverted" : _presetName + " + Invert");
                txtStatus.Text = "Inverted (Space for original)";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Invert error: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        // =================================================================
        //  COLOR MAPS — ASYNC + try/finally
        // =================================================================
        private void btnColorBone_Click(object sender, RoutedEventArgs e)
            => _ = RunColorAsync("Bone Density", ColormapTypes.Bone);

        private void btnColorThermal_Click(object sender, RoutedEventArgs e)
            => _ = RunColorAsync("Thermal", ColormapTypes.Hot);

        private async Task RunColorAsync(string name, ColormapTypes map)
        {
            if (!EnsureImage() || isProcessing) return;
            isProcessing = true;
            txtStatus.Text = $"Applying {name}...";
            try
            {
                var src = originalImage!;
                src.Freeze();
                BitmapSource? res = null;

                await Task.Run(() =>
                {
                    // Base = the grey enhanced stage; the colour map runs after the sliders
                    using var m = ImageEnhancer.BitmapSourceToMat(src);
                    using var enhanced = ImageEnhancer.FullEnhance(m, 3.0, 0.6, 0.8);
                    res = ImageEnhancer.MatToBitmapSource(enhanced);
                    res.Freeze();
                });

                if (res != null) SetPresetBase(res, g => ImageEnhancer.ApplyColorMap(g, map), name);
                txtStatus.Text = $"{name} applied";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error in {name}: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        // =================================================================
        //  EDGE MAP — ASYNC + try/finally
        // =================================================================
        private async void btnEdgeMap_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureImage() || isProcessing) return;
            isProcessing = true;
            txtStatus.Text = "Applying edge map...";
            try
            {
                var src = originalImage!;
                src.Freeze();
                BitmapSource? res = null;

                await Task.Run(() =>
                {
                    using var m = ImageEnhancer.BitmapSourceToMat(src);
                    using var enhanced = ImageEnhancer.FullEnhance(m, 3.0, 0.5, 0.8);
                    using var edges = ImageEnhancer.ApplyEdgeEnhance(enhanced, 0.5);
                    res = ImageEnhancer.MatToBitmapSource(edges);
                    res.Freeze();
                });

                if (res != null) SetPresetBase(res, g => ImageEnhancer.ApplyColorMap(g, ColormapTypes.Cividis), "Edge map");
                txtStatus.Text = "Edge map — highlights boundaries and cracks";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Edge map error: {ex.Message}";
            }
            finally
            {
                isProcessing = false;
            }
        }

        // =================================================================
        //  SLIDERS — live, fine-tuning ON TOP of the active preset
        //
        //  A preset/filter stores its result as the slider BASE (_presetBase); colour maps
        //  store their grey stage plus the colour map as a final step (_presetPost), so
        //  sliders adjust the grey image and the colours are re-applied after. Moving a slider
        //  re-renders base → sliders → post after a short debounce; only the latest values are
        //  rendered, and a result for an image that has since changed is thrown away.
        // =================================================================
        private BitmapSource? _presetBase;          // null = no preset → sliders work on originalImage
        private Func<Mat, Mat>? _presetPost;        // e.g. colour map, applied after the sliders
        private string _presetName = "";
        private bool _suppressSliderRender;         // set while Reset/new-image move the sliders
        private bool _rendering, _renderPending;
        private System.Windows.Threading.DispatcherTimer? _sliderDebounce;

        private void EnhSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderLabels();
            if (_suppressSliderRender || originalImage == null) return;

            if (_sliderDebounce == null)
            {
                _sliderDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                _sliderDebounce.Tick += (s, a) => { _sliderDebounce!.Stop(); RenderEnhancement(); };
            }
            _sliderDebounce.Stop();
            _sliderDebounce.Start();
        }

        private bool SlidersAtDefault =>
            Math.Abs(sliderBrightness.Value) <= 1 && sliderContrast.Value <= 0.1 &&
            Math.Abs(sliderGamma.Value - 1.0) <= 0.01 && sliderSharp.Value <= 0.05;

        // Sliders back to defaults WITHOUT triggering a render
        private void ResetSlidersSilently()
        {
            _sliderDebounce?.Stop();
            _suppressSliderRender = true;
            sliderBrightness.Value = 0;
            sliderContrast.Value = 0;
            sliderGamma.Value = 1.0;
            sliderSharp.Value = 0;
            _suppressSliderRender = false;
            UpdateSliderLabels();
        }

        // Called whenever a different image becomes the original (load, capture, gallery, crop,
        // clear) — old preset and slider settings must not carry over to it.
        private void ResetEnhancementState()
        {
            ResetSlidersSilently();
            _presetBase = null;
            _presetPost = null;
            _presetName = "";
        }

        // A preset/filter finished: make it the slider base and show it (with any slider
        // adjustments already dialled in re-applied on top).
        private void SetPresetBase(BitmapSource baseImg, Func<Mat, Mat>? post, string name)
        {
            _presetBase = baseImg;
            _presetPost = post;
            _presetName = name;
            RenderEnhancement();
        }

        // base → sliders → post, rendered off the UI thread; latest values win.
        private async void RenderEnhancement()
        {
            if (_rendering) { _renderPending = true; return; }
            _rendering = true;
            try
            {
                do
                {
                    _renderPending = false;
                    var src = _presetBase ?? originalImage;
                    if (src == null) return;

                    // Nothing to do: show the base (or the original) directly
                    if (SlidersAtDefault && _presetPost == null)
                    {
                        imgDisplay.Source = src;
                        enhancedImage = _presetBase; // null when back to the original
                        txtStatus.Text = _presetBase != null ? $"{_presetName} — Space to compare" : "Original";
                        continue;
                    }

                    double br = sliderBrightness.Value, cl = sliderContrast.Value;
                    double gm = sliderGamma.Value, sh = sliderSharp.Value;
                    var post = _presetPost;
                    src.Freeze();

                    BitmapSource res = await Task.Run(() =>
                    {
                        Mat cur = ImageEnhancer.BitmapSourceToMat(src);
                        void Step(Func<Mat, Mat> f) { var next = f(cur); cur.Dispose(); cur = next; }

                        if (cl > 0.1) Step(m => ImageEnhancer.ApplyCLAHE(m, cl));
                        if (sh > 0.05) Step(m => ImageEnhancer.ApplySharpen(m, 1.5, sh));
                        if (Math.Abs(gm - 1.0) > 0.01) Step(m => ImageEnhancer.ApplyGamma(m, gm));
                        if (Math.Abs(br) > 1) Step(m => ImageEnhancer.ApplyBrightness(m, br));
                        if (post != null) Step(post);

                        var bmp = ImageEnhancer.MatToBitmapSource(cur);
                        cur.Dispose();
                        return bmp;
                    });

                    // Image or preset changed while rendering → this result is stale
                    if (!ReferenceEquals(src, _presetBase ?? originalImage)) continue;

                    imgDisplay.Source = res;
                    enhancedImage = res;
                    string adj = SlidersAtDefault ? "" : $"  ·  CLAHE {cl:F1}  Sharp {sh:F1}  Gamma {gm:F2}  Bright {br:F0}";
                    txtStatus.Text = $"{(_presetName == "" ? "Adjusted" : _presetName)}{adj} — Space to compare";
                }
                while (_renderPending);
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Slider error: {ex.Message}";
            }
            finally
            {
                _rendering = false;
            }
        }

        private void UpdateSliderLabels()
        {
            if (txtBrightVal == null || txtContrastVal == null || txtGammaVal == null || txtSharpVal == null) return;
            txtBrightVal.Text = $"{sliderBrightness.Value:F0}";
            txtContrastVal.Text = $"{sliderContrast.Value:F1}";
            txtGammaVal.Text = $"{sliderGamma.Value:F2}";
            txtSharpVal.Text = $"{sliderSharp.Value:F1}";
        }

        // Reset = sliders AND preset back to the original image
        private void btnResetSliders_Click(object sender, RoutedEventArgs e)
        {
            ResetEnhancementState();

            if (originalImage != null)
            {
                imgDisplay.Source = originalImage;
                enhancedImage = null;
            }
            isProcessing = false;
            txtStatus.Text = "Reset to original (Ctrl+Z)";
        }

        // =================================================================
        //  COMPARE
        // =================================================================
        private void btnSplitView_Click(object sender, RoutedEventArgs e)
        {
            if (originalImage == null || enhancedImage == null)
            {
                txtStatus.Text = "Enhance an image first, then compare";
                return;
            }

            if (imgDisplay.Source == enhancedImage)
            {
                imgDisplay.Source = originalImage;
                txtStatus.Text = "ORIGINAL — click Compare for enhanced";
            }
            else
            {
                imgDisplay.Source = enhancedImage;
                txtStatus.Text = "ENHANCED — click Compare for original";
            }
        }

        // =================================================================
        //  IMAGE TABS
        // =================================================================
        // Single handler for all modality tabs — the Tag carries the modality name.
        // Highlights the active tab, updates the gallery label, and filters the gallery.
        private void Modality_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            currentModality = btn.Tag as string ?? "All";

            foreach (var t in new[] { tabAll, tabIntraoral, tabPanoramic, tabCeph, tabCBCT })
                t.Style = (Style)FindResource(t == btn ? "TabBtnActive" : "TabBtn");

            if (txtGalleryFilter != null)
                txtGalleryFilter.Text = currentModality == "All" ? "All Images" : btn.Content?.ToString() ?? currentModality;

            LoadGalleryThumbnails();
            txtStatus.Text = currentModality == "All"
                ? "Showing all images"
                : $"Filtered to {btn.Content} images";
        }

        // =================================================================
        //  IMPORT DICOM (placeholder)
        // =================================================================
        private void btnImportDICOM_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "DICOM Import/Export coming in future update.\n\n" +
                "Requires: fo-dicom NuGet package",
                "DICOM — Coming Soon",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // =================================================================
        //  PRINT
        // =================================================================
        private void btnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (imgDisplay.Source == null) { MessageBox.Show("No image to print."); return; }

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() == true)
            {
                var visual = new DrawingVisual();
                using (var ctx = visual.RenderOpen())
                {
                    var bmp = imgDisplay.Source as BitmapSource;
                    if (bmp != null)
                    {
                        double pageW = dlg.PrintableAreaWidth;
                        double pageH = dlg.PrintableAreaHeight;
                        double scale = Math.Min(pageW / bmp.PixelWidth, pageH / bmp.PixelHeight) * 0.9;
                        double w = bmp.PixelWidth * scale;
                        double h = bmp.PixelHeight * scale;
                        double x = (pageW - w) / 2;
                        double y = (pageH - h) / 2;

                        ctx.DrawImage(bmp, new System.Windows.Rect(x, y, w, h));
                        string patientLabel = _selectedPatientId.HasValue
                            ? (db.GetPatientById(_selectedPatientId.Value)?.Name ?? "Unassigned")
                            : "Unassigned";
                        ctx.DrawText(
                            new FormattedText(
                                $"Patient: {patientLabel}  |  {DateTime.Now:yyyy-MM-dd HH:mm}",
                                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                new Typeface("Segoe UI"), 10, Brushes.Black, 1),
                            new System.Windows.Point(20, 20));
                    }
                }
                dlg.PrintVisual(visual, "DentalX X-Ray Print");
                txtStatus.Text = "Printed successfully";
            }
        }

        // =================================================================
        //  2D / 3D VIEWER
        // =================================================================
        private void btnViewer_Click(object sender, RoutedEventArgs e)
        {
            if (imgDisplay.Source == null)
            {
                MessageBox.Show("Load or capture an image first.");
                return;
            }

            // Toggle between normal and enhanced views
            if (enhancedImage != null && imgDisplay.Source != enhancedImage)
            {
                imgDisplay.Source = enhancedImage;
                txtStatus.Text = "2D Viewer — showing enhanced view";
            }
            else if (originalImage != null)
            {
                imgDisplay.Source = originalImage;
                txtStatus.Text = "2D Viewer — showing original view";
            }

            // Reset zoom to fit
            ResetViewTransforms();
        }

        // =================================================================
        //  DELETE IMAGE from gallery
        // =================================================================
        private void btnDeleteImage_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentImagePath) || !File.Exists(currentImagePath))
            {
                MessageBox.Show("No image selected to delete.");
                return;
            }

            var result = MessageBox.Show(
                $"Delete this image?\n{Path.GetFileName(currentImagePath)}\n\nThis cannot be undone.",
                "Delete Image", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Release ALL locks on the file BEFORE deleting it
                    string pathToDelete = currentImagePath;
                    imgDisplay.Source = null;
                    originalImage = null;
                    ResetEnhancementState(); // new original → old preset/slider settings don't carry over
                    enhancedImage = null;
                    currentImagePath = "";
                    ClearAllMeasureVisuals();
                    db.DeleteMeasurementsForImage(pathToDelete);
                    galleryPanel.Children.Clear(); // releases thumbnail BitmapImage file locks

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    File.Delete(pathToDelete);
                    LoadGalleryThumbnails();
                    UpdateStorageInfo();
                    txtStatus.Text = "Image deleted";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not delete: {ex.Message}");
                }
            }
        }

        private void GalleryGridToggle_Click(object sender, RoutedEventArgs e)
        {
            _galleryGridMode = !_galleryGridMode;
            galleryRow.Height = new GridLength(_galleryGridMode ? 300 : 130);
            galleryScroll.HorizontalScrollBarVisibility = _galleryGridMode ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            galleryScroll.VerticalScrollBarVisibility = _galleryGridMode ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            btnGalleryGridToggle.Content = _galleryGridMode ? "☰ Strip View" : "⊞ Grid View";
        }

        private void btnPlaceholder_Click(object sender, RoutedEventArgs e)
        {
            txtStatus.Text = "This feature is coming in a future update";
        }

        // =================================================================
        //  GALLERY — Load thumbnails from DentalX_Captures
        // =================================================================
        private void LoadGalleryThumbnails()
        {
            galleryPanel.Children.Clear();
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
            if (!Directory.Exists(dir)) return;

            // Modality filter — captures are tagged via filename prefix (e.g. "panoramic_...png").
            // Legacy/untagged files (e.g. "capture_...png") are treated as Intraoral.
            string[] knownPrefixes = { "intraoral_", "panoramic_", "cephalometric_", "cbct_" };
            var files = Directory.GetFiles(dir, "*.png")
                .Where(f =>
                {
                    if (currentModality == "All") return true;
                    string name = Path.GetFileName(f).ToLower();
                    if (name.StartsWith(currentModality.ToLower() + "_")) return true;
                    return currentModality == "Intraoral" && !knownPrefixes.Any(p => name.StartsWith(p));
                })
                .OrderByDescending(f => File.GetCreationTime(f))
                .Take(20);

            foreach (var file in files)
            {
                var isSelected = currentImagePath == file;
                var border = new Border
                {
                    Width = 110,
                    Height = 95,
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x22, 0x36)),
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = isSelected
                        ? new SolidColorBrush(Color.FromRgb(0x2F, 0xD9, 0xC4))
                        : new SolidColorBrush(Color.FromRgb(0x1E, 0x33, 0x50)),
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    Cursor = Cursors.Hand,
                    Tag = file
                };

                var stack = new StackPanel();
                var thumb = new Image
                {
                    Height = 65,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(3, 3, 3, 1)
                };

                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(file);
                    bi.DecodePixelHeight = 65;
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    thumb.Source = bi;
                }
                catch { continue; }

                var info = new FileInfo(file);
                var label = new TextBlock
                {
                    Text = $"{info.CreationTime:dd MMM yyyy HH:mm}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x96, 0xA5)),
                    FontSize = 9,
                    Margin = new Thickness(6, 0, 0, 0)
                };

                stack.Children.Add(thumb);
                stack.Children.Add(label);
                border.Child = stack;

                border.MouseLeftButtonDown += (s, ev) =>
                {
                    var path = (s as Border)?.Tag as string ?? "";
                    if (File.Exists(path))
                    {
                        var img = LoadBitmapUnlocked(path);
                        imgDisplay.Source = img;
                        originalImage = img;
                        ResetEnhancementState(); // new original → old preset/slider settings don't carry over
                        enhancedImage = null;
                        currentImagePath = path;
                        ResetViewTransforms();
                        LoadMeasurementsForCurrentImage();
                        LoadGalleryThumbnails();
                        txtStatus.Text = "Loaded: " + Path.GetFileName(path);
                    }
                };

                galleryPanel.Children.Add(border);
            }
        }

        // =================================================================
        //  STORAGE INFO
        // =================================================================
        private void UpdateStorageInfo()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    long totalBytes = files.Sum(f => new FileInfo(f).Length);
                    double mb = totalBytes / (1024.0 * 1024.0);
                    txtStorage.Text = $"Storage: {files.Length} images ({mb:F1} MB)";
                }
            }
            catch { }
        }

        // =================================================================
        //  HELPERS
        // =================================================================
        private bool EnsureImage()
        {
            if (originalImage == null && imgDisplay.Source is BitmapSource src)
                originalImage = src;

            if (originalImage == null)
            {
                MessageBox.Show("Load or capture an image first.");
                return false;
            }
            return true;
        }

        private void SaveBitmapSource(BitmapSource source, string path)
        {
            BitmapEncoder encoder;
            if (path.EndsWith(".tif"))
                encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
            else if (path.EndsWith(".jpg"))
            {
                encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                if (source.Format == PixelFormats.Gray16) // JPEG is 8-bit only
                    source = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            }
            else
                encoder = new PngBitmapEncoder(); // keeps 16-bit grey for captures

            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }

        // Loads an image FULLY into memory so the file is NOT left locked on disk.
        // Without OnLoad, WPF keeps the file handle open and File.Delete later throws
        // "the process cannot access the file because it is being used by another process".
        private static BitmapImage LoadBitmapUnlocked(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            // PreservePixelFormat: without it WPF decodes 16-bit grey captures as 8-bit Bgr32,
            // silently throwing away the sensor's extra grey levels before any enhancement.
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.PreservePixelFormat;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // =================================================================
        //  NEW PATIENT (opens the proper modal — same one the Patients page uses)
        // =================================================================
        private void NewPatientQuick_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewPatientWindow { Owner = System.Windows.Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                LoadPatientList();
                _selectedPatientId = dlg.NewPatientId;
                var p = db.GetPatientById(dlg.NewPatientId);
                txtAttachedPatient.Text = p != null ? $"Selected: {p.Name} (P-{p.Id:D5})" : "";
                txtStatus.Text = p != null ? $"New patient '{p.Name}' created and selected" : "New patient created";
            }
        }

        // =================================================================
        //  SAVE ENHANCED / EXPORT
        // =================================================================
        private void btnSaveEnhanced_Click(object sender, RoutedEventArgs e)
        {
            if (imgDisplay.Source == null) { MessageBox.Show("No image to export."); return; }

            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|TIFF|*.tif|JPEG|*.jpg",
                FileName = $"enhanced_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dlg.ShowDialog() == true)
            {
                SaveBitmapSource((BitmapSource)imgDisplay.Source, dlg.FileName);
                txtStatus.Text = $"Exported: {Path.GetFileName(dlg.FileName)}";
            }
        }

        // =================================================================
        //  SEARCH PATIENTS
        // =================================================================
        private void txtSearchPatient_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = txtSearchPatient.Text.Trim().ToLower();
            lstPatients.Items.Clear();
            foreach (var p in db.GetAllPatients())
            {
                if (string.IsNullOrEmpty(query) ||
                    p.Name.ToLower().Contains(query) ||
                    p.Phone.ToLower().Contains(query))
                {
                    lstPatients.Items.Add($"{p.Id} | {p.Name} | {p.Age} | {p.Date}");
                }
            }
        }

        private void LoadPatientList()
        {
            lstPatients.Items.Clear();
            foreach (var p in db.GetAllPatients())
                lstPatients.Items.Add($"{p.Id} | {p.Name} | {p.Age} | {p.Date}");
        }

        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff",
                Title = "Select X-Ray Image"
            };

            if (dlg.ShowDialog() == true)
            {
                currentImagePath = dlg.FileName;
                BitmapSource img = LoadBitmapUnlocked(currentImagePath);

                // A raw sensor file (raw_*.tif, linear dark-subtracted 16-bit) is converted exactly
                // like a fresh capture and saved into the captures folder as a normal PNG — so old
                // raws can be re-processed, and the raw file itself is never touched.
                if (img.Format == PixelFormats.Gray16 &&
                    Path.GetFileName(currentImagePath).StartsWith("raw_", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = new ushort[img.PixelWidth * img.PixelHeight];
                    img.CopyPixels(raw, img.PixelWidth * 2, 0);
                    img = ImageEnhancer.RawToDisplayBitmap(raw, img.PixelWidth, img.PixelHeight, out _);

                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
                    string stamp = Path.GetFileNameWithoutExtension(currentImagePath).Substring(4); // "20261014_160846"
                    string modPrefix = currentModality == "All" ? "intraoral" : currentModality.ToLower();
                    currentImagePath = PatientFolders.UniqueDestination(dir, $"{modPrefix}_{stamp}.png");
                    SaveBitmapSource(img, currentImagePath);
                }

                imgDisplay.Source = img;
                originalImage = img;
                ResetEnhancementState(); // new original → old preset/slider settings don't carry over
                enhancedImage = null;
                ResetViewTransforms();
                LoadMeasurementsForCurrentImage();
                LoadGalleryThumbnails();
                txtStatus.Text = "Loaded: " + Path.GetFileName(currentImagePath);
            }
        }

        // Attaches the CURRENT image to an EXISTING, selected patient — this replaces the
        // old free-form "Save Patient" button, which always inserted a new patient row
        // (even for a name that already existed), silently creating duplicates.
        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientId == null)
            {
                MessageBox.Show("Select a patient from the list first (or create one with ＋ New Patient).");
                return;
            }
            if (string.IsNullOrEmpty(currentImagePath))
            {
                MessageBox.Show("Capture or load an image first.");
                return;
            }

            var patient = db.GetPatientById(_selectedPatientId.Value);
            if (patient == null)
            {
                MessageBox.Show("That patient no longer exists — refresh the list.");
                return;
            }

            string modality = currentModality == "All" ? "Intraoral" : currentModality;
            string desktopFolder = PatientFolders.GetDesktopPatientFolder(patient.Id, patient.Name);
            string appDataFolder = PatientFolders.GetAppDataPatientFolder(patient.Id, patient.Name);
            bool showingEnhanced = enhancedImage != null && imgDisplay.Source == enhancedImage
                                   && !ReferenceEquals(enhancedImage, _lastAttachedEnhanced);
            var saved = new List<string>();

            // 1) ORIGINAL — the file on disk at currentImagePath is the raw capture and is NEVER
            // written to here. Attaching is the moment the app first learns which patient an image
            // belongs to, so it's brought into that patient's folder once (plus an app-data shadow
            // copy). If it's already there (attached earlier), it's left untouched — this is what
            // stops a later "enhance → attach" from overwriting the original.
            if (!PatientFolders.IsInsideFolder(currentImagePath, desktopFolder))
            {
                string attachedPath = currentImagePath;
                try
                {
                    if (File.Exists(currentImagePath))
                    {
                        string fileName = Path.GetFileName(currentImagePath);
                        string desktopDest = PatientFolders.UniqueDestination(desktopFolder, fileName);

                        // Captures in the flat staging folder are moved; a file loaded from anywhere
                        // else (USB stick, another folder) is copied, so the user's source isn't removed.
                        string stagingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");
                        bool isStaged = string.Equals(Path.GetFullPath(Path.GetDirectoryName(currentImagePath)!).TrimEnd('\\'),
                                                      Path.GetFullPath(stagingDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                        if (isStaged) File.Move(currentImagePath, desktopDest);
                        else File.Copy(currentImagePath, desktopDest, overwrite: false);

                        attachedPath = desktopDest;
                        currentImagePath = desktopDest;

                        File.Copy(desktopDest, PatientFolders.UniqueDestination(appDataFolder, fileName), overwrite: false);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Attached, but couldn't move the file into the patient folder:\n{ex.Message}");
                }

                db.AddExamination(patient.Id, attachedPath, modality, "Attached from Imaging");
                saved.Add("original");
            }

            // 2) ENHANCED — sliders/presets only change the on-screen preview, so if that's what's
            // displayed, save it as a SEPARATE file in <patient folder>\Enhanced\<name>-enhanced.png,
            // recorded as its own examination, so both versions show in the patient's history.
            if (showingEnhanced)
            {
                try
                {
                    string enhancedName = PatientFolders.EnhancedFileName(Path.GetFileName(currentImagePath));
                    string enhancedDest = PatientFolders.UniqueDestination(PatientFolders.GetEnhancedFolder(desktopFolder), enhancedName);
                    SaveBitmapSource(enhancedImage!, enhancedDest);
                    File.Copy(enhancedDest,
                        PatientFolders.UniqueDestination(PatientFolders.GetEnhancedFolder(appDataFolder), Path.GetFileName(enhancedDest)),
                        overwrite: false);

                    db.AddExamination(patient.Id, enhancedDest, modality, $"Enhanced version of {Path.GetFileName(currentImagePath)}");
                    _lastAttachedEnhanced = enhancedImage;
                    saved.Add("enhanced");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't save the enhanced image:\n{ex.Message}");
                }
            }

            if (saved.Count == 0)
            {
                txtStatus.Text = $"Already saved to {patient.Name} — nothing new to attach";
                return;
            }

            txtAttachedPatient.Text = $"✓ Attached to {patient.Name} (P-{patient.Id:D5})";
            txtStatus.Text = $"Saved {string.Join(" + ", saved)} image to {patient.Name}";
            LoadGalleryThumbnails();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            string pathToDelete = currentImagePath;
            imgDisplay.Source = null;
            originalImage = null;
            enhancedImage = null;
            currentImagePath = "";
            isProcessing = false;
            ClearAllMeasureVisuals();
            ResetViewTransforms();
            btnResetSliders_Click(sender, e);
            txtStatus.Text = "Cleared";
        }

        private void btnPatients_Click(object sender, RoutedEventArgs e)
        {
            LoadPatientList();
            txtStatus.Text = "Patient list refreshed";
        }

        // Selecting a patient here just marks them as the attach target — it does NOT load
        // their old images into the viewer (that's what Patient Profile is for). Keeps this
        // panel's one job clear: "who does the CURRENT image belong to."
        private void lstPatients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstPatients.SelectedItem == null) { _selectedPatientId = null; return; }

            var selected = lstPatients.SelectedItem.ToString()!;
            var idStr = selected.Split('|')[0].Trim();

            if (int.TryParse(idStr, out int id))
            {
                var patient = db.GetPatientById(id);
                if (patient != null)
                {
                    _selectedPatientId = patient.Id;
                    txtAttachedPatient.Text = $"Selected: {patient.Name} (P-{patient.Id:D5})";
                }
            }
        }
    }
}
