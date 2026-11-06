using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DentalX
{
    // Installs the Hamamatsu sensor's Windows USB driver automatically, so a doctor never has
    // to open PowerShell or Device Manager themselves. Safe to call on every launch: it first
    // checks whether the driver is already staged (fast, no admin needed) and only triggers the
    // one-time elevated install if it genuinely isn't there yet.
    public static class DriverInstaller
    {
        private const string DriverInfName = "IntraOralXrayImageSensor.inf";
        private const string DriverMarker = "intraoralxrayimagesensor";

        public static void EnsureInstalled()
        {
            try
            {
                string driverFolder = Path.Combine(AppContext.BaseDirectory, "Driver");
                string infPath = Path.Combine(driverFolder, DriverInfName);
                if (!File.Exists(infPath)) return; // no driver package bundled with this build

                if (IsDriverStaged()) return; // already installed on this PC — nothing to do

                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = $"/add-driver \"{infPath}\" /install",
                    UseShellExecute = true,
                    Verb = "runas", // triggers the one-time UAC prompt required to install any driver
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(60000);

                if (!IsDriverStaged())
                {
                    ShowFailurePopup(
                        "The sensor driver installation didn't complete (it may have been cancelled at the " +
                        "Windows permission prompt).\n\nYou can install it manually: open the 'Driver' folder " +
                        "next to DentalX.exe, right-click IntraOralXrayImageSensor.inf → Install.");
                }
            }
            catch (Exception ex)
            {
                // Never let a driver-install hiccup stop the app from starting.
                ShowFailurePopup(
                    $"Couldn't check or install the sensor driver automatically ({ex.Message}).\n\n" +
                    "Real X-ray capture may not work until it's installed manually — see the 'Driver' folder " +
                    "next to DentalX.exe.");
            }
        }

        // Read-only check — does not need admin rights, safe to run on every launch.
        private static bool IsDriverStaged()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = "/enum-drivers",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi)!;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(10000);
                return output.IndexOf(DriverMarker, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false; // if we genuinely can't tell, fall through and attempt the install
            }
        }

        private static void ShowFailurePopup(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(message, "Sensor Driver Not Installed", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }
}
