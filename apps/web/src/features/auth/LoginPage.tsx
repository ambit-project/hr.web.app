import { Alert, Button } from "@heroui/react";
import { useQueryClient } from "@tanstack/react-query";
import { getRouteApi, useRouter } from "@tanstack/react-router";
import { BriefcaseBusiness, LogIn } from "lucide-react";
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
		<main className="grid min-h-screen bg-background lg:grid-cols-[minmax(0,1fr)_minmax(28rem,36rem)]">
			<BrandPanel />
			<section className="flex items-center justify-center px-6 py-12">
				<div className="flex w-full max-w-sm flex-col gap-6">
					<div className="lg:hidden">
						<BrandMark />
					</div>
					<header>
						<h1 className="text-2xl font-semibold">{t("auth.heading")}</h1>
						<p className="mt-1 text-sm text-muted">{t("auth.title")}</p>
					</header>
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
									<LogIn className="size-4" />
									{t("auth.submit")}
								</Button>
							)}
						</form.Subscribe>
					</form>
					{devLogin ? (
						<div className="flex flex-col gap-3 border-t border-border pt-5">
							<p className="text-xs font-medium uppercase tracking-wide text-muted">
								{t("auth.dev.hint")}
							</p>
							<div className="grid gap-2 sm:grid-cols-3">
								{DEV_ROLES.map((role) => (
									<Button
										key={role}
										variant="secondary"
										isPending={devPending === role}
										isDisabled={devPending !== null}
										onPress={() => void signInAs(role)}
									>
										{t(`roles.${role}`)}
									</Button>
								))}
							</div>
						</div>
					) : null}
				</div>
			</section>
		</main>
	);
}

function BrandMark({ inverted = false }: { inverted?: boolean }) {
	const { t } = useTranslation();
	return (
		<div className="flex items-center gap-2">
			<span
				className={`flex size-8 items-center justify-center ${
					inverted ? "bg-white text-black" : "bg-accent text-accent-foreground"
				}`}
			>
				<BriefcaseBusiness className="size-4" />
			</span>
			<span className="text-sm font-semibold">{t("app.name")}</span>
		</div>
	);
}

/** Left half on wide screens: a photo of people at work under a navy veil, with what the app is for. */
function BrandPanel() {
	const { t } = useTranslation();
	const lines = ["files", "payroll", "pit"] as const;
	return (
		<aside
			className="relative hidden flex-col justify-between overflow-hidden p-10 text-white lg:flex"
			style={{
				backgroundImage: "url(/images/login.jpg)",
				backgroundSize: "cover",
				backgroundPosition: "center",
			}}
		>
			<div className="absolute inset-0 bg-gradient-to-br from-[#0b1324]/95 via-[#17202a]/85 to-[#2448d8]/55" />
			<div className="relative">
				<BrandMark inverted />
			</div>
			<div className="relative flex flex-col gap-6">
				{lines.map((line) => (
					<div key={line}>
						<p className="text-5xl font-bold tracking-tight">{t(`auth.brand.${line}.title`)}</p>
						<p className="mt-1 text-sm text-white/75">{t(`auth.brand.${line}.text`)}</p>
					</div>
				))}
			</div>
			<p className="relative text-xs text-white/60">{t("auth.brand.tagline")}</p>
		</aside>
	);
}
