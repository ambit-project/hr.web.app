import { createServerFn } from "@tanstack/react-start";
import { getMyPpk } from "#/api/endpoints/my-ppk/my-ppk";
import {
	addPpkSettings,
	applyPpkReenrollment,
	createPpkDeclaration,
	deletePpkDeclaration,
	deletePpkParticipation,
	enrollInPpk,
	getEmployeePpk,
	getPpkReenrollment,
	listPpkCandidates,
	listPpkSettings,
	markPpkReenrollmentInformed,
} from "#/api/endpoints/ppk/ppk";
import type {
	AddPpkSettingsRequest,
	CreatePpkDeclarationRequest,
	EnrollInPpkRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

type Removal = { id: string; rowVersion: number };
type ByYear = { id: string; year: number };

// Company PPK

export const listPpkSettingsFn = createServerFn({ method: "GET" })
	.validator((companyId: string) => companyId)
	.handler(({ data }) => callApi(() => listPpkSettings(data, apiOptions())));

export const addPpkSettingsFn = createServerFn({ method: "POST" })
	.validator((input: { companyId: string; request: AddPpkSettingsRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addPpkSettings(data.companyId, data.request, apiOptions()))),
	);

export const listPpkCandidatesFn = createServerFn({ method: "GET" })
	.validator((input: { companyId: string; on: string }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listPpkCandidates(data.companyId, { on: data.on }, apiOptions()))),
	);

export const getPpkReenrollmentFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => getPpkReenrollment(data.id, data.year, apiOptions()))),
	);

export const markPpkInformedFn = createServerFn({ method: "POST" })
	.validator((input: ByYear & { informedAt: string }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				markPpkReenrollmentInformed(
					data.id,
					data.year,
					{ informedAt: data.informedAt },
					apiOptions(),
				),
			),
		),
	);

export const applyPpkReenrollmentFn = createServerFn({ method: "POST" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => applyPpkReenrollment(data.id, data.year, apiOptions()))),
	);

// Employee record

export const getEmployeePpkFn = createServerFn({ method: "GET" })
	.validator((employeeId: string) => employeeId)
	.handler(({ data }) => toResult(() => callApi(() => getEmployeePpk(data, apiOptions()))));

export const enrollInPpkFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: EnrollInPpkRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => enrollInPpk(data.employeeId, data.request, apiOptions()))),
	);

export const deletePpkParticipationFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deletePpkParticipation(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

export const createPpkDeclarationFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: CreatePpkDeclarationRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => createPpkDeclaration(data.employeeId, data.request, apiOptions())),
		),
	);

export const deletePpkDeclarationFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deletePpkDeclaration(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

// Self-service

export const getMyPpkFn = createServerFn({ method: "GET" })
	.validator((employeeId: string) => employeeId)
	.handler(({ data }) => callApi(() => getMyPpk(data, apiOptions())));
