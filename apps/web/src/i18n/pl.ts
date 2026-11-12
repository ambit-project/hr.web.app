// Polish is the only language today; keys are grouped by area so an English file can follow without code changes.
export const pl = {
	app: {
		name: "Kadry i Płace",
		nav: { positions: "Stanowiska", my: "Moje dokumenty" },
	},
	common: {
		add: "Dodaj",
		cancel: "Anuluj",
		save: "Zapisz",
		search: "Szukaj",
		previous: "Poprzednia",
		next: "Następna",
		loading: "Wczytywanie…",
		serverError: "Serwer nie odpowiedział poprawnie. Spróbuj ponownie.",
	},
	auth: {
		title: "Zaloguj się, aby kontynuować.",
		login: "Login lub e-mail",
		password: "Hasło",
		submit: "Zaloguj",
		failed: "Nieprawidłowy login lub hasło albo konto jest zablokowane.",
		logout: "Wyloguj",
		noAccess: "Twoje konto nie ma jeszcze żadnej roli. Poproś administratora o nadanie uprawnień.",
		dev: {
			hint: "Środowisko deweloperskie — logowanie bez hasła:",
			as: "Zaloguj jako {{role}}",
		},
	},
	roles: { kadry: "Kadry", administrator: "Administrator", pracownik: "Pracownik" },
	my: {
		title: "Moje dokumenty",
		empty: "Paski wypłat, PIT-y i zgody pojawią się tutaj, gdy kadry powiążą konto z kartoteką.",
	},
	validation: {
		required: "Pole jest wymagane.",
		maxLength: "Wartość jest za długa.",
		duplicate: "Taka wartość już istnieje.",
		unknown: "Nieprawidłowa wartość.",
	},
	positions: {
		title: "Stanowiska",
		searchPlaceholder: "Szukaj po nazwie",
		empty: "Brak stanowisk.",
		total_one: "{{count}} stanowisko",
		total_few: "{{count}} stanowiska",
		total_many: "{{count}} stanowisk",
		total_other: "{{count}} stanowiska",
		columns: { name: "Nazwa", description: "Opis", active: "Aktywne" },
		active: { yes: "tak", no: "nie" },
		create: {
			title: "Nowe stanowisko",
			name: "Nazwa",
			description: "Opis",
			created: "Dodano stanowisko „{{name}}”.",
		},
	},
} as const;
