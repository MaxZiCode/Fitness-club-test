using DataAccessLayer.Entities;

namespace DataAccessLayer.Repositories;

public sealed record ClassWithBookedSeats(
    Class Class,
    int BookedSeats);
