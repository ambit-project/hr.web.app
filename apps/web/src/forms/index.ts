import { createFormHook } from "@tanstack/react-form";
import { fieldContext, formContext } from "./context";
import { TextInput } from "./TextInput";

export const { useAppForm } = createFormHook({
	fieldComponents: { TextInput },
	formComponents: {},
	fieldContext,
	formContext,
});

export { text } from "./validation";
