export const employeesKeys = {
	all: ["employees"] as const,
	lists: () => [...employeesKeys.all, "list"] as const,
	list: (params: unknown) => [...employeesKeys.lists(), params] as const,
	detail: (id: string) => [...employeesKeys.all, "detail", id] as const,
	addresses: (id: string) => [...employeesKeys.all, "addresses", id] as const,
	bankAccounts: (id: string) => [...employeesKeys.all, "bank-accounts", id] as const,
	readAudit: (id: string, params: unknown) =>
		[...employeesKeys.all, "read-audit", id, params] as const,
};
