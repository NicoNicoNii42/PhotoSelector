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
        /// Creates default statistics data by scanning folder contents.
        /// </summary>
        /// <returns>A new StatisticsData instance with values from folder scanning.</returns>
        private static StatisticsData CreateDefaultStatistics()
        {
            return ScanFolderStatistics();
        }
        
        /// <summary>
        /// Scans the destination folders to count existing files (non-recursive, same extension as sorting).
        /// </summary>
        /// <param name="goodFolder">Full path to good destination.</param>
        /// <param name="veryGoodFolder">Full path to very good destination.</param>
        /// <param name="sortedOutFolder">Full path to sorted-out destination.</param>
        /// <returns>Statistics data based on current folder contents.</returns>
        public static StatisticsData ScanFolderStatistics(
            string? goodFolder = null,
            string? veryGoodFolder = null,
            string? sortedOutFolder = null)
        {
            try
            {
                goodFolder ??= AppConfig.GetGoodFolderPath();
                veryGoodFolder ??= AppConfig.GetVeryGoodFolderPath();
                sortedOutFolder ??= AppConfig.GetSortedOutFolderPath();
                
                int goodCount = Directory.Exists(goodFolder) ? 
                    Directory.GetFiles(goodFolder, AppConfig.FileExtension).Length : 0;
                
                int veryGoodCount = Directory.Exists(veryGoodFolder) ? 
                    Directory.GetFiles(veryGoodFolder, AppConfig.FileExtension).Length : 0;
                
                int sortedOutCount = Directory.Exists(sortedOutFolder) ? 
                    Directory.GetFiles(sortedOutFolder, AppConfig.FileExtension).Length : 0;
                
                int totalPhotosProcessed = goodCount + veryGoodCount + sortedOutCount;
                
                return new StatisticsData
                {
                    TotalPhotosProcessed = totalPhotosProcessed,
                    GoodCount = goodCount,
                    VeryGoodCount = veryGoodCount,
                    SortedOutCount = sortedOutCount,
                    LastSessionDate = DateTime.Now,
                    SessionCount = 0
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning folder statistics: {ex.Message}");
                return new StatisticsData
                {
                    TotalPhotosProcessed = 0,
                    GoodCount = 0,
                    VeryGoodCount = 0,
                    SortedOutCount = 0,
                    LastSessionDate = DateTime.Now,
                    SessionCount = 0
                };
            }
        }
        
        /// <summary>
        /// Merges scanned folder statistics with persistent statistics.
        /// </summary>
        /// <param name="persistentData">The persistent statistics data.</param>
        /// <param name="goodFolder">Good destination path for this session.</param>
        /// <param name="veryGoodFolder">Very good destination path for this session.</param>
        /// <param name="sortedOutFolder">Sorted-out destination path for this session.</param>
        /// <returns>Updated statistics data with folder scanning results.</returns>
        public static StatisticsData MergeWithFolderScan(
            StatisticsData persistentData,
            string goodFolder,
            string veryGoodFolder,
            string sortedOutFolder)
        {
            var folderStats = ScanFolderStatistics(goodFolder, veryGoodFolder, sortedOutFolder);
            
            // Use the maximum values between persistent data and folder scan
            // This ensures we don't lose data if files were moved manually
            return new StatisticsData
            {
                TotalPhotosProcessed = Math.Max(persistentData.TotalPhotosProcessed, folderStats.TotalPhotosProcessed),
                GoodCount = Math.Max(persistentData.GoodCount, folderStats.GoodCount),
                VeryGoodCount = Math.Max(persistentData.VeryGoodCount, folderStats.VeryGoodCount),
                SortedOutCount = Math.Max(persistentData.SortedOutCount, folderStats.SortedOutCount),
                LastSessionDate = persistentData.LastSessionDate,
                SessionCount = persistentData.SessionCount
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
