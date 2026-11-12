// Tabs of the employee page, kept in the URL (?tab=…) so a link can open a section directly.
export const EMPLOYEE_TABS = ["data", "addresses", "bank", "account", "reads"] as const;
export type EmployeeTab = (typeof EMPLOYEE_TABS)[number];

// Address kinds checked by employees.addresses (db/changelog/1-ddl/20261112-12-employees.sql).
export const ADDRESS_KINDS = ["residence", "correspondence"] as const;
export type AddressKind = (typeof ADDRESS_KINDS)[number];
