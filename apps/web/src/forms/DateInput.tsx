import { useTranslation } from "react-i18next";
import { DayInput } from "#/components/ui/DateInputs";
import { useFieldContext } from "./context";
import { errorCode } from "./errors";

export function DateInput({ label, isRequired }: { label: string; isRequired?: boolean }) {
	const field = useFieldContext<string>();
	const { t } = useTranslation();
	const code = errorCode(field.state.meta.errors);
	const showError = Boolean(code) && field.state.meta.isTouched;

	return (
		<DayInput
			label={label}
			value={field.state.value}
			onChange={(value) => field.handleChange(value ?? "")}
			onBlur={field.handleBlur}
			isRequired={isRequired}
			isInvalid={showError}
			errorMessage={showError && code ? t(`validation.${code}`) : null}
		/>
	);
}
