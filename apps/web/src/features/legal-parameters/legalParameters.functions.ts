import { createServerFn } from "@tanstack/react-start";
import {
	addLegalParameter,
	listLegalParameters,
} from "#/api/endpoints/legal-parameters/legal-parameters";
import type { AddLegalParameterRequest, ListLegalParametersParams } from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listLegalParametersFn = createServerFn({ method: "GET" })
	.validator((input: ListLegalParametersParams) => input)
	.handler(({ data }) => callApi(() => listLegalParameters(data, apiOptions())));

export const addLegalParameterFn = createServerFn({ method: "POST" })
	.validator((input: AddLegalParameterRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => addLegalParameter(data, apiOptions()))));
