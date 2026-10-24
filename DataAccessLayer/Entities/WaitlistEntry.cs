using Common.Database.Interfaces;

namespace DataAccessLayer.Entities;

public class WaitlistEntry : IEntity
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public long ClassId { get; set; }

    public int Seats { get; set; }

    public int InitialPosition { get; set; }

    public string Status { get; set; } = null!;

    public string IdempotencyKeyHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
