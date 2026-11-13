namespace HrWebApp.Api.Features.Documents;

public static class DocumentsEndpoints
{
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        services.AddScoped<PayslipsHandler>();
        services.AddScoped<TaxFormsHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/payroll-entries/{id:guid}")
            .WithTags("Documents")
            .MapGet("/payslip", GetPayslip.Handle)
            .WithName("GetPayslip")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var employee = app.MapGroup("/employees/{id:guid}").WithTags("Documents");
        employee
            .MapGet("/payslips", ListPayslips.HandleEmployee)
            .WithName("ListEmployeePayslips")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        employee
            .MapGet("/wage-card/{year:int}", GetWageCard.Handle)
            .WithName("GetWageCard")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        employee
            .MapGet("/tax-forms", ListTaxForms.HandleEmployee)
            .WithName("ListEmployeeTaxForms")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var company = app.MapGroup("/companies/{id:guid}").WithTags("Documents");
        company
            .MapGet("/pit11/{year:int}", ListPit11.Handle)
            .WithName("ListPit11")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapPost("/pit11/{year:int}", GeneratePit11.Handle)
            .WithName("GeneratePit11")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        company
            .MapGet("/tax-summary/{year:int}", GetTaxSummary.Handle)
            .WithName("GetTaxSummary")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var taxForm = app.MapGroup("/tax-forms/{id:guid}").WithTags("Documents");
        taxForm
            .MapGet("/", GetTaxForm.Handle)
            .WithName("GetTaxForm")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        taxForm
            .MapGet("/xml", GetTaxFormXml.Handle)
            .WithName("GetTaxFormXml")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        taxForm
            .MapPost("/sent", MarkTaxFormSent.Handle)
            .WithName("MarkTaxFormSent")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        taxForm
            .MapPost("/given", MarkTaxFormGiven.Handle)
            .WithName("MarkTaxFormGiven")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGroup("/deduction-consents/{id:guid}")
            .WithTags("Documents")
            .MapGet("/document", GetConsentDocument.Handle)
            .WithName("GetConsentDocument")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var my = app.MapGroup("/my/{employeeId:guid}").WithTags("MyDocuments");
        my.MapGet("/payslips", ListPayslips.HandleMy)
            .WithName("ListMyPayslips")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        my.MapGet("/tax-forms", ListTaxForms.HandleMy)
            .WithName("ListMyTaxForms")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
