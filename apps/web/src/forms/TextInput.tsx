import { Description, FieldError, Input, Label, TextArea, TextField } from "@heroui/react";
import { useTranslation } from "react-i18next";
import { useFieldContext } from "./context";
import { errorCode } from "./errors";

type TextInputProps = {
	label: string;
	description?: string;
	isRequired?: boolean;
	multiline?: boolean;
	type?: "text" | "password" | "email";
	autoComplete?: string;
};

export function TextInput({
	label,
	description,
	isRequired,
	multiline,
	type,
	autoComplete,
}: TextInputProps) {
	const field = useFieldContext<string>();
	const { t } = useTranslation();
	const code = errorCode(field.state.meta.errors);
	const showError = Boolean(code) && field.state.meta.isTouched;

	return (
		<TextField
			name={field.name}
			value={field.state.value}
			onChange={field.handleChange}
			onBlur={field.handleBlur}
			isRequired={isRequired}
			isInvalid={showError}
			validationBehavior="aria"
		>
			<Label>{label}</Label>
			{multiline ? <TextArea rows={3} /> : <Input type={type} autoComplete={autoComplete} />}
			{description ? <Description>{description}</Description> : null}
			<FieldError>{showError && code ? t(`validation.${code}`) : null}</FieldError>
		</TextField>
	);
}
