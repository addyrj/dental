using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DentalX
{
    // The Shell: persistent title bar + left nav, with a content area that swaps between
    // page UserControls. Every nav destination resolves to something real — either the
    // working Imaging page/Dashboard, or an honestly-labeled ComingSoonPage (never fake data).
    public partial class MainWindow : System.Windows.Window, IShellHost
    {
        private XRayViewerPage? _xrayPage;
        private Button[] _navButtons = System.Array.Empty<Button>();
        private TextBlock[] _navLabels = System.Array.Empty<TextBlock>();
        private bool _sensorConnected = false;
        private string _sensorDetails = "";
        private bool _sidebarExpanded = true;
        private const double SidebarExpandedWidth = 168, SidebarCollapsedWidth = 56;

        public bool IsSensorConnected => _sensorConnected;
        public string SensorDetails => _sensorDetails;

        public MainWindow()
        {
            InitializeComponent();

            _navButtons = new[]
            {
                navDashboard, navPatients, navAppointments, navImaging, navAI, navCompare,
                navTreatment, navReports, navDicom, navDevices, navBackup, navSettings
            };
            _navLabels = new[]
            {
                lblDashboard, lblPatients, lblAppointments, lblImaging, lblAI, lblCompare,
                lblTreatment, lblReports, lblDicom, lblDevices, lblBackup, lblSettings
            };

            txtUserName.Text = CurrentSession.DisplayName;
            txtUserRole.Text = CurrentSession.User?.Role ?? "Guest";
            txtAvatarInitials.Text = CurrentSession.Initials;

            NavigateTo("Dashboard");
        }

        // ═══ IShellHost — lets pages reach shell-level chrome/navigation without owning it ═══
        public void SetSensorIndicator(bool connected)
        {
            _sensorConnected = connected;
            var green = new SolidColorBrush(Color.FromRgb(0x25, 0xD3, 0x66)); // #25d366
            var red = new SolidColorBrush(Color.FromRgb(0xBB, 0x21, 0x24));   // #bb2124
            hdrSensorPill.Background = connected ? green : red;
            hdrSensorText.Text = connected ? "Connected" : "Disconnected";
        }

        public void SetSensorDetails(string details)
        {
            _sensorDetails = details;

            // Header shows just the serial (first "·"-separated segment); the full detail
            // string (type/firmware/resolution) stays available via SensorDetails for the
            // Devices page.
            if (string.IsNullOrEmpty(details))
            {
                hdrSensorDetail.Text = "";
                hdrSensorDetail.Visibility = Visibility.Collapsed;
            }
            else
            {
                string shortLabel = details.Split('·')[0].Trim();
                hdrSensorDetail.Text = $"· {shortLabel}";
                hdrSensorDetail.Visibility = Visibility.Visible;
            }
        }

        public void OpenPatientProfile(int patientId)
        {
            SelectNav(navPatients);
            SetContent(new PatientProfilePage(patientId) { Shell = this });
        }

        // Every page swap fades in rather than popping instantly — a small touch that makes
        // navigation feel considered rather than abrupt.
        private void SetContent(UserControl page)
        {
            contentHost.Content = page;
            contentHost.BeginAnimation(UIElement.OpacityProperty, null);
            contentHost.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            contentHost.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        public void OpenPatientsList()
        {
            SelectNav(navPatients);
            NavigateTo("Patients");
        }

        public void GoToImaging()
        {
            SelectNav(navImaging);
            NavigateTo("Imaging");
        }

        // ═══ SIDEBAR COLLAPSE/EXPAND ═══
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarExpanded = !_sidebarExpanded;
            sidebarColumn.Width = new GridLength(_sidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth);

            // NavBtn's 16px padding is sized for the expanded (168px) column — left uncorrected,
            // it crowds the icon out to a sliver against the 56px collapsed width. Re-center it.
            foreach (var btn in _navButtons)
                btn.Padding = _sidebarExpanded ? new Thickness(16, 0, 16, 0) : new Thickness(9, 0, 9, 0);

            // Labels/quick-actions fade instead of popping, timed to roughly match the collapse.
            foreach (var lbl in _navLabels)
            {
                if (_sidebarExpanded) lbl.Visibility = Visibility.Visible;
                var fade = new DoubleAnimation(_sidebarExpanded ? 0 : 1, _sidebarExpanded ? 1 : 0, TimeSpan.FromMilliseconds(150));
                if (!_sidebarExpanded) fade.Completed += (s, e2) => lbl.Visibility = Visibility.Collapsed;
                lbl.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            if (_sidebarExpanded) quickActionsPanel.Visibility = Visibility.Visible;
            var qaFade = new DoubleAnimation(_sidebarExpanded ? 0 : 1, _sidebarExpanded ? 1 : 0, TimeSpan.FromMilliseconds(150));
            if (!_sidebarExpanded) qaFade.Completed += (s, e2) => quickActionsPanel.Visibility = Visibility.Collapsed;
            quickActionsPanel.BeginAnimation(UIElement.OpacityProperty, qaFade);
        }

        // ═══ NAVIGATION ═══
        private XRayViewerPage GetXrayPage()
        {
            _xrayPage ??= new XRayViewerPage { Shell = this };
            return _xrayPage;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            SelectNav(btn);
            NavigateTo(btn.Tag as string ?? "Dashboard");
        }

        private void SelectNav(Button target)
        {
            foreach (var b in _navButtons)
                b.Style = (Style)FindResource(b == target ? "NavBtnActive" : "NavBtn");
        }

        private void NavigateTo(string dest)
        {
            switch (dest)
            {
                case "Dashboard":
                    SetContent(new DashboardPage { Shell = this });
                    break;

                case "Appointments":
                    SetContent(new AppointmentsPage { Shell = this });
                    break;

                case "Imaging":
                    SetContent(GetXrayPage());
                    break;

                case "Patients":
                    SetContent(new PatientsListPage { Shell = this });
                    break;

                case "AI":
                    SetContent(new AIAnalysisPage());
                    break;

                case "Compare":
                    SetContent(new CompareImagesPage());
                    break;

                case "Treatment":
                    SetContent(new TreatmentPlanPage { Shell = this });
                    break;

                case "Reports":
                    SetContent(new ReportsPage());
                    break;

                case "Dicom":
                    SetContent(new DicomPage());
                    break;

                case "Devices":
                    SetContent(new DeviceManagementPage { Shell = this });
                    break;

                case "Backup":
                    if (!CurrentSession.IsAdmin)
                    {
                        MessageBox.Show("Backup & Restore is restricted to Admin accounts.", "Access restricted",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        SetContent(new ComingSoonPage("Backup & Restore", "Restricted to Admin accounts.", "🔒"));
                        return;
                    }
                    SetContent(new BackupRestorePage());
                    break;

                case "Settings":
                    if (!CurrentSession.IsAdmin)
                    {
                        MessageBox.Show("Settings is restricted to Admin accounts.", "Access restricted",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        SetContent(new ComingSoonPage("Settings", "Restricted to Admin accounts.", "🔒"));
                        return;
                    }
                    SetContent(new SettingsPage());
                    break;

                default:
                    SetContent(new ComingSoonPage("Coming Soon", "This screen is being built."));
                    break;
            }
        }

        // ═══ QUICK ACTIONS ═══
        private void QuickNewPatient_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewPatientWindow { Owner = this };
            if (dlg.ShowDialog() == true)
                OpenPatientProfile(dlg.NewPatientId);
        }

        private void QuickPrint_Click(object sender, RoutedEventArgs e)
        {
            SelectNav(navImaging); NavigateTo("Imaging");
            GetXrayPage().TriggerPrint();
        }

        // ═══ HEADER ICONS ═══
        private void SettingsIcon_Click(object sender, MouseButtonEventArgs e)
        {
            SelectNav(navSettings);
            NavigateTo("Settings");
        }

        private void Avatar_Click(object sender, MouseButtonEventArgs e)
        {
            var result = MessageBox.Show($"Log out of DentalX ({CurrentSession.DisplayName})?", "Logout",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            _xrayPage?.Cleanup();
            CurrentSession.User = null;

            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            Close();
        }

        // ═══ QUICK PATIENT SEARCH (Ctrl+P) ═══
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var popup = new QuickPatientSearchWindow { Owner = this, Shell = this };
                popup.Show();
                e.Handled = true;
            }
        }

        // ═══ CLOSE ═══
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _xrayPage?.Cleanup();
        }
    }
}
