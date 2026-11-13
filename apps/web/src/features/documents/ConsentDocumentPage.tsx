import { useQuery } from "@tanstack/react-query";
import { getRouteApi, useRouter } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { formatMoney } from "#/lib/money";
import { consentDocumentQuery } from "./documentsQueries";
import { DocField, PrintPage } from "./PrintLayout";

const route = getRouteApi("/consent-documents/$id");

type Category =
	| "voluntaryEmployer"
	| "voluntaryOther"
	| "advance"
	| "penalty"
	| "alimony"
	| "enforcement";

/**
 * Consent to a deduction for signing (R11; KP art. 91). After signing Kadry enter the signing date on the consent
 * (006) — the scan is not stored [D].
 */
export function ConsentDocumentPage() {
	const { t } = useTranslation();
	const router = useRouter();
	const { id } = route.useParams();
	const { data, isPending, error } = useQuery(consentDocumentQuery(id));

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
			{data ? (
				<div className="flex flex-col gap-4 text-[13px]">
					<div className="grid grid-cols-2 gap-1">
						<DocField label={t("documents.consent.employer")}>
							{data.companyName}
							<div className="font-normal">
								NIP {data.companyNip} · {data.companyAddress}
							</div>
						</DocField>
						<DocField label={t("documents.consent.employee")}>
							{data.firstName} {data.lastName}
							<div className="font-normal">
								{data.employeeNumber}
								{data.employeeAddress ? ` · ${data.employeeAddress}` : ""}
							</div>
						</DocField>
					</div>
					<header className="text-center">
						<h1>{t("documents.consent.title")}</h1>
						<p className="text-[11px]">{t("documents.consent.legal")}</p>
					</header>
					<p className="leading-relaxed">
						{t("documents.consent.body", {
							name: `${data.firstName} ${data.lastName}`,
							component: `${data.componentName} (${data.componentCode})`,
							category: data.deductionCategory
								? t(`documents.consent.categories.${data.deductionCategory as Category}`)
								: "",
							amount: formatMoney(data.monthlyAmount),
							from: data.validFrom,
							to: data.validTo
								? t("documents.consent.untilEnd", { date: data.validTo })
								: t("documents.consent.noEnd"),
						})}
					</p>
					<p className="text-[11px]">{t("documents.consent.limits")}</p>
					<div className="grid grid-cols-3 gap-8">
						<div className="print-doc__signature">{t("documents.consent.place")}</div>
						<div className="print-doc__signature">{t("documents.consent.employeeSignature")}</div>
						<div className="print-doc__signature">{t("documents.consent.employerSignature")}</div>
					</div>
					{data.signedAt ? (
						<p className="text-[10px] text-muted">
							{t("documents.consent.signedAt", { date: data.signedAt })}
						</p>
					) : null}
				</div>
			) : null}
		</PrintPage>
	);
}
