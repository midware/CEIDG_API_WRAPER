# Bezpieczenstwo leadbase.network

Wersja robocza: 2026-06-30  
Status: draft do przegladu prawnego/technicznego przed publikacja.

## 1. Cel dokumentu

Ten dokument opisuje podstawowe zabezpieczenia techniczne i organizacyjne leadbase.network.

## 2. Zabezpieczenia konta i API

- hasla przechowywane w postaci hasha;
- klucze API pokazywane tylko raz przy utworzeniu;
- w bazie przechowywany jest hash klucza API i prefiks;
- mozliwosc rotacji kluczy API;
- logowanie uzyc API;
- rate limiting;
- blokada kont przy naduzyciach.

## 3. Infrastruktura

Do uzupelnienia:

- dostawca hostingu;
- region przechowywania danych;
- model backupow;
- monitoring;
- dostep administracyjny;
- szyfrowanie dyskow/baz;
- polityka aktualizacji.

## 4. Dane i dostep

- dostep do danych powinien miec tylko uprawniony personel;
- dostepy administracyjne powinny byc nadawane zgodnie z zasada najmniejszych uprawnien;
- dostepy powinny byc odbierane po zakonczeniu wspolpracy;
- dane produkcyjne nie powinny byc kopiowane do srodowisk testowych bez potrzeby i zabezpieczen.

## 5. Incydenty

W przypadku podejrzenia naruszenia:

1. zabezpieczamy logi;
2. ograniczamy dostep;
3. analizujemy zakres danych;
4. dokumentujemy incydent;
5. oceniamy obowiazek zgloszenia do UODO i osob, ktorych dane dotycza;
6. wdrazamy dzialania naprawcze.

Kontakt bezpieczenstwa: [UZUPELNIC: security@leadbase.network]

