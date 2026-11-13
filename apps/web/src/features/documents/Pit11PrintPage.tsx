import { useQuery } from "@tanstack/react-query";
import { getRouteApi, useRouter } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import type { GetTaxFormResponse } from "#/api/models";
import { Notice } from "#/features/time-absence/Notice";
import { formatDecimal } from "#/lib/money";
import { taxFormQuery } from "./documentsQueries";
import { DocField, PrintPage } from "./PrintLayout";
import {
	isWholePosition,
	type PartERow,
	PIT11_PART_E,
	PIT11_PART_G_AMOUNTS,
	positionValue,
	type TaxFormPurpose,
} from "./types";

const route = getRouteApi("/tax-forms/$id");

/** Amount of a field: whole złoty for advances and tick boxes, grosze for amounts, empty when the field is empty. */
function amountText(position: number, value: number | undefined): string {
	if (value === undefined) return "";
	return isWholePosition(position) ? String(value) : formatDecimal(value);
}

/** Field of the form: its number and value, like a box on PIT-11(29). */
function Box({ form, position }: { form: GetTaxFormResponse; position: number | null }) {
	if (position === null) return <td className="bg-[var(--color-table-head)]" />;
	return (
		<td className="text-right">
			<span className="print-doc__pos float-left">{position}.</span>
			{amountText(position, positionValue(form.data.positions, position))}
		</td>
	);
}

/** PIT-11(29) as a printable page laid out in the form's parts and positions (R8, R10). */
export function Pit11PrintPage() {
	const { t } = useTranslation();
	const router = useRouter();
	const { id } = route.useParams();
	const { data: form, isPending, error } = useQuery(taxFormQuery(id));

	return (
		<PrintPage
			isPending={isPending}
			error={error}
			back={
				<button type="button" className="hover:underline" onClick={() => router.history.back()}>
					{t("documents.back")}
				</button>
			}
		>
			{form ? <Pit11Document form={form} /> : null}
		</PrintPage>
	);
}

