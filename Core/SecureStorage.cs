using System.Security.Cryptography;
using System.Text;

namespace ProxyMaster.Core;

/// <summary>
/// Шифрует/расшифровывает чувствительные данные через Windows DPAPI.
/// Данные зашифрованы ключом текущего пользователя — другой пользователь
/// или другая машина не смогут расшифровать.
/// </summary>
internal static class SecureStorage
{
    // Дополнительная энтропия — усложняет перебор даже при доступе к файлу
    private static readonly byte[] _entropy =
        Encoding.UTF8.GetBytes("ProxyMaster-v1-3f8a9c2b");

    /// <summary>Шифрует строку. Возвращает Base64-строку или "" для пустого ввода.</summary>
    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        try
        {
            byte[] data      = Encoding.UTF8.GetBytes(plaintext);
            byte[] encrypted = ProtectedData.Protect(data, _entropy,
                                                     DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch { return ""; }
    }

    /// <summary>Расшифровывает строку, полученную через Protect(). Возвращает "" при ошибке.</summary>
    public static string Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return "";
        try
        {
            byte[] data      = Convert.FromBase64String(ciphertext);
            byte[] decrypted = ProtectedData.Unprotect(data, _entropy,
                                                       DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return ""; }
    }
}
