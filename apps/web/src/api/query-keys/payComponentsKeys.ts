// One area for the whole module 006: consents, contract components and garnishments refer to each other (a consent in
// use, a component type in use), so a change anywhere reads the whole area again.
export const payComponentsKeys = {
	all: ["pay-components"] as const,
	types: (includeInactive: boolean) =>
		[...payComponentsKeys.all, "types", { includeInactive }] as const,
	byEmployment: (employmentId: string) =>
		[...payComponentsKeys.all, "employment", employmentId] as const,
	consents: (employeeId: string) => [...payComponentsKeys.all, "consents", employeeId] as const,
	garnishments: (employeeId: string) =>
		[...payComponentsKeys.all, "garnishments", employeeId] as const,
};
