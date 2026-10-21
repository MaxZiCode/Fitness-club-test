using System.Text;
using System.Text.Json;
using ErrorOr;

namespace ServiceLayer.Schedule;

public sealed class ScheduleContinuationTokenService : IScheduleContinuationTokenService
{
    public ErrorOr<ScheduleCursor> Decode(string token)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var cursor = JsonSerializer.Deserialize<ScheduleCursor>(json);
            return cursor is null
                ? InvalidToken()
                : cursor;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return InvalidToken();
        }
    }

    public string Encode(ScheduleCursor cursor) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)));

    private static Error InvalidToken() =>
        ScheduleErrors.InvalidContinuationToken;
}
