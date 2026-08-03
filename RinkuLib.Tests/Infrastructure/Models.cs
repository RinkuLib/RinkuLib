using RinkuLib.DbParsing;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>Matches the Users table of <see cref="SqliteDb"/>, usable without the Email column.</summary>
public record UserRow(long ID, string Name, string? Email = null);

/// <summary>Matches the Users table with a renamed member.</summary>
public record NamedUser(long ID, [Alt("Name")] string Username, string? Email);
