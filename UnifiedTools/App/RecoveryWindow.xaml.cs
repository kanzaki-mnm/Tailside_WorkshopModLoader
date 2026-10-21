using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ModEnvironmentSetup.Core;
using UnifiedTools.Core;
namespace UnifiedTools;
public partial class RecoveryWindow:Window
{
    private readonly EnvironmentController controller;
    private string? root;
    private EnvironmentView? view;
    private bool busy,loadingLanguage,removalCompleted;
    private string? friendlyResultKey;
    private readonly List<string> diagnosticErrors=[];
    public const string PermissionMessage="You don't have permission to change this folder. To continue, choose Restart as administrator.";
    public const string ElevationCancelledMessage="Restarting as administrator was cancelled. You can try again from this window at any time.";
    public const string CleanupMessage="Close this window, then you may delete BepInEx/TailsideModSetup. Keep any reports or repair backups you need first.";
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};
    public bool CanSaveAutomatically{get{try{return root!=null&&controller.OwnershipOf(root)==Ownership.Owned;}catch{return false;}}}
    public EnvironmentView? CurrentView=>view;
    public string? GameRoot=>root;
    private static string T(string text)=>ModManager.UiLanguage.Get(text);
    public RecoveryWindow(EnvironmentController controller,string? root)
    {
        this.controller=controller;this.root=root;InitializeComponent();
        Height=Math.Min(Height,SystemParameters.WorkArea.Height-40);MaxHeight=SystemParameters.WorkArea.Height;
        LoadLanguage();RefreshView();timer.Tick+=(_,_)=>{if(!busy)RefreshView();};timer.Start();Closed+=(_,_)=>timer.Stop();
        Closing+=(_,e)=>{if(busy)e.Cancel=true;};
    }
    private void LoadLanguage()
    {
        loadingLanguage=true;
        try{
            if(root!=null){ToolStorage.SetGameRoot(root);ModManager.UiLanguage.Load(importLegacy:false);}
            LanguageChoice.ItemsSource=ModManager.UiLanguage.Options.Count>0?ModManager.UiLanguage.Options:new List<ModManager.LanguageOption>{new("auto","Auto (OS) / 自動")};
            LanguageChoice.SelectedValue=ModManager.UiLanguage.Choice;
        }finally{loadingLanguage=false;}
    }
    public void RefreshView()
    {
        if(friendlyResultKey!=null)LastResult.Text=T(friendlyResultKey);
        VersionText.Text=$"{App.ToolVersion} · Unofficial community tool";
        Title=T("Tailside Mod Environment Setup & Recovery");PageTitle.Text=T("Mod Environment Setup & Recovery");
        Browse.Content=T("Change folder…");Guide.Content=T(SupportInfo.GuideButton);Folder.Content=T("Open game folder");Report.Content=T("Save report…");Admin.Content=T("Restart as administrator");
        DetailsPanel.Header=T("Game won't start? Repair & recovery");Repair.Content=T("Repair…");Remove.Content=T("Remove Mod environment");KeepTool.Content=T("Keep this tool in game folder");
        Location.Text=root??T("Choose the folder containing Tailside.exe.");
        Folder.IsEnabled=root!=null;Report.IsEnabled=root!=null;Browse.IsEnabled=!busy;LanguageChoice.IsEnabled=!busy;
        OwnedActions.Visibility=Visibility.Collapsed;RecoveryActions.Visibility=Visibility.Collapsed;ManualPanel.Visibility=Visibility.Collapsed;Primary.IsEnabled=false;Primary.Visibility=Visibility.Visible;Pause.Visibility=Visibility.Visible;
        StatusIcon.Kind=StatusSymbolKind.Package;
        StatusHeading.Foreground=new SolidColorBrush(Colors.SaddleBrown);
        if(root==null){StatusHeading.Text=T("Set up your Mod environment");StatusText.Text=T("Choose Tailside's game folder to get started.");Primary.Content=T("Choose folder…");Primary.IsEnabled=!busy;return;}
        try{view=controller.Inspect(root);}catch(Exception e){view=null;StatusIcon.Kind=StatusSymbolKind.Warning;StatusHeading.Text=T("The environment could not be checked safely");StatusText.Text=e.Message;ShowManual();return;}
        var owned=view.Ownership==Ownership.Owned;var manual=view.Ownership is Ownership.Manual or Ownership.Unverified;var running=App.Running();
        SourceText.Text=T(owned?"Installed by this tool":manual?"Not managed by this tool":"No Mod environment detected");
        StatusHeading.Foreground=new SolidColorBrush(Colors.SaddleBrown);
        UsageHint.Text="";

        if(running)UsageHint.Text=T("Tailside is running. Close the game normally before changing the environment.");
        if(busy)UsageHint.Text=T("Applying changes… Please wait.");
        if(removalCompleted){
            StatusIcon.Kind=StatusSymbolKind.Check;
            SourceText.Text="";
            StatusHeading.Text=T("Mod environment removed");
            StatusHeading.Foreground=new SolidColorBrush(Colors.DarkGreen);
            StatusText.Text=T("The files installed by this tool have been removed. Your mods and settings are kept.");
            UsageHint.Text=T(CleanupMessage);
            Primary.Content=T("Check again");Primary.IsEnabled=!busy;
            return;
        }
        if(manual){
            StatusIcon.Kind=view.Ownership==Ownership.Unverified?StatusSymbolKind.Warning:StatusSymbolKind.Information;
            StatusHeading.Text=T(view.Ownership==Ownership.Unverified?"Installation record cannot be verified":"Manually installed environment");
            StatusText.Text=T("This tool will not change, repair, disable, remove, or copy files into this environment. Follow the manual instructions below.");
            Primary.Content=T("Check again");Primary.IsEnabled=!busy;
            if(view.Ownership==Ownership.Manual){Primary.Visibility=Visibility.Collapsed;Primary.IsEnabled=false;}
            ShowManual();return;
        }
        if(owned){
            OwnedActions.Visibility=Visibility.Visible;
            RecoveryActions.Visibility=Visibility.Visible;
            var ready=view.Installation.State==SetupState.Installed&&view.Runtime=="Enabled";
            var update=view.Runtime=="Enabled"&&(view.Installation.State is SetupState.Installed or SetupState.NeedsUpdate)&&controller.HasUpdate(root);
            StatusIcon.Kind=update?StatusSymbolKind.Update:ready?StatusSymbolKind.Check:view.Runtime=="Paused"?StatusSymbolKind.Paused:StatusSymbolKind.Warning;
            StatusHeading.Text=T(update?"A Mod environment update is available":ready?"Mod environment installed":view.Runtime=="Paused"?"Mod environment temporarily disabled":"Recovery may be needed");
            if(ready)StatusHeading.Foreground=new SolidColorBrush(Color.FromRgb(49,111,66));
            StatusText.Text=update?T("Update while keeping your mods and settings."):ready?T("No additional setup is needed."):!GameFolder.IsValid(root)?T("This game runtime is not supported. Keep the Mod environment disabled or remove it."):view.Runtime=="Paused"?T("The environment is disabled. Re-enable it when the problem is resolved."):T(view.Installation.Message);
            Pause.Content=T(view.Runtime=="Paused"?"Re-enable Mod environment":"Temporarily disable Mod environment");
            Pause.Visibility=update?Visibility.Collapsed:Visibility.Visible;
            Pause.IsEnabled=!busy&&!running&&ModManager.Core.ManagerEngine.IsGame(root)&&(view.Runtime=="Enabled"||view.Runtime=="Paused"&&GameFolder.IsValid(root));
            Repair.IsEnabled=!busy&&!running&&controller.Setup.CanRepairOwned(root);
            Remove.IsEnabled=!busy&&!running&&controller.Setup.CanRemoveForRecovery(root);
            KeepTool.IsEnabled=!busy&&!running;
            Primary.Content=T(update?"Update":view.Installation.CanInstall?"Repair environment":"Check again");
            Primary.IsEnabled=!busy&&(!view.Installation.CanInstall||!running);
            if(update)Primary.IsEnabled=!busy&&!running;
            Primary.Visibility=ready&&!update||view.Runtime=="Paused"?Visibility.Collapsed:Visibility.Visible;
        }else{
            StatusHeading.Text=T("Mod environment not installed");StatusText.Text=T("Set up the environment needed to use mods.");
            if(!view.Installation.CanInstall)StatusText.Text=T(view.Installation.Message);
            Primary.Content=T("Install");Primary.IsEnabled=!busy&&!running&&view.Installation.CanInstall;
        }
    }
    private void ShowManual()
    {
        ManualPanel.Visibility=Visibility.Visible;OwnedActions.Visibility=Visibility.Collapsed;RecoveryActions.Visibility=Visibility.Collapsed;

        ManualHeading.Text=T("Manual disable and removal");
        ManualInstructions.Text=T("Close Tailside before making changes.\n\nWorkshop loader only: move BepInEx/patchers/TailsideWorkshopModLoader.dll outside BepInEx. BepInEx and regular plugins remain installed.\n\nFor the standard Windows BepInEx installation used here: move the BepInEx-provided winhttp.dll outside the game folder to stop BepInEx loading. Keep it so you can restore it later. Do not move an unknown DLL.\n\nTo remove the remaining BepInEx files, first back up any mods and settings you want to keep, then remove only files you know you installed. This is not a general uninstaller for other loaders or hardpatched game files.\n\nIf you keep regular plugins after removing only this loader, set HideManagerGameObject=true in BepInEx/config/BepInEx.cfg.\n\nIf an installation record is damaged, use a trusted backup or ask for help. This tool will not guess which files it owns.");
    }
    private void BrowseClicked(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFolderDialog{Title=T("Choose the Tailside folder"),InitialDirectory=root!=null&&Directory.Exists(root)?root:Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)};
        if(dialog.ShowDialog(this)!=true)return;root=dialog.FolderName;removalCompleted=false;DetailsPanel.IsExpanded=false;LoadLanguage();friendlyResultKey=null;LastResult.Text="";RefreshView();
    }
    private void FolderClicked(object sender,RoutedEventArgs e){if(root!=null)Try(()=>Process.Start(new ProcessStartInfo(root){UseShellExecute=true}));}
    private void GuideClicked(object sender,RoutedEventArgs e)=>ModTools.Desktop.HelpSupport.OpenGuide(error=>LastResult.Text=T(error.Message));
    private void LanguageChanged(object sender,SelectionChangedEventArgs e)
    {
        if(loadingLanguage||LanguageChoice.SelectedValue is not string code)return;
        Try(()=>{ModManager.UiLanguage.Select(code,persist:CanSaveAutomatically);RefreshView();});
    }
    private void ShowError(Exception error,string context)
    {
        var native=error is System.ComponentModel.Win32Exception win32?$"NativeErrorCode: {win32.NativeErrorCode}\n":"";
        diagnosticErrors.Add($"{DateTimeOffset.Now:O} | Operation: {context} | Folder: {root}\n{native}{error}");
        if(diagnosticErrors.Count>10)diagnosticErrors.RemoveAt(0);
        friendlyResultKey=error is UnauthorizedAccessException?PermissionMessage:
            context=="administrator restart" && error is System.ComponentModel.Win32Exception{NativeErrorCode:1223}?ElevationCancelledMessage:null;
        LastResult.Text=friendlyResultKey!=null?T(friendlyResultKey):error.Message;
        if(friendlyResultKey!=null)Admin.Visibility=Visibility.Visible;
    }
    private void Try(Action action,string context="action"){try{action();}catch(Exception e){ShowError(e,context);}}
    public string DiagnosticReport()=>
        $"Tailside Mod Environment Setup & Recovery {App.ToolVersion}\n{DateTimeOffset.Now:O}\nFolder: {root}\nOwnership: {view?.Ownership}\n{view?.Installation.State}\n{view?.Runtime}\n{view?.Detail}\n{LastResult.Text}\n\nRecent technical details (up to 10 errors):\n"+
        (diagnosticErrors.Count==0?"None":string.Join("\n\n",diagnosticErrors))+"\nLocal report; nothing uploaded.";
    public async Task ExecuteAsync(string action)
    {
        if(root==null||busy)return;var selected=root;busy=true;RefreshView();
        try{
            var result=await Task.Run(()=>{
                switch(action){
                    case "install":controller.Setup.Install(selected);controller.KeepTool(selected,Environment.ProcessPath!);removalCompleted=false;return "Setup complete!";
                    case "update":controller.Update(selected,Environment.ProcessPath!);return "Environment and game-local tool updated. Mods and settings are kept.";
                    case "repair":controller.Setup.RepairOwned(selected);return "Verified environment files restored. Changed owned files were backed up.";
                    case "pause":controller.Manager.PauseEnvironment(selected);return "Environment disabled. Mods and settings are kept.";
                    case "resume":controller.Manager.ResumeEnvironment(selected);return "Environment re-enabled. Launch from Steam.";
                    case "remove":var kept=controller.Remove(selected);removalCompleted=kept.Count==0;return T("Removal complete. Mods, settings, and the tool are kept.")+"\n"+(removalCompleted?T(CleanupMessage):T("Some runtime files were kept. Review the list before cleaning up the tool folder."))+"\n"+T("Kept changed or unowned runtime files:")+" "+(kept.Count==0?T("None"):string.Join("\n",kept));
                    case "tool":controller.KeepTool(selected,Environment.ProcessPath!);return "Tool copy saved in the game folder.";
                    default:throw new ArgumentException("Unknown action");
                }
            });
            friendlyResultKey=null;LastResult.Text=T(result);if(action=="install")LoadLanguage();
        }catch(Exception e){ShowError(e,action);}
        finally{busy=false;RefreshView();}
    }
    private async void PrimaryClicked(object sender,RoutedEventArgs e)
    {
        if(root==null){BrowseClicked(sender,e);return;}
        RefreshView();if(root!=null&&view?.Ownership==Ownership.Owned&&controller.HasUpdate(root)&&view.Installation.State is SetupState.Installed or SetupState.NeedsUpdate)await ExecuteAsync("update");
        else if(view?.Installation.CanInstall==true)await ExecuteAsync(view.Ownership==Ownership.Owned?"repair":"install");else RefreshView();
    }
    private async void PauseClicked(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,T("Change the environment's enabled state? Mods and settings will be kept."),T("Confirm"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        await ExecuteAsync(view?.Runtime=="Paused"?"resume":"pause");
    }
    private async void RepairClicked(object sender,RoutedEventArgs e)
    {
        if(MessageBox.Show(this,T("Restore owned environment files? Changed owned files will be backed up. Unknown files will not be overwritten."),T("Confirm repair"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        await ExecuteAsync("repair");
    }
    public string RemovalSummary()
    {
        if(root==null)throw new InvalidOperationException();
        return T("Remove only verified files created by this tool. Changed files are kept. Mods, settings, translations, and the tool are kept.")+"\n"+T("Installation and recovery records are updated to match the removal result.")+"\n\n"+
            string.Join("\n",controller.RemovalFiles(root).Select(file=>{
                var relative=file.Path=="winhttp.dll"&&!File.Exists(SetupEngine.SafePath(root,file.Path))&&File.Exists(SetupEngine.SafePath(root,ModManager.Core.ManagerEngine.ParkedPath))?ModManager.Core.ManagerEngine.ParkedPath:file.Path;
                var path=SetupEngine.SafePath(root,relative);return (File.Exists(path)?SetupEngine.HashFile(path)==file.Hash?T("Remove: "):T("Keep changed: "):T("Already missing: "))+relative;
            }));
    }
    private async void RemoveClicked(object sender,RoutedEventArgs e)
    {
        try{
            var dialog=new Window{Title=T("Confirm removal"),Owner=this,Width=620,Height=450,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background};
            var panel=new DockPanel{Margin=new Thickness(16)};var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Bottom);
            var yes=new Button{Content=T("Remove verified files"),Margin=new Thickness(8)};var no=new Button{Content=T("Cancel"),Margin=new Thickness(8)};yes.Click+=(_,_)=>dialog.DialogResult=true;no.Click+=(_,_)=>dialog.DialogResult=false;
            buttons.Children.Add(no);buttons.Children.Add(yes);panel.Children.Add(buttons);panel.Children.Add(new TextBox{Text=RemovalSummary(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});dialog.Content=panel;
            if(dialog.ShowDialog()==true)await ExecuteAsync("remove");
        }catch(Exception error){ShowError(error,"removal preview");}
    }
    private async void KeepToolClicked(object sender,RoutedEventArgs e)=>await ExecuteAsync("tool");
    private void ReportClicked(object sender,RoutedEventArgs e)
    {
        var initial=ModTools.Desktop.HelpSupport.ReportDirectory(CanSaveAutomatically,error=>LastResult.Text=T("The default report folder is unavailable. Choose another save location."));
        var dialog=new SaveFileDialog{FileName="Tailside-ModEnvironment-report.txt",InitialDirectory=initial,Filter="Text report|*.txt"};
        if(dialog.ShowDialog(this)==true)Try(()=>File.WriteAllText(dialog.FileName,DiagnosticReport()));
    }
    private void AdminClicked(object sender,RoutedEventArgs e)=>Try(()=>{
        var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};if(root!=null){info.ArgumentList.Add("--game-dir");info.ArgumentList.Add(root);}Process.Start(info);Close();
    },"administrator restart");
}





