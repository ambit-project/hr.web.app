using HrWebApp.Api.Database;
using Npgsql;

namespace HrWebApp.Api.Features.Employments;

/// <summary>
/// Pola wersji warunków — wspólne dla nowej umowy i nowej wersji. Które są wymagane, zależy od rodzaju umowy
/// i sprawdza to baza (specyfikacja 004, R10–R13): <c>PayBasis</c> = <c>monthly</c>, <c>hourly</c>, <c>lumpSum</c>;
/// <c>CostKind</c> = <c>standard</c>, <c>increased</c>, <c>flat20</c>, <c>author50</c>; <c>Insurances</c> z
/// <c>pension</c>, <c>disability</c>, <c>sickness</c>, <c>accident</c>, <c>health</c>, <c>fp</c>, <c>fgsp</c>.
/// </summary>
public interface IEmploymentTermsFields
{
    Guid? PositionId { get; }
    int? FteNumerator { get; }
    int? FteDenominator { get; }
    string? PayBasis { get; }
    decimal? BaseAmount { get; }
    string? ZusTitleCode { get; }
    string? ZusPensionCode { get; }
    string? ZusDisabilityCode { get; }
    string[]? Insurances { get; }
    string? CostKind { get; }
}

public static class EmploymentTermsFields
{
    /// <summary>Parametry w kolejności rutyn employment_create i terms_add.</summary>
    public static NpgsqlParameter[] Params(IEmploymentTermsFields terms) =>
        [
            Db.Param(terms.PositionId),
            Db.Param(terms.FteNumerator),
            Db.Param(terms.FteDenominator),
            Db.Param(terms.PayBasis),
            Db.Param(terms.BaseAmount),
            Db.Param(terms.ZusTitleCode),
            Db.Param(terms.ZusPensionCode),
            Db.Param(terms.ZusDisabilityCode),
            Db.Param(terms.Insurances ?? []),
            Db.Param(terms.CostKind),
        ];
}
