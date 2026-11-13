// Query keys of PPK (008). Every PPK write invalidates `all`: a declaration changes the employee's rates, the
// candidate list and the re-enrolment list at once.
export const ppkKeys = {
	all: ["ppk"] as const,
	settings: (companyId: string) => [...ppkKeys.all, "settings", companyId] as const,
	candidates: (companyId: string, on: string) =>
		[...ppkKeys.all, "candidates", companyId, on] as const,
	reenrollment: (companyId: string, year: number) =>
		[...ppkKeys.all, "reenrollment", companyId, year] as const,
	employee: (employeeId: string) => [...ppkKeys.all, "employee", employeeId] as const,
	my: (employeeId: string) => [...ppkKeys.all, "my", employeeId] as const,
};
