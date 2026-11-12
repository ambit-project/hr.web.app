export const auditKeys = {
	all: ["audit"] as const,
	list: (params: unknown) => [...auditKeys.all, "list", params] as const,
};
