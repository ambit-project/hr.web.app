import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { pl } from "#/i18n/pl";
import { type TraceRow, type TraceValue, traceRows } from "./types";

type TraceKey = keyof typeof pl.payroll.trace.keys;

/** Label of a trace key; a key the engine adds later shows as it is rather than disappearing. */
function keyLabel(t: TFunction<"translation">, key: string): string {
	if (/^\d+$/.test(key)) return `${key}.`;
	return key in pl.payroll.trace.keys ? t(`payroll.trace.keys.${key as TraceKey}`) : key;
}

function ValueText({ value }: { value: TraceValue }) {
	const { t } = useTranslation();
	switch (value.type) {
		case "text":
			return <>{value.text}</>;
		case "bool":
			return <>{value.value ? t("common.yes") : t("common.no")}</>;
		case "ids":
			return (
				<>
					{t("payroll.trace.ids", { count: value.ids.length })}
					{value.ids.length > 0 ? (
						<code className="ml-2 text-xs text-muted">{value.ids.join(", ")}</code>
					) : null}
				</>
			);
		case "param":
			return (
				<>
					{value.value}
					{value.validFrom ? (
						<span className="ml-2 text-xs text-muted">
							{t("payroll.trace.validFrom", { date: value.validFrom })}
						</span>
					) : null}
				</>
			);
		default:
			return null;
	}
}

function rowLabel(t: TFunction<"translation">, row: TraceRow): string {
	return row.value.type === "param" ? row.value.code : keyLabel(t, row.key);
}

/** The trace of a line as an indented list of labelled values (R24). */
export function TraceView({ trace }: { trace: unknown }) {
	const { t } = useTranslation();
	const rows = traceRows(trace);
	if (rows.length === 0)
		return <p className="text-xs text-muted">{t("payroll.payslip.noTrace")}</p>;
	return (
		<dl className="grid grid-cols-[minmax(12rem,max-content)_1fr] gap-x-4 gap-y-0.5 text-xs">
			{rows.map((row) => (
				<div key={row.id} className="contents">
					<dt
						className={row.value.type === "group" ? "font-medium" : "text-muted"}
						style={{ paddingLeft: `${row.depth * 1}rem` }}
					>
						{row.value.type === "param" ? (
							<>
								<span className="text-muted">{t("payroll.trace.param")}: </span>
								<code>{row.value.code}</code>
							</>
						) : (
							rowLabel(t, row)
						)}
					</dt>
					<dd className="tabular-nums">
						<ValueText value={row.value} />
					</dd>
				</div>
			))}
		</dl>
	);
}

/** Flat trace for one line of text (details of a warning). */
export function TraceInline({ trace }: { trace: unknown }) {
	const { t } = useTranslation();
	const rows = traceRows(trace).filter((row) => row.value.type !== "group");
	if (rows.length === 0) return null;
	return (
		<span className="ml-2 text-xs text-muted">
			{rows.map((row, index) => (
				<span key={row.id}>
					{index > 0 ? "; " : ""}
					{rowLabel(t, row)}: <ValueText value={row.value} />
				</span>
			))}
		</span>
	);
}
