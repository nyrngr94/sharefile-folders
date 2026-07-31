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

    private void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.ShareFile.ClientId) ||
            _settings.ShareFile.ClientId.StartsWith("PASTE_") ||
            _settings.ShareFile.ClientId.StartsWith("Add "))
        {
            SignInStatus.Text = "appsettings.local.json is missing the ShareFile Client ID/Secret. Ask IT to fill it in.";
            return;
        }

        SignInStatus.Text = "";

        var authorizeUrl = _client.BuildAuthorizeUrl(_settings.ShareFile.Subdomain, _settings.ShareFile.ClientId);
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        CodeEntryPanel.Visibility = Visibility.Visible;
        StatusText.Text = "Sign in using the browser window, then paste the code it shows you above.";
    }

    private async void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            SignInStatus.Text = "Paste the code from the browser page first.";
            return;
        }

        ContinueButton.IsEnabled = false;
        SignInStatus.Text = "";
        StatusText.Text = "Signing in...";
        ProgressBar.Visibility = Visibility.Visible;

        try
        {
            await _client.CompleteSignInAsync(
                _settings.ShareFile.Subdomain,
                _settings.ShareFile.ClientId,
                _settings.ShareFile.ClientSecret,
                code);

            SignInPanel.Visibility = Visibility.Collapsed;
            FolderPanel.Visibility = Visibility.Visible;
            FolderListBox.Visibility = Visibility.Visible;
            RootFolderBox.Text = string.IsNullOrWhiteSpace(_settings.ShareFile.RootFolderPath)
                ? "allshared"
                : _settings.ShareFile.RootFolderPath;
            StatusText.Text = "";

            await LoadFolderListAsync();
        }
        catch (Exception ex)
        {
            SignInStatus.Text = $"Sign-in failed: {ex.Message}";
            StatusText.Text = "";
        }
        finally
        {
            ContinueButton.IsEnabled = true;
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
            _rootFolderId = await _client.GetRootFolderIdAsync(RootFolderBox.Text);
            _folders = await _client.GetSubfolderListAsync(_rootFolderId);
            FolderListBox.ItemsSource = _folders.Select(f => f.Name).ToList();
            StatusText.Text = _folders.Count > 0
                ? $"{_folders.Count} folder(s) found."
                : "No subfolders found here. Try a different root above (e.g. 'home' or 'allshared') and click Load Folders again.";
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

    private async void DebugButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIndex = FolderListBox.SelectedIndex;
        if (selectedIndex < 0)
        {
            StatusText.Text = "Select a client folder first.";
            return;
        }

        var folder = _folders[selectedIndex];

        DebugButton.IsEnabled = false;
        StatusText.Text = "Fetching raw API response...";
        ProgressBar.Visibility = Visibility.Visible;

        try
        {
            var json = await _client.GetRawChildrenDebugJsonAsync(folder.Id);
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sharefile-debug.json");
            System.IO.File.WriteAllText(path, json);
            StatusText.Text = $"Saved to {path}";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Debug fetch failed: {ex.Message}";
        }
        finally
        {
            DebugButton.IsEnabled = true;
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

            var baseMessage = $"Done. {rows.Count} item(s) written to {dialog.FileName}";
            StatusText.Text = generator.LinkFetchError switch
            {
                null => baseMessage,
                _ when generator.LinkFetchGaveUp => $"{baseMessage}. Document links stopped after repeated failures: {generator.LinkFetchError}",
                _ => $"{baseMessage}. Some document links could not be generated: {generator.LinkFetchError}"
            };

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
