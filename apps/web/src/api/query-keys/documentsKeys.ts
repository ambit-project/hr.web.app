// Query keys of documents (010). Every PIT-11 write invalidates `all`: generating forms changes the company list, the
// employee's versions and the printed form at once. Payslips and the wage card come from closed payroll lists, so a
// payroll write (payrollKeys) does not reach here — they are read fresh on each visit (no cache across lists).
export const documentsKeys = {
	all: ["documents"] as const,
	payslip: (entryId: string) => [...documentsKeys.all, "payslip", entryId] as const,
	payslips: (employeeId: string) => [...documentsKeys.all, "payslips", employeeId] as const,
	wageCard: (employeeId: string, year: number) =>
		[...documentsKeys.all, "wageCard", employeeId, year] as const,
	pit11: (companyId: string, year: number) =>
		[...documentsKeys.all, "pit11", companyId, year] as const,
	taxForm: (id: string) => [...documentsKeys.all, "taxForm", id] as const,
	taxForms: (employeeId: string) => [...documentsKeys.all, "taxForms", employeeId] as const,
	taxSummary: (companyId: string, year: number) =>
		[...documentsKeys.all, "taxSummary", companyId, year] as const,
	consentDocument: (id: string) => [...documentsKeys.all, "consentDocument", id] as const,
};
