using DataAccessLayer.UnitOfWork;
using ErrorOr;

namespace ServiceLayer.Enrollments;

public sealed class EnrollmentCardService(
    IUnitOfWork unitOfWork,
    IEnrollmentCardCacheService enrollmentCardCacheService) : IEnrollmentCardService
{
    public async Task<ErrorOr<EnrollmentCard>> GetEnrollmentCardAsync(
        GetEnrollmentCardCommand command,
        CancellationToken ct = default)
    {
        var card = await enrollmentCardCacheService.GetOrCreateAsync(
            command.UserId,
            command.EnrollmentId,
            async ct =>
            {
                var enrollmentEntity = await unitOfWork.Enrollment.GetByIdAndUserWithClassAndRoomNoTrackingAsync(
                    command.EnrollmentId,
                    command.UserId,
                    ct);
                if (enrollmentEntity is null)
                    return null;

                var pass = await unitOfWork.Pass.GetByUserNoTrackingAsync(
                    command.UserId,
                    ct);

                return EnrollmentCard.FromEnrollment(enrollmentEntity, pass);
            },
            ct);

        return card is null ? Error.NotFound() : card;
    }
}
