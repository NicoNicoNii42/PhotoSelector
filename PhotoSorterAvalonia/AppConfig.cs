using System;

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
        /// Name of the "good" photos subfolder.
        /// Default: "good"
        /// </summary>
        public const string GoodFolderName = "good";
        
        /// <summary>
        /// Name of the "very good" photos subfolder.
        /// Default: "verygood"
        /// </summary>
        public const string VeryGoodFolderName = "verygood";
        
        /// <summary>
        /// Name of the "sorted out" photos subfolder.
        /// Default: "sortedout"
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
        public const double ZoomFactor = 1.1;
        
        /// <summary>
        /// Trackpad/wheel zoom maps exponent = DeltaY / this value. Smaller = faster zoom
        /// (typical wheel notch is ~120 delta units).
        /// </summary>
        public const double WheelScrollZoomNotchDivisor = 1.0;
        
        /// <summary>
        /// Multiplier for drag-to-pan after mapping pointer movement from viewbox space (1 = default).
        /// Increase (e.g. 1.5–2.5) if panning still feels slow.
        /// </summary>
        public const double PointerPanSpeedMultiplier = 2.0;
        
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
        /// Maximum number of images to keep in RAM cache.
        /// Default: 20
        /// </summary>
        public const int MaxCacheSize = 20;
        
        /// <summary>
        /// Number of adjacent images to preload (before and after current).
        /// Default: 2
        /// </summary>
        public const int PreloadRange = 2;
        
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
        /// Gets the full path to the "good" folder.
        /// </summary>
        public static string GetGoodFolderPath()
        {
            return System.IO.Path.Combine(SourceFolder, GoodFolderName);
        }
        
        /// <summary>
        /// Gets the full path to the "very good" folder.
        /// </summary>
        public static string GetVeryGoodFolderPath()
        {
            return System.IO.Path.Combine(SourceFolder, VeryGoodFolderName);
        }
        
        /// <summary>
        /// Gets the full path to the "sorted out" folder.
        /// </summary>
        public static string GetSortedOutFolderPath()
        {
            return System.IO.Path.Combine(SourceFolder, SortedOutFolderName);
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
            
            if (MaxCacheSize <= 0)
                throw new InvalidOperationException("MaxCacheSize must be greater than 0");
            
            if (PreloadRange < 0)
                throw new InvalidOperationException("PreloadRange cannot be negative");
            
            if (ExifToolTimeoutMs <= 0)
                throw new InvalidOperationException("ExifToolTimeoutMs must be greater than 0");
        }
    }
}