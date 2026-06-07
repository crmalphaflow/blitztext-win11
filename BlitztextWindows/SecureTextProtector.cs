using System.Security.Cryptography;
using System.Text;

namespace BlitztextWindows;

public static class SecureTextProtector
{
    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var data = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return "";
        }

        try
        {
            var encrypted = Convert.FromBase64String(protectedValue);
            var data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return "";
        }
    }
}
