namespace ServiceLayer.Idempotency;

public interface IIdempotencyKeyHasher
{
    string Hash(string idempotencyKey);
}
