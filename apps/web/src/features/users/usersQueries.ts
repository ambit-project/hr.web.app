import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type { CreateUserRequest, SetUserPasswordRequest, UpdateUserRequest } from "#/api/models";
import { usersKeys } from "#/api/query-keys";
import { unwrap } from "#/lib/problem";
import { createUserFn, listUsersFn, setUserPasswordFn, updateUserFn } from "./users.functions";

export const USERS_PAGE_SIZE = 20;

export type UsersSearch = { search?: string; page?: number };

export function usersQuery({ search, page }: UsersSearch) {
	const params = { search, page: page ?? 1, pageSize: USERS_PAGE_SIZE };
	return queryOptions({
		queryKey: usersKeys.list(params),
		queryFn: () => listUsersFn({ data: params }),
	});
}

function useUserMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		onSettled: () => queryClient.invalidateQueries({ queryKey: usersKeys.all }),
	});
}

export function useCreateUser() {
	return useUserMutation(async (request: CreateUserRequest) =>
		unwrap(await createUserFn({ data: request })),
	);
}

export function useUpdateUser() {
	return useUserMutation(async (variables: { id: string; request: UpdateUserRequest }) =>
		unwrap(await updateUserFn({ data: variables })),
	);
}

export function useSetUserPassword() {
	return useUserMutation(async (variables: { id: string; request: SetUserPasswordRequest }) =>
		unwrap(await setUserPasswordFn({ data: variables })),
	);
}
