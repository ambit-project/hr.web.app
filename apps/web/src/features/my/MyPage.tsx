import { Card, Spinner } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { ConsentsTable } from "#/features/pay-components/ConsentsTable";
import { consentsQuery } from "#/features/pay-components/payComponentsQueries";
import { TaxDeclarationsTable } from "#/features/tax-declarations/TaxDeclarationsTable";
import { taxDeclarationsQuery } from "#/features/tax-declarations/taxDeclarationsQueries";

const route = getRouteApi("/my/");

export function MyPage() {
	const { t } = useTranslation();
	const { me } = route.useRouteContext();
	const profiles = me?.profiles ?? [];

	return (
		<section className="flex flex-col gap-4">
			<h1 className="text-2xl font-semibold">{t("my.title")}</h1>
			{profiles.length === 0 ? (
				<p className="text-muted">{t("my.noProfile")}</p>
			) : (
				<>
					{profiles.length > 1 ? (
						<p className="text-sm text-muted">{t("my.profilesHint")}</p>
					) : null}
					<div className="flex flex-col gap-4">
						{profiles.map((profile) => (
							<Card key={profile.employeeId}>
								<Card.Header>
									<Card.Title>{profile.companyShortName}</Card.Title>
								</Card.Header>
								<Card.Content className="flex flex-col gap-3">
									<h2 className="text-sm font-semibold">{t("my.taxDeclarations")}</h2>
									<ProfileDeclarations employeeId={profile.employeeId} />
									<h2 className="pt-2 text-sm font-semibold">{t("my.deductionConsents")}</h2>
									<ProfileConsents employeeId={profile.employeeId} />
									<p className="text-sm text-muted">{t("my.empty")}</p>
								</Card.Content>
							</Card>
						))}
					</div>
				</>
			)}
		</section>
	);
}

function ProfileDeclarations({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(taxDeclarationsQuery(employeeId));
	return isPending ? (
		<Spinner aria-label={t("common.loading")} />
	) : (
		<TaxDeclarationsTable items={data ?? []} />
	);
}

function ProfileConsents({ employeeId }: { employeeId: string }) {
	const { t } = useTranslation();
	const { data, isPending } = useQuery(consentsQuery(employeeId));
	return isPending ? (
		<Spinner aria-label={t("common.loading")} />
	) : (
		<ConsentsTable items={data ?? []} />
	);
}
