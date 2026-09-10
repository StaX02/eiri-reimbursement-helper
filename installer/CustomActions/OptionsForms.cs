using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Eiri.Reimbursement.Installer;

public class InstallerForm : Form
{
    static InstallerForm() => Application.EnableVisualStyles();
    protected readonly TableLayoutPanel ContentPanel = new TableLayoutPanel
    {
        Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Padding = new Padding(24)
    };

    protected InstallerForm(string title)
    {
        Text = title;
        Font = new Font("Microsoft YaHei UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(650, 0);
        Controls.Add(ContentPanel);
    }

    protected void AddText(string text, bool heading = false)
    {
        var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 0, 0, 16) };
        if (heading) label.Font = new Font(Font.FontFamily, 15, FontStyle.Bold);
        ContentPanel.Controls.Add(label);
    }

    protected Button AddButtons(string actionText, Action action)
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 20, 0, 0) };
        var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(96, 36), DialogResult = DialogResult.Cancel };
        var confirm = new Button { Text = actionText, AutoSize = true, MinimumSize = new Size(120, 36) };
        confirm.Click += (_, __) => action();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(confirm);
        ContentPanel.Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = confirm;
        return confirm;
    }
}

public sealed class InstallOptionsForm : InstallerForm
{
    private readonly TextBox installPath;
    private readonly TextBox dataPath;
    private readonly CheckBox shortcut;
    private readonly Label error;
    public string InstallDirectory => InstallerActions.NormalizeDirectory(installPath.Text);
    public string DataDirectory => InstallerActions.NormalizeDirectory(dataPath.Text);
    public bool DesktopShortcut => shortcut.Checked;

    public InstallOptionsForm(string installDirectory, string dataDirectory, bool desktopShortcut, bool upgrading)
        : base("安装 Eiri 发票报销助手")
    {
        AddText(upgrading ? "升级发票报销助手" : "安装发票报销助手", true);
        AddText("应用供本机所有用户使用。数据保存位置仅应用于当前 Windows 用户。\n开始菜单快捷方式会自动添加。");
        installPath = AddDirectory("应用安装位置(&I)", installDirectory, false, upgrading);
        dataPath = AddDirectory("数据保存位置(&D)", dataDirectory, true, upgrading);
        AddText(upgrading
            ? "升级沿用现有目录并保留全部数据。迁移资料库请使用应用内的备份与恢复。"
            : "数据将保存在专用的 EiriReimbursementHelper 文件夹中。\n已有资料库时请选择原位置；更换位置不会自动迁移数据。");
        shortcut = new CheckBox { Text = "添加桌面快捷方式(&S)", Checked = desktopShortcut, AutoSize = true };
        ContentPanel.Controls.Add(shortcut);
        error = new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = SystemColors.HotTrack, AccessibleName = "目录校验结果" };
        ContentPanel.Controls.Add(error);
        AddButtons(upgrading ? "升级" : "安装", () =>
        {
            try
            {
                InstallerActions.ValidateDirectories(InstallDirectory, DataDirectory);
                // Verify user data storage is writable without leaving application data behind.
                Directory.CreateDirectory(DataDirectory);
                string probe = Path.Combine(DataDirectory, ".eiri-write-probe-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                error.Text = "无法使用所选目录：" + exception.Message;
                dataPath.Focus();
            }
        });
    }

    private TextBox AddDirectory(string label, string path, bool data, bool locked)
    {
        AddText(label);
        var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var input = new TextBox { Text = path, Dock = DockStyle.Fill, Width = 470, ReadOnly = locked, AccessibleName = label };
        var browse = new Button { Text = "浏览…", AutoSize = true, Enabled = !locked, AccessibleName = "浏览" + label };
        browse.Click += (_, __) =>
        {
            using (var picker = new FolderBrowserDialog { Description = data ? "选择数据保存位置（自动添加 EiriReimbursementHelper 子文件夹）" : "选择应用安装文件夹", SelectedPath = input.Text })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                input.Text = data && !Path.GetFileName(picker.SelectedPath.TrimEnd('\\')).Equals(InstallerActions.LibraryFolder, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(picker.SelectedPath, InstallerActions.LibraryFolder) : picker.SelectedPath;
            }
        };
        row.Controls.Add(input);
        row.Controls.Add(browse);
        ContentPanel.Controls.Add(row);
        return input;
    }
}

public sealed class RemoveOptionsForm : InstallerForm
{
    private readonly CheckBox delete;
    public bool DeleteData => delete.Checked;

    public RemoveOptionsForm(string directory) : base("卸载 Eiri 发票报销助手")
    {
        AddText("卸载发票报销助手", true);
        AddText("将移除应用及快捷方式。默认保留当前 Windows 用户的资料库，重新安装后可继续使用。");
        AddText("数据保存位置：");
        ContentPanel.Controls.Add(new TextBox { Text = directory, ReadOnly = true, Dock = DockStyle.Fill, AccessibleName = "待处理的数据保存位置" });
        delete = new CheckBox { Text = "同时永久删除当前用户的应用数据(&D)", AutoSize = true, Margin = new Padding(0, 20, 0, 16) };
        ContentPanel.Controls.Add(delete);
        AddText("勾选后将删除订单、报销单、发票、受管材料和本地设置，无法撤销。\n其他 Windows 用户的数据、已导出的文件和外部备份包会保留。");
        var confirm = AddButtons("卸载并保留数据", () => { DialogResult = DialogResult.OK; Close(); });
        delete.CheckedChanged += (_, __) => confirm.Text = delete.Checked ? "卸载并删除数据" : "卸载并保留数据";
    }
}
