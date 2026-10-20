using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public sealed record ClassWithEnrollmentAndBookedSeats(
    Class Class,
    int BookedSeats,
    Enrollment Enrollment);
