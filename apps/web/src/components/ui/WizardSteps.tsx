/** Step indicator of a wizard dialog: done, current and next steps; a done step can be clicked to go back. */
export function WizardSteps({
	steps,
	current,
	onSelect,
}: {
	steps: readonly string[];
	current: number;
	onSelect: (index: number) => void;
}) {
	return (
		<ol className="wizard-steps flex flex-wrap items-center gap-2 text-sm">
			{steps.map((label, index) => {
				const state = index < current ? "done" : index === current ? "current" : "next";
				return (
					<li key={label} className="flex items-center gap-2">
						{index > 0 ? <span className="h-px w-6 bg-border" aria-hidden /> : null}
						<button
							type="button"
							disabled={state === "next"}
							aria-current={state === "current" ? "step" : undefined}
							onClick={() => onSelect(index)}
							className={`flex items-center gap-2 rounded-md px-2 py-1 ${
								state === "current"
									? "bg-accent-soft font-medium text-accent-soft-foreground"
									: state === "done"
										? "text-foreground hover:bg-default"
										: "text-muted"
							}`}
						>
							<span
								className={`flex size-5 items-center justify-center rounded-full text-xs ${
									state === "next" ? "border border-border" : "bg-accent text-accent-foreground"
								}`}
							>
								{index + 1}
							</span>
							{label}
						</button>
					</li>
				);
			})}
		</ol>
	);
}

/** Index of the first step that owns one of the fields with a server error; null when none does. */
export function firstStepWithError(
	stepFields: readonly (readonly string[])[],
	errors: Record<string, unknown> | undefined,
): number | null {
	const failed = Object.keys(errors ?? {});
	const index = stepFields.findIndex((fields) => fields.some((field) => failed.includes(field)));
	return index === -1 ? null : index;
}
