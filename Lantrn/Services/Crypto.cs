using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Lantrn.Services;

public static class Crypto
{
    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    // URL-safe, so it can go into a link as it is.
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
