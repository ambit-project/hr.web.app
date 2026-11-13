type MetaLike = { isTouched: boolean; errorMap: object; errorSourceMap: object };

/** Minimal slice of the TanStack Form API needed to put server errors on fields. */
type FieldMetaSetter<TField extends string> = {
	setFieldMeta: (field: TField, updater: <M extends MetaLike>(meta: M) => M) => void;
};

/**
 * Field codes from the database (U0001) land on the fields like client-side errors. Returns false when the problem
 * named no known field, so the caller can show a general message instead.
 */
export function applyFieldErrors<TField extends string>(
	form: FieldMetaSetter<TField>,
	errors: Record<string, string[]> | undefined,
	fields: readonly TField[],
): boolean {
	let applied = false;
	for (const [field, codes] of Object.entries(errors ?? {})) {
		if ((fields as readonly string[]).includes(field)) {
			form.setFieldMeta(field as TField, (meta) => ({
				...meta,
				isTouched: true,
				// Marked as a form-level change error, so the form's next change validation clears it: a server error
				// must not outlive the values it was about and block the next submit (form-core keeps errors whose
				// source is "field").
				errorMap: { ...meta.errorMap, onChange: codes[0] },
				errorSourceMap: { ...meta.errorSourceMap, onChange: "form" },
			}));
			applied = true;
		}
	}
	return applied;
}
