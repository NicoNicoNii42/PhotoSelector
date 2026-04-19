using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia;
using System;
using System.Threading.Tasks;

namespace PhotoSorterAvalonia
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            var s = AppSettings.Current;
            RootPathText.Text = s.RootFolder;
            GoodFolderText.Text = s.GoodFolderName;
            VeryGoodFolderText.Text = s.VeryGoodFolderName;
            SortedOutFolderText.Text = s.SortedOutFolderName;
        }

        private async void BrowseRoot_Click(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider.CanPickFolder)
            {
                var opts = new FolderPickerOpenOptions
                {
                    Title = "Choose library root folder",
                    AllowMultiple = false,
                };
                var folders = await StorageProvider.OpenFolderPickerAsync(opts);
                if (folders.Count == 0)
                    return;
                if (folders[0].TryGetLocalPath() is { } path)
                    RootPathText.Text = path;
            }
            else
            {
                await ShowErrorAsync("Folder picking is not available on this platform.");
            }
        }

        private async void Ok_Click(object? sender, RoutedEventArgs e)
        {
            var data = new AppSettings.Data
            {
                RootFolder = RootPathText.Text ?? "",
                GoodFolderName = GoodFolderText.Text ?? "",
                VeryGoodFolderName = VeryGoodFolderText.Text ?? "",
                SortedOutFolderName = SortedOutFolderText.Text ?? "",
            };

            if (AppSettings.Validate(data, requireRootExists: true) is { } err)
            {
                await ShowErrorAsync(err);
                return;
            }

            AppSettings.Save(data);
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private async Task ShowErrorAsync(string message)
        {
            var ok = new Button { Content = "OK", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
            var sp = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
            sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            sp.Children.Add(ok);
            var dlg = new Window
            {
                Title = "Settings",
                Width = 440,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = sp,
            };
            ok.Click += (_, _) => dlg.Close();
            await dlg.ShowDialog(this);
        }
    }
}
