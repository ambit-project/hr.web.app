// Query keys of payroll lists (009). Every payroll write invalidates `all`: calculating a list changes its summary,
// its payslips and the year totals at once.
export const payrollKeys = {
	all: ["payroll"] as const,
	settings: (companyId: string) => [...payrollKeys.all, "settings", companyId] as const,
	lists: (companyId: string, year: number) =>
		[...payrollKeys.all, "lists", companyId, year] as const,
	payroll: (id: string) => [...payrollKeys.all, "payroll", id] as const,
	members: (id: string) => [...payrollKeys.all, "members", id] as const,
	entries: (id: string) => [...payrollKeys.all, "entries", id] as const,
	entry: (id: string) => [...payrollKeys.all, "entry", id] as const,
	yearTotals: (employeeId: string, year: number) =>
		[...payrollKeys.all, "yearTotals", employeeId, year] as const,
};
