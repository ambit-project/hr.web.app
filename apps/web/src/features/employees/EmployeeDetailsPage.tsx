import { AlertDialog, Button, Spinner, Tabs, toast } from "@heroui/react";
import { useQuery } from "@tanstack/react-query";
import { getRouteApi, Link as RouterLink, useNavigate } from "@tanstack/react-router";
import { useRef } from "react";
import { useTranslation } from "react-i18next";
import type { GetEmployeeResponse } from "#/api/models";
import { EditButton } from "#/components/ui/AddButton";
import { hasRole } from "#/features/auth/session";
import { EmploymentsSection } from "#/features/employments/EmploymentsSection";
import { DeductionsSection } from "#/features/pay-components/DeductionsSection";
import { TaxDeclarationsSection } from "#/features/tax-declarations/TaxDeclarationsSection";
import { birthDateWithAge } from "#/lib/age";
import { ProblemError } from "#/lib/problem";
import { EmployeeDialog, type EmployeeDialogHandle } from "./EmployeeDialog";
import { employeeQuery, useDeleteEmployee } from "./employeesQueries";
import { AccountSection } from "./sections/AccountSection";
import { AddressesSection } from "./sections/AddressesSection";
import { BankSection } from "./sections/BankSection";
import { ReadsSection } from "./sections/ReadsSection";
import { EMPLOYEE_TABS, type EmployeeStatus, type EmployeeTab } from "./types";

const route = getRouteApi("/employees/$id");

export function EmployeeDetailsPage() {
	const { t } = useTranslation();
	const { id } = route.useParams();
	const search = route.useSearch();
	const navigate = route.useNavigate();
	const { me } = route.useRouteContext();
	const { data: employee, isPending } = useQuery(employeeQuery(id));
	const tab: EmployeeTab = search.tab ?? "data";
	// Read audit is for the Administrator; Kadry would get 403 from the database.
	const tabs = EMPLOYEE_TABS.filter((item) => item !== "reads" || hasRole(me, "administrator"));

	if (isPending || !employee) {
		return <Spinner aria-label={t("common.loading")} />;
	}

	return (
		<section className="flex flex-col gap-4">
			<RouterLink
				to="/employees"
				search={{ companyId: employee.companyId }}
				className="text-sm text-muted hover:underline"
			>
				← {t("employees.title")}
			</RouterLink>
			<header>
				<h1 className="text-2xl font-semibold">
					{employee.lastName} {employee.firstName}
				</h1>
				<p className="text-sm text-muted">
					{employee.companyShortName} · {t("employees.fields.number")}{" "}
					<code>{employee.number}</code> · {t("employees.fields.acronym")}{" "}
					<code>{employee.acronym}</code>
				</p>
			</header>

			<Tabs
				selectedKey={tab}
				onSelectionChange={(key) =>
					void navigate({
						search: { tab: key === "data" ? undefined : (key as EmployeeTab) },
						replace: true,
					})
				}
			>
				<Tabs.ListContainer>
					<Tabs.List aria-label={t("employees.title")}>
						{tabs.map((item) => (
							<Tabs.Tab key={item} id={item}>
								{t(`employees.tabs.${item}`)}
								<Tabs.Indicator />
							</Tabs.Tab>
						))}
					</Tabs.List>
				</Tabs.ListContainer>
				<Tabs.Panel id="data" className="pt-4">
					<DataSection employee={employee} />
				</Tabs.Panel>
				<Tabs.Panel id="employments" className="pt-4">
					<EmploymentsSection employeeId={employee.id} />
				</Tabs.Panel>
				<Tabs.Panel id="taxDeclarations" className="pt-4">
					<TaxDeclarationsSection employeeId={employee.id} />
				</Tabs.Panel>
				<Tabs.Panel id="deductions" className="pt-4">
					<DeductionsSection employeeId={employee.id} />
				</Tabs.Panel>
				<Tabs.Panel id="addresses" className="pt-4">
					<AddressesSection employeeId={employee.id} />
				</Tabs.Panel>
				<Tabs.Panel id="bank" className="pt-4">
					<BankSection employee={employee} />
				</Tabs.Panel>
				<Tabs.Panel id="account" className="pt-4">
					<AccountSection employee={employee} />
				</Tabs.Panel>
				{tabs.includes("reads") ? (
					<Tabs.Panel id="reads" className="pt-4">
						<ReadsSection employeeId={employee.id} />
					</Tabs.Panel>
				) : null}
			</Tabs>
		</section>
	);
}

