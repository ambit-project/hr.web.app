export const companiesKeys = {
	all: ["companies"] as const,
	lists: () => [...companiesKeys.all, "list"] as const,
	list: (params: unknown) => [...companiesKeys.lists(), params] as const,
};
