import { Button, Label, Modal, Radio, RadioGroup, toast } from "@heroui/react";
import { forwardRef, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";
import type { CopyEmployeesResponse } from "#/api/models";
import { CompanySelect } from "#/features/companies/CompanySelect";
import { ProblemError } from "#/lib/problem";
import { useCopyEmployees } from "./employeesQueries";
import type { EmployeeStatus } from "./types";

export type CopyFilter = { companyId?: string; search?: string; status?: EmployeeStatus };

export type CopyEmployeesDialogHandle = {
	open: (input: { selectedIds: string[]; filter: CopyFilter; filterTotal: number }) => void;
};

type Target = { selectedIds: string[]; filter: CopyFilter; filterTotal: number };

const MAX_COPY = 500;

/** Copies employee records to another company: the selected rows or everything the list filter matches (003 R18–R22). */
export const CopyEmployeesDialog = forwardRef<CopyEmployeesDialogHandle>(
	function CopyEmployeesDialog(_props, ref) {
		const [target, setTarget] = useState<Target | null>(null);
		useImperativeHandle(ref, () => ({ open: setTarget }), []);
		return (
			<Modal>
				<Modal.Backdrop
					isDismissable={false}
					isKeyboardDismissDisabled
					isOpen={target !== null}
					onOpenChange={(open) => !open && setTarget(null)}
				>
					<Modal.Container scroll="inside">
						{target ? <CopyForm target={target} onDone={() => setTarget(null)} /> : null}
					</Modal.Container>
				</Modal.Backdrop>
			</Modal>
		);
	},
);

function CopyForm({ target, onDone }: { target: Target; onDone: () => void }) {
	const { t } = useTranslation();
	const copy = useCopyEmployees();
	const hasSelection = target.selectedIds.length > 0;
	const [mode, setMode] = useState<"selected" | "filter">(hasSelection ? "selected" : "filter");
	const [company, setCompany] = useState("");
	const [error, setError] = useState<string | null>(null);
	const [result, setResult] = useState<CopyEmployeesResponse | null>(null);
	const count = mode === "selected" ? target.selectedIds.length : target.filterTotal;

	const submit = async () => {
		if (!company) {
			setError(t("validation.required"));
			return;
		}
		try {
			const response = await copy.mutateAsync({
				targetCompanyId: company,
				employeeIds: mode === "selected" ? target.selectedIds : [],
				companyId: mode === "filter" ? (target.filter.companyId ?? null) : null,
				search: mode === "filter" ? (target.filter.search ?? null) : null,
				status: mode === "filter" ? (target.filter.status ?? null) : null,
			});
			setResult(response);
			toast.success(t("employees.copy.done", { count: response.copied }));
		} catch (problem) {
			const code =
				problem instanceof ProblemError
					? (problem.problem.errors?.targetCompanyId?.[0] ??
						problem.problem.errors?.employeeIds?.[0])
					: undefined;
			if (code) setError(t(`validation.${code}`, t("validation.unknown")));
			else toast.danger(t("common.serverError"));
		}
	};

	return (
		<Modal.Dialog className="form-dialog">
			<Modal.Header>
				<Modal.Heading>{t("employees.copy.title")}</Modal.Heading>
			</Modal.Header>
			<Modal.Body>
				{result ? (
					<div className="flex flex-col gap-3 text-sm">
						<p>{t("employees.copy.done", { count: result.copied })}</p>
						{result.skipped.length > 0 ? (
							<>
								<p className="font-medium">
									{t("employees.copy.skipped", { count: result.skipped.length })}
								</p>
								<ul className="max-h-64 overflow-auto border border-border">
									{result.skipped.map((item) => (
										<li
											key={item.employeeId}
											className="flex justify-between gap-4 border-b border-border px-3 py-1.5 last:border-b-0"
										>
											<span>{item.name}</span>
											<span className="text-muted">
												{t(
													`employees.copy.reasons.${item.reason as "sameCompany" | "peselExists" | "personExists"}`,
												)}
											</span>
										</li>
									))}
								</ul>
							</>
						) : null}
					</div>
				) : (
					<div className="flex flex-col gap-4">
						<p className="text-sm text-muted">{t("employees.copy.hint")}</p>
						<RadioGroup value={mode} onChange={(value) => setMode(value as "selected" | "filter")}>
							<Label>{t("employees.copy.what")}</Label>
							<Radio value="selected" isDisabled={!hasSelection}>
								<Radio.Content>
									<Radio.Control>
										<Radio.Indicator />
									</Radio.Control>
									{t("employees.copy.selected", { count: target.selectedIds.length })}
								</Radio.Content>
							</Radio>
							<Radio value="filter">
								<Radio.Content>
									<Radio.Control>
										<Radio.Indicator />
									</Radio.Control>
									{t("employees.copy.filter", { count: target.filterTotal })}
								</Radio.Content>
							</Radio>
						</RadioGroup>
						<CompanySelect
							label={t("employees.copy.target")}
							value={company}
							onChange={(value) => {
								setCompany(value);
								setError(null);
							}}
							isRequired
							errorMessage={error}
						/>
						{count > MAX_COPY ? (
							<p className="text-sm text-danger">{t("validation.tooMany")}</p>
						) : null}
					</div>
				)}
			</Modal.Body>
			<Modal.Footer>
				{result ? (
					<Button onPress={onDone}>{t("common.close")}</Button>
				) : (
					<>
						<Button slot="close" variant="secondary">
							{t("common.cancel")}
						</Button>
						<Button
							isPending={copy.isPending}
							isDisabled={count === 0 || count > MAX_COPY}
							onPress={() => void submit()}
						>
							{t("employees.copy.submit", { count })}
						</Button>
					</>
				)}
			</Modal.Footer>
		</Modal.Dialog>
	);
}
