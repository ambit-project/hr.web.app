export const employmentsKeys = {
	all: ["employments"] as const,
	byEmployee: (employeeId: string) => [...employmentsKeys.all, "employee", employeeId] as const,
	detail: (id: string) => [...employmentsKeys.all, "detail", id] as const,
};
