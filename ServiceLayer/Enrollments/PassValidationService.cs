using DataAccessLayer.Entities;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class PassValidationService : IPassValidationService
{
    public ErrorOr<Success> ValidateEnrollmentCreate(Pass pass, int requestedSeats, DateOnly date)
    {
        return ValidatePassExpired(pass, date)
            .ThenEnsure(_ => ValidatePassExhausted(pass, requestedSeats));
    }

    public ErrorOr<Success> ValidateEnrollmentUpdate(Pass pass, int difference, DateOnly date)
    {
        if (difference > 0)
        {
            return ValidatePassExpired(pass, date)
                .ThenEnsure(_ => ValidatePassExhausted(pass, difference));
        }

        return Result.Success;
    }

    private static ErrorOr<Success> ValidatePassExpired(Pass pass, DateOnly date)
    {
        if (pass.ValidUntil < date)
            return PassErrors.PassExpired;

        return Result.Success;
    }

    private static ErrorOr<Success> ValidatePassExhausted(Pass pass, int requestedSeats)
    {
        if (pass.RemainingVisits < requestedSeats)
            return PassErrors.PassExhausted;

        return Result.Success;
    }
}
