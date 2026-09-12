using System.IO;
using Eiri.Reimbursement.Desktop;

namespace Eiri.Reimbursement.Desktop.Tests;

public sealed class ExportPreferencesTests
{
    [Fact]
    public void ExportDirectorySurvivesReopeningAndCanBeCleared()
    {
        string root = Path.Combine(Path.GetTempPath(), "eiri-export-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "settings.json");
            var preferences = new ExportPreferences(path);
            Assert.Null(preferences.LoadDirectory());
            preferences.SaveDirectory(root);
            Assert.Equal(root, new ExportPreferences(path).LoadDirectory());
            Assert.Throws<IOException>(() => preferences.SaveDirectory(Path.Combine(root, "missing")));
            Assert.Equal(root, new ExportPreferences(path).LoadDirectory());
            new ExportPreferences(path).SaveDirectory(null);
            Assert.Null(preferences.LoadDirectory());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