function Pit11Document({ form }: { form: GetTaxFormResponse }) {
	const { t } = useTranslation();
	const { data } = form;
	const address = data.taxpayer.address;
	const positions = data.positions;
	const costsKind = positionValue(positions, 28);
	const reliefs = ([118, 119, 120] as const).filter((p) => positionValue(positions, p) === 1);

	return (
		<div className="flex flex-col gap-2">
			{!form.isLatest ? (
				<div className="no-print">
					<Notice>{t("documents.pit11Form.notLatest")}</Notice>
				</div>
			) : null}
			{data.missing.length > 0 ? (
				<div className="no-print">
					<Notice status="danger">{t("documents.pit11Form.missing")}</Notice>
				</div>
			) : null}
			<header className="grid grid-cols-[1fr_auto] items-end gap-2">
				<div>
					<h1>PIT-11</h1>
					<p className="text-[11px] font-semibold">{t("documents.pit11Form.heading")}</p>
				</div>
				<div className="grid grid-cols-2 gap-1">
					<DocField label={t("documents.pit11Form.yearLabel")}>{form.year}</DocField>
					<DocField label="PIT-11(29)">
						{t("documents.pit11Form.version", { version: form.version })}
					</DocField>
				</div>
			</header>
			<DocField label={t("documents.pit11Form.payerNip")}>{data.payer.nip}</DocField>

			<h2>{t("documents.pit11Form.partA")}</h2>
			<div className="grid grid-cols-2 gap-1">
				<DocField label={t("documents.pit11Form.office")}>
					{data.taxOffice ? `${data.taxOffice.code} ${data.taxOffice.name}` : "—"}
				</DocField>
				<DocField label={t("documents.pit11Form.purpose")}>
					{t(`documents.pit11Form.purposes.${form.purpose as TaxFormPurpose}`)}
				</DocField>
			</div>

			<h2>{t("documents.pit11Form.partB")}</h2>
			<div className="grid grid-cols-[1fr_2fr] gap-1">
				<DocField label={t("documents.pit11Form.payerKind")}>
					{t("documents.pit11Form.payerKindValue")}
				</DocField>
				<DocField label={t("documents.pit11Form.payerName")}>{data.payer.name}</DocField>
			</div>

			<h2>{t("documents.pit11Form.partC")}</h2>
			<div className="grid grid-cols-4 gap-1">
				<div className="col-span-2">
					<DocField label={t("documents.pit11Form.residence")}>
						{t("documents.pit11Form.residenceValue")}
					</DocField>
				</div>
				<div className="col-span-2">
					<DocField label={t("documents.pit11Form.pesel")}>{form.pesel ?? "—"}</DocField>
				</div>
				<div className="col-span-2">
					<DocField label={t("documents.pit11Form.lastName")}>{data.taxpayer.lastName}</DocField>
				</div>
				<DocField label={t("documents.pit11Form.firstName")}>{data.taxpayer.firstName}</DocField>
				<DocField label={t("documents.pit11Form.birthDate")}>{data.taxpayer.birthDate}</DocField>
				<DocField label={t("documents.pit11Form.country")}>{address?.country ?? ""}</DocField>
				<div className="col-span-2">
					<DocField label={t("documents.pit11Form.street")}>{address?.street ?? ""}</DocField>
				</div>
				<div className="grid grid-cols-2 gap-1">
					<DocField label={t("documents.pit11Form.house")}>{address?.house ?? ""}</DocField>
					<DocField label={t("documents.pit11Form.apartment")}>{address?.apartment ?? ""}</DocField>
				</div>
				<div className="col-span-3">
					<DocField label={t("documents.pit11Form.city")}>{address?.city ?? ""}</DocField>
				</div>
				<DocField label={t("documents.pit11Form.postalCode")}>{address?.postalCode ?? ""}</DocField>
			</div>

			<h2>{t("documents.pit11Form.partD")}</h2>
			<DocField label={t("documents.pit11Form.costsKind")}>
				{costsKind === undefined
					? ""
					: t(`documents.pit11Form.costsKinds.${String(costsKind) as "1" | "2" | "3" | "4"}`)}
			</DocField>

			<h2>{t("documents.pit11Form.partE")}</h2>
			<table>
				<thead>
					<tr>
						<th className="w-[34%]">a. {t("documents.pit11Form.columns.source")}</th>
						<th>b. {t("documents.pit11Form.columns.income")}</th>
						<th>c. {t("documents.pit11Form.columns.costs")}</th>
						<th>d. {t("documents.pit11Form.columns.net")}</th>
						<th>e. {t("documents.pit11Form.columns.exempt")}</th>
						<th>f. {t("documents.pit11Form.columns.advance")}</th>
					</tr>
				</thead>
				<tbody>
					{PIT11_PART_E.map((row) => (
						<tr key={row.row}>
							<td>{t(`documents.pit11Form.rows.${String(row.row) as PartERow}`)}</td>
							{row.cells.map((cell, index) => (
								// biome-ignore lint/suspicious/noArrayIndexKey: columns b–f are fixed positions of the form
								<Box key={index} form={form} position={cell} />
							))}
						</tr>
					))}
				</tbody>
			</table>
			<table>
				<tbody>
					{(["95", "96", "97"] as const).map((p) => (
						<tr key={p}>
							<td>{t(`documents.pit11Form.social.${p}`)}</td>
							<Box form={form} position={Number(p)} />
						</tr>
					))}
				</tbody>
			</table>

			<h2>{t("documents.pit11Form.partG")}</h2>
			<table>
				<tbody>
					{PIT11_PART_G_AMOUNTS.map((p) => (
						<tr key={p}>
							<td className={[110, 111, 112, 113, 115, 116, 117].includes(p) ? "pl-6" : ""}>
								{t(`documents.pit11Form.partGItems.${String(p) as `${typeof p}`}`)}
							</td>
							<Box form={form} position={p} />
						</tr>
					))}
				</tbody>
			</table>
			<div className="grid grid-cols-4 gap-1">
				{([118, 119, 120] as const).map((p) => (
					<DocField key={p} label={t(`documents.pit11Form.reliefs.${String(p) as `${typeof p}`}`)}>
						{reliefs.includes(p) ? "☒" : "☐"}
					</DocField>
				))}
				<DocField label={t("documents.pit11Form.attachment")}>
					{t("documents.pit11Form.attachmentNo")}
				</DocField>
			</div>

			<h2>{t("documents.pit11Form.partH")}</h2>
			<div className="print-doc__signature">{t("documents.pit11Form.signature")}</div>
			<p className="text-[10px] text-muted">{t("documents.pit11Form.footer")}</p>
		</div>
	);
}
