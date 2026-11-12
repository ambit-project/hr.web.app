using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HrWebApp.Api.Common;

public static class ApiDocument
{
    /// <summary>
    /// Dokument OpenAPI z ręcznie pisanych endpointów (nie z bazy). Migawka leży w apps/web/openapi/hr-v1.json
    /// i z niej orval generuje klienta frontu, więc dokument nie może zależeć od adresu, pod którym działa API.
    /// </summary>
    public static IServiceCollection AddApiDocument(this IServiceCollection services)
    {
        // Liczby tylko jako liczby: domyślne AllowReadingFromString daje w schemacie "integer | string".
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict
        );
        return services.AddOpenApi(options =>
        {
            // Nazwy schematów z typem zewnętrznym i argumentami generycznymi (ListPositionsItem,
            // PagedListOfListPositionsItem) — inaczej dwa zagnieżdżone Item zlałyby się w jeden schemat.
            options.CreateSchemaReferenceId = type =>
                OpenApiOptions.CreateDefaultSchemaReferenceId(type) is null
                    ? null
                    : SchemaName(type.Type);
            // orval czyta OpenAPI 3.1; 3.2 (domyślne w .NET 11) jeszcze nie.
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddDocumentTransformer(
                (document, _, _) =>
                {
                    document.Info = new OpenApiInfo { Title = "hr.web.app API", Version = "v1" };
                    document.Servers = [];
                    return Task.CompletedTask;
                }
            );
        });
    }

    private static string SchemaName(Type type)
    {
        var prefix = type.DeclaringType?.Name ?? "";
        if (!type.IsGenericType)
        {
            return prefix + type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return prefix
            + name
            + "Of"
            + string.Join("And", type.GetGenericArguments().Select(SchemaName));
    }
}
