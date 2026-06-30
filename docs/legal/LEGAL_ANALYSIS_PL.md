# leadbase.network - analiza prawna i compliance dla Polski

Wersja robocza: 2026-06-30  
Status: draft do przegladu przez radce prawnego/adwokata przed publikacja.

## 1. Punkt odniesienia: co robi leadbase.io

Leadbase.io ma rozbudowane centrum prawne obejmujace m.in. regulamin, polityke prywatnosci, cookie policy, bezpieczenstwo, DPA, EULA, portal prywatnosci, opt-out i wnioski o prawa danych. Publicznie komunikuje model B2B lead intelligence, enrichment, CRM integrations, API, credit billing, usage dashboard, compliance, opt-out i obsluge praw z RODO.

Elementy, ktore warto przeniesc koncepcyjnie, ale nie tekstowo:

- centralne "Centrum prawne";
- osobne dokumenty: Regulamin, Polityka prywatnosci, Cookie policy, Bezpieczenstwo, DPA, Opt-out, Prawa osob;
- jasny opis zrodel danych, kategorii danych i podstaw prawnych;
- rozdzielenie danych klienta, danych technicznych, danych rozliczeniowych i danych firmowych;
- procedura opt-out/suppression list, zeby nie przywracac usunietych profili;
- zakazane zastosowania: spam, nękanie, dyskryminacja, odsprzedaz danych bez podstawy, scraping, obchodzenie limitow.

## 2. Najwazniejsza roznica dla leadbase.network

leadbase.network nie jest kopia Leadbase.io. Nasz produkt opiera sie przede wszystkim na mirrorze danych CEIDG oraz API pozwalajacym pobierac rekordy firm. Dane kontaktowe typu telefon, email, WWW i PKD moga pochodzic z CEIDG lub innych jawnie zdefiniowanych zrodel publicznych/oficjalnych, jezeli zostana dodane.

To wymusza inny model prawny:

- wobec uzytkownikow platformy jestesmy administratorem danych konta, rozliczen, logow, kluczy API i historii zapytan;
- wobec danych firmowych z CEIDG jestesmy samodzielnym administratorem przetwarzania na potrzeby budowy, utrzymania i udostepniania bazy/API;
- klient pobierajacy dane przez API staje sie odrebnym administratorem dalszego wykorzystania danych;
- DPA/powierzenie ma sens tylko dla funkcji, w ktorych klient przekazuje nam swoje dane do przetworzenia na jego polecenie, np. upload listy, enrichment listy klienta, CRM, kampanie email/SMS;
- nie nalezy twierdzic, ze klient moze dowolnie wysylac marketing bez zgody. Trzeba jasno przerzucic na klienta obowiazek posiadania podstawy prawnej i zgód/wyjatkow wymaganych dla komunikacji elektronicznej.

## 3. Polska: kluczowe obszary prawne

### RODO

RODO dotyczy osob fizycznych, w tym jednoosobowych dzialalnosci gospodarczych, jezeli dane identyfikuja osobe. CEIDG zawiera dane przedsiebiorcow bedacych osobami fizycznymi, dlatego traktujemy baze jako dane osobowe w zakresie, w jakim identyfikuje przedsiebiorce.

Minimalny model:

- art. 6 ust. 1 lit. f RODO - uzasadniony interes: prowadzenie bazy B2B, zapewnienie dostepu do danych publicznego rejestru, weryfikacja kontrahentow, prospecting B2B;
- art. 6 ust. 1 lit. b RODO - umowa z uzytkownikiem platformy;
- art. 6 ust. 1 lit. c RODO - ksiegowosc, podatki, roszczenia, obowiazki prawne;
- art. 6 ust. 1 lit. a RODO - newsletter, nieobowiazkowe cookies/analityka, zgody marketingowe tam, gdzie wymagane.

Trzeba wdrozyc:

- obowiazek informacyjny dla osob, ktorych dane sa w bazie, najlepiej przez publiczna strone "Informacja dla osob z bazy CEIDG";
- opt-out / sprzeciw / usuniecie lub ograniczenie profilu;
- suppression list, zeby osoba po opt-out nie byla zaciagana ponownie;
- dokumentacje testu rownowagi interesow dla art. 6 ust. 1 lit. f RODO;
- rejestr czynnosci przetwarzania;
- procedury naruszen danych;
- retencje logow i danych.

