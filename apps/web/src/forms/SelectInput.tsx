import { useTranslation } from "react-i18next";
import { type Option, OptionSelect } from "#/components/ui/OptionSelect";
import { useFieldContext } from "./context";
import { errorCode } from "./errors";

export function SelectInput({
	label,
	options,
	isRequired,
}: {
	label: string;
	options: readonly Option[];
	isRequired?: boolean;
}) {
	const field = useFieldContext<string>();
	const { t } = useTranslation();
	const code = errorCode(field.state.meta.errors);
	const showError = Boolean(code) && field.state.meta.isTouched;

	return (
		<OptionSelect
			label={label}
			value={field.state.value}
			options={options}
			onChange={field.handleChange}
			onBlur={field.handleBlur}
			isRequired={isRequired}
			errorMessage={showError && code ? t(`validation.${code}`) : null}
		/>
	);
}
