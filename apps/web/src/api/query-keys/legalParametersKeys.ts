export const legalParametersKeys = {
	all: ["legal-parameters"] as const,
	lists: () => [...legalParametersKeys.all, "list"] as const,
	list: (params: unknown) => [...legalParametersKeys.lists(), params] as const,
};
