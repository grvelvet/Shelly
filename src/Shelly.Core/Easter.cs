using System.Security.Cryptography;
using System.Text;

namespace Shelly.Core;

/// <summary>Easter egg in About. Three clicks within <see cref="Window"/> open it; the caption is picked at
/// random from <see cref="CaptionKeys"/> (never the same one twice in a row).</summary>
public sealed class TripleClick
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);
    readonly List<DateTime> clicks = new();

    /// <summary>Records a click; true on the third click within the window (then it starts over).</summary>
    public bool Click(DateTime now)
    {
        clicks.RemoveAll(t => now - t > Window || t > now);
        clicks.Add(now);
        if (clicks.Count < 3) return false;
        clicks.Clear();
        return true;
    }
}

public static class Mascot
{
    /// <summary>The encrypted mascot image payload, embedded as a WPF Resource of the Shelly project (450 x 600, about 42 KB).</summary>
    public const string ResourcePath = "Assets/mascot.dat";
    public const string TitleKey = "eggTitle";
    public static readonly string[] CaptionKeys = { "eggCaption1", "eggCaption2", "eggCaption3", "eggCaption4", "eggCaption5" };

    /// <summary>Derives the 256-bit AES key for the mascot payload.</summary>
    static byte[] DeriveKey()
    {
        var seed = "Shelly::Mascot::Secret::2026::" + TitleKey + "::" + string.Join(';', CaptionKeys) + "::" + Links.DonateShort;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        byte[] mask = { 0x53, 0x68, 0x65, 0x6C, 0x6C, 0x79, 0x4D, 0x61, 0x73, 0x63, 0x6F, 0x74, 0x32, 0x30, 0x32, 0x36 };
        for (int i = 0; i < hash.Length; i++)
            hash[i] ^= mask[i % mask.Length];
        return hash;
    }

    /// <summary>Encrypts raw image bytes with AES-CBC and prepends the 16-byte random IV.</summary>
    public static byte[] EncryptImage(byte[] plainData)
    {
        ArgumentNullException.ThrowIfNull(plainData);
        byte[] key = DeriveKey();
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        byte[] cipher = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);
        byte[] result = new byte[16 + cipher.Length];
        Buffer.BlockCopy(iv, 0, result, 0, 16);
        Buffer.BlockCopy(cipher, 0, result, 16, cipher.Length);
        return result;
    }

    /// <summary>Decrypts the encrypted mascot payload into raw JPEG bytes.</summary>
    public static byte[] DecryptImage(byte[] cipherData)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        if (cipherData.Length < 32)
            throw new CryptographicException("Cipher data is too short to be a valid encrypted payload.");
        byte[] key = DeriveKey();
        byte[] iv = new byte[16];
        Buffer.BlockCopy(cipherData, 0, iv, 0, 16);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherData, 16, cipherData.Length - 16);
    }

    /// <summary>Decrypts an encrypted stream into raw JPEG bytes.</summary>
    public static byte[] DecryptImage(Stream cipherStream)
    {
        ArgumentNullException.ThrowIfNull(cipherStream);
        using var ms = new MemoryStream();
        cipherStream.CopyTo(ms);
        return DecryptImage(ms.ToArray());
    }

    /// <summary>A caption index other than <paramref name="last"/> (when there is a choice).</summary>
    public static int NextCaption(Random rnd, int last)
    {
        int n = CaptionKeys.Length;
        if (n < 2) return 0;
        int i = rnd.Next(n - 1);
        return last >= 0 && last < n && i >= last ? i + 1 : i;
    }
}
