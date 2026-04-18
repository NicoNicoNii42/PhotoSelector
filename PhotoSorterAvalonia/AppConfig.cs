using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhotoSorterAvalonia
{
    /// <summary>
    /// Configuration settings for the Photo Sorter application.
    /// This file contains all configurable constants used throughout the application.
    /// </summary>
    public static class AppConfig
    {
        // ============================================
        // Folder Configuration
        // ============================================
        
        /// <summary>
        /// Source folder containing photos to sort.
        /// Default: "/Users/niconiconii/Library/Mobile Documents/com~apple~CloudDocs/DCIM/100LEICA"
        /// </summary>
        public const string SourceFolder = "/Users/niconiconii/Library/Mobile Documents/com~apple~CloudDocs/DCIM/100LEICA";
        
        /// <summary>
        /// File extension pattern for photos to sort.
        /// Default: "*.DNG"
        /// </summary>
        public const string FileExtension = "*.DNG";
        
        /// <summary>
        /// Relative path (from the working folder) for "good" moves. May include subfolders, e.g. "good".
        /// </summary>
        public const string GoodFolderName = "good";
        
        /// <summary>
        /// Relative path (from the working folder) for "very good" moves, e.g. "verygood" or "round2/verygood".
        /// </summary>
        public const string VeryGoodFolderName = "verygood";
        
        /// <summary>
        /// Relative path (from the working folder) for "sorted out" moves, e.g. "sortedout" or "verygood/sortedout".
        /// </summary>
        public const string SortedOutFolderName = "sortedout";
        
        // ============================================
        // Zoom and Rotation Configuration
        // ============================================
        
        /// <summary>
        /// Minimum zoom scale (20% of original size).
        /// Default: 0.2
        /// </summary>
        public const double MinZoomScale = 0.2;
        
        /// <summary>
        /// Maximum zoom scale (500% of original size).
        /// Default: 5.0
        /// </summary>
        public const double MaxZoomScale = 5.0;
        
        /// <summary>
        /// Zoom factor for each zoom in/out step.
        /// Default: 1.1 (10% zoom per step)
        /// </summary>
        public const double ZoomFactor = 1.05;
        
        /// <summary>
        /// Trackpad/wheel zoom maps exponent = DeltaY / this value. Smaller = faster zoom
        /// (typical wheel notch is ~120 delta units).
        /// </summary>
        public const double WheelScrollZoomNotchDivisor = 1.0;
        
        /// <summary>
        /// Scales drag-to-pan (1 = pointer movement matches image motion in the photo area at any zoom/rotation).
        /// Increase (e.g. 1.5–2.5) if panning still feels slow.
        /// </summary>
        public const double PointerPanSpeedMultiplier = 1.0;
        
        /// <summary>
        /// Rotation step in degrees for manual rotation.
        /// Default: 90.0 (90° per rotation step)
        /// </summary>
        public const double RotationStep = 90.0;
        
        /// <summary>
        /// Default zoom scale when loading a new photo.
        /// Default: 1.0 (100% - original size)
        /// </summary>
        public const double DefaultZoomScale = 1.0;
        
        /// <summary>
        /// Default rotation angle when loading a new photo.
        /// Default: 0.0 (0° - no rotation)
        /// </summary>
        public const double DefaultRotation = 0.0;
        
        // ============================================
        // Cache Configuration
        // ============================================
        
        /// <summary>
        /// Maximum decode width for the fast first-pass preview while the full-resolution image loads.
        /// Smaller values decode faster; larger values look sharper during the handoff.
        /// </summary>
        public const int PreviewDecodeMaxWidth = 1920;
        
        /// <summary>
        /// Maximum number of full-resolution decoded images in RAM (LRU). Keep small; previews cover ahead/behind navigation.
        /// </summary>
        public const int MaxFullImageCacheSize = 8;
        
        /// <summary>
        /// Maximum number of downscaled preview bitmaps in RAM (LRU). Preload fills this ring around the current index.
        /// </summary>
        public const int MaxPreviewCacheSize = 28;
        
        /// <summary>
        /// How far ahead and behind the current photo to prefetch preview bitmaps into <see cref="MaxPreviewCacheSize"/>.
        /// </summary>
        public const int PreviewPreloadRange = 15;
        
        // ============================================
        // UI Configuration
        // ============================================
        
        /// <summary>
        /// Timeout in milliseconds for EXIF tool calls.
        /// Default: 2000 (2 seconds)
        /// </summary>
        public const int ExifToolTimeoutMs = 2000;
        
        // ============================================
        // Helper Methods
        // ============================================
        
        /// <summary>
        /// Combines a base directory with a relative path that may contain '/' or '\' segments.
        /// </summary>
        public static string CombineUnderWorkingFolder(string workingFolder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return workingFolder;
            var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Aggregate(workingFolder, Path.Combine);
        }
        
        /// <summary>Destination folder for "good" under <paramref name="workingFolder"/>.</summary>
        public static string GetGoodFolderPath(string workingFolder) =>
            CombineUnderWorkingFolder(workingFolder, GoodFolderName);
        
        /// <summary>Destination folder for "very good" under <paramref name="workingFolder"/>.</summary>
        public static string GetVeryGoodFolderPath(string workingFolder) =>
            CombineUnderWorkingFolder(workingFolder, VeryGoodFolderName);
        
        /// <summary>Destination folder for "sorted out" under <paramref name="workingFolder"/>.</summary>
        public static string GetSortedOutFolderPath(string workingFolder) =>
            CombineUnderWorkingFolder(workingFolder, SortedOutFolderName);
        
        /// <summary>Default destinations when no working folder override exists (uses <see cref="SourceFolder"/>).</summary>
        public static string GetGoodFolderPath() => GetGoodFolderPath(SourceFolder);
        
        public static string GetVeryGoodFolderPath() => GetVeryGoodFolderPath(SourceFolder);
        
        public static string GetSortedOutFolderPath() => GetSortedOutFolderPath(SourceFolder);
        
        /// <summary>
        /// Working-folder presets anchored at <see cref="SourceFolder"/>: the root plus each sort destination under that root.
        /// </summary>
        public static List<(string Label, string Path)> GetWorkingFolderPresetChoices()
        {
            string root;
            try
            {
                root = Path.GetFullPath(SourceFolder);
            }
            catch
            {
                root = SourceFolder;
            }
            
            return new List<(string Label, string Path)>
            {
                ("Root", root),
                ("Good", GetGoodFolderPath(root)),
                ("Very good", GetVeryGoodFolderPath(root)),
                ("Sorted out", GetSortedOutFolderPath(root)),
            };
        }
        
        /// <summary>
        /// Validates that all configuration values are valid.
        /// Throws exceptions if configuration is invalid.
        /// </summary>
        public static void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceFolder))
                throw new InvalidOperationException("SourceFolder cannot be empty");
            
            if (string.IsNullOrWhiteSpace(FileExtension))
                throw new InvalidOperationException("FileExtension cannot be empty");
            
            if (MinZoomScale <= 0)
                throw new InvalidOperationException("MinZoomScale must be greater than 0");
            
            if (MaxZoomScale <= MinZoomScale)
                throw new InvalidOperationException("MaxZoomScale must be greater than MinZoomScale");
            
            if (ZoomFactor <= 1.0)
                throw new InvalidOperationException("ZoomFactor must be greater than 1.0");
            
            if (WheelScrollZoomNotchDivisor <= 0)
                throw new InvalidOperationException("WheelScrollZoomNotchDivisor must be greater than 0");
            
            if (PointerPanSpeedMultiplier <= 0)
                throw new InvalidOperationException("PointerPanSpeedMultiplier must be greater than 0");
            
            if (RotationStep <= 0 || RotationStep > 360)
                throw new InvalidOperationException("RotationStep must be between 0 and 360 degrees");
            
            if (MaxFullImageCacheSize <= 0)
                throw new InvalidOperationException("MaxFullImageCacheSize must be greater than 0");
            
            if (MaxPreviewCacheSize <= 0)
                throw new InvalidOperationException("MaxPreviewCacheSize must be greater than 0");
            
            if (PreviewPreloadRange < 0)
                throw new InvalidOperationException("PreviewPreloadRange cannot be negative");
            
            if (ExifToolTimeoutMs <= 0)
                throw new InvalidOperationException("ExifToolTimeoutMs must be greater than 0");
        }
    }
}