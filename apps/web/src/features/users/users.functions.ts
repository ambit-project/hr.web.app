import { createServerFn } from "@tanstack/react-start";
import { createUser, listUsers, setUserPassword, updateUser } from "#/api/endpoints/users/users";
import type {
	CreateUserRequest,
	ListUsersParams,
	SetUserPasswordRequest,
	UpdateUserRequest,
} from "#/api/models";
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

export const listUsersFn = createServerFn({ method: "GET" })
	.validator((input: ListUsersParams) => input)
	.handler(({ data }) => callApi(() => listUsers(data, apiOptions())));

export const createUserFn = createServerFn({ method: "POST" })
	.validator((input: CreateUserRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => createUser(data, apiOptions()))));

export const updateUserFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateUserRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateUser(data.id, data.request, apiOptions()))),
	);

export const setUserPasswordFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: SetUserPasswordRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => setUserPassword(data.id, data.request, apiOptions()))),
	);
