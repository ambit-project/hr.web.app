using HrWebApp.Api.Database;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HrWebApp.Api.Features.Ppk;

public static class ListPpkSettings
{
    /// <summary>
    /// Okresy ustawień PPK spółki od najnowszego: instytucja finansowa, umowa o zarządzanie i wpłata dodatkowa
    /// pracodawcy w procentach (0–2,5). Pusty <c>ValidTo</c> — okres otwarty.
    /// </summary>
    public sealed record Item(
        Guid Id,
        DateOnly ValidFrom,
        DateOnly? ValidTo,
        string InstitutionName,
        string AgreementNumber,
        DateOnly AgreementDate,
        decimal EmployerAdditionalRate
    );

    public static async Task<Ok<List<Item>>> Handle(
        Guid id,
        PpkSettingsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(await handler.ListAsync(id, cancellationToken));
}

public static class AddPpkSettings
{
    /// <summary>
    /// Nowy okres od 1. dnia miesiąca (<c>validFrom</c>: <c>invalid</c>, <c>beforeCurrent</c>); ten sam dzień co
    /// ostatni okres — jego zmiana, późniejszy — poprzedni kończy się dzień wcześniej. Wpłata dodatkowa pracodawcy
    /// 0–2,5%.
    /// </summary>
    public sealed record Request(
        DateOnly? ValidFrom,
        string? InstitutionName,
        string? AgreementNumber,
        DateOnly? AgreementDate,
        decimal? EmployerAdditionalRate
    );

    public sealed record Response(Guid Id);

    public static async Task<Ok<Response>> Handle(
        Guid id,
        Request request,
        PpkSettingsHandler handler,
        CancellationToken cancellationToken
    ) => TypedResults.Ok(new Response(await handler.AddAsync(id, request, cancellationToken)));
}

public sealed class PpkSettingsHandler(DbSession db)
{
    public async Task<List<ListPpkSettings.Item>> ListAsync(
        Guid companyId,
        CancellationToken cancellationToken
    ) =>
        await db.QueryAsync(
            """
            SELECT id, valid_from, valid_to, institution_name, agreement_number, agreement_date,
                   employer_additional_rate
            FROM ppk_app.company_settings_list($1)
            """,
            [Db.Param(companyId)],
            reader => new ListPpkSettings.Item(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateOnly>(5),
                reader.GetDecimal(6)
            ),
            cancellationToken
        );

    public async Task<Guid> AddAsync(
        Guid companyId,
        AddPpkSettings.Request request,
        CancellationToken cancellationToken
    ) =>
        await db.ScalarAsync<Guid>(
            "CALL ppk_app.company_settings_set($1, $2, $3, $4, $5, $6, NULL)",
            [
                Db.Param(companyId),
                Db.Param(request.ValidFrom),
                Db.Param(request.InstitutionName),
                Db.Param(request.AgreementNumber),
                Db.Param(request.AgreementDate),
                Db.Param(request.EmployerAdditionalRate),
            ],
            cancellationToken
        );
}
