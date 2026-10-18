using System.Security.Cryptography;
using System.Text;

namespace ServiceLayer.Idempotency;

public sealed class IdempotencyKeyHasher : IIdempotencyKeyHasher
{
    public string Hash(string idempotencyKey)
    {
        var bytes = Encoding.UTF8.GetBytes(idempotencyKey);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
