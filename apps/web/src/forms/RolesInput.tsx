import { Checkbox, FieldError, Label } from "@heroui/react";
import { useTranslation } from "react-i18next";
import type { Role } from "#/features/auth/auth.functions";
import { useFieldContext } from "./context";
import { errorCode } from "./errors";

const ROLES: Role[] = ["kadry", "administrator", "pracownik"];

export function RolesInput({ label }: { label: string }) {
	const field = useFieldContext<string[]>();
	const { t } = useTranslation();
	const code = errorCode(field.state.meta.errors);
	const showError = Boolean(code) && field.state.meta.isTouched;

	const toggle = (role: Role, selected: boolean) => {
		const next = selected
			? [...field.state.value, role]
			: field.state.value.filter((value) => value !== role);
		field.handleChange(next);
		field.handleBlur();
	};

	return (
		<fieldset className="flex flex-col gap-2">
			<Label>{label}</Label>
			{ROLES.map((role) => (
				<Checkbox
					key={role}
					isSelected={field.state.value.includes(role)}
					onChange={(selected) => toggle(role, selected)}
				>
					<Checkbox.Content>
						<Checkbox.Control>
							<Checkbox.Indicator />
						</Checkbox.Control>
						{t(`roles.${role}`)}
					</Checkbox.Content>
				</Checkbox>
			))}
			{showError && code ? (
				<FieldError className="text-sm text-danger">{t(`validation.${code}`)}</FieldError>
			) : null}
		</fieldset>
	);
}
