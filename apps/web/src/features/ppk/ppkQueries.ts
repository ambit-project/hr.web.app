import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddPpkSettingsRequest,
	CreatePpkDeclarationRequest,
	EnrollInPpkRequest,
} from "#/api/models";
import { ppkKeys } from "#/api/query-keys";
import { ProblemError, unwrap } from "#/lib/problem";
import {
	addPpkSettingsFn,
	applyPpkReenrollmentFn,
	createPpkDeclarationFn,
	deletePpkDeclarationFn,
	deletePpkParticipationFn,
	enrollInPpkFn,
	getEmployeePpkFn,
	getMyPpkFn,
	getPpkReenrollmentFn,
	listPpkCandidatesFn,
	listPpkSettingsFn,
	markPpkInformedFn,
} from "./ppk.functions";

/** A rule answer (4xx: a year without a cycle) does not change on a second try. */
const retryServerFailures = (failureCount: number, error: unknown) =>
	!(error instanceof ProblemError && error.problem.status < 500) && failureCount < 1;

export function ppkSettingsQuery(companyId: string) {
	return queryOptions({
		queryKey: ppkKeys.settings(companyId),
		queryFn: () => listPpkSettingsFn({ data: companyId }),
	});
}

export function ppkCandidatesQuery(companyId: string, on: string) {
	return queryOptions({
		queryKey: ppkKeys.candidates(companyId, on),
		queryFn: async () => unwrap(await listPpkCandidatesFn({ data: { companyId, on } })),
		retry: retryServerFailures,
	});
}

export function ppkReenrollmentQuery(companyId: string, year: number) {
	return queryOptions({
		queryKey: ppkKeys.reenrollment(companyId, year),
		queryFn: async () => unwrap(await getPpkReenrollmentFn({ data: { id: companyId, year } })),
		retry: retryServerFailures,
	});
}

export function employeePpkQuery(employeeId: string) {
	return queryOptions({
		queryKey: ppkKeys.employee(employeeId),
		queryFn: async () => unwrap(await getEmployeePpkFn({ data: employeeId })),
		retry: retryServerFailures,
	});
}

export function myPpkQuery(employeeId: string) {
	return queryOptions({
		queryKey: ppkKeys.my(employeeId),
		queryFn: () => getMyPpkFn({ data: employeeId }),
	});
}

function usePpkMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the data on screen is stale.
		onSettled: () => queryClient.invalidateQueries({ queryKey: ppkKeys.all }),
	});
}

export function useAddPpkSettings() {
	return usePpkMutation(async (variables: { companyId: string; request: AddPpkSettingsRequest }) =>
		unwrap(await addPpkSettingsFn({ data: variables })),
	);
}

export function useMarkPpkInformed() {
	return usePpkMutation(async (variables: { id: string; year: number; informedAt: string }) =>
		unwrap(await markPpkInformedFn({ data: variables })),
	);
}

export function useApplyPpkReenrollment() {
	return usePpkMutation(async (variables: { id: string; year: number }) =>
		unwrap(await applyPpkReenrollmentFn({ data: variables })),
	);
}

export function useEnrollInPpk() {
	return usePpkMutation(async (variables: { employeeId: string; request: EnrollInPpkRequest }) =>
		unwrap(await enrollInPpkFn({ data: variables })),
	);
}

export function useDeletePpkParticipation() {
	return usePpkMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deletePpkParticipationFn({ data: variables })),
	);
}

export function useCreatePpkDeclaration() {
	return usePpkMutation(
		async (variables: { employeeId: string; request: CreatePpkDeclarationRequest }) =>
			unwrap(await createPpkDeclarationFn({ data: variables })),
	);
}

export function useDeletePpkDeclaration() {
	return usePpkMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deletePpkDeclarationFn({ data: variables })),
	);
}
