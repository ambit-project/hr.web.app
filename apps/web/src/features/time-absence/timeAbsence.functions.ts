import { createServerFn } from "@tanstack/react-start";
import {
	createAbsence,
	deleteAbsence,
	listAbsences,
	listAbsenceTypes,
	updateAbsence,
} from "#/api/endpoints/absences/absences";
import { addHoliday, deleteHoliday, listHolidays } from "#/api/endpoints/holidays/holidays";
import { getLeaveEntitlement, setLeaveEntitlement } from "#/api/endpoints/leave/leave";
import {
	addMandateHours,
	deleteMandateHours,
	listMandateHours,
	updateMandateHours,
} from "#/api/endpoints/mandate-hours/mandate-hours";
import { getMyLeaveBalance, listMyAbsences } from "#/api/endpoints/my-time-absence/my-time-absence";
import { addOvertime, deleteOvertime, listOvertime } from "#/api/endpoints/overtime/overtime";
import {
	generateWorkCalendar,
	getEmploymentNorm,
	getWorkCalendarMonth,
	getWorkCalendarYear,
	setWorkCalendarDay,
} from "#/api/endpoints/work-calendar/work-calendar";
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
import { toResult } from "#/lib/problem";
import { apiOptions, callApi } from "#/server/api";

type Removal = { id: string; rowVersion: number };
type ByYear = { id: string; year: number };

// Holidays (dictionary)

export const listHolidaysFn = createServerFn({ method: "GET" })
	.validator((year: number) => year)
	.handler(({ data }) => callApi(() => listHolidays({ year: data }, apiOptions())));

export const addHolidayFn = createServerFn({ method: "POST" })
	.validator((input: AddHolidayRequest) => input)
	.handler(({ data }) => toResult(() => callApi(() => addHoliday(data, apiOptions()))));

export const deleteHolidayFn = createServerFn({ method: "POST" })
	.validator((date: string) => date)
	.handler(({ data }) => toResult(() => callApi(() => deleteHoliday(data, apiOptions()))));

// Company calendar

export const getCalendarYearFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) => callApi(() => getWorkCalendarYear(data.id, data.year, apiOptions())));

export const getCalendarMonthFn = createServerFn({ method: "GET" })
	.validator((input: ByYear & { month: number }) => input)
	.handler(({ data }) =>
		callApi(() => getWorkCalendarMonth(data.id, data.year, data.month, apiOptions())),
	);

export const generateCalendarFn = createServerFn({ method: "POST" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => generateWorkCalendar(data.id, data.year, apiOptions()))),
	);

export const setCalendarDayFn = createServerFn({ method: "POST" })
	.validator((input: { companyId: string; date: string; request: SetCalendarDayRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => setWorkCalendarDay(data.companyId, data.date, data.request, apiOptions())),
		),
	);

// Contract: norm, absences, leave, overtime, mandate hours. Reads that can end in a 409 with a reason (no calendar
// for the year) travel as results, so the section can name the reason.

export const getNormFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => getEmploymentNorm(data.id, data.year, undefined, apiOptions()))),
	);

export const listAbsenceTypesFn = createServerFn({ method: "GET" }).handler(() =>
	callApi(() => listAbsenceTypes(apiOptions())),
);

export const listAbsencesFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listAbsences(data.id, { year: data.year }, apiOptions()))),
	);

export const createAbsenceFn = createServerFn({ method: "POST" })
	.validator((input: { employmentId: string; request: CreateAbsenceRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => createAbsence(data.employmentId, data.request, apiOptions()))),
	);

export const updateAbsenceFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateAbsenceRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateAbsence(data.id, data.request, apiOptions()))),
	);

export const deleteAbsenceFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteAbsence(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

export const getLeaveFn = createServerFn({ method: "GET" })
	.validator((input: ByYear & { baseDays?: number; firstJob?: boolean }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() =>
				getLeaveEntitlement(
					data.id,
					data.year,
					{ baseDays: data.baseDays, firstJob: data.firstJob },
					apiOptions(),
				),
			),
		),
	);

export const setLeaveFn = createServerFn({ method: "POST" })
	.validator((input: ByYear & { request: SetLeaveEntitlementRequest }) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => setLeaveEntitlement(data.id, data.year, data.request, apiOptions())),
		),
	);

export const listOvertimeFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listOvertime(data.id, { year: data.year }, apiOptions()))),
	);

export const addOvertimeFn = createServerFn({ method: "POST" })
	.validator((input: { employmentId: string; request: AddOvertimeRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addOvertime(data.employmentId, data.request, apiOptions()))),
	);

export const deleteOvertimeFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteOvertime(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

export const listMandateHoursFn = createServerFn({ method: "GET" })
	.validator((input: ByYear) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => listMandateHours(data.id, { year: data.year }, apiOptions()))),
	);

export const addMandateHoursFn = createServerFn({ method: "POST" })
	.validator((input: { employmentId: string; request: AddMandateHoursRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => addMandateHours(data.employmentId, data.request, apiOptions()))),
	);

export const updateMandateHoursFn = createServerFn({ method: "POST" })
	.validator((input: { id: string; request: UpdateMandateHoursRequest }) => input)
	.handler(({ data }) =>
		toResult(() => callApi(() => updateMandateHours(data.id, data.request, apiOptions()))),
	);

export const deleteMandateHoursFn = createServerFn({ method: "POST" })
	.validator((input: Removal) => input)
	.handler(({ data }) =>
		toResult(() =>
			callApi(() => deleteMandateHours(data.id, { rowVersion: data.rowVersion }, apiOptions())),
		),
	);

// "Moje dokumenty"

export const getMyLeaveFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; year: number }) => input)
	.handler(({ data }) =>
		callApi(() => getMyLeaveBalance(data.employeeId, data.year, apiOptions())),
	);

export const listMyAbsencesFn = createServerFn({ method: "GET" })
	.validator((input: { employeeId: string; year: number }) => input)
	.handler(({ data }) => callApi(() => listMyAbsences(data.employeeId, data.year, apiOptions())));
