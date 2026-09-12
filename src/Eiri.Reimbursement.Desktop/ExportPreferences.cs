using System.IO;
using System.Text.Json;

namespace Eiri.Reimbursement.Desktop;

public sealed class ExportPreferences(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EiriReimbursementHelper", "export-settings.json");

    public string? LoadDirectory()
    {
        if (!File.Exists(_filePath)) return null;
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_filePath));
        return string.IsNullOrWhiteSpace(settings?.Directory) ? null : settings.Directory;
    }

    public void SaveDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
                throw new IOException("请选择一个存在的完整文件夹路径。");
            directory = Path.GetFullPath(directory);
        }
        else directory = null;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        string temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Settings(directory)));
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Settings(string? Directory);
}
