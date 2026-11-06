using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Medvya.BuildingBlocks.Infrastructure;

/// <summary>
/// Npgsql refuses to write a DateTimeOffset whose offset is not zero to timestamptz. Clients send local offsets (+05:30), so every
/// DateTimeOffset is normalised to UTC on write (AGENTS 3.5). Call from each DbContext's ConfigureConventions.
/// </summary>
public static class UtcDateTimeOffset
{
    public static void Apply(ModelConfigurationBuilder builder) =>
        builder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();

    sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
}
