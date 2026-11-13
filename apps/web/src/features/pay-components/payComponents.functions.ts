import { createServerFn } from "@tanstack/react-start";
import {
	createDeductionConsent,
	deleteDeductionConsent,
	endDeductionConsent,
	listDeductionConsents,
	updateDeductionConsent,
} from "#/api/endpoints/deduction-consents/deduction-consents";
import {
	addEmploymentComponent,
	deleteEmploymentComponent,
	endEmploymentComponent,
	listEmploymentComponents,
} from "#/api/endpoints/employment-components/employment-components";
import {
	createGarnishment,
	deleteGarnishment,
	listGarnishments,
	setGarnishmentStatus,
	updateGarnishment,
} from "#/api/endpoints/garnishments/garnishments";
import {
	createPayComponentType,
	listPayComponentTypes,
	updatePayComponentType,
} from "#/api/endpoints/pay-component-types/pay-component-types";
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
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

type Removal = { id: string; rowVersion: number };

// Component types (dictionary)

export const listComponentTypesFn = createServerFn({ method: "GET" })
	.validator((input: { includeInactive: boolean }) => input)
	.handler(({ data }) => callApi(() => listPayComponentTypes(data, apiOptions())));

export const createComponentTypeFn = createServerFn({ method: "POST" })
	.validator((input: CreateComponentTypeRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createPayComponentType(data, apiOptions()))));

export const updateComponentTypeFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateComponentTypeRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updatePayComponentType(data.id, data.request, apiOptions()))),
	);

// Components of a contract

export const listEmploymentComponentsFn = createServerFn({ method: "GET" })
	.validator((employmentId: string) => employmentId)
	.handler(({ data }) => callApi(() => listEmploymentComponents(data, apiOptions())));

export const addEmploymentComponentFn = createServerFn({ method: "POST" })
	.validator((input: { employmentId: string; request: AddEmploymentComponentRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => addEmploymentComponent(data.employmentId, data.request, apiOptions())),
		),
	);

export const endEmploymentComponentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: EndEmploymentComponentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => endEmploymentComponent(data.id, data.request, apiOptions()))),
	);

export const deleteEmploymentComponentFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				deleteEmploymentComponent(data.id, { rowVersion: data.rowVersion }, apiOptions()),
			),
		),
	);

// Deduction consents

export const listConsentsFn = createServerFn({ method: "GET" })
	.validator((employeeId: string) => employeeId)
	.handler(({ data }) => callApi(() => listDeductionConsents(data, apiOptions())));

export const createConsentFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: CreateConsentRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => createDeductionConsent(data.employeeId, data.request, apiOptions())),
		),
	);

export const updateConsentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateConsentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateDeductionConsent(data.id, data.request, apiOptions()))),
	);

export const endConsentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: EndConsentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => endDeductionConsent(data.id, data.request, apiOptions()))),
	);

export const deleteConsentFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteDeductionConsent(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

// Garnishments

export const listGarnishmentsFn = createServerFn({ method: "GET" })
	.validator((employeeId: string) => employeeId)
	.handler(({ data }) => callApi(() => listGarnishments(data, apiOptions())));

export const createGarnishmentFn = createServerFn({ method: "POST" })
	.validator((input: { employeeId: string; request: CreateGarnishmentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => createGarnishment(data.employeeId, data.request, apiOptions()))),
	);

export const updateGarnishmentFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateGarnishmentRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateGarnishment(data.id, data.request, apiOptions()))),
	);

export const setGarnishmentStatusFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: SetGarnishmentStatusRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => setGarnishmentStatus(data.id, data.request, apiOptions()))),
	);

export const deleteGarnishmentFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteGarnishment(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);
