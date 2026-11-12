import { createFormHook } from "@tanstack/react-form";
import { fieldContext, formContext } from "./context";
import { SwitchInput } from "./SwitchInput";
import { TextInput } from "./TextInput";

export const { useAppForm } = createFormHook({
	fieldComponents: { TextInput, SwitchInput },
	formComponents: {},
	fieldContext,
	formContext,
});

export { text } from "./validation";
