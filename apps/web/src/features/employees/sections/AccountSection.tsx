import { Button, toast } from "@heroui/react";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import type { GetEmployeeResponse } from "#/api/models";
import { text, useAppForm } from "#/forms";
import { applyFieldErrors } from "#/forms/serverErrors";
import { ProblemError, serverErrorText } from "#/lib/problem";
import { useLinkEmployeeAccount, useUnlinkEmployeeAccount } from "../employeesQueries";

export function AccountSection({ employee }: { employee: GetEmployeeResponse }) {
	const { t } = useTranslation();
	const formId = useId();
	const link = useLinkEmployeeAccount();
	const unlink = useUnlinkEmployeeAccount();

	const form = useAppForm({
		defaultValues: { account: "" },
		validators: { onChange: z.object({ account: text({ required: true, max: 200 }) }) },
		onSubmit: async ({ value, formApi }) => {
			try {
				await link.mutateAsync({ id: employee.id, account: value.account });
				toast.success(t("employees.account.linked"));
				formApi.reset();
			} catch (error) {
				if (
					!(error instanceof ProblemError) ||
					!applyFieldErrors(formApi, error.problem.errors, ["account"] as const)
				) {
					toast.danger(serverErrorText(t));
				}
			}
		},
	});

	return (
		<div className="flex max-w-xl flex-col gap-4">
			<p className="text-sm text-muted">{t("employees.account.hint")}</p>
			<p className="text-sm">
				{t("employees.fields.account")}:{" "}
				{employee.accountLogin ? <code>{employee.accountLogin}</code> : t("employees.account.none")}
			</p>
			{employee.accountLogin ? (
				<div>
					<Button
						variant="secondary"
						isPending={unlink.isPending}
						onPress={() =>
							unlink.mutate(employee.id, {
								onSuccess: () => toast.success(t("employees.account.unlinked")),
								onError: () => toast.danger(serverErrorText(t)),
							})
						}
					>
						{t("employees.account.unlink")}
					</Button>
				</div>
			) : null}
			<form
				id={formId}
				className="flex items-end gap-2"
				noValidate
				onSubmit={(event) => {
					event.preventDefault();
					void form.handleSubmit();
				}}
			>
				<div className="flex-1">
					<form.AppField name="account">
						{(field) => <field.TextInput label={t("employees.account.loginOrEmail")} isRequired />}
					</form.AppField>
				</div>
				<form.Subscribe selector={(state) => state.isSubmitting}>
					{(isSubmitting) => (
						<Button type="submit" form={formId} isPending={isSubmitting}>
							{t("employees.account.link")}
						</Button>
					)}
				</form.Subscribe>
			</form>
		</div>
	);
}