### Marketing B2B, email, telefon, SMS

Samo posiadanie danych firmowych nie oznacza automatycznego prawa do wysylki kampanii email/SMS ani dzwonienia w kazdej sytuacji. Dla komunikacji handlowej i marketingu bezposredniego klient musi sam ocenic wymogi RODO oraz przepisow dotyczacych komunikacji elektronicznej, w tym zgody lub innej dopuszczalnej podstawy.

W regulaminie i API trzeba zapisac:

- klient odpowiada za legalnosc kontaktu z osobami/firmami;
- klient nie moze uzywac danych do spamu, masowego nękania ani automatycznych kampanii bez podstawy prawnej;
- leadbase.network nie gwarantuje, ze dany rekord mozna legalnie wykorzystac do kazdego kanalu marketingowego;
- przyszle moduly CRM/email/SMS musza miec osobne zgody, suppression list i mechanizmy wypisu.

### Dane CEIDG i jakosc danych

Trzeba podkreslic:

- dane moga byc niepelne, opoznione, bledne lub zmienione w zrodlach;
- API nie jest rejestrem urzedowym i nie zastepuje weryfikacji w CEIDG/KRS/GUS;
- klient powinien sprawdzic dane przed wazna decyzja gospodarcza;
- limity i dostep do danych zrodlowych moga sie zmieniac.

### Cookies i analityka

Na start, jezeli aplikacja uzywa tylko cookies technicznych do logowania/sesji, wystarczy prosta polityka cookies i brak agresywnego banera marketingowego. Jesli dodamy GA4, Meta Pixel, LinkedIn Insight Tag, Hotjar itp., potrzebny bedzie CMP/banner ze zgoda przed odpaleniem niekoniecznych skryptow.

### Platnosci i tokeny

Regulamin musi objac:

- pakiety tokenow;
- brak gwarancji zwrotu za zuzyte tokeny;
- kiedy tokeny wygasaja albo nie wygasaja;
- mozliwosc blokady konta przy naruszeniu prawa;
- faktury i VAT;
- B2B-only, aby ograniczyc obowiazki konsumenckie.

## 4. Dokumenty do publikacji

Rekomendowany zestaw:

1. `REGULAMIN_PL.md` - regulamin uslugi/API.
2. `PRIVACY_POLICY_PL.md` - polityka prywatnosci.
3. `COOKIES_POLICY_PL.md` - polityka cookies.
4. `DPA_PL.md` - umowa powierzenia dla funkcji procesora.
5. `SECURITY_PL.md` - bezpieczenstwo i TOM.
6. `DATA_RIGHTS_AND_OPT_OUT_PL.md` - prawa osob i opt-out.

## 5. Otwarte dane do uzupelnienia przed publikacja

- pelna nazwa operatora;
- forma prawna, NIP, REGON/KRS/CEIDG;
- adres siedziby;
- email prawny, email privacy, email support;
- czy jest IOD/DPO;
- dostawca hostingu;
- dostawca email;
- dostawca platnosci;
- narzedzia analityczne;
- retencja danych produkcyjnych, logow, backupow;
- czy tokeny wygasaja;
- czy usluga jest tylko B2B;
- docelowy sad i prawo wlasciwe.

## 6. Rekomendacje wdrozeniowe w produkcie

- Dodac stopke: Regulamin, Prywatnosc, Cookies, Bezpieczenstwo, Opt-out.
- Dodac checkbox akceptacji regulaminu i polityki prywatnosci przy rejestracji.
- Dodac osobny checkbox na marketing do uzytkownika platformy.
- Dodac endpoint/formularz opt-out dla osob z bazy.
- Dodac suppression list w bazie.
- Dodac log zgód i wersji dokumentow zaakceptowanych przez uzytkownika.
- Dodac w panelu informacyjny tekst: "Dane nalezy wykorzystywac zgodnie z RODO i przepisami o komunikacji elektronicznej".
- Dla API dodac naglowek/sekcje docs: legal basis is customer's responsibility.

