import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { ListUsersItem } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError } from "#/lib/problem";
import { useCreateUser, useSetUserPassword, useUpdateUser } from "./usersQueries";

export type UserDialogHandle = {
	create: () => void;
	edit: (user: ListUsersItem) => void;
	password: (user: ListUsersItem) => void;
};

type AccountTarget = { mode: "create" } | { mode: "edit"; user: ListUsersItem };
type Target = AccountTarget | { mode: "password"; user: ListUsersItem };

export const UserDialog = forwardRef<UserDialogHandle>(function UserDialog(_props, ref) {
	const [target, setTarget] = useState<Target | null>(null);
	useImperativeHandle(
		ref,
		() => ({
			create: () => setTarget({ mode: "create" }),
			edit: (user) => setTarget({ mode: "edit", user }),
			password: (user) => setTarget({ mode: "password", user }),
		}),
		[],
	);
	const close = () => setTarget(null);

	return (
		<Modal>
			<Modal.Backdrop
				isDismissable={false}
				isKeyboardDismissDisabled
				isOpen={target !== null}
				onOpenChange={(open) => !open && close()}
			>
				<Modal.Container scroll="inside">
					{target?.mode === "password" ? (
						<PasswordForm user={target.user} onDone={close} />
					) : target ? (
						<AccountForm target={target} onDone={close} />
					) : null}
				</Modal.Container>
			</Modal.Backdrop>
		</Modal>
	);
});

// Password length is checked by the database against Auth__MinPasswordLength; here only "not empty" on create.
const accountSchema = z.object({
	login: text({ required: true, max: 100 }),
	email: text({ required: true, max: 200 }),
	displayName: text({ required: true, max: 200 }),
	roles: z.array(z.string()).min(1, "required"),
	password: z.string(),
	isActive: z.boolean(),
});

const ACCOUNT_FIELDS = ["login", "email", "displayName", "roles", "password"] as const;

function AccountForm({ target, onDone }: { target: AccountTarget; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const create = useCreateUser();
	const update = useUpdateUser();
	const user = target.mode === "edit" ? target.user : null;

	const form = useAppForm({
		defaultValues: user
			? {
					login: user.login,
					email: user.email,
					displayName: user.displayName,
					roles: [...user.roles],
					password: "",
					isActive: user.isActive,
				}
			: {
					login: "",
					email: "",
					displayName: "",
					roles: [] as string[],
					password: "",
					isActive: true,
				},
		validators: { onChange: accountSchema },
		onSubmit: async ({ value, formApi }) => {
			try {
				if (user) {
					await update.mutateAsync({
						id: user.id,
						request: {
							rowVersion: user.rowVersion,
							email: value.email,
							displayName: value.displayName,
							roles: value.roles,
							isActive: value.isActive,
						},
					});
					toast.success(t("users.edit.saved", { login: value.login }));
				} else {
					await create.mutateAsync({
						login: value.login,
						email: value.email,
						displayName: value.displayName,
						roles: value.roles,
						password: value.password,
					});
					toast.success(t("users.create.created", { login: value.login.trim() }));
				}
				onDone();
			} catch (error) {
				if (error instanceof ProblemError && error.problem.status === 409) {
					// A 409 here is either a stale row or an attempt to take away one's own administrator access.
					toast.warning(`${t("users.edit.selfLockout")} ${t("common.conflict")}`);
					onDone();
				} else if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, ACCOUNT_FIELDS)
				) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{user ? t("users.edit.title") : t("users.create.title")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<form
					id={formId}
					className="flex flex-col gap-4"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					{user ? (
						<p className="text-sm">
							{t("users.fields.login")}: <code>{user.login}</code>
						</p>
					) : (
						<form.AppField name="login">
							{(field) => <field.TextInput label={t("users.fields.login")} isRequired />}
						</form.AppField>
					)}
					<div className="grid gap-4 sm:grid-cols-2">
						<form.AppField name="displayName">
							{(field) => <field.TextInput label={t("users.fields.displayName")} isRequired />}
						</form.AppField>
						<form.AppField name="email">
							{(field) => (
								<field.TextInput label={t("users.fields.email")} isRequired type="email" />
							)}
						</form.AppField>
					</div>
					<form.AppField name="roles">
						{(field) => <field.RolesInput label={t("users.fields.roles")} />}
					</form.AppField>
					{user ? (
						<form.AppField name="isActive">
							{(field) => <field.SwitchInput label={t("users.fields.isActive")} />}
						</form.AppField>
					) : (
						<form.AppField name="password">
							{(field) => (
								<field.TextInput
									label={t("users.fields.password")}
									isRequired
									type="password"
									autoComplete="new-password"
								/>
							)}
						</form.AppField>
					)}
				</form>
			</Modal.Body>
			<Modal.Footer>
				<Button slot="close" variant="secondary">
					{t("common.cancel")}
				</Button>
				<form.Subscribe selector={(state) => state.isSubmitting}>
					{(isSubmitting) => (
						<Button type="submit" form={formId} isPending={isSubmitting}>
							{t("common.save")}
						</Button>
					)}
				</form.Subscribe>
			</Modal.Footer>
		</Modal.Dialog>
	);
}

function PasswordForm({ user, onDone }: { user: ListUsersItem; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const setPassword = useSetUserPassword();

	const form = useAppForm({
		defaultValues: { password: "" },
		validators: { onChange: z.object({ password: text({ required: true, max: 200 }) }) },
		onSubmit: async ({ value, formApi }) => {
			try {
				await setPassword.mutateAsync({ id: user.id, request: { password: value.password } });
				toast.success(t("users.password.saved"));
				onDone();
			} catch (error) {
				if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, ["password"] as const)
				) {
					toast.danger(t("common.serverError"));
				}
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("users.password.title", { login: user.login })}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t("users.password.hint")}</p>
				<form
					id={formId}
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="password">
						{(field) => (
							<field.TextInput
								label={t("users.fields.newPassword")}
								isRequired
								type="password"
								autoComplete="new-password"
							/>
						)}
					</form.AppField>
				</form>
			</Modal.Body>
			<Modal.Footer>
				<Button slot="close" variant="secondary">
					{t("common.cancel")}
				</Button>
				<form.Subscribe selector={(state) => state.isSubmitting}>
					{(isSubmitting) => (
						<Button type="submit" form={formId} isPending={isSubmitting}>
							{t("common.save")}
						</Button>
					)}
				</form.Subscribe>
			</Modal.Footer>
		</Modal.Dialog>
	);
}
