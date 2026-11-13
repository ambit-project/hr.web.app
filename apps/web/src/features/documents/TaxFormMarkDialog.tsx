import { Button, Modal, toast } from "@heroui/react";
import { forwardRef, useId, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { useAppForm } from "#/forms";
import { onSaveError } from "#/forms/feedback";
import { applyFieldErrors } from "#/forms/serverErrors";
import { useMarkTaxFormGiven, useMarkTaxFormSent } from "./documentsQueries";
import { GIVEN_HOWS, type GivenHow, todayIso } from "./types";

/** Which mark to set on which form; `name` titles the dialog. */
type Target = {
	mode: "sent" | "given";
	id: string;
	name: string;
	givenAt: string | null;
	givenHow: string | null;
};

export type TaxFormMarkDialogHandle = {
	sent: (id: string, name: string) => void;
	given: (id: string, name: string, givenAt: string | null, givenHow: string | null) => void;
};

/** Marks a PIT-11 as sent to the tax office (date) or handed to the taxpayer (date and way) — R7, R9. */
export const TaxFormMarkDialog = forwardRef<TaxFormMarkDialogHandle>(
	function TaxFormMarkDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(
			ref,
			() => ({
				sent: (id, name) => setTarget({ mode: "sent", id, name, givenAt: null, givenHow: null }),
				given: (id, name, givenAt, givenHow) =>
					setTarget({ mode: "given", id, name, givenAt, givenHow }),
			}),
			[],
		);
		const close = () => setTarget(null);

		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={target !== null}
					onOpenChange={(open) => !open && close()}
				>
					<Modal.Container scroll="inside">
						{target ? <MarkForm target={target} onDone={close} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

const schema = z.object({
	date: z.string().min(1, "required"),
	givenHow: z.string(),
});

function MarkForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const formId = useId();
	const sent = useMarkTaxFormSent();
	const given = useMarkTaxFormGiven();
	const isSent = target.mode === "sent";
	const texts = isSent ? "documents.pit11.sentDialog" : "documents.pit11.givenDialog";

	const form = useAppForm({
		defaultValues: {
			date: target.givenAt ?? todayIso(),
			givenHow: target.givenHow ?? "paper",
		},
		validators: { onChange: schema },
		onSubmit: async ({ value, formApi }) => {
			try {
				if (isSent) {
					await sent.mutateAsync({ id: target.id, request: { sentAt: value.date } });
				} else {
					await given.mutateAsync({
						id: target.id,
						request: { givenAt: value.date, givenHow: value.givenHow },
					});
				}
				toast.success(t(`${texts}.saved`));
				onDone();
			} catch (error) {
				onSaveError(
					t,
					error,
					(errors) => {
						// The database names the date sentAt or givenAt; the form has one date field.
						const mapped = Object.fromEntries(
							Object.entries(errors ?? {}).map(([key, codes]) => [
								key === "sentAt" || key === "givenAt" ? "date" : key,
								codes,
							]),
						);
						return applyFieldErrors(formApi, mapped, ["date", "givenHow"]);
					},
					onDone,
				);
			}
		},
	});

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t(`${texts}.title`, { name: target.name })}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				<p className="mb-4 text-sm text-muted">{t(`${texts}.hint`)}</p>
				<form
					id={formId}
					className="grid gap-4 sm:grid-cols-2"
					noValidate
					onSubmit={(event) => {
						event.preventDefault();
						void form.handleSubmit();
					}}
				>
					<form.AppField name="date">
						{(field) => (
							<field.DateInput
								label={
									isSent
										? t("documents.pit11.sentDialog.sentAt")
										: t("documents.pit11.givenDialog.givenAt")
								}
								isRequired
							/>
						)}
					</form.AppField>
					{isSent ? null : (
						<form.AppField name="givenHow">
							{(field) => (
								<field.SelectInput
									label={t("documents.pit11.givenDialog.givenHow")}
									options={GIVEN_HOWS.map((how) => ({
										id: how,
										label: t(`documents.taxForms.givenHows.${how as GivenHow}`),
									}))}
									isRequired
								/>
							)}
						</form.AppField>
					)}
				</form>
			</Modal.Body>
			<Modal.Footer>
				<Button slot="close" variant="secondary">
					{t("common.cancel")}
				</Button>
				<form.Subscribe selector={(state) => state.isSubmitting}>
					{(isSubmitting) => (
						<Button type="submit" form={formId} isPending={isSubmitting}>
							{t("common.save")}
						</Button>
					)}
				</form.Subscribe>
			</Modal.Footer>
		</Modal.Dialog>
	);
}
