using System;
using System.Security.Cryptography;
using System.Text;

namespace 币安量化机器人.Services;

public static class SecretVaultService
{
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(plainText);
        byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
        {
            return string.Empty;
        }

        byte[] encrypted = Convert.FromBase64String(cipher);
        byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }
}