function DataSection({ employee }: { employee: GetEmployeeResponse }) {
	const { t } = useTranslation();
	const dialog = useRef<EmployeeDialogHandle>(null);
	const remove = useDeleteEmployee();
	const navigate = useNavigate();

	const rows: [string, string | null][] = [
		[t("employees.fields.firstName"), employee.firstName],
		[t("employees.fields.secondName"), employee.secondName],
		[t("employees.fields.lastName"), employee.lastName],
		[t("employees.fields.pesel"), employee.pesel],
		[t("employees.fields.identityDocument"), employee.identityDocument],
		[t("employees.fields.birthDate"), birthDateWithAge(employee.birthDate)],
		[
			t("employees.fields.taxOffice"),
			employee.taxOfficeCode ? `${employee.taxOfficeCode} ${employee.taxOfficeName ?? ""}` : null,
		],
		[t("employees.fields.email"), employee.email],
		[t("employees.status.label"), t(`employees.status.${employee.status as EmployeeStatus}`)],
		[
			t("employees.retention.label"),
			employee.retentionOpen
				? t("employees.retention.open")
				: (employee.retentionUntil ?? t("employees.retention.none")),
		],
	];

	const onDelete = async () => {
		try {
			await remove.mutateAsync(employee.id);
			toast.success(t("employees.delete.deleted"));
			await navigate({ to: "/employees", search: { companyId: employee.companyId } });
		} catch (error) {
			toast.danger(
				error instanceof ProblemError && error.problem.status === 409
					? error.problem.reason === "retention"
						? t("reasons.retention")
						: t("employees.delete.blocked")
					: t("common.serverError"),
			);
		}
	};

	return (
		<div className="flex flex-col gap-4">
			<dl className="grid max-w-2xl grid-cols-[12rem_1fr] gap-x-4 gap-y-2 text-sm">
				{rows.map(([label, value]) => (
					<div key={label} className="contents">
						<dt className="text-muted">{label}</dt>
						<dd>{value ?? "—"}</dd>
					</div>
				))}
			</dl>
			<div className="flex gap-2">
				<EditButton label={t("common.edit")} onPress={() => dialog.current?.edit(employee)} />
				<AlertDialog>
					<Button variant="danger-soft">{t("employees.delete.action")}</Button>
					<AlertDialog.Backdrop isDismissable={false} isKeyboardDismissDisabled>
						<AlertDialog.Container>
							<AlertDialog.Dialog className="sm:max-w-[420px]">
								<AlertDialog.Header>
									<AlertDialog.Icon status="danger" />
									<AlertDialog.Heading>{t("employees.delete.title")}</AlertDialog.Heading>
								</AlertDialog.Header>
								<AlertDialog.Body>
									<p>{t("employees.delete.body")}</p>
								</AlertDialog.Body>
								<AlertDialog.Footer>
									<Button slot="close" variant="tertiary">
										{t("common.cancel")}
									</Button>
									<Button slot="close" variant="danger" onPress={() => void onDelete()}>
										{t("employees.delete.action")}
									</Button>
								</AlertDialog.Footer>
							</AlertDialog.Dialog>
						</AlertDialog.Container>
					</AlertDialog.Backdrop>
				</AlertDialog>
			</div>
			<EmployeeDialog ref={dialog} />
		</div>
	);
}
