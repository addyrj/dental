using System;
using System.IO;

namespace DentalX
{
    // Builds the per-patient folder used for organizing attached X-ray images — both the
    // user-visible Desktop copy and a redundant shadow copy. Folder names are ID-prefixed so
    // they stay unique and stable even if two patients share a name or a name is corrected
    // later; the name suffix just makes the folder human-browsable in Explorer.
    public static class PatientFolders
    {
        public static string SanitizeName(string name)
        {
            string safe = (name ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            safe = string.Join("_", safe.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            return string.IsNullOrEmpty(safe) ? "Unnamed" : safe;
        }

        public static string FolderName(int patientId, string name) => $"P{patientId:D5}_{SanitizeName(name)}";

        public static string DesktopRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures", "Patients");

        // A redundant shadow copy of every attached image, kept in the machine's shared app-data
        // area rather than beside the program binaries — writable without admin rights and never
        // wiped by a reinstall/update, unlike the install folder.
        public static string AppDataRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DentalX", "Patients");

        public static string GetDesktopPatientFolder(int patientId, string name)
        {
            string folder = Path.Combine(DesktopRoot, FolderName(patientId, name));
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetAppDataPatientFolder(int patientId, string name)
        {
            string folder = Path.Combine(AppDataRoot, FolderName(patientId, name));
            Directory.CreateDirectory(folder);
            return folder;
        }

        // Enhanced/processed versions live in a subfolder of the patient folder, so the original
        // capture (the diagnostic record) is never overwritten and stays easy to tell apart.
        public const string EnhancedSubfolder = "Enhanced";
        public const string EnhancedSuffix = "-enhanced";

        public static string GetEnhancedFolder(string patientFolder)
        {
            string folder = Path.Combine(patientFolder, EnhancedSubfolder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        // "intraoral_20261016_101500.png" -> "intraoral_20261016_101500-enhanced.png". Strips an
        // existing suffix first so re-enhancing an enhanced copy doesn't stack "-enhanced-enhanced".
        public static string EnhancedFileName(string originalFileName)
        {
            string name = Path.GetFileNameWithoutExtension(originalFileName);
            if (name.EndsWith(EnhancedSuffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - EnhancedSuffix.Length);
            return name + EnhancedSuffix + ".png";
        }

        public static bool IsInsideFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        // Returns a destination path inside 'folder' for 'fileName', appending a numeric suffix
        // if a file with that name already exists there — so re-attaching a same-named capture
        // never silently overwrites an earlier image.
        public static string UniqueDestination(string folder, string fileName)
        {
            string dest = Path.Combine(folder, fileName);
            if (!File.Exists(dest)) return dest;

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int i = 2;
            while (File.Exists(dest))
            {
                dest = Path.Combine(folder, $"{name}_{i}{ext}");
                i++;
            }
            return dest;
        }
    }
}
