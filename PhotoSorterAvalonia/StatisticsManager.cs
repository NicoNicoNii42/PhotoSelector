using System;
using System.IO;
using System.Text.Json;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// Manages persistent statistics storage between application runs.
    /// </summary>
    public static class StatisticsManager
    {
        private const string StatisticsFileName = "photo_sorter_stats.json";
        private static readonly string StatisticsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoSorter",
            StatisticsFileName);

        /// <summary>
        /// Represents the statistics data structure.
        /// </summary>
        public class StatisticsData
        {
            public int TotalPhotosProcessed { get; set; }
            public int GoodCount { get; set; }
            public int VeryGoodCount { get; set; }
            public int SortedOutCount { get; set; }
            public DateTime LastSessionDate { get; set; }
            public int SessionCount { get; set; }
        }

        /// <summary>
        /// Loads statistics from persistent storage.
        /// </summary>
        /// <returns>The loaded statistics data, or default values if no data exists.</returns>
        public static StatisticsData LoadStatistics()
        {
            try
            {
                if (File.Exists(StatisticsFilePath))
                {
                    string json = File.ReadAllText(StatisticsFilePath);
                    return JsonSerializer.Deserialize<StatisticsData>(json) ?? CreateDefaultStatistics();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading statistics: {ex.Message}");
            }

            return CreateDefaultStatistics();
        }

        /// <summary>
        /// Saves statistics to persistent storage.
        /// </summary>
        /// <param name="data">The statistics data to save.</param>
        public static void SaveStatistics(StatisticsData data)
        {
            try
            {
                // Ensure the directory exists
                string directory = Path.GetDirectoryName(StatisticsFilePath)!;
                Directory.CreateDirectory(directory);

                // Update session information
                data.LastSessionDate = DateTime.Now;
                data.SessionCount++;

                // Save to file
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StatisticsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving statistics: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates default statistics data.
        /// </summary>
        /// <returns>A new StatisticsData instance with default values.</returns>
        private static StatisticsData CreateDefaultStatistics()
        {
            return new StatisticsData
            {
                TotalPhotosProcessed = 0,
                GoodCount = 0,
                VeryGoodCount = 0,
                SortedOutCount = 0,
                LastSessionDate = DateTime.MinValue,
                SessionCount = 0
            };
        }

        /// <summary>
        /// Merges current session statistics with persistent statistics.
        /// </summary>
        /// <param name="persistentData">The persistent statistics data.</param>
        /// <param name="currentGood">Current session good count.</param>
        /// <param name="currentVeryGood">Current session very good count.</param>
        /// <param name="currentSortedOut">Current session sorted out count.</param>
        /// <param name="currentTotal">Current session total photos processed.</param>
        /// <returns>Updated statistics data.</returns>
        public static StatisticsData MergeStatistics(
            StatisticsData persistentData,
            int currentGood,
            int currentVeryGood,
            int currentSortedOut,
            int currentTotal)
        {
            return new StatisticsData
            {
                TotalPhotosProcessed = persistentData.TotalPhotosProcessed + currentTotal,
                GoodCount = persistentData.GoodCount + currentGood,
                VeryGoodCount = persistentData.VeryGoodCount + currentVeryGood,
                SortedOutCount = persistentData.SortedOutCount + currentSortedOut,
                LastSessionDate = DateTime.Now,
                SessionCount = persistentData.SessionCount + 1
            };
        }

        /// <summary>
        /// Gets the statistics file path for debugging purposes.
        /// </summary>
        /// <returns>The full path to the statistics file.</returns>
        public static string GetStatisticsFilePath()
        {
            return StatisticsFilePath;
        }
    }
}