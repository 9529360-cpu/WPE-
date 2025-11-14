using System;
using System.Security.Cryptography;
using System.Text;

namespace 币安量化机器人.Services;

public static class SecretVaultService
{
    // Environment variable name for cross-platform AES key (Base64, 32 bytes for AES-256)
    private const string EnvKeyName = "SECRET_VAULT_KEY";

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        // Non-Windows fallback: AES-GCM with key from environment variable
        var keyBase64 = Environment.GetEnvironmentVariable(EnvKeyName);
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new PlatformNotSupportedException($"Secret encryption on non-Windows requires environment variable '{EnvKeyName}' containing a base64-encoded 32-byte key.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Environment variable '{EnvKeyName}' is not valid base64.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException($"Environment variable '{EnvKeyName}' must be 32 bytes when decoded (AES-256 key). Current length: {key.Length}");
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        // Try to use AesGcm constructor that accepts tag size via reflection when available
        var aesType = typeof(AesGcm);
        var ctorWithTag = aesType.GetConstructor(new[] { typeof(byte[]), typeof(int) });
        if (ctorWithTag != null)
        {
            using var aes = (AesGcm?)ctorWithTag.Invoke(new object[] { key, tag.Length });
            if (aes == null)
                throw new InvalidOperationException("Failed to construct AesGcm with tag length");
            aes.Encrypt(nonce, plaintextBytes, cipher, tag, null);
        }
        else
        {
            // Fallback to canonical constructor; suppress SYSLIB0053 warning for compatibility
#pragma warning disable SYSLIB0053
            using (var aes = new AesGcm(key))
            {
                aes.Encrypt(nonce, plaintextBytes, cipher, tag, null);
            }
#pragma warning restore SYSLIB0053
        }

        // store as: nonce|tag|cipher (all base64 concatenated with :)
        return string.Join(':', Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
    }

    public static string Decrypt(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
        {
            return string.Empty;
        }

        if (OperatingSystem.IsWindows())
        {
            byte[] encrypted = Convert.FromBase64String(cipher);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }

        // Non-Windows fallback expects format nonce:tag:cipher (base64 parts)
        var parts = cipher.Split(':');
        if (parts.Length != 3)
        {
            throw new FormatException("Invalid cipher format for non-Windows secret vault. Expected 'nonce:tag:cipher' base64 parts.");
        }

        var keyBase64 = Environment.GetEnvironmentVariable(EnvKeyName);
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new PlatformNotSupportedException($"Secret decryption on non-Windows requires environment variable '{EnvKeyName}' containing a base64-encoded 32-byte key.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Environment variable '{EnvKeyName}' is not valid base64.", ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException($"Environment variable '{EnvKeyName}' must be 32 bytes when decoded (AES-256 key). Current length: {key.Length}");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipherBytes = Convert.FromBase64String(parts[2]);
        var decrypted = new byte[cipherBytes.Length];

        var aesType = typeof(AesGcm);
        var ctorWithTag = aesType.GetConstructor(new[] { typeof(byte[]), typeof(int) });
        if (ctorWithTag != null)
        {
            using var aes = (AesGcm?)ctorWithTag.Invoke(new object[] { key, tag.Length });
            if (aes == null)
                throw new InvalidOperationException("Failed to construct AesGcm with tag length");
            aes.Decrypt(nonce, cipherBytes, tag, decrypted, null);
        }
        else
        {
#pragma warning disable SYSLIB0053
            using (var aes = new AesGcm(key))
            {
                aes.Decrypt(nonce, cipherBytes, tag, decrypted, null);
            }
#pragma warning restore SYSLIB0053
        }

        return Encoding.UTF8.GetString(decrypted);
    }
}
