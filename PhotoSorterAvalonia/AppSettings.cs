using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// User-editable paths: library root and relative destination folder names.
    /// </summary>
    public static class AppSettings
    {
        private const string FileName = "photo_sorter_app.json";
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoSorter",
            FileName);

        public sealed class Data
        {
            public string RootFolder { get; set; } = "";

            public string GoodFolderName { get; set; } = AppConfig.DefaultGoodFolderName;

            public string VeryGoodFolderName { get; set; } = AppConfig.DefaultVeryGoodFolderName;

            public string SortedOutFolderName { get; set; } = AppConfig.DefaultSortedOutFolderName;
        }

        private static Data _current = new();

        public static Data Current => _current;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<Data>(json);
                    if (loaded != null)
                    {
                        _current = Normalize(loaded);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading app settings: {ex.Message}");
            }

            string root = AppConfig.DefaultRootFolder;
            if (!Directory.Exists(root))
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (!string.IsNullOrWhiteSpace(pictures) && Directory.Exists(pictures))
                    root = pictures;
            }

            _current = Normalize(new Data
            {
                RootFolder = root,
                GoodFolderName = AppConfig.DefaultGoodFolderName,
                VeryGoodFolderName = AppConfig.DefaultVeryGoodFolderName,
                SortedOutFolderName = AppConfig.DefaultSortedOutFolderName,
            });
        }

        public static void Reload() => Load();

        public static void Save(Data data)
        {
            try
            {
                _current = Normalize(data);
                string directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving app settings: {ex.Message}");
            }
        }

        private static Data Normalize(Data d)
        {
            return new Data
            {
                RootFolder = (d.RootFolder ?? "").Trim(),
                GoodFolderName = (d.GoodFolderName ?? "").Trim(),
                VeryGoodFolderName = (d.VeryGoodFolderName ?? "").Trim(),
                SortedOutFolderName = (d.SortedOutFolderName ?? "").Trim(),
            };
        }

        /// <summary>Returns null if valid; otherwise an error message.</summary>
        /// <param name="requireRootExists">When true (e.g. saving from Settings), the root must exist on disk.</param>
        public static string? Validate(Data d, bool requireRootExists = true)
        {
            d = Normalize(d);
            if (string.IsNullOrWhiteSpace(d.RootFolder))
                return "Library root folder cannot be empty.";

            if (requireRootExists && !Directory.Exists(d.RootFolder))
                return "Library root folder does not exist or is not reachable.";

            string? e = ValidateRelativeFolderName(d.GoodFolderName, "Good folder name");
            if (e != null) return e;
            e = ValidateRelativeFolderName(d.VeryGoodFolderName, "Very good folder name");
            if (e != null) return e;
            e = ValidateRelativeFolderName(d.SortedOutFolderName, "Sorted out folder name");
            if (e != null) return e;

            return null;
        }

        private static string? ValidateRelativeFolderName(string relative, string label)
        {
            if (string.IsNullOrWhiteSpace(relative))
                return $"{label} cannot be empty.";

            if (Path.IsPathRooted(relative))
                return $"{label} must be a relative path (no drive or root).";

            var segments = relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return $"{label} cannot be empty.";

            if (segments.Any(s => s == "." || s == ".."))
                return $"{label} cannot use '.' or '..' segments.";

            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (string seg in segments)
            {
                if (seg.IndexOfAny(invalid) >= 0)
                    return $"{label} contains invalid characters.";
            }

            return null;
        }
    }
}
