using Npgsql;

namespace HrWebApp.Api.Database;

public static class Db
{
    /// <summary>Parametr pozycyjny ($1, $2, …) z typem z C# — null zostaje typowanym NULL-em.</summary>
    public static NpgsqlParameter<T> Param<T>(T value) => new() { TypedValue = value };
}
