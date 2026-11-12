export const taxOfficesKeys = {
	all: ["tax-offices"] as const,
	lists: () => [...taxOfficesKeys.all, "list"] as const,
	list: (params: unknown) => [...taxOfficesKeys.lists(), params] as const,
};
