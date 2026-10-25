using System.ComponentModel.DataAnnotations;
using ServiceLayer.Analytics;

namespace WebApi.Analytics;

public sealed record LoadRequest(
    [Required] DateOnly? From,
    [Required] DateOnly? To,
    long? RoomId) : IValidatableObject
{
    public LoadQuery ToQuery() => new(From!.Value, To!.Value, RoomId);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From.HasValue && To.HasValue && To.Value < From.Value)
        {
            yield return new ValidationResult(
                "'to' must not be earlier than 'from'.",
                [nameof(To)]);
        }
    }
}
