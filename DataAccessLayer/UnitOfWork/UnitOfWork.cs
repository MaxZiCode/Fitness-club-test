using Common.Database.UnitOfWork;
using DataAccessLayer.Entities;
using DataAccessLayer.Repositories;

namespace DataAccessLayer.UnitOfWork;

public interface IUnitOfWork : IUnitOfWorkBase
{
    IRoomRepository Room { get; }

    IClassRepository Class { get; }

    IEnrollmentRepository Enrollment { get; }

    IWaitlistEntryRepository WaitlistEntry { get; }

    IPassRepository Pass { get; }
}

public class UnitOfWork : UnitOfWorkBase, IUnitOfWork
{
    public UnitOfWork(ClubDbContext dbContext)
        : base(dbContext)
    {
    }

    public IRoomRepository Room => GetRepository<Room, IRoomRepository>(set => new RoomRepository(set));

    public IClassRepository Class => GetRepository<Class, IClassRepository>(set => new ClassRepository(set));

    public IEnrollmentRepository Enrollment =>
        GetRepository<Enrollment, IEnrollmentRepository>(set => new EnrollmentRepository(set));

    public IWaitlistEntryRepository WaitlistEntry =>
        GetRepository<WaitlistEntry, IWaitlistEntryRepository>(
            set => new WaitlistEntryRepository(set));

    public IPassRepository Pass => GetRepository<Pass, IPassRepository>(set => new PassRepository(set));
}
