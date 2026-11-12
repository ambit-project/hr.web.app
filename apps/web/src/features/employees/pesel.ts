// Mirrors employees.pesel_valid and employees.pesel_birth_date (db/changelog/modules/20-employees/employees.helpers.sql).
// Only a hint for the form (birth date filled from PESEL); the database stays the authority.
const WEIGHTS = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
const CENTURY: Record<number, number> = { 4: 1800, 0: 1900, 1: 2000, 2: 2100, 3: 2200 };

/** Birth date written in a PESEL as YYYY-MM-DD, or null when the number or the date is not valid. */
export function peselBirthDate(value: string): string | null {
	const pesel = value.replace(/\D/g, "");
	if (!/^\d{11}$/.test(pesel)) return null;
	const digits = [...pesel].map(Number);
	const sum = WEIGHTS.reduce((total, weight, index) => total + weight * (digits[index] ?? 0), 0);
	if ((10 - (sum % 10)) % 10 !== digits[10]) return null;

	const rawMonth = Number(pesel.slice(2, 4));
	const century = CENTURY[Math.floor(rawMonth / 20)];
	if (century === undefined) return null;
	const year = century + Number(pesel.slice(0, 2));
	const month = rawMonth % 20;
	const day = Number(pesel.slice(4, 6));
	const date = new Date(Date.UTC(year, month - 1, day));
	if (
		date.getUTCFullYear() !== year ||
		date.getUTCMonth() !== month - 1 ||
		date.getUTCDate() !== day
	) {
		return null;
	}
	return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}
