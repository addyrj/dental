using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace DentalX
{
    public class Patient
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Age { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Notes { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public string Date { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Title { get; set; } = "";
        public string DOB { get; set; } = "";
        public string Address { get; set; } = "";
        public string Country { get; set; } = "";
        public string PhotoPath { get; set; } = "";
    }

    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "Assistant"; // Admin | Dentist | Assistant
    }

    // One captured/attached image tied to a patient — the patient's exam history.
    public class Examination
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string ImagePath { get; set; } = "";
        public string Modality { get; set; } = "";
        public string Date { get; set; } = "";
        public string Notes { get; set; } = "";
        public int ToothNumber { get; set; } = 0; // 0 = not linked to a specific tooth
    }

    // Per-tooth condition for a patient's FDI-notation tooth chart.
    public class ToothCondition
    {
        public int PatientId { get; set; }
        public int ToothNumber { get; set; }
        public string Condition { get; set; } = "Healthy";
        public string Notes { get; set; } = "";
    }

    // One planned/recommended/completed treatment item for a patient (optionally a tooth).
    public class TreatmentItem
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = ""; // populated only by GetAllTreatmentItems()
        public string Tooth { get; set; } = "";
        public string Treatment { get; set; } = "";
        public string Status { get; set; } = "Planned";   // Planned | Recommended | Completed
        public string Priority { get; set; } = "Medium";  // Low | Medium | High
        public string Notes { get; set; } = "";
        public string CreatedDate { get; set; } = "";
    }

    // A saved on-image measurement (Distance / Tooth Length / Angle). Coordinates are stored in
    // the source bitmap's own pixel space, so they stay meaningful regardless of the viewer's
    // zoom/pan/rotation at the time they're redrawn.
    public class Measurement
    {
        public int Id { get; set; }
        public string ImagePath { get; set; } = "";
        public string Type { get; set; } = "Distance"; // Distance | ToothLength | Angle
        public int ToothNumber { get; set; } = 0;
        public double X1, Y1, X2, Y2, X3, Y3;
        public bool HasThirdPoint { get; set; } = false;
        public string ValueText { get; set; } = "";
        public string CreatedDate { get; set; } = "";
    }

    // A scheduled visit for a patient.
    public class Appointment
    {
        public int Id { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; } = ""; // populated by GetAllAppointments()
        public string AppointmentDate { get; set; } = ""; // yyyy-MM-dd
        public string AppointmentTime { get; set; } = ""; // HH:mm
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "Scheduled"; // Scheduled | Completed | Cancelled
    }

    public class DatabaseHelper
    {
        private string dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "DentalX.db"
        );

        private string ConnectionString => $"Data Source={dbPath}";

        // True for the one run where a default admin account had to be created
        // (empty Users table on first launch). LoginWindow surfaces this to the user.
        public bool DidSeedDefaultAdmin { get; private set; } = false;

        public DatabaseHelper()
        {
            CreateDatabase();
        }

        private void CreateDatabase()
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            Exec(conn, @"CREATE TABLE IF NOT EXISTS Patients (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Age TEXT,
                Phone TEXT,
                Notes TEXT,
                ImagePath TEXT,
                Date TEXT
            )");
            EnsureColumn(conn, "Patients", "Gender", "TEXT");
            EnsureColumn(conn, "Patients", "Title", "TEXT");
            EnsureColumn(conn, "Patients", "DOB", "TEXT");
            EnsureColumn(conn, "Patients", "Address", "TEXT");
            EnsureColumn(conn, "Patients", "Country", "TEXT");
            EnsureColumn(conn, "Patients", "PhotoPath", "TEXT");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                FullName TEXT,
                Role TEXT NOT NULL DEFAULT 'Assistant',
                CreatedDate TEXT
            )");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS Examinations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PatientId INTEGER NOT NULL,
                ImagePath TEXT,
                Modality TEXT,
                Date TEXT,
                Notes TEXT
            )");
            EnsureColumn(conn, "Examinations", "ToothNumber", "INTEGER DEFAULT 0");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS ToothConditions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PatientId INTEGER NOT NULL,
                ToothNumber INTEGER NOT NULL,
                Condition TEXT NOT NULL DEFAULT 'Healthy',
                Notes TEXT,
                UpdatedDate TEXT,
                UNIQUE(PatientId, ToothNumber)
            )");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS TreatmentPlans (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PatientId INTEGER NOT NULL,
                Tooth TEXT,
                Treatment TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Planned',
                Priority TEXT NOT NULL DEFAULT 'Medium',
                Notes TEXT,
                CreatedDate TEXT
            )");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT
            )");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS Measurements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ImagePath TEXT NOT NULL,
                Type TEXT NOT NULL,
                ToothNumber INTEGER DEFAULT 0,
                X1 REAL, Y1 REAL, X2 REAL, Y2 REAL, X3 REAL, Y3 REAL,
                HasThirdPoint INTEGER DEFAULT 0,
                ValueText TEXT,
                CreatedDate TEXT
            )");

            Exec(conn, @"CREATE TABLE IF NOT EXISTS Appointments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PatientId INTEGER NOT NULL,
                AppointmentDate TEXT NOT NULL,
                AppointmentTime TEXT NOT NULL,
                Reason TEXT,
                Status TEXT NOT NULL DEFAULT 'Scheduled',
                CreatedDate TEXT
            )");

            SeedDefaultAdminIfNeeded(conn);
            MigrateLegacyImagesToExaminations(conn);
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = new SqliteCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        // Adds a column if it doesn't already exist yet — safe to call every startup.
        private static void EnsureColumn(SqliteConnection conn, string table, string column, string type)
        {
            using var check = new SqliteCommand($"PRAGMA table_info({table})", conn);
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            reader.Close();
            Exec(conn, $"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }

        private void SeedDefaultAdminIfNeeded(SqliteConnection conn)
        {
            using var check = new SqliteCommand("SELECT COUNT(*) FROM Users", conn);
            long count = (long)check.ExecuteScalar()!;
            if (count == 0)
            {
                CreateUserInternal(conn, "admin", "admin123", "Dr. Therapist", "Admin");
                DidSeedDefaultAdmin = true;
            }
        }

        // One-time migration: patients created before the Examinations table existed only
        // had a single ImagePath. Turn that into their first examination record so history
        // isn't empty for existing patients.
        private void MigrateLegacyImagesToExaminations(SqliteConnection conn)
        {
            using var check = new SqliteCommand("SELECT COUNT(*) FROM Examinations", conn);
            long count = (long)check.ExecuteScalar()!;
            if (count > 0) return;

            var toInsert = new List<(int id, string path, string date)>();
            using (var patients = new SqliteCommand(
                "SELECT Id, ImagePath, Date FROM Patients WHERE ImagePath IS NOT NULL AND ImagePath != ''", conn))
            using (var reader = patients.ExecuteReader())
            {
                while (reader.Read())
                    toInsert.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
            }

            foreach (var (id, path, date) in toInsert)
            {
                using var ins = new SqliteCommand(
                    "INSERT INTO Examinations (PatientId, ImagePath, Modality, Date, Notes) VALUES (@p,@i,@m,@d,@n)", conn);
                ins.Parameters.AddWithValue("@p", id);
                ins.Parameters.AddWithValue("@i", path);
                ins.Parameters.AddWithValue("@m", "Intraoral");
                ins.Parameters.AddWithValue("@d", string.IsNullOrEmpty(date) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : date);
                ins.Parameters.AddWithValue("@n", "Migrated from legacy single-image record");
                ins.ExecuteNonQuery();
            }
        }

        // ═══ PASSWORD HASHING (PBKDF2 — built into .NET, no extra dependency) ═══
        private static (string hash, string salt) HashPassword(string password)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        private static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            byte[] saltBytes = Convert.FromBase64String(storedSalt);
            byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(hashBytes, Convert.FromBase64String(storedHash));
        }

        // ═══ USERS ═══
        public void CreateUser(string username, string password, string fullName, string role)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            CreateUserInternal(conn, username, password, fullName, role);
        }

        private void CreateUserInternal(SqliteConnection conn, string username, string password, string fullName, string role)
        {
            var (hash, salt) = HashPassword(password);
            string sql = @"INSERT INTO Users (Username, PasswordHash, PasswordSalt, FullName, Role, CreatedDate)
                            VALUES (@u, @h, @s, @f, @r, @d)";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@f", fullName ?? "");
            cmd.Parameters.AddWithValue("@r", role);
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public User? ValidateLogin(string username, string password)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            string sql = "SELECT Id, Username, PasswordHash, PasswordSalt, FullName, Role FROM Users WHERE Username = @u";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@u", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            string hash = reader.GetString(2);
            string salt = reader.GetString(3);
            if (!VerifyPassword(password, hash, salt)) return null;

            return new User
            {
                Id = reader.GetInt32(0),
                Username = reader.GetString(1),
                FullName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Role = reader.GetString(5)
            };
        }

        public List<User> GetAllUsers()
        {
            var users = new List<User>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Id, Username, FullName, Role FROM Users ORDER BY Id", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(new User
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    FullName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Role = reader.GetString(3)
                });
            }
            return users;
        }

        // Verifies oldPassword, then rewrites the stored hash. Returns false (no change made)
        // if oldPassword doesn't match.
        public bool ChangePassword(string username, string oldPassword, string newPassword)
        {
            if (ValidateLogin(username, oldPassword) == null) return false;

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            var (hash, salt) = HashPassword(newPassword);
            using var cmd = new SqliteCommand("UPDATE Users SET PasswordHash=@h, PasswordSalt=@s WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.ExecuteNonQuery();
            return true;
        }

        // ═══ PATIENTS ═══
        // Returns the new patient's Id.
        public int SavePatient(Patient patient)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            string sql = @"INSERT INTO Patients (Name, Age, Phone, Notes, ImagePath, Date, Gender, Title, DOB, Address, Country, PhotoPath)
                            VALUES (@Name, @Age, @Phone, @Notes, @ImagePath, @Date, @Gender, @Title, @DOB, @Address, @Country, @PhotoPath);
                            SELECT last_insert_rowid();";

            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Name", patient.Name);
            cmd.Parameters.AddWithValue("@Age", patient.Age ?? "");
            cmd.Parameters.AddWithValue("@Phone", patient.Phone ?? "");
            cmd.Parameters.AddWithValue("@Notes", patient.Notes ?? "");
            cmd.Parameters.AddWithValue("@ImagePath", patient.ImagePath ?? "");
            cmd.Parameters.AddWithValue("@Date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@Gender", patient.Gender ?? "");
            cmd.Parameters.AddWithValue("@Title", patient.Title ?? "");
            cmd.Parameters.AddWithValue("@DOB", patient.DOB ?? "");
            cmd.Parameters.AddWithValue("@Address", patient.Address ?? "");
            cmd.Parameters.AddWithValue("@Country", patient.Country ?? "");
            cmd.Parameters.AddWithValue("@PhotoPath", patient.PhotoPath ?? "");

            return (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdatePatient(Patient patient)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"UPDATE Patients SET Name=@Name, Age=@Age, Phone=@Phone, Notes=@Notes, Gender=@Gender,
                            Title=@Title, DOB=@DOB, Address=@Address, Country=@Country, PhotoPath=@PhotoPath WHERE Id=@Id";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Name", patient.Name);
            cmd.Parameters.AddWithValue("@Age", patient.Age ?? "");
            cmd.Parameters.AddWithValue("@Phone", patient.Phone ?? "");
            cmd.Parameters.AddWithValue("@Notes", patient.Notes ?? "");
            cmd.Parameters.AddWithValue("@Gender", patient.Gender ?? "");
            cmd.Parameters.AddWithValue("@Title", patient.Title ?? "");
            cmd.Parameters.AddWithValue("@DOB", patient.DOB ?? "");
            cmd.Parameters.AddWithValue("@Address", patient.Address ?? "");
            cmd.Parameters.AddWithValue("@Country", patient.Country ?? "");
            cmd.Parameters.AddWithValue("@PhotoPath", patient.PhotoPath ?? "");
            cmd.Parameters.AddWithValue("@Id", patient.Id);
            cmd.ExecuteNonQuery();
        }

        private static Patient ReadPatient(SqliteDataReader reader) => new Patient
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Age = reader.IsDBNull(2) ? "" : reader.GetString(2),
            Phone = reader.IsDBNull(3) ? "" : reader.GetString(3),
            Notes = reader.IsDBNull(4) ? "" : reader.GetString(4),
            ImagePath = reader.IsDBNull(5) ? "" : reader.GetString(5),
            Date = reader.IsDBNull(6) ? "" : reader.GetString(6),
            Gender = reader.IsDBNull(7) ? "" : reader.GetString(7),
            Title = reader.IsDBNull(8) ? "" : reader.GetString(8),
            DOB = reader.IsDBNull(9) ? "" : reader.GetString(9),
            Address = reader.IsDBNull(10) ? "" : reader.GetString(10),
            Country = reader.IsDBNull(11) ? "" : reader.GetString(11),
            PhotoPath = reader.IsDBNull(12) ? "" : reader.GetString(12)
        };

        private const string PatientColumns = "Id, Name, Age, Phone, Notes, ImagePath, Date, Gender, Title, DOB, Address, Country, PhotoPath";

        public Patient? GetPatientById(int id)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand($"SELECT {PatientColumns} FROM Patients WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadPatient(reader) : null;
        }

        public List<Patient> GetAllPatients()
        {
            var patients = new List<Patient>();

            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using var cmd = new SqliteCommand($"SELECT {PatientColumns} FROM Patients ORDER BY Id DESC", conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
                patients.Add(ReadPatient(reader));

            return patients;
        }

        public void DeletePatient(int id)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            using (var cmd = new SqliteCommand("DELETE FROM Examinations WHERE PatientId=@Id", conn))
            { cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand("DELETE FROM ToothConditions WHERE PatientId=@Id", conn))
            { cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand("DELETE FROM TreatmentPlans WHERE PatientId=@Id", conn))
            { cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand("DELETE FROM Appointments WHERE PatientId=@Id", conn))
            { cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand("DELETE FROM Patients WHERE Id=@Id", conn))
            { cmd.Parameters.AddWithValue("@Id", id); cmd.ExecuteNonQuery(); }
        }

        // ═══ EXAMINATIONS (per-patient capture history) ═══
        public int AddExamination(int patientId, string imagePath, string modality, string? notes)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO Examinations (PatientId, ImagePath, Modality, Date, Notes, ToothNumber)
                            VALUES (@p, @i, @m, @d, @n, 0);
                            SELECT last_insert_rowid();";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            cmd.Parameters.AddWithValue("@i", imagePath ?? "");
            cmd.Parameters.AddWithValue("@m", string.IsNullOrEmpty(modality) ? "Intraoral" : modality);
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@n", notes ?? "");
            return (int)(long)cmd.ExecuteScalar()!;
        }

        public List<Examination> GetExaminationsForPatient(int patientId)
        {
            var list = new List<Examination>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT Id, ImagePath, Modality, Date, Notes, ToothNumber FROM Examinations WHERE PatientId=@p ORDER BY Date DESC", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Examination
                {
                    Id = reader.GetInt32(0),
                    PatientId = patientId,
                    ImagePath = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Modality = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Date = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Notes = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ToothNumber = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
            return list;
        }

        public int GetExaminationCount(int patientId)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM Examinations WHERE PatientId=@p", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            return (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateExaminationNotes(int examId, string notes)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("UPDATE Examinations SET Notes=@n WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@n", notes ?? "");
            cmd.Parameters.AddWithValue("@id", examId);
            cmd.ExecuteNonQuery();
        }

        public void SetExaminationTooth(int examId, int toothNumber)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("UPDATE Examinations SET ToothNumber=@t WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@t", toothNumber);
            cmd.Parameters.AddWithValue("@id", examId);
            cmd.ExecuteNonQuery();
        }

        // ═══ TOOTH CHART (FDI notation, per patient) ═══
        public void SetToothCondition(int patientId, int toothNumber, string condition, string? notes)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO ToothConditions (PatientId, ToothNumber, Condition, Notes, UpdatedDate)
                            VALUES (@p, @t, @c, @n, @d)
                            ON CONFLICT(PatientId, ToothNumber) DO UPDATE SET
                                Condition = excluded.Condition,
                                Notes = excluded.Notes,
                                UpdatedDate = excluded.UpdatedDate";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            cmd.Parameters.AddWithValue("@t", toothNumber);
            cmd.Parameters.AddWithValue("@c", condition);
            cmd.Parameters.AddWithValue("@n", notes ?? "");
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public List<ToothCondition> GetToothConditions(int patientId)
        {
            var list = new List<ToothCondition>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT ToothNumber, Condition, Notes FROM ToothConditions WHERE PatientId=@p", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ToothCondition
                {
                    PatientId = patientId,
                    ToothNumber = reader.GetInt32(0),
                    Condition = reader.GetString(1),
                    Notes = reader.IsDBNull(2) ? "" : reader.GetString(2)
                });
            }
            return list;
        }

        // ═══ TREATMENT PLANS ═══
        public int AddTreatmentItem(int patientId, string tooth, string treatment, string priority, string status, string? notes)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO TreatmentPlans (PatientId, Tooth, Treatment, Status, Priority, Notes, CreatedDate)
                            VALUES (@p,@t,@tr,@s,@pr,@n,@d);
                            SELECT last_insert_rowid();";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            cmd.Parameters.AddWithValue("@t", tooth ?? "");
            cmd.Parameters.AddWithValue("@tr", treatment);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@pr", priority);
            cmd.Parameters.AddWithValue("@n", notes ?? "");
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return (int)(long)cmd.ExecuteScalar()!;
        }

        public List<TreatmentItem> GetTreatmentItemsForPatient(int patientId)
        {
            var list = new List<TreatmentItem>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT Id, Tooth, Treatment, Status, Priority, Notes, CreatedDate FROM TreatmentPlans WHERE PatientId=@p ORDER BY CreatedDate DESC", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TreatmentItem
                {
                    Id = reader.GetInt32(0),
                    PatientId = patientId,
                    Tooth = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Treatment = reader.GetString(2),
                    Status = reader.GetString(3),
                    Priority = reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    CreatedDate = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }
            return list;
        }

        public int GetTreatmentCount(int patientId)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT COUNT(*) FROM TreatmentPlans WHERE PatientId=@p", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            return (int)(long)cmd.ExecuteScalar()!;
        }

        // Every treatment item across every patient, newest first — for the master worklist page.
        public List<TreatmentItem> GetAllTreatmentItems()
        {
            var list = new List<TreatmentItem>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"SELECT t.Id, t.PatientId, p.Name, t.Tooth, t.Treatment, t.Status, t.Priority, t.CreatedDate
                            FROM TreatmentPlans t JOIN Patients p ON t.PatientId = p.Id
                            ORDER BY t.CreatedDate DESC";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TreatmentItem
                {
                    Id = reader.GetInt32(0),
                    PatientId = reader.GetInt32(1),
                    PatientName = reader.GetString(2),
                    Tooth = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Treatment = reader.GetString(4),
                    Status = reader.GetString(5),
                    Priority = reader.GetString(6),
                    CreatedDate = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }
            return list;
        }

        public void UpdateTreatmentStatus(int id, string status)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("UPDATE TreatmentPlans SET Status=@s WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteTreatmentItem(int id)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM TreatmentPlans WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ═══ APPOINTMENTS ═══
        public int AddAppointment(int patientId, string date, string time, string reason)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO Appointments (PatientId, AppointmentDate, AppointmentTime, Reason, Status, CreatedDate)
                            VALUES (@p, @d, @t, @r, 'Scheduled', @c);
                            SELECT last_insert_rowid();";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            cmd.Parameters.AddWithValue("@d", date);
            cmd.Parameters.AddWithValue("@t", time);
            cmd.Parameters.AddWithValue("@r", reason ?? "");
            cmd.Parameters.AddWithValue("@c", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return (int)(long)cmd.ExecuteScalar()!;
        }

        // Every appointment across every patient, soonest first — for the master schedule page.
        public List<Appointment> GetAllAppointments()
        {
            var list = new List<Appointment>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"SELECT a.Id, a.PatientId, p.Name, a.AppointmentDate, a.AppointmentTime, a.Reason, a.Status
                            FROM Appointments a JOIN Patients p ON a.PatientId = p.Id
                            ORDER BY a.AppointmentDate ASC, a.AppointmentTime ASC";
            using var cmd = new SqliteCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Appointment
                {
                    Id = reader.GetInt32(0),
                    PatientId = reader.GetInt32(1),
                    PatientName = reader.GetString(2),
                    AppointmentDate = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    AppointmentTime = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Reason = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Status = reader.GetString(6)
                });
            }
            return list;
        }

        public List<Appointment> GetAppointmentsForPatient(int patientId)
        {
            var list = new List<Appointment>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                "SELECT Id, AppointmentDate, AppointmentTime, Reason, Status FROM Appointments WHERE PatientId=@p ORDER BY AppointmentDate ASC, AppointmentTime ASC", conn);
            cmd.Parameters.AddWithValue("@p", patientId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Appointment
                {
                    Id = reader.GetInt32(0),
                    PatientId = patientId,
                    AppointmentDate = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    AppointmentTime = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Reason = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Status = reader.GetString(4)
                });
            }
            return list;
        }

        public void UpdateAppointmentStatus(int id, string status)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("UPDATE Appointments SET Status=@s WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAppointment(int id)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM Appointments WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ═══ MEASUREMENTS (Distance / Tooth Length / Angle overlays saved per image) ═══
        public int SaveMeasurement(Measurement m)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO Measurements (ImagePath, Type, ToothNumber, X1, Y1, X2, Y2, X3, Y3, HasThirdPoint, ValueText, CreatedDate)
                            VALUES (@ip,@ty,@tn,@x1,@y1,@x2,@y2,@x3,@y3,@h3,@v,@d);
                            SELECT last_insert_rowid();";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ip", m.ImagePath ?? "");
            cmd.Parameters.AddWithValue("@ty", m.Type);
            cmd.Parameters.AddWithValue("@tn", m.ToothNumber);
            cmd.Parameters.AddWithValue("@x1", m.X1);
            cmd.Parameters.AddWithValue("@y1", m.Y1);
            cmd.Parameters.AddWithValue("@x2", m.X2);
            cmd.Parameters.AddWithValue("@y2", m.Y2);
            cmd.Parameters.AddWithValue("@x3", m.X3);
            cmd.Parameters.AddWithValue("@y3", m.Y3);
            cmd.Parameters.AddWithValue("@h3", m.HasThirdPoint ? 1 : 0);
            cmd.Parameters.AddWithValue("@v", m.ValueText ?? "");
            cmd.Parameters.AddWithValue("@d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return (int)(long)cmd.ExecuteScalar()!;
        }

        public List<Measurement> GetMeasurementsForImage(string imagePath)
        {
            var list = new List<Measurement>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                @"SELECT Id, Type, ToothNumber, X1, Y1, X2, Y2, X3, Y3, HasThirdPoint, ValueText, CreatedDate
                  FROM Measurements WHERE ImagePath=@ip ORDER BY Id", conn);
            cmd.Parameters.AddWithValue("@ip", imagePath ?? "");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Measurement
                {
                    Id = reader.GetInt32(0),
                    ImagePath = imagePath,
                    Type = reader.GetString(1),
                    ToothNumber = reader.GetInt32(2),
                    X1 = reader.GetDouble(3),
                    Y1 = reader.GetDouble(4),
                    X2 = reader.GetDouble(5),
                    Y2 = reader.GetDouble(6),
                    X3 = reader.IsDBNull(7) ? 0 : reader.GetDouble(7),
                    Y3 = reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                    HasThirdPoint = reader.GetInt32(9) == 1,
                    ValueText = reader.IsDBNull(10) ? "" : reader.GetString(10),
                    CreatedDate = reader.IsDBNull(11) ? "" : reader.GetString(11)
                });
            }
            return list;
        }

        public void UpdateMeasurement(int id, double x1, double y1, double x2, double y2, double x3, double y3, string valueText)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand(
                "UPDATE Measurements SET X1=@x1,Y1=@y1,X2=@x2,Y2=@y2,X3=@x3,Y3=@y3,ValueText=@v WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@x1", x1);
            cmd.Parameters.AddWithValue("@y1", y1);
            cmd.Parameters.AddWithValue("@x2", x2);
            cmd.Parameters.AddWithValue("@y2", y2);
            cmd.Parameters.AddWithValue("@x3", x3);
            cmd.Parameters.AddWithValue("@y3", y3);
            cmd.Parameters.AddWithValue("@v", valueText ?? "");
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteMeasurement(int id)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM Measurements WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteMeasurementsForImage(string imagePath)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("DELETE FROM Measurements WHERE ImagePath=@ip", conn);
            cmd.Parameters.AddWithValue("@ip", imagePath ?? "");
            cmd.ExecuteNonQuery();
        }

        // ═══ APP SETTINGS (simple key/value store) ═══
        public string GetSetting(string key, string defaultValue = "")
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqliteCommand("SELECT Value FROM AppSettings WHERE Key=@k", conn);
            cmd.Parameters.AddWithValue("@k", key);
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? defaultValue : (string)result;
        }

        public void SetSetting(string key, string value)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            string sql = @"INSERT INTO AppSettings (Key, Value) VALUES (@k, @v)
                            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
            using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value ?? "");
            cmd.ExecuteNonQuery();
        }
    }
}
