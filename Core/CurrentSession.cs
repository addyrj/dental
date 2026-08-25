using System;

namespace DentalX
{
    // Holds the logged-in user for the lifetime of the process. Set once by LoginWindow
    // on successful sign-in, cleared on logout.
    public static class CurrentSession
    {
        public static User? User { get; set; }

        public static bool IsAdmin => User?.Role == "Admin";
        public static bool IsDentistOrAdmin => User?.Role == "Dentist" || IsAdmin;

        public static string DisplayName =>
            !string.IsNullOrWhiteSpace(User?.FullName) ? User!.FullName : (User?.Username ?? "Guest");

        public static string Initials
        {
            get
            {
                var name = DisplayName.Trim();
                if (name.Length == 0) return "??";
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
                return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
            }
        }
    }

    // Lets a page that doesn't own the shell chrome (e.g. the header sensor pill) reach
    // back to the shell without a hard dependency on the MainWindow type.
    public interface IShellHost
    {
        void SetSensorIndicator(bool connected);
        void SetSensorDetails(string details);
        bool IsSensorConnected { get; }
        string SensorDetails { get; }
        void OpenPatientProfile(int patientId);
        void OpenPatientsList();
        void GoToImaging();
    }
}
