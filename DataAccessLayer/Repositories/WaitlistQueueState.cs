namespace DataAccessLayer.Repositories;

public sealed record WaitlistQueueState(int WaitingCount, bool HasWaitingForUser);
