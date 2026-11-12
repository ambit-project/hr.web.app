export const usersKeys = {
	all: ["users"] as const,
	lists: () => [...usersKeys.all, "list"] as const,
	list: (params: unknown) => [...usersKeys.lists(), params] as const,
};
