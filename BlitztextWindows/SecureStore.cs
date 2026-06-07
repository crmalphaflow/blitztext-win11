using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BlitztextWindows;

public sealed class SecureStore
{
    private readonly string keyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Blitztext",
        "openai.key");

    public void SaveApiKey(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        var data = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(keyPath, encrypted);
    }

    public string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(keyPath))
            {
                return null;
            }

            var encrypted = File.ReadAllBytes(keyPath);
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }

    public bool HasApiKey() => !string.IsNullOrWhiteSpace(LoadApiKey());

    public void DeleteApiKey()
    {
        try { File.Delete(keyPath); } catch { }
    }
}
