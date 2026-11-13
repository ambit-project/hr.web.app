export const taxDeclarationsKeys = {
	all: ["tax-declarations"] as const,
	byEmployee: (employeeId: string, onlyActive: boolean) =>
		[...taxDeclarationsKeys.all, "employee", employeeId, { onlyActive }] as const,
};
