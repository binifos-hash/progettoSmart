using System.Security.Cryptography;
using System.Text;

public static class PasswordHelper
{
    public static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        var combined = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);
        return Convert.ToBase64String(combined);
    }

    public static bool VerifyPassword(string storedBase64, string password)
    {
        try
        {
            var combined = Convert.FromBase64String(storedBase64);
            var salt = new byte[16];
            Buffer.BlockCopy(combined, 0, salt, 0, salt.Length);
            var hash = new byte[32];
            Buffer.BlockCopy(combined, salt.Length, hash, 0, hash.Length);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var testHash = pbkdf2.GetBytes(32);
            return testHash.SequenceEqual(hash);
        }
        catch
        {
            return false;
        }
    }

    public static string GenerateTemporaryPassword(int len = 10)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var sb = new StringBuilder(len);
        using var rng = RandomNumberGenerator.Create();
        var buf = new byte[4];
        for (var i = 0; i < len; i++)
        {
            rng.GetBytes(buf);
            var v = BitConverter.ToUInt32(buf, 0);
            sb.Append(chars[(int)(v % (uint)chars.Length)]);
        }
        return sb.ToString();
    }
}