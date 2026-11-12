import axios, { type AxiosRequestConfig } from "axios";

// Generated functions run on the server (inside server functions), which pass baseURL pointing at the API.
const instance = axios.create({ paramsSerializer: { indexes: null } });

export function customInstance<T>(config: AxiosRequestConfig, options?: AxiosRequestConfig) {
	return instance<T>({ ...config, ...options }).then(({ data }) => data);
}

export type ErrorType<TError> = import("axios").AxiosError<TError>;
export type BodyType<TBody> = TBody;
