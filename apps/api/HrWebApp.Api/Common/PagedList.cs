namespace HrWebApp.Api.Common;

public sealed record PagedList<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize);

public static class Paging
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static int Size(int? pageSize) =>
        Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

    public static int Number(int? page) => Math.Max(page ?? 1, 1);

    public static int Offset(int page, int size) => (page - 1) * size;
}
