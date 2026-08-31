using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DiaryOut.App;

/// <summary>
/// 登录会话持久化：仅保存 token（相当于 Cookie），不保存密码。
/// 使用 DPAPI（当前用户）加密存储于 %LocalAppData%\DiaryOut\session.dat。
/// </summary>
public static class SessionStore
{
    private sealed record Session(string Token, long UserId, string? UserName);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DiaryOut", "session.dat");

    public static (string Token, long UserId, string? UserName)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<Session>(Encoding.UTF8.GetString(plain));
            return session is null || string.IsNullOrEmpty(session.Token)
                ? null
                : (session.Token, session.UserId, session.UserName);
        }
        catch
        {
            return null; // 损坏/跨用户时要求重新登录
        }
    }

    public static void Save(string token, long userId, string? userName)
    {
        var json = JsonSerializer.Serialize(new Session(token, userId, userName));
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, encrypted);
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { /* 忽略 */ }
    }
}
