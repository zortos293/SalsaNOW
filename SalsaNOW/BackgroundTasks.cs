using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SalsaNOW
{
    internal static class BackgroundTasks
    {
 

        public static async Task CloseHandlesLaunchersHelper(CancellationToken token)
        {
            const string launcherPath = @"C:\Users\user\boosteroid-experience\LaunchersHelper.exe";
            string processName = Path.GetFileNameWithoutExtension(launcherPath);
            var processes = Process.GetProcessesByName(processName);

            foreach (var process in processes)
            {
                IntPtr processHandle = IntPtr.Zero;

                try
                {
                    processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, process.Id);
                    if (processHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var pathBuilder = new StringBuilder(260);
                    int pathLength = pathBuilder.Capacity;
                    if (!NativeMethods.QueryFullProcessImageName(processHandle, 0, pathBuilder, ref pathLength))
                    {
                        continue;
                    }

                    string currentPath = pathBuilder.ToString();

                    if (!string.Equals(currentPath, launcherPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    SalsaLogger.Info($"Closing LaunchersHelper process: {currentPath}");

                    try
                    {
                        if (process.CloseMainWindow())
                        {
                            await Task.Run(() => process.WaitForExit(5000));
                        }
                    }
                    catch { }

                    if (process.HasExited)
                    {
                        SalsaLogger.Info("LaunchersHelper.exe closed successfully.");
                    }
                    else
                    {
                        SalsaLogger.Warn("LaunchersHelper.exe did not exit after the close request.");
                    }
                }
                catch (Exception ex)
                {
                    SalsaLogger.Error($"Failed to close LaunchersHelper.exe: {ex.Message}");
                }
                finally
                {
                    if (processHandle != IntPtr.Zero)
                    {
                        NativeMethods.CloseHandle(processHandle);
                    }

                    process.Dispose();
                }
            }
        }


        public static Task CleanlogsLauncherHelper(CancellationToken token)
        {
            const string logPath = @"C:\users\user\boosteroid-experience\logs\launchershelper.log";

            if (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            try
            {
                string logDirectory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                using (var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                }

                SalsaLogger.Info("Cleared launchershelper.log.");
            }
            catch (Exception ex)
            {
                SalsaLogger.Error($"Failed to clear launchershelper.log: {ex.Message}");
            }

            return Task.CompletedTask;
        }


        // Monitors Desktop and Start Menu shortcuts, syncing them to the persistent SalsaNOW directory
        public static async Task StartShortcutsSavingAsync(string globalDirectory, CancellationToken token)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string startMenuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
            string shortcutsDir = Path.Combine(globalDirectory, "Shortcuts");
            string backupDir = Path.Combine(globalDirectory, "Backup Shortcuts");

            Directory.CreateDirectory(shortcutsDir);
            Directory.CreateDirectory(backupDir);

            // 1. Initial Sync: Throw saved icons onto the fresh Desktop immediately
            try
            {
                var allFiles = Directory.GetFiles(shortcutsDir, "*.lnk", SearchOption.AllDirectories);
                foreach (string shortcut in allFiles)
                {
                    File.Copy(shortcut, Path.Combine(desktopPath, Path.GetFileName(shortcut)), true);
                }
                SalsaLogger.Info("Initial Desktop shortcut sync completed.");
            }
            catch (Exception ex) { SalsaLogger.Error($"Initial shortcut sync failed: {ex.Message}"); }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(5000, token);

                    // 2. Protect core components from user deletion
                    RestoreShortcut(desktopPath, shortcutsDir, backupDir, "PeaZip File Explorer Archiver.lnk");
                    RestoreShortcut(desktopPath, shortcutsDir, backupDir, "System Informer.lnk");

                    // 3. Sync Desktop to Shortcuts (Overwrite MUST be false to prevent corrupting existing backups)
                    try
                    {
                        var lnkFilesDesktop = Directory.GetFiles(desktopPath, "*.lnk", SearchOption.AllDirectories);
                        foreach (var file in lnkFilesDesktop)
                        {
                            string destPath = Path.Combine(shortcutsDir, Path.GetFileName(file));
                            if (!File.Exists(destPath))
                            {
                                try 
                                { 
                                    File.Copy(file, destPath, false); 
                                    SalsaLogger.Info($"Backed up new shortcut: {Path.GetFileName(file)}");
                                } 
                                catch { }
                            }
                        }
                    }
                    catch { }

                    // 4. Sync Shortcuts To Start Menu
                    try
                    {
                        var lnkFilesStart = Directory.GetFiles(shortcutsDir, "*.lnk", SearchOption.AllDirectories);
                        foreach (var file in lnkFilesStart)
                        {
                            string destPath = Path.Combine(startMenuPath, Path.GetFileName(file));
                            if (!File.Exists(destPath))
                            {
                                try 
                                { 
                                    if (!Directory.Exists(startMenuPath)) Directory.CreateDirectory(startMenuPath);
                                    File.Copy(file, destPath, false); 
                                    SalsaLogger.Info($"Copied shortcut over to Start Menu: {Path.GetFileName(file)}");
                                } 
                                catch { }
                            }
                        }
                    }
                    catch { }

                    // 5. Cleanup: Move deleted shortcuts from the primary folder to the long-term backup
                    try
                    {
                        var lnkFilesBackup = Directory.GetFiles(shortcutsDir, "*.lnk", SearchOption.AllDirectories);
                        foreach (var backupFile in lnkFilesBackup)
                        {
                            string fileName = Path.GetFileName(backupFile);
                            string originalPath = Path.Combine(desktopPath, fileName);

                            if (!File.Exists(originalPath))
                            {
                                if (File.Exists(Path.Combine(backupDir, fileName)))
                                {
                                    File.Delete(backupFile);
                                }
                                else
                                {
                                    File.Move(backupFile, Path.Combine(backupDir, fileName));
                                    SalsaLogger.Info($"Moved deleted shortcut to long-term backup: {fileName}");
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (TaskCanceledException) { }
        }

        // Restores a specific shortcut from either the primary or backup directory
        private static void RestoreShortcut(string desktop, string shortcuts, string backup, string name)
        {
            string targetDesktopPath = Path.Combine(desktop, name);
            if (!File.Exists(targetDesktopPath))
            {
                string sourcePath = Path.Combine(shortcuts, name);
                if (!File.Exists(sourcePath)) sourcePath = Path.Combine(backup, name);

                if (File.Exists(sourcePath))
                {
                    try 
                    { 
                        File.Copy(sourcePath, targetDesktopPath); 
                        SalsaLogger.Warn($"Restored missing core component: {name}");
                        new Thread(() => MessageBox.Show($"{Path.GetFileNameWithoutExtension(name)} is a core component and cannot be removed.", "SalsaNOW", MessageBoxButtons.OK, MessageBoxIcon.Information)).Start();
                    } 
                    catch { }
                }
            }
        }

    
    }
}
