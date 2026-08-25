using Microsoft.Win32;
using System;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DentalX
{
    public partial class BackupRestorePage : UserControl
    {
        private readonly DatabaseHelper db = new DatabaseHelper();
        private readonly string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX.db");
        private readonly string capturesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DentalX_Captures");

        public BackupRestorePage()
        {
            InitializeComponent();
            Loaded += (s, e) => RefreshLastBackup();
        }

        private void RefreshLastBackup()
        {
            var last = db.GetSetting("LastBackupDate", "");
            txtLastBackup.Text = string.IsNullOrEmpty(last) ? "No backup has been made yet." : $"Last backup: {last}";
        }

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "DentalX Backup|*.zip",
                FileName = $"DentalX_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);

                using (var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create))
                {
                    if (File.Exists(dbPath))
                        zip.CreateEntryFromFile(dbPath, "DentalX.db");

                    if (Directory.Exists(capturesDir))
                    {
                        foreach (var file in Directory.GetFiles(capturesDir, "*", SearchOption.AllDirectories))
                        {
                            string rel = "captures/" + Path.GetRelativePath(capturesDir, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, rel);
                        }
                    }
                }

                db.SetSetting("LastBackupDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                RefreshLastBackup();
                ShowStatus($"Backup saved to {dlg.FileName}", false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Backup failed: {ex.Message}", true);
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "DentalX Backup|*.zip" };
            if (dlg.ShowDialog() != true) return;

            var confirm = MessageBox.Show(
                "This replaces your CURRENT patient database and captured images with the contents of this backup.\n\n" +
                "Anything created after the backup was made will be lost. Continue?",
                "Confirm Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using var zip = ZipFile.OpenRead(dlg.FileName);

                var dbEntry = zip.GetEntry("DentalX.db");
                if (dbEntry != null)
                    dbEntry.ExtractToFile(dbPath, overwrite: true);

                Directory.CreateDirectory(capturesDir);
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("captures/") || entry.FullName.EndsWith("/")) continue;
                    string relPath = entry.FullName.Substring("captures/".Length);
                    string destPath = Path.Combine(capturesDir, relPath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                ShowStatus("Restore complete. Restart DentalX to see the restored data.", false);
            }
            catch (Exception ex)
            {
                ShowStatus($"Restore failed: {ex.Message}", true);
            }
        }

        private void ShowStatus(string msg, bool isError)
        {
            txtStatus.Text = msg;
            txtStatus.Foreground = isError ? (Brush)FindResource("BrDanger") : (Brush)FindResource("BrSuccess");
            txtStatus.Visibility = Visibility.Visible;
        }
    }
}
