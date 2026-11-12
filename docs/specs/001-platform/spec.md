# 001 Platforma — specyfikacja

Model: [01-platform.md](../../domain/01-platform.md). Decyzje właściciela: role i logowanie (decyzja 9), ochrona danych
(decyzja 10), parametry prawne z datą (PRODUCT.md).

## Cel

Każdy, kto otwiera aplikację, musi się zalogować. Wie, kim jest, a baza wie, kto wykonuje operację, i sprawdza
uprawnienia. Administrator prowadzi spółki grupy i parametry prawne z okresem obowiązywania. Każda zmiana zostawia
ślad w audycie. Programista loguje się jednym kliknięciem na dowolną rolę, ale tylko w Development.

## Wymagania

### Logowanie i sesja
- **R1.** Logowanie loginem albo e-mailem i hasłem. Hasło sprawdza baza; aplikacja nigdy nie przechowuje hasła ani
  jego skrótu poza bazą.
- **R2.** Po udanym logowaniu powstaje sesja z nieprzewidywalnym tokenem; baza trzyma tylko skrót tokenu. Sesja
  wygasa po czasie z konfiguracji (`Auth__SessionHours`, domyślnie 12). Wylogowanie usuwa sesję od razu.
- **R3.** Przeglądarka nie widzi tokenu w JavaScripcie: trzyma go ciasteczko `httpOnly`, `SameSite=Lax`, `Secure`
  poza Development, ustawiane przez serwer frontu.
- **R4.** Po `Auth__MaxFailedAttempts` (domyślnie 5) nieudanych próbach z rzędu konto jest zablokowane na
  `Auth__LockoutMinutes` (domyślnie 15). Komunikat błędu nie zdradza, czy login istnieje.
- **R5.** Nieaktywne konto nie może się zalogować, a jego sesje przestają działać.
- **R6.** Każde żądanie API poza logowaniem, `/health` i opcjami logowania wymaga ważnej sesji, a bez niej zwraca 401.
  Front przy 401 przechodzi na stronę logowania.
- **R7.** `GET /api/auth/me` zwraca konto, role i profile (profile od modułu 003; dziś pusta lista).

### Logowanie deweloperskie
- **R8.** Na stronie logowania są przyciski „Zaloguj jako Kadry / Administrator / Pracownik” (konta z danych
  deweloperskich), bez hasła.
- **R9.** Działa tylko, gdy środowisko to `Development` **i** `Auth__DevLogin=true`. W każdym innym przypadku endpoint
  nie istnieje (404), a przycisków nie ma w HTML. Wymaga testu.

### Pierwszy administrator
- **R10.** Gdy w bazie nie ma żadnego Administratora, API przy starcie zakłada go z `Auth__BootstrapAdmin__Login`,
  `__Email` i `__Password`, jeśli są ustawione. Gdy Administrator istnieje, ustawienia są ignorowane.

### Role i uprawnienia
- **R11.** Role: `Kadry`, `Administrator`, `Pracownik`; konto może mieć kilka. Uprawnienia sprawdza baza w każdej
  rutynie kontraktu; brak uprawnień → `U0004` (403).
- **R12.** Aktor (id konta) trafia do ustawienia sesji bazy w każdym żądaniu; rutyny zapisują go w audycie
  i w `created_by`.
- **R13.** Stanowiska: lista dla Kadr i Administratora, dodawanie dla Kadr i Administratora (zmiana względem
  szkieletu, gdzie nie było logowania).

### Użytkownicy
- **R14.** Administrator widzi listę kont, zakłada konto (login, e-mail, nazwa, role, hasło początkowe), zmienia role
  i aktywność. Hasło: co najmniej `Auth__MinPasswordLength` znaków (domyślnie 12).
- **R15.** Administrator nie może odebrać sobie roli Administratora ani wyłączyć własnego konta (`U0003`), żeby nie
  zostać bez administratora.

### Spółki
- **R16.** Administrator zakłada i edytuje spółkę: nazwa, nazwa krótka (unikalna), NIP (unikalny, poprawna suma
  kontrolna), REGON (9 albo 14 cyfr, suma kontrolna), adres, aktywność. Kadry i Administrator widzą listę.
- **R17.** Edycja wymaga wersji wiersza; edycja nieaktualnej wersji → `U0003` (409).

### Parametry prawne
- **R18.** Administrator widzi parametry z okresami i źródłem, z filtrem kodu i „obowiązujące w dniu”.
- **R19.** Administrator dodaje nową wartość parametru od daty; poprzedni otwarty okres tego kodu kończy się dzień
  wcześniej. Wartość od daty wcześniejszej niż początek obowiązującego okresu → `U0001`.
- **R20.** Baza ma parametry 2026 z [09-payroll.md](../../domain/09-payroll.md#parametry-scenariusza) (dane, nie
  dane deweloperskie), ze źródłami.
- **R21.** Funkcja wewnętrzna zwraca wartość parametru w dniu; brak wartości → błąd, nie zero.

### Audyt
- **R22.** Każdy zapis kontraktu (konto, rola, spółka, parametr, stanowisko) zostawia wpis audytu: kto, kiedy, encja,
  id, akcja, stan przed i po. Wpisów nie da się zmienić ani usunąć przez API.
- **R23.** Tabela audytu odczytów istnieje z rutyną zapisu; używają jej moduły od 003.
- **R24.** Administrator przegląda audyt zmian (filtr encji, id, konta, okresu).

## Przypadki brzegowe
- Login z różną wielkością liter → to samo konto. Spacje na brzegach są obcinane.
- Dwa równoczesne logowania tego samego konta → dwie niezależne sesje.
- Sesja wygasła w trakcie pracy → następne żądanie 401 → strona logowania z powrotem na stronę, z której przyszedł.
- Parametr dodany z datą równą początkowi obecnego okresu → zastępuje go (to korekta pomyłki), z wpisem audytu.

## Poza zakresem 001
- Logowanie OIDC — **część 2 tej specyfikacji** (R25–R27 dopiszę po wdrożeniu R1–R24): konfiguracja dostawcy z env,
  powiązanie po e-mailu, brak hasła dla takich kont.
- Reset hasła e-mailem i potwierdzanie e-maila.
- Powiązanie konta z kartoteką (wymaga modułu 003).
- Szyfrowanie PESEL (moduł 003).
