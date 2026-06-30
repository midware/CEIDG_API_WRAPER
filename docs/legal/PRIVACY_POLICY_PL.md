# Polityka prywatnosci leadbase.network

Wersja robocza: 2026-06-30  
Status: draft do przegladu prawnego przed publikacja.

## 1. Administrator danych

Administratorem danych jest [UZUPELNIC: pelna nazwa, adres, NIP/REGON/KRS/CEIDG], operator serwisu leadbase.network.

Kontakt:

- email privacy: [UZUPELNIC: privacy@leadbase.network]
- email support: [UZUPELNIC: support@leadbase.network]
- adres korespondencyjny: [UZUPELNIC]
- IOD/DPO: [UZUPELNIC: czy powolano; jesli nie, wskazac kontakt privacy]

## 2. Zakres polityki

Polityka opisuje przetwarzanie danych w zwiazku z:

- korzystaniem ze strony leadbase.network;
- rejestracja i logowaniem;
- uzywaniem API;
- rozliczaniem tokenow i platnosci;
- obsluga supportu;
- przetwarzaniem danych firmowych w bazie leadbase.network;
- przyszlymi funkcjami CRM, enrichment, kampanii email/SMS, o ile zostana wlaczone.

## 3. Kategorie danych

### Dane uzytkownikow platformy

- email, haslo w postaci hasha, nazwa wyswietlana;
- dane firmy i dane do faktury, jezeli sa podane;
- klucze API w postaci hashy i prefiksow;
- historia logowania, zapytan API, zuzycia tokenow;
- dane techniczne: IP, user-agent, logi bledow, zdarzenia bezpieczenstwa;
- korespondencja z supportem.

### Dane firmowe w bazie

W zaleznosci od zrodel i zakresu API:

- identyfikatory CEIDG, NIP, REGON;
- firma/nazwa dzialalnosci;
- status dzialalnosci;
- imie i nazwisko przedsiebiorcy, jezeli wystepuje w publicznym rejestrze;
- adres wykonywania dzialalnosci lub adres korespondencyjny, jezeli jest publiczny;
- PKD;
- telefon, email, strona WWW, jezeli sa dostepne w zrodle;
- surowy payload techniczny z API zrodlowego, jezeli jest przechowywany.

### Dane automatyczne i cookies

- cookies sesyjne i bezpieczenstwa;
- local storage dla funkcji demo;
- logi serwera;
- dane analityczne, jezeli wlaczymy narzedzia analityczne po uzyskaniu wymaganych zgód.

## 4. Zrodla danych

Dane moga pochodzic z:

- CEIDG i API CEIDG;
- zrodel publicznych i oficjalnych wskazanych w dokumentacji;
- danych podanych przez uzytkownika;
- danych wygenerowanych podczas korzystania z API;
- przyszlych integracji CRM lub uploadow klienta, jezeli zostana wlaczone.

## 5. Cele i podstawy prawne

| Cel | Podstawa prawna |
| --- | --- |
| Utworzenie konta, logowanie, obsluga API | art. 6 ust. 1 lit. b RODO |
| Rozliczenia, faktury, podatki | art. 6 ust. 1 lit. c RODO |
| Bezpieczenstwo, antifraud, logi | art. 6 ust. 1 lit. f RODO |
| Budowa i utrzymanie bazy firmowej B2B | art. 6 ust. 1 lit. f RODO |
| Udostepnianie danych firmowych klientom B2B przez API | art. 6 ust. 1 lit. f RODO |
| Obsluga zapytan, reklamacji, roszczen | art. 6 ust. 1 lit. b, c lub f RODO |
| Newsletter i marketing Operatora | art. 6 ust. 1 lit. a RODO lub lit. f, gdy dopuszczalne |
| Nieobowiazkowe cookies/analityka | zgoda, art. 6 ust. 1 lit. a RODO |

