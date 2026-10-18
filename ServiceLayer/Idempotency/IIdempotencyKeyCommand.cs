namespace ServiceLayer.Idempotency;

public interface IIdempotencyKeyCommand
{
    string IdempotencyKey { get; }
}
