// One area for module 007: absences, leave limits, overtime and the norm all read the company calendar and each
// other (an absence changes the leave balance, a calendar day changes hours of absences), so a change anywhere reads
// the whole area again.
export const timeAbsenceKeys = {
	all: ["time-absence"] as const,
	holidays: (year: number) => [...timeAbsenceKeys.all, "holidays", year] as const,
	calendarYear: (companyId: string, year: number) =>
		[...timeAbsenceKeys.all, "calendar", companyId, year] as const,
	calendarMonth: (companyId: string, year: number, month: number) =>
		[...timeAbsenceKeys.all, "calendar", companyId, year, month] as const,
	absenceTypes: () => [...timeAbsenceKeys.all, "absence-types"] as const,
	absences: (employmentId: string, year: number) =>
		[...timeAbsenceKeys.all, "absences", employmentId, year] as const,
	leave: (employmentId: string, year: number, proposal?: { baseDays: number; firstJob: boolean }) =>
		[...timeAbsenceKeys.all, "leave", employmentId, year, proposal ?? null] as const,
	norm: (employmentId: string, year: number) =>
		[...timeAbsenceKeys.all, "norm", employmentId, year] as const,
	overtime: (employmentId: string, year: number) =>
		[...timeAbsenceKeys.all, "overtime", employmentId, year] as const,
	mandateHours: (employmentId: string, year: number) =>
		[...timeAbsenceKeys.all, "mandate-hours", employmentId, year] as const,
	myLeave: (employeeId: string, year: number) =>
		[...timeAbsenceKeys.all, "my-leave", employeeId, year] as const,
	myAbsences: (employeeId: string, year: number) =>
		[...timeAbsenceKeys.all, "my-absences", employeeId, year] as const,
};
