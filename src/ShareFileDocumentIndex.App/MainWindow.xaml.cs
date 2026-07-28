using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace ShareFileDocumentIndex.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ShareFileClient _client = new();
    private string _rootFolderId = "";
    private List<(string Id, string Name)> _folders = new();

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            SignInStatus.Text = "Enter your ShareFile email and password.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ShareFile.ClientId) ||
            _settings.ShareFile.ClientId.StartsWith("PASTE_"))
        {
            SignInStatus.Text = "appsettings.json is missing the ShareFile Client ID/Secret. Ask IT to fill it in.";
            return;
        }

        SignInButton.IsEnabled = false;
        SignInStatus.Text = "";
        StatusText.Text = "Signing in...";
        ProgressBar.Visibility = Visibility.Visible;

        try
        {
            await _client.SignInAsync(
                _settings.ShareFile.Subdomain,
                _settings.ShareFile.ClientId,
                _settings.ShareFile.ClientSecret,
                email,
                password);

            _rootFolderId = await _client.GetHomeFolderIdAsync(_settings.ShareFile.RootFolderPath);
            await LoadFolderListAsync();

            SignInPanel.Visibility = Visibility.Collapsed;
            FolderPanel.Visibility = Visibility.Visible;
            FolderListBox.Visibility = Visibility.Visible;
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            SignInStatus.Text = $"Sign-in failed: {ex.Message}";
            StatusText.Text = "";
        }
        finally
        {
            SignInButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadFolderListAsync();
    }

    private async Task LoadFolderListAsync()
    {
        StatusText.Text = "Loading client folders...";
        ProgressBar.Visibility = Visibility.Visible;
        try
        {
            _folders = await _client.GetSubfolderListAsync(_rootFolderId);
            FolderListBox.ItemsSource = _folders.Select(f => f.Name).ToList();
            StatusText.Text = $"{_folders.Count} folder(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load folders: {ex.Message}";
        }
        finally
        {
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIndex = FolderListBox.SelectedIndex;
        if (selectedIndex < 0)
        {
            StatusText.Text = "Select a client folder first.";
            return;
        }

        var folder = _folders[selectedIndex];

        var dialog = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"{folder.Name} - Document Index - {DateTime.Now:yyyy-MM-dd}.xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        GenerateButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;

        var progress = new Progress<string>(msg => StatusText.Text = msg);

        try
        {
            var generator = new ReportGenerator(_client, IncludeLinksCheck.IsChecked == true);
            var rows = await generator.BuildAsync(folder.Id, folder.Name, progress);

            StatusText.Text = "Writing Excel file...";
            ExcelReportWriter.Write(dialog.FileName, folder.Name, rows);

            StatusText.Text = $"Done. {rows.Count} item(s) written to {dialog.FileName}";

            var result = MessageBox.Show("Report generated. Open it now?", "Done", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            GenerateButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Collapsed;
        }
    }
}
