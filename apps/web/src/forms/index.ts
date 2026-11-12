import { createFormHook } from "@tanstack/react-form";
import { fieldContext, formContext } from "./context";
import { DateInput } from "./DateInput";
import { RolesInput } from "./RolesInput";
import { SwitchInput } from "./SwitchInput";
import { TextInput } from "./TextInput";

export const { useAppForm } = createFormHook({
	fieldComponents: { TextInput, SwitchInput, DateInput, RolesInput },
	formComponents: {},
	fieldContext,
	formContext,
});

export { text } from "./validation";
