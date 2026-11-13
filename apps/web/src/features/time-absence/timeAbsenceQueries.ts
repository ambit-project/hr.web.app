import { queryOptions, useMutation, useQueryClient } from "@tanstack/react-query";
import type {
	AddHolidayRequest,
	AddMandateHoursRequest,
	AddOvertimeRequest,
	CreateAbsenceRequest,
	SetCalendarDayRequest,
	SetLeaveEntitlementRequest,
	UpdateAbsenceRequest,
	UpdateMandateHoursRequest,
} from "#/api/models";
import { timeAbsenceKeys } from "#/api/query-keys";
import { ProblemError, unwrap } from "#/lib/problem";
import {
	addHolidayFn,
	addMandateHoursFn,
	addOvertimeFn,
	createAbsenceFn,
	deleteAbsenceFn,
	deleteHolidayFn,
	deleteMandateHoursFn,
	deleteOvertimeFn,
	fillStatutoryHolidaysFn,
	generateCalendarFn,
	getCalendarMonthFn,
	getCalendarYearFn,
	getLeaveFn,
	getMyLeaveFn,
	getNormFn,
	listAbsencesFn,
	listAbsenceTypesFn,
	listHolidaysFn,
	listMandateHoursFn,
	listMyAbsencesFn,
	listOvertimeFn,
	setCalendarDayFn,
	setLeaveFn,
	updateAbsenceFn,
	updateMandateHoursFn,
} from "./timeAbsence.functions";

/** A rule answer (4xx: no calendar, wrong contract kind) does not change on a second try. */
const retryServerFailures = (failureCount: number, error: unknown) =>
	!(error instanceof ProblemError && error.problem.status < 500) && failureCount < 1;

export function holidaysQuery(year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.holidays(year),
		queryFn: () => listHolidaysFn({ data: year }),
	});
}

export function calendarYearQuery(companyId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.calendarYear(companyId, year),
		queryFn: () => getCalendarYearFn({ data: { id: companyId, year } }),
	});
}

export function calendarMonthQuery(companyId: string, year: number, month: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.calendarMonth(companyId, year, month),
		queryFn: () => getCalendarMonthFn({ data: { id: companyId, year, month } }),
	});
}

/** The dictionary is fixed in the migration, so one read per session is enough. */
export function absenceTypesQuery() {
	return queryOptions({
		queryKey: timeAbsenceKeys.absenceTypes(),
		queryFn: () => listAbsenceTypesFn(),
		staleTime: Number.POSITIVE_INFINITY,
	});
}

export function absencesQuery(employmentId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.absences(employmentId, year),
		queryFn: async () => unwrap(await listAbsencesFn({ data: { id: employmentId, year } })),
		retry: retryServerFailures,
	});
}

/** Proposal for another base or first-job flag is asked for while the dialog changes them. */
export function leaveQuery(
	employmentId: string,
	year: number,
	proposal?: { baseDays: number; firstJob: boolean },
) {
	return queryOptions({
		queryKey: timeAbsenceKeys.leave(employmentId, year, proposal),
		queryFn: async () =>
			unwrap(await getLeaveFn({ data: { id: employmentId, year, ...proposal } })),
		retry: retryServerFailures,
	});
}

export function normQuery(employmentId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.norm(employmentId, year),
		queryFn: async () => unwrap(await getNormFn({ data: { id: employmentId, year } })),
		retry: retryServerFailures,
	});
}

export function overtimeQuery(employmentId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.overtime(employmentId, year),
		queryFn: async () => unwrap(await listOvertimeFn({ data: { id: employmentId, year } })),
		retry: retryServerFailures,
	});
}

export function mandateHoursQuery(employmentId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.mandateHours(employmentId, year),
		queryFn: async () => unwrap(await listMandateHoursFn({ data: { id: employmentId, year } })),
		retry: retryServerFailures,
	});
}

export function myLeaveQuery(employeeId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.myLeave(employeeId, year),
		queryFn: () => getMyLeaveFn({ data: { employeeId, year } }),
	});
}

export function myAbsencesQuery(employeeId: string, year: number) {
	return queryOptions({
		queryKey: timeAbsenceKeys.myAbsences(employeeId, year),
		queryFn: () => listMyAbsencesFn({ data: { employeeId, year } }),
	});
}

function useTimeAbsenceMutation<TVariables, TResult>(
	mutationFn: (variables: TVariables) => Promise<TResult>,
) {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn,
		// Also after a failure: a 409 means the data on screen is stale.
		onSettled: () => queryClient.invalidateQueries({ queryKey: timeAbsenceKeys.all }),
	});
}

export function useAddHoliday() {
	return useTimeAbsenceMutation(async (request: AddHolidayRequest) =>
		unwrap(await addHolidayFn({ data: request })),
	);
}

/** Adds the statutory holidays of the year that the dictionary lacks; resolves to how many were added. */
export function useFillStatutoryHolidays() {
	return useTimeAbsenceMutation(async (year: number) =>
		unwrap(await fillStatutoryHolidaysFn({ data: year })),
	);
}

export function useDeleteHoliday() {
	return useTimeAbsenceMutation(async (date: string) =>
		unwrap(await deleteHolidayFn({ data: date })),
	);
}

export function useGenerateCalendar() {
	return useTimeAbsenceMutation(async (variables: { id: string; year: number }) =>
		unwrap(await generateCalendarFn({ data: variables })),
	);
}

export function useSetCalendarDay() {
	return useTimeAbsenceMutation(
		async (variables: { companyId: string; date: string; request: SetCalendarDayRequest }) =>
			unwrap(await setCalendarDayFn({ data: variables })),
	);
}

export function useCreateAbsence() {
	return useTimeAbsenceMutation(
		async (variables: { employmentId: string; request: CreateAbsenceRequest }) =>
			unwrap(await createAbsenceFn({ data: variables })),
	);
}

export function useUpdateAbsence() {
	return useTimeAbsenceMutation(async (variables: { id: string; request: UpdateAbsenceRequest }) =>
		unwrap(await updateAbsenceFn({ data: variables })),
	);
}

export function useDeleteAbsence() {
	return useTimeAbsenceMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteAbsenceFn({ data: variables })),
	);
}

export function useSetLeave() {
	return useTimeAbsenceMutation(
		async (variables: { id: string; year: number; request: SetLeaveEntitlementRequest }) =>
			unwrap(await setLeaveFn({ data: variables })),
	);
}

export function useAddOvertime() {
	return useTimeAbsenceMutation(
		async (variables: { employmentId: string; request: AddOvertimeRequest }) =>
			unwrap(await addOvertimeFn({ data: variables })),
	);
}

export function useDeleteOvertime() {
	return useTimeAbsenceMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteOvertimeFn({ data: variables })),
	);
}

export function useAddMandateHours() {
	return useTimeAbsenceMutation(
		async (variables: { employmentId: string; request: AddMandateHoursRequest }) =>
			unwrap(await addMandateHoursFn({ data: variables })),
	);
}

export function useUpdateMandateHours() {
	return useTimeAbsenceMutation(
		async (variables: { id: string; request: UpdateMandateHoursRequest }) =>
			unwrap(await updateMandateHoursFn({ data: variables })),
	);
}

export function useDeleteMandateHours() {
	return useTimeAbsenceMutation(async (variables: { id: string; rowVersion: number }) =>
		unwrap(await deleteMandateHoursFn({ data: variables })),
	);
}
