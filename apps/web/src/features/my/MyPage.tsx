import { Card } from "@heroui/react";
import { getRouteApi } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";

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
					<div className="grid gap-4 sm:grid-cols-2">
						{profiles.map((profile) => (
							<Card key={profile.employeeId}>
								<Card.Header>
									<Card.Title>{profile.companyShortName}</Card.Title>
								</Card.Header>
								<Card.Content>
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
