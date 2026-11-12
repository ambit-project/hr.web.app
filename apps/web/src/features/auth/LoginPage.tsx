import { Alert, Button, Card, Separator } from "@heroui/react";
import { useQueryClient } from "@tanstack/react-query";
import { getRouteApi, useRouter } from "@tanstack/react-router";
import { useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { text, useAppForm } from "#/forms";
import { devLoginFn, loginFn, type Role } from "./auth.functions";

const route = getRouteApi("/login");

const schema = z.object({
	login: text({ required: true, max: 200 }),
	password: text({ required: true, max: 200 }),
});

const DEV_ROLES: Role[] = ["kadry", "administrator", "pracownik"];

export function LoginPage() {
	const { t } = useTranslation();
	const { devLogin } = route.useLoaderData();
	const search = route.useSearch();
	const router = useRouter();
	const queryClient = useQueryClient();
	const formId = useId();
	const [failed, setFailed] = useState(false);
	const [devPending, setDevPending] = useState<Role | null>(null);

	// A fresh session means a fresh cache: nothing fetched for the previous account may leak into this one.
	const enter = async () => {
		queryClient.clear();
		await router.invalidate();
		await router.navigate({ to: search.redirect ?? "/" });
	};

	const form = useAppForm({
		defaultValues: { login: "", password: "" },
		validators: { onChange: schema },
		onSubmit: async ({ value }) => {
			const result = await loginFn({ data: value });
			setFailed(!result.ok);
			if (result.ok) await enter();
		},
	});

	const signInAs = async (role: Role) => {
		setDevPending(role);
		try {
			const result = await devLoginFn({ data: { role } });
			if (result.ok) await enter();
			else setFailed(true);
		} finally {
			setDevPending(null);
		}
	};

	return (
		<main className="flex min-h-screen items-center justify-center bg-background p-4">
			<Card className="w-full max-w-sm">
				<Card.Header>
					<Card.Title>{t("app.name")}</Card.Title>
					<Card.Description>{t("auth.title")}</Card.Description>
				</Card.Header>
				<Card.Content className="flex flex-col gap-4">
					{failed ? (
						<Alert status="danger">
							<Alert.Indicator />
							<Alert.Content>
								<Alert.Description>{t("auth.failed")}</Alert.Description>
							</Alert.Content>
						</Alert>
					) : null}
					<form
						id={formId}
						className="flex flex-col gap-4"
						noValidate
						onSubmit={(event) => {
							event.preventDefault();
							void form.handleSubmit();
						}}
					>
						<form.AppField name="login">
							{(field) => (
								<field.TextInput label={t("auth.login")} isRequired autoComplete="username" />
							)}
						</form.AppField>
						<form.AppField name="password">
							{(field) => (
								<field.TextInput
									label={t("auth.password")}
									isRequired
									type="password"
									autoComplete="current-password"
								/>
							)}
						</form.AppField>
						<form.Subscribe selector={(state) => state.isSubmitting}>
							{(isSubmitting) => (
								<Button type="submit" isPending={isSubmitting} className="w-full">
									{t("auth.submit")}
								</Button>
							)}
						</form.Subscribe>
					</form>
					{devLogin ? (
						<>
							<Separator />
							<p className="text-sm text-muted">{t("auth.dev.hint")}</p>
							<div className="flex flex-col gap-2">
								{DEV_ROLES.map((role) => (
									<Button
										key={role}
										className="w-full"
										variant="secondary"
										isPending={devPending === role}
										isDisabled={devPending !== null}
										onPress={() => void signInAs(role)}
									>
										{t("auth.dev.as", { role: t(`roles.${role}`) })}
									</Button>
								))}
							</div>
						</>
					) : null}
				</Card.Content>
			</Card>
		</main>
	);
}
