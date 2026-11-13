import { createFormHook } from "@tanstack/react-form";
import { CheckboxInput } from "./CheckboxInput";
import { fieldContext, formContext } from "./context";
import { DateInput } from "./DateInput";
import { RolesInput } from "./RolesInput";
import { SelectInput } from "./SelectInput";
import { SwitchInput } from "./SwitchInput";
import { TextInput } from "./TextInput";

export const { useAppForm } = createFormHook({
	fieldComponents: { TextInput, SwitchInput, CheckboxInput, DateInput, RolesInput, SelectInput },
	formComponents: {},
	fieldContext,
	formContext,
});

export { text } from "./validation";
