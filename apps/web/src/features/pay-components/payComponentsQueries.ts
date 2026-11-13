import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddEmploymentComponentRequest,
	CreateComponentTypeRequest,
	CreateConsentRequest,
	CreateGarnishmentRequest,
	EndConsentRequest,
	EndEmploymentComponentRequest,
	SetGarnishmentStatusRequest,
	UpdateComponentTypeRequest,
	UpdateConsentRequest,
	UpdateGarnishmentRequest,
} from "#/api/models";
import { payComponentsKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import {
	addEmploymentComponentFn,
	createComponentTypeFn,
	createConsentFn,
	createGarnishmentFn,
	deleteConsentFn,
	deleteEmploymentComponentFn,
	deleteGarnishmentFn,
	endConsentFn,
	endEmploymentComponentFn,
	listComponentTypesFn,
	listConsentsFn,
	listEmploymentComponentsFn,
	listGarnishmentsFn,
	setGarnishmentStatusFn,
	updateComponentTypeFn,
	updateConsentFn,
	updateGarnishmentFn,
} from "./payComponents.functions";

export function componentTypesQuery(includeInactive = false) {
	return queryOptions({
		queryKey: payComponentsKeys.types(includeInactive),
		queryFn: () => listComponentTypesFn({ data: { includeInactive } }),
	});
}

/** Reading contract components (amounts) writes a read-audit entry, so the page does not refetch on focus. */
export function employmentComponentsQuery(employmentId: string) {
	return queryOptions({
		queryKey: payComponentsKeys.byEmployment(employmentId),
		queryFn: () => listEmploymentComponentsFn({ data: employmentId }),
		staleTime: Number.POSITIVE_INFINITY,
		refetchOnWindowFocus: false,
	});
}

export function consentsQuery(employeeId: string) {
	return queryOptions({
		queryKey: payComponentsKeys.consents(employeeId),
		queryFn: () => listConsentsFn({ data: employeeId }),
	});
}

/** Garnishments are read-audited (debts are sensitive data), so no refetch on focus or remount. */
export function garnishmentsQuery(employeeId: string) {
	return queryOptions({
		queryKey: payComponentsKeys.garnishments(employeeId),
		queryFn: () => listGarnishmentsFn({ data: employeeId }),
		staleTime: Number.POSITIVE_INFINITY,
		refetchOnWindowFocus: false,
	});
}

function usePayComponentsMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the data on screen is stale.
		onSettled: () => queryClient.invalidateQueries({ queryKey: payComponentsKeys.all }),
	});
}

export function useCreateComponentType() {
	return usePayComponentsMutation(async (request: CreateComponentTypeRequest) =>
		unwrap(await createComponentTypeFn({ data: request })),
	);
}

export function useUpdateComponentType() {
	return usePayComponentsMutation(
		async (variables: { id: string; request: UpdateComponentTypeRequest }) =>
			unwrap(await updateComponentTypeFn({ data: variables })),
	);
}

export function useAddEmploymentComponent() {
	return usePayComponentsMutation(
		async (variables: { employmentId: string; request: AddEmploymentComponentRequest }) =>
			unwrap(await addEmploymentComponentFn({ data: variables })),
	);
}

export function useEndEmploymentComponent() {
	return usePayComponentsMutation(
		async (variables: { id: string; request: EndEmploymentComponentRequest }) =>
			unwrap(await endEmploymentComponentFn({ data: variables })),
	);
}

export function useDeleteEmploymentComponent() {
	return usePayComponentsMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteEmploymentComponentFn({ data: variables })),
	);
}

export function useCreateConsent() {
	return usePayComponentsMutation(
		async (variables: { employeeId: string; request: CreateConsentRequest }) =>
			unwrap(await createConsentFn({ data: variables })),
	);
}

export function useUpdateConsent() {
	return usePayComponentsMutation(
		async (variables: { id: string; request: UpdateConsentRequest }) =>
			unwrap(await updateConsentFn({ data: variables })),
	);
}

export function useEndConsent() {
	return usePayComponentsMutation(async (variables: { id: string; request: EndConsentRequest }) =>
		unwrap(await endConsentFn({ data: variables })),
	);
}

export function useDeleteConsent() {
	return usePayComponentsMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteConsentFn({ data: variables })),
	);
}

export function useCreateGarnishment() {
	return usePayComponentsMutation(
		async (variables: { employeeId: string; request: CreateGarnishmentRequest }) =>
			unwrap(await createGarnishmentFn({ data: variables })),
	);
}

export function useUpdateGarnishment() {
	return usePayComponentsMutation(
		async (variables: { id: string; request: UpdateGarnishmentRequest }) =>
			unwrap(await updateGarnishmentFn({ data: variables })),
	);
}

export function useSetGarnishmentStatus() {
	return usePayComponentsMutation(
		async (variables: { id: string; request: SetGarnishmentStatusRequest }) =>
			unwrap(await setGarnishmentStatusFn({ data: variables })),
	);
}

export function useDeleteGarnishment() {
	return usePayComponentsMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteGarnishmentFn({ data: variables })),
	);
}
