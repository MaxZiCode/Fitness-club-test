using System.ComponentModel.DataAnnotations;
using ServiceLayer.Schedule;

namespace WebApi.Schedule;

public sealed record ScheduleRequest(
    [Required] DateTimeOffset? From,
    [Required] DateTimeOffset? To,
    long? RoomId,
    [Range(1, 100)] int Limit = 20,
    string? ContinuationToken = null) : IValidatableObject
{
    public ScheduleQuery ToQuery(long userId) => new(
        From.GetValueOrDefault().UtcDateTime,
        To.GetValueOrDefault().UtcDateTime,
        RoomId,
        Limit,
        ContinuationToken,
        userId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From.HasValue && To.HasValue && From.Value >= To.Value)
        {
            yield return new ValidationResult(
                "'to' must be later than 'from'.",
                [nameof(To)]);
        }
    }
}
