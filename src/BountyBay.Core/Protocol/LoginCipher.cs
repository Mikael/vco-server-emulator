using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BountyBay.Core.Protocol;

public static class LoginCipher
{
    private static readonly byte[] AccountKey = Convert.FromHexString("66F928E8A1009B7629A01F8DEF458919");
    private static readonly byte[] PasswordKey = Convert.FromHexString("106EBA557761C77E1EEA0DEB676293DC");
    private static readonly uint[] AccountMask = [0xF8EBD0A7, 0xC4BADC9C, 0xA84FD0A2, 0x74CF24B7, 0x26E24C92, 0x38EFDA09, 0xC3B2D047, 0x28EFD49C];
    private static readonly uint[] PasswordMask = [0xF24D83E5, 0xAB2526D8, 0x32B93688, 0xAFFDFE20];

    public static string DecryptAccount(ReadOnlySpan<byte> encrypted)
    {
        byte[] plain = new byte[32];
        Decrypt(AccountKey, encrypted[..16], plain.AsSpan(0, 16));
        Decrypt(AccountKey, encrypted[16..32], plain.AsSpan(16, 16));
        Unmask(plain, AccountMask);
        int end = Array.IndexOf(plain, (byte)0);
        return Encoding.Latin1.GetString(plain, 0, end < 0 ? plain.Length : end);
    }

    public static string DecryptPasswordHash(ReadOnlySpan<byte> field)
    {
        byte[] head = new byte[16];
        Decrypt(PasswordKey, field[..16], head);
        Unmask(head, PasswordMask);
        return (Encoding.Latin1.GetString(head) + Encoding.Latin1.GetString(field[16..32])).TrimEnd('\0').ToLowerInvariant();
    }

    private static void Unmask(byte[] data, uint[] mask)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            Span<byte> word = data.AsSpan(4 * i, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(word, BinaryPrimitives.ReadUInt32LittleEndian(word) ^ mask[i]);
        }
    }

    private static void Decrypt(byte[] key, ReadOnlySpan<byte> input, Span<byte> output)
    {
        byte[] a = input[..8].ToArray();
        byte[] b = input[8..16].ToArray();
        byte[] first = key[..8];
        byte[] second = key[8..16];
        a = Single(first, a, encrypt: false);
        b = Single(first, b, encrypt: false);
        Swap(a, b);
        a = Single(second, a, encrypt: true);
        b = Single(second, b, encrypt: true);
        Swap(a, b);
        a = Single(first, a, encrypt: false);
        b = Single(first, b, encrypt: false);
        a.CopyTo(output[..8]);
        b.CopyTo(output[8..16]);
    }

    private static void Swap(byte[] a, byte[] b)
    {
        for (int i = 0; i < 4; i++) (a[4 + i], b[i]) = (b[i], a[4 + i]);
    }

    private static byte[] Single(byte[] key, byte[] block, bool encrypt)
    {
        using DES des = DES.Create();
        des.Key = key;
        return encrypt ? des.EncryptEcb(block, PaddingMode.None) : des.DecryptEcb(block, PaddingMode.None);
    }
}
