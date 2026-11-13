namespace HrWebApp.Api.Features.TimeAbsence;

public static class TimeAbsenceEndpoints
{
    public static IServiceCollection AddTimeAbsence(this IServiceCollection services)
    {
        services.AddScoped<CalendarHandler>();
        services.AddScoped<AbsencesHandler>();
        services.AddScoped<LeaveHandler>();
        services.AddScoped<WorkTimeHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapTimeAbsence(this IEndpointRouteBuilder app)
    {
        var holidays = app.MapGroup("/holidays").WithTags("Holidays");
        holidays.MapGet("/", ListHolidays.Handle).WithName("ListHolidays");
        holidays
            .MapPost("/", AddHoliday.Handle)
            .WithName("AddHoliday")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);
        holidays
            .MapDelete("/{date}", DeleteHoliday.Handle)
            .WithName("DeleteHoliday")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var calendar = app.MapGroup("/companies/{id:guid}/calendar").WithTags("WorkCalendar");
        calendar
            .MapPost("/{year:int}", GenerateCalendar.Handle)
            .WithName("GenerateWorkCalendar")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        calendar
            .MapGet("/{year:int}", GetCalendarYear.Handle)
            .WithName("GetWorkCalendarYear")
            .ProducesProblem(StatusCodes.Status404NotFound);
        calendar
            .MapGet("/{year:int}/{month:int}", GetCalendarMonth.Handle)
            .WithName("GetWorkCalendarMonth")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        calendar
            .MapPut("/days/{date}", SetCalendarDay.Handle)
            .WithName("SetWorkCalendarDay")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGroup("/absence-types")
            .WithTags("Absences")
            .MapGet("/", ListAbsenceTypes.Handle)
            .WithName("ListAbsenceTypes");

        var byEmployment = app.MapGroup("/employments/{id:guid}");
        byEmployment
            .MapGet("/absences", ListAbsences.Handle)
            .WithTags("Absences")
            .WithName("ListAbsences")
            .ProducesProblem(StatusCodes.Status404NotFound);
        byEmployment
            .MapPost("/absences", CreateAbsence.Handle)
            .WithTags("Absences")
            .WithName("CreateAbsence")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapGet("/leave/{year:int}", GetLeaveEntitlement.Handle)
            .WithTags("Leave")
            .WithName("GetLeaveEntitlement")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapPut("/leave/{year:int}", SetLeaveEntitlement.Handle)
            .WithTags("Leave")
            .WithName("SetLeaveEntitlement")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapGet("/norm/{year:int}", GetEmploymentNorm.Handle)
            .WithTags("WorkCalendar")
            .WithName("GetEmploymentNorm")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapGet("/overtime", ListOvertime.Handle)
            .WithTags("Overtime")
            .WithName("ListOvertime")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapPost("/overtime", AddOvertime.Handle)
            .WithTags("Overtime")
            .WithName("AddOvertime")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapGet("/mandate-hours", ListMandateHours.Handle)
            .WithTags("MandateHours")
            .WithName("ListMandateHours")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        byEmployment
            .MapPost("/mandate-hours", AddMandateHours.Handle)
            .WithTags("MandateHours")
            .WithName("AddMandateHours")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var absences = app.MapGroup("/absences").WithTags("Absences");
        absences
            .MapPut("/{id:guid}", UpdateAbsence.Handle)
            .WithName("UpdateAbsence")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        absences
            .MapDelete("/{id:guid}", DeleteAbsence.Handle)
            .WithName("DeleteAbsence")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGroup("/overtime")
            .WithTags("Overtime")
            .MapDelete("/{id:guid}", DeleteOvertime.Handle)
            .WithName("DeleteOvertime")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var mandateHours = app.MapGroup("/mandate-hours").WithTags("MandateHours");
        mandateHours
            .MapPut("/{id:guid}", UpdateMandateHours.Handle)
            .WithName("UpdateMandateHours")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        mandateHours
            .MapDelete("/{id:guid}", DeleteMandateHours.Handle)
            .WithName("DeleteMandateHours")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        var my = app.MapGroup("/my/{employeeId:guid}").WithTags("MyTimeAbsence");
        my.MapGet("/leave/{year:int}", GetMyLeaveBalance.Handle)
            .WithName("GetMyLeaveBalance")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        my.MapGet("/absences/{year:int}", ListMyAbsences.Handle)
            .WithName("ListMyAbsences")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}
