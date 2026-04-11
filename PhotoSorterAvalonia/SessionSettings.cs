using System;
using System.IO;
using System.Text.Json;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// Persists per-machine session options (e.g. which folder is being sorted).
    /// </summary>
    public static class SessionSettings
    {
        private const string FileName = "photo_sorter_session.json";
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoSorter",
            FileName);

        public sealed class Data
        {
            /// <summary>Absolute path of the folder whose files (non-recursive) form the sort queue.</summary>
            public string? WorkingFolder { get; set; }
        }

        public static Data Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<Data>(json) ?? new Data();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading session settings: {ex.Message}");
            }

            return new Data();
        }

        public static void Save(Data data)
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving session settings: {ex.Message}");
            }
        }
    }
}
