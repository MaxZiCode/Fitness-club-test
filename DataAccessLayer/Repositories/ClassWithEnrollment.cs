using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public sealed record ClassWithEnrollment(
    Class Class,
    Enrollment Enrollment);
