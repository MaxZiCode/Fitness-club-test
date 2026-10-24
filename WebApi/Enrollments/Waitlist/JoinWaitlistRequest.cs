using System.ComponentModel.DataAnnotations;
using ServiceLayer.Enrollments.Waitlist;

namespace WebApi.Enrollments.Waitlist;

public sealed record JoinWaitlistRequest(
    [Required] long? ClassId,
    [Required, Range(1, int.MaxValue)] int? Seats)
{
    public JoinWaitlistCommand ToCommand(string idempotencyKey, long userId) =>
        new(ClassId!.Value, Seats!.Value, idempotencyKey, userId);
}
