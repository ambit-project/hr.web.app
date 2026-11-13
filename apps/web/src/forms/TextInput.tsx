import {
	Button,
	Description,
	FieldError,
	Input,
	InputGroup,
	Label,
	TextArea,
	TextField,
} from "@heroui/react";
import { Eye, EyeOff } from "lucide-react";
import { useState } from "react";
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
			{multiline ? (
				<TextArea rows={3} />
			) : type === "password" ? (
				<PasswordInput autoComplete={autoComplete} />
			) : (
				<Input type={type} autoComplete={autoComplete} />
			)}
			{description ? <Description>{description}</Description> : null}
			<FieldError>{showError && code ? t(`validation.${code}`) : null}</FieldError>
		</TextField>
	);
}

/** A password with a show/hide toggle, so the Administrator can check what they typed before saving. */
function PasswordInput({ autoComplete }: { autoComplete?: string }) {
	const { t } = useTranslation();
	const [visible, setVisible] = useState(false);
	const label = visible ? t("common.hidePassword") : t("common.showPassword");

	return (
		<InputGroup>
			<InputGroup.Input type={visible ? "text" : "password"} autoComplete={autoComplete} />
			<InputGroup.Suffix className="pr-1">
				<Button
					isIconOnly
					size="sm"
					variant="ghost"
					aria-label={label}
					aria-pressed={visible}
					onPress={() => setVisible((value) => !value)}
				>
					{visible ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
				</Button>
			</InputGroup.Suffix>
		</InputGroup>
	);
}
