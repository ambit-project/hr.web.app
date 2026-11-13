/** Full years on `on` for a birth date "YYYY-MM-DD"; null for a malformed date. */
export function ageOn(birthDate: string, on: Date = new Date()): number | null {
	const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(birthDate);
	if (!match) return null;
	const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
	let age = on.getFullYear() - year;
	const beforeBirthday =
		on.getMonth() + 1 < month || (on.getMonth() + 1 === month && on.getDate() < day);
	if (beforeBirthday) age -= 1;
	return age;
}

/** "1985-02-11 (41)" — birth date with the age in brackets, as tables show it. */
export function birthDateWithAge(birthDate: string, on?: Date): string {
	const age = ageOn(birthDate, on);
	return age === null ? birthDate : `${birthDate} (${age})`;
}
