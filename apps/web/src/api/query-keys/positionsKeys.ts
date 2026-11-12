export const positionsKeys = {
	all: ["positions"] as const,
	lists: () => [...positionsKeys.all, "list"] as const,
	list: (params: unknown) => [...positionsKeys.lists(), params] as const,
};
