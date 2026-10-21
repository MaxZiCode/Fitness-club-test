using ErrorOr;

namespace ServiceLayer.Schedule;

public interface IScheduleContinuationTokenService
{
    ErrorOr<ScheduleCursor> Decode(string token);

    string Encode(ScheduleCursor cursor);
}