Uzasadniony interes obejmuje prowadzenie uslugi B2B, zapewnienie bezpieczenstwa, ochrone roszczen, analize rynku, udostepnianie danych z publicznych zrodel gospodarczych oraz przeciwdzialanie naduzyciom. Osoba, ktorej dane dotycza, moze wniesc sprzeciw.

## 6. Role RODO

1. Dla kont uzytkownikow, rozliczen, logow i bazy leadbase.network Operator jest administratorem danych.
2. Klient, ktory pobiera dane przez API i wykorzystuje je u siebie, jest odrebnym administratorem dalszego przetwarzania.
3. Operator moze byc procesorem tylko w zakresie funkcji, w ktorych Klient przekazuje wlasne dane do przetworzenia na jego polecenie, np. upload listy lub przyszle funkcje CRM/kampanii. Wtedy stosuje sie DPA.

## 7. Odbiorcy danych

Dane moga byc przekazywane:

- dostawcy hostingu i infrastruktury;
- dostawcy poczty transakcyjnej;
- dostawcy platnosci;
- dostawcy monitoringu bledow;
- biuru rachunkowemu i doradcom prawnym;
- organom publicznym, jezeli wymagaja tego przepisy;
- klientom API w zakresie danych firmowych udostepnianych w ramach Serwisu.

Lista konkretnych dostawcow powinna zostac uzupelniona przed publikacja.

## 8. Transfer poza EOG

Co do zasady wybieramy dostawcow z Europejskiego Obszaru Gospodarczego. Jezeli dane sa przekazywane poza EOG, stosujemy mechanizmy wymagane przez RODO, np. decyzje stwierdzajace odpowiedni stopien ochrony, standardowe klauzule umowne lub dodatkowe zabezpieczenia.

## 9. Retencja

Wstepny model retencji:

- konto aktywne: przez czas korzystania z uslugi;
- dane konta po zamknieciu: do 30 dni, chyba ze potrzebne do roszczen/ksiegowosci;
- faktury i dane ksiegowe: zgodnie z przepisami podatkowymi;
- logi API: 12 miesiecy, chyba ze potrzebne do bezpieczenstwa lub roszczen;
- historia tokenow: przez czas wymagany dla rozliczen;
- backupy: do [UZUPELNIC] dni;
- dane firmowe: przez czas utrzymywania bazy, z okresowa aktualizacja i z uwzglednieniem opt-out/sprzeciwu;
- suppression list: tak dlugo, jak potrzebne do respektowania sprzeciwu i nieprzywracania profilu.

## 10. Prawa osob

Osobie, ktorej dane dotycza, przysluguje:

- prawo dostepu do danych;
- prawo sprostowania;
- prawo usuniecia;
- prawo ograniczenia;
- prawo przenoszenia, jezeli ma zastosowanie;
- prawo sprzeciwu wobec przetwarzania na podstawie uzasadnionego interesu;
- prawo cofniecia zgody;
- prawo skargi do Prezesa Urzedu Ochrony Danych Osobowych.

Wnioski mozna skladac na [UZUPELNIC: privacy@leadbase.network] albo przez formularz opt-out.

## 11. Profilowanie i automatyczne decyzje

Na etapie obecnym Serwis nie powinien podejmowac decyzji wywolujacych skutki prawne wobec osob fizycznych w pelni automatycznie. Jezeli wprowadzimy scoring leadow, segmentacje lub AI, opiszemy logike i konsekwencje w tej polityce.

## 12. Bezpieczenstwo

Stosujemy lub planujemy stosowac:

- hashowanie hasel;
- przechowywanie hashy kluczy API zamiast pelnych kluczy;
- TLS;
- separacje srodowisk;
- logowanie zdarzen bezpieczenstwa;
- backupy;
- kontrole dostepu;
- rate limiting;
- monitoring bledow;
- procedury incydentow.

## 13. Zmiany polityki

Polityka moze byc aktualizowana. O istotnych zmianach poinformujemy uzytkownikow w Serwisie lub emailem.

