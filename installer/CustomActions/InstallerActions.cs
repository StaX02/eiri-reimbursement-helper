using System;
using System.IO;
using FileAttributes = System.IO.FileAttributes;
using System.Threading;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace Eiri.Reimbursement.Installer;

public static class InstallerActions
{
    public const string RegistryPath = @"Software\StaX02\Eiri Reimbursement Helper";
    public const string LibraryFolder = "EiriReimbursementHelper";

    [CustomAction]
    public static ActionResult LoadSettings(Session session)
    {
        using (var machine = Registry.LocalMachine.OpenSubKey(RegistryPath))
        using (var user = Registry.CurrentUser.OpenSubKey(RegistryPath))
        {
            SetDefault(session, "INSTALLFOLDER", machine?.GetValue("InstallDirectory") as string ?? FindInstalledDirectory(session)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "StaX02", "Eiri Reimbursement Helper"));
            SetDefault(session, "DATADIRECTORY", user?.GetValue("DataDirectory") as string
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LibraryFolder));
            SetDefault(session, "ADDDESKTOPSHORTCUT", machine?.GetValue("DesktopShortcut") as string ?? "1");
        }
        return ActionResult.Success;
    }

    private static void SetDefault(Session session, string property, string value)
    {
        if (string.IsNullOrEmpty(session[property])) session[property] = value;
    }

    private static string FindInstalledDirectory(Session session)
    {
        // Versions before 0.4 did not persist the directory. The stable executable component does.
        string componentCode = session.Database.ExecuteScalar("SELECT `ComponentId` FROM `Component` WHERE `Component` = 'MainExecutableComponent'") as string;
        if (string.IsNullOrEmpty(componentCode)) return null;
        try
        {
            string executable = new ComponentInstallation(componentCode).Path;
            return File.Exists(executable) ? Path.GetDirectoryName(executable) : null;
        }
        catch (InstallerException) { return null; }
    }

    [CustomAction]
    public static ActionResult ConfigureInstall(Session session) => OnSta(() =>
    {
        using (var dialog = new InstallOptionsForm(session["INSTALLFOLDER"], session["DATADIRECTORY"],
                   session["ADDDESKTOPSHORTCUT"] == "1", !string.IsNullOrEmpty(session["WIX_UPGRADE_DETECTED"])))
        {
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return ActionResult.UserExit;
            session["INSTALLFOLDER"] = dialog.InstallDirectory;
            session["DATADIRECTORY"] = dialog.DataDirectory;
            session["ADDDESKTOPSHORTCUT"] = dialog.DesktopShortcut ? "1" : "0";
            return ActionResult.Success;
        }
    });

    [CustomAction]
    public static ActionResult ValidateSettings(Session session)
    {
        try
        {
            ValidateDirectories(session["INSTALLFOLDER"], session["DATADIRECTORY"]);
            if (!string.IsNullOrEmpty(session["Installed"]) || !string.IsNullOrEmpty(session["WIX_UPGRADE_DETECTED"]))
            {
                string installedDirectory = FindInstalledDirectory(session);
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string dataDirectory = key?.GetValue("DataDirectory") as string
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LibraryFolder);
                    if ((installedDirectory != null && !NormalizeDirectory(installedDirectory).Equals(NormalizeDirectory(session["INSTALLFOLDER"]), StringComparison.OrdinalIgnoreCase))
                        || !NormalizeDirectory(dataDirectory).Equals(NormalizeDirectory(session["DATADIRECTORY"]), StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("升级或修复时需要沿用现有目录。请先使用备份与恢复功能迁移资料库。");
                }
            }
            return ActionResult.Success;
        }
        catch (Exception exception)
        {
            session.Log(exception.ToString());
            using (var message = new Record(1))
            {
                message[0] = exception.Message;
                session.Message(InstallMessage.Error, message);
            }
            return ActionResult.Failure;
        }
    }

    public static void ValidateDirectories(string installDirectory, string dataDirectory)
    {
        string install = NormalizeDirectory(installDirectory);
        string data = NormalizeDirectory(dataDirectory);
        if (!string.Equals(Path.GetFileName(data), LibraryFolder, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("数据目录的最后一级必须为 EiriReimbursementHelper，请使用“浏览”选择保存位置。");
        if (Within(data, install) || Within(install, data))
            throw new ArgumentException("应用安装目录与数据目录应相互独立，请重新选择位置。");
        RejectReparseAncestors(data);
    }

    public static string NormalizeDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 3 || value[1] != ':' || value[2] != '\\'
            || value.IndexOfAny(new[] { '*', '?', '"', ';' }) >= 0)
            throw new ArgumentException("请输入本机磁盘上的完整目录，例如 D:\\EiriReimbursementHelper。");
        string path = Path.GetFullPath(value).TrimEnd('\\');
        if (path.Length <= 3 || File.Exists(path))
            throw new ArgumentException("请选择一个专用文件夹，不能使用磁盘根目录或文件。");
        return path;
    }

    private static bool Within(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);

    private static void RejectReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("数据目录不能包含符号链接或目录联接，请选择普通文件夹。");
    }

    [CustomAction]
    public static ActionResult PrepareDataRemoval(Session session)
    {
        // Resolve from the installing user's persisted setting, never an uninstall command-line path.
        string root;
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            root = key?.GetValue("DataDirectory") as string
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LibraryFolder);

        if (int.TryParse(session["UILevel"], out int level) && level > 2)
        {
            ActionResult result = OnSta(() =>
            {
                using (var dialog = new RemoveOptionsForm(root))
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return ActionResult.UserExit;
                    session["DELETEAPPDATA"] = dialog.DeleteData ? "1" : "0";
                    return ActionResult.Success;
                }
            });
            if (result != ActionResult.Success) return result;
        }
        if (session["DELETEAPPDATA"] == "1")
        {
            var data = new CustomActionData();
            data.Add("Root", root);
            session["DeleteApplicationData"] = data.ToString();
        }
        return ActionResult.Success;
    }

    [CustomAction]
    public static ActionResult DeleteApplicationData(Session session)
    {
        try
        {
            DeleteLibraryData(session.CustomActionData["Root"]);
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
                key?.DeleteValue("DataDirectory", false);
            return ActionResult.Success;
        }
        catch (Exception exception)
        {
            session.Log("Application data cleanup failed: " + exception);
            using (var message = new Record(1))
            {
                message[0] = "应用已卸载，部分数据未能删除。请关闭占用资料库的程序后手动清理。";
                session.Message(InstallMessage.Warning, message);
            }
            // A commit action must not roll the MSI back after data has already been removed.
            return ActionResult.Success;
        }
    }

    public static void DeleteLibraryData(string root)
    {
        root = NormalizeDirectory(root);
        if (!Path.GetFileName(root).Equals(LibraryFolder, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Unrecognized application data directory.");
        RejectReparseAncestors(root);
        if (!Directory.Exists(root)) return;
        // Only application-owned entries are removed. Other files in the chosen folder survive.
        string[] folders = { "originals", "cache", "staging", "logs" };
        string[] files = { "library.db", "library.db-wal", "library.db-shm", "library.db-journal" };
        foreach (string folder in folders)
            CheckTree(Path.Combine(root, folder));
        foreach (string file in files)
            if (File.Exists(Path.Combine(root, file)) && (File.GetAttributes(Path.Combine(root, file)) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to delete a linked file.");
        foreach (string folder in folders)
            if (Directory.Exists(Path.Combine(root, folder))) Directory.Delete(Path.Combine(root, folder), true);
        foreach (string file in files) File.Delete(Path.Combine(root, file));
        if (Directory.GetFileSystemEntries(root).Length == 0) Directory.Delete(root);
    }

    private static void CheckTree(string directory)
    {
        if (!Directory.Exists(directory)) return;
        RejectReparseAncestors(directory);
        foreach (string entry in Directory.GetFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to traverse a linked entry.");
            if ((attributes & FileAttributes.Directory) != 0) CheckTree(entry);
        }
    }

    private static ActionResult OnSta(Func<ActionResult> action)
    {
        ActionResult result = ActionResult.Failure;
        Exception failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
        return result;
    }
}
