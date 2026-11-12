export const zusCodesKeys = {
	all: ["zus-codes"] as const,
	lists: () => [...zusCodesKeys.all, "list"] as const,
	list: (params: unknown) => [...zusCodesKeys.lists(), params] as const,
};
