using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ModEnvironmentSetup.Core;

namespace ModTools.Desktop;

public static class HelpSupport
{
    public static bool FatalErrorActive { get; private set; }

    public static string ReportDirectory(bool managed, Action<Exception>? onError = null)
    {
        if(managed)try{
            var directory=ToolStorage.FilePath("logs/reports");
            Directory.CreateDirectory(directory);
            return directory;
        }catch(Exception error){onError?.Invoke(error);}
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static void OpenGuide(Action<Exception> onError)
    {
        if(!SupportInfo.HasGuideUrl){onError(new IOException(SupportInfo.GuideNotice));return;}
        try { Process.Start(new ProcessStartInfo(SupportInfo.GuideUrl) { UseShellExecute = true }); }
        catch (Exception error) { onError(new IOException("The guide could not be opened. You can copy this address into your browser: " + SupportInfo.GuideUrl, error)); }
    }

    public static void Attach(Application app, string name, string version, Func<string,string>? translate = null, Func<bool>? canSaveAutomatically = null)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (FatalErrorActive) { app.Shutdown(1); return; }
            FatalErrorActive = true;
            var report = SupportInfo.FatalReport(name, version, e.Exception);
            var saved = canSaveAutomatically?.Invoke()==false ? null : SaveEmergencyReport(name, report);
            try
            {
                foreach (Window existing in app.Windows) existing.IsEnabled = false;
                ShowFailure(name, report, saved, translate);
            }
            catch { /* Error handling must not throw recursively. The local report is best effort. */ }
            finally { app.Shutdown(1); }
        };
        // Background-thread fatal exceptions may terminate before any UI can be displayed.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
                if(canSaveAutomatically?.Invoke()!=false)SaveEmergencyReport(name, SupportInfo.FatalReport(name, version, error));
        };
    }

    private static string? SaveEmergencyReport(string name, string report)
    {
        try
        {
            var directory = ToolStorage.FilePath("logs/reports");
            Directory.CreateDirectory(directory);
            var path = ToolStorage.FilePath("logs/reports/" + name.Replace(" ", "") + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, report);
            return path;
        }
        catch { return null; }
    }

    private static void ShowFailure(string name, string report, string? saved, Func<string,string>? translate)
    {
        string T(string text) => translate?.Invoke(text) ?? text;
        var dialog = new Window
        {
            Title = name + T(" — Something went wrong"), Width = 620, MinWidth = 500,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.CanMinimize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(245,240,233)),
            Foreground = new SolidColorBrush(Color.FromRgb(56,43,39)), FontFamily = new FontFamily("Segoe UI"), FontSize = 14
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = T("Something went wrong"), FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = name + T(" cannot continue and will close. Open the guide for help, or save this report before closing."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,8) });
        panel.Children.Add(new TextBlock { Text = saved == null ? T("An automatic report could not be saved. Use Save report below.") : T("A local report was saved to:\n") + saved, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        panel.Children.Add(new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,12,0,8) });
        var status = new TextBlock { Text = T(SupportInfo.GuideNotice), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,0,0,12) };
        panel.Children.Add(status);
        var buttons = new WrapPanel();
        var guide = new Button { Content = T(SupportInfo.GuideButton), Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,8,0) };
        guide.Click += (_, _) => OpenGuide(error => status.Text = error.Message);
        var save = new Button { Content = T("Save report…"), Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,8,0) };
        save.Click += (_, _) =>
        {
            try
            {
                var file = new SaveFileDialog { InitialDirectory = saved != null ? Path.GetDirectoryName(saved) : ToolStorage.DirectoryPath, FileName = name.Replace(" ", "") + "-error.txt", Filter = T("Text report (*.txt)|*.txt") };
                if (file.ShowDialog(dialog) != true) return;
                File.WriteAllText(file.FileName, report); status.Text = T("Report saved locally. Nothing was uploaded.");
            }
            catch (Exception error) { status.Text = T("The report could not be saved: ") + error.Message; }
        };
        var close = new Button { Content = T("Close"), Padding = new Thickness(12,8,12,8) };
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(guide); buttons.Children.Add(save); buttons.Children.Add(close);
        panel.Children.Add(buttons); dialog.Content = panel; dialog.ShowDialog();
    }
}

