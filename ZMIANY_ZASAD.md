# Aktualizacja zasad — 9 października 2026

Zmiany są w skryptach. Panel decyzji o gaszeniu powstaje podczas gry z kopii istniejącego `Entangling_Panel`, dzięki czemu zachowuje jego tło, ramkę, przyciski, czcionki i możliwość przeciągania. Pole Zmęczenia w `Set_states_Panel` dodajesz ręcznie zgodnie ze swoim planem.

## Zachowanie po zmianach

1. **Odporny:** połowa obrażeń po uwzględnieniu pancerza, zaokrąglona w górę. Przykłady: 1 → 1, 3 → 2, 5 → 3. Dotyczy także obrażeń zaklęć obsługiwanych przez `ApplyDamageToTarget` i obrażeń Podpalenia. Niewrażliwość nadal całkowicie ignoruje odpowiedni typ.
2. **Zatrucie:** odporność zmniejsza nadawany poziom, również z zaokrągleniem w górę: poziom 1 → 1, 3 → 2, 5 → 3. Działa przy trafieniu zatrutą bronią, zmianie poziomu przez zaklęcie oraz ręcznym wpisaniu poziomu w panelu stanów. Przechowywany jest już zmniejszony poziom: kolejne tury, odświeżanie panelu i wczytanie zapisu nie dzielą go ponownie. Odporność nie usuwa Zatrucia. Niewrażliwość na truciznę oraz dotychczasowa cecha Nieumarły nadal chronią całkowicie.
3. **Bezrozumny:** nie wykonuje testów Charyzmy, Inteligencji, Siły Woli ani umiejętności `Cool`, `Spellcasting`, `Pray`, `Channeling`, `MagicLanguage`. Zakaz działa również, gdy test podaje tylko nazwę umiejętności. Zablokowany test nie otwiera panelu rzutu i nie wywołuje Szczęścia/Pechu. Zaklęcie wymagające takiego rzutu obronnego jest pomijane w całości, przed dodaniem efektu czasowego, zmianą cech lub obrażeniami, niezależnie od trudności. Bezrozumny ignoruje również strach i efekt cechy Żarłoczny wymagający SW; rzucanie zaklęć jest blokowane przed wydaniem akcji.
4. **Atak okazyjny:** jedna jednostka może wykonać jeden taki atak w rundzie, także gdy później oddala się od niej inny przeciwnik. Osobna rezerwacja blokuje uruchomienie dwóch ataków jednocześnie. Wykorzystanie jest oznaczane po sprawdzeniu poprawności ataku i nie odbiera zwykłej akcji. Nowa runda odnawia limit. Zapis gry pamięta jego wykorzystanie.
5. **Zmęczenie:** `Unit.Fatiqued` ma poziomy 1–5; 0 oznacza brak stanu. Poziom odejmowany jest jako modyfikator od testów, m.in. ataków, parowania, uników, Atletyki, Odporności, SW, rzucania zaklęć i Refleksu na inicjatywę. Obejmuje ręczne i automatyczne rzuty, również przerzut Specjalisty. Rzuty obrażeń oraz losowanie wyniku z tabel Trafień Krytycznych i Gniewu Bożego nie otrzymują kary za Zmęczenie. Wpisy w panelu i nadanie przez zaklęcia są ograniczane do 0–5. Stan jest zapisywany i wczytywany.
6. **Podpalenie:** k6 obrażeń występuje na początku tury jednostki, przed decyzją o gaszeniu. Pancerz nie pomaga. Odporność na ogień zmniejsza obrażenia o połowę w górę i pozostawia Podpalenie; niewrażliwość ignoruje efekt. Gracz wybiera **„Gaś (akcja)”** albo **„Nie gaś”**. Próba zużywa akcję przed testem Atletyki na Zwinności (14+), również przy porażce lub anulowaniu ręcznego rzutu. Sukces usuwa Podpalenie; odmowa pozostawia akcję. Udane gaszenie nie cofa obrażeń otrzymanych na początku tej tury. Postać nieprzytomna lub pozbawiona akcji nie podejmuje próby. Jednostki sterowane automatycznie próbują się gasić za akcję; gracz sterowany ręcznie dostaje wybór także przy automatycznych kościach. Deszcz, wskoczenie do wody i pomoc sojusznika można rozliczyć przez ręczne zdjęcie stanu Podpalenia w istniejącym panelu stanów.
7. **Pech w przegranym teście walki:** atak uszkadza używaną broń; parowanie uszkadza faktycznie wybraną broń do parowania, także tarczę, bez sprawdzania pancerza. Unik uszkadza pancerz wyłącznie na trafionej lokalizacji. Jeśli nie ma na niej noszonego pancerza, wywoływane jest Trafienie Krytyczne, nawet gdy inne lokalizacje są opancerzone. Naturalny pancerz nie jest traktowany jako noszony przedmiot. Przy kilku elementach na trafionej lokalizacji wybierany jest sprawny element, z dotychczasowym pierwszeństwem płytowego, kolczego i skórzanego. Pancerz z innej lokalizacji nigdy nie jest wybierany.

Konsekwencje jakości pozostają zgodne z wcześniejszą implementacją: zwykły przedmiot jest uszkadzany; słaby jest oznaczany jako zniszczony z komunikatem, aby usunąć go z ekwipunku. Oba otrzymują `Broken`. Wyższe jakości nie są uszkadzane przez tę regułę. Broń naturalna i atak bez broni są wyłączone z uszkadzania przedmiotów.

## Gdzie dokładnie

| Plik | Zmienione miejsca |
| --- | --- |
| `Assets/Scripts/Unit.cs` | Pola `Fatiqued`, `HasUsedOpportunityAttack`, `TurnStartedInRound`; ochrona wyboru jednostek i rozpoczynania ataków podczas rozliczania początku tury. |
| `Assets/Scripts/Managers/DataManager.cs` | Odpowiedniki nowych pól w `UnitData`; dodane zapisywanie `CanDoAction`, aby zużyta akcja gaszenia była zachowana po wczytaniu. |
| `Assets/Scripts/Managers/DiceRollManager.cs` | `CanPerformTest`, `TestSkill`, `WaitForRollValue`, `OnSubmitRoll`, `IsDoubleDigit`: zakaz testów Bezrozumnych, kara Zmęczenia, spójny wynik zablokowanego testu, wyłączenie zerowych kości z dubletów oraz opcja `applyFatigue` dla rzutów do tabel. |
| `Assets/Scripts/Managers/CombatManager.cs` | `ApplyDamageToTarget`: zaokrąglenie odporności w górę i zmniejszanie poziomu trucizny. `Attack`, `AttackCoroutine`, `CheckForOpportunityAttack`: limit i rezerwacja ataku okazyjnego. Sekcja Pecha, `DamageEquipmentOnFumble`, `GetArmorForDodgeFumble`: rozdzielenie broni i pancerza. `Defense`, `Parry`, `Dodge`: reset i aktualizacja rodzaju obrony także dla AI. `CreateExtinguishPanel`: kopia istniejącego UI z własnymi przyciskami. `CriticalWoundRoll`: brak kary Zmęczenia do tabeli. Akcje i próba uwolnienia z Unieruchomienia czekają na rozliczenie Podpalenia. |
| `Assets/Scripts/Managers/StatesManager.cs` | `ResolveTurnStart`, `Ablaze`, `ChooseExtinguish`, `ResetTurnStartState`: obrażenia na początku tury, panel decyzji i gaszenie za akcję. `UpdateUnitStates` nie wywołuje już Podpalenia na końcu tury. `GetPoisonLevel`, `Poison`: rozdzielenie odporności od niewrażliwości. `SetUnitState`, `LoadUnitStates`: obsługa Zmęczenia i poziomu trucizny; odświeżanie pól bez ponownego uruchamiania zmiany stanu. `FearTest`: ochrona Bezrozumnych. |
| `Assets/Scripts/Managers/RoundsManager.cs` | `BeginTurn`, `ResolveUnitTurnStart`: jednokrotne rozliczenie początku tury. `NextRound`: odnowienie limitu ataku okazyjnego. `AutoCombat`: oczekiwanie na obrażenia i wybór gaszenia. `DoAction`: możliwość zużycia akcji gaszenia bez przedwczesnego zakończenia tury. Zakończenie tury, przejście rundy i reset gry uwzględniają trwającą decyzję. |
| `Assets/Scripts/Managers/InitiativeQueueManager.cs` | `DisplayInitiativeQueue`: rozpoczęcie tury aktywnej jednostki. `InvokeSelectUnitCoroutine`: oczekiwanie na zakończenie rozliczania Podpalenia. |
| `Assets/Scripts/Managers/GameManager.cs` | `RefreshPauseState` i `Update`: wstrzymanie akcji podczas rozliczania Podpalenia i wyboru gaszenia. |
| `Assets/Scripts/Managers/MagicManager.cs` | `CastingSpellMode`, `CastSpell`, `HandleSpellEffect`: ograniczenia Bezrozumnych. Nadawanie Zatrucia uwzględnia odporność, nadawanie Zmęczenia ogranicza poziom. Efekty czasowe zapisują rzeczywiście nadany poziom tych stanów. `GodsWrath`: losowanie tabeli bez kary Zmęczenia. |
| `Assets/Scripts/Managers/UnitsManager.cs` | `HungryTrait`: Bezrozumny ignoruje efekt wymagający testu SW. |
| `Assets/Scripts/Stats.cs` | `HasResistance`, `IsUnaffectedBy`, `HasDamageType`: wspólne rozpoznawanie typów, w tym polskich nazw. `UpdateSpellEffects`: usuwanie czasowego Zmęczenia/Zatrucia z zachowaniem poprawnego zakresu poziomów. |
| `Assets/Scripts/Managers/SaveAndLoadManager.cs` | Wznowienie początku tury po wczytaniu oraz obsługa starszych zapisów bez `CanDoAction`; pola nowych stanów wczytuje istniejący mechanizm refleksji. |

## Dodanie Zmęczenia do panelu

- Utwórz `TMP_InputField` o nazwie **`Fatiqued_input`** i tagu **`State`**. Obsługiwany jest także poprawnie zapisany wariant **`Fatigued_input`**.
- Podepnij istniejące `StatesManager.SetUnitState(GameObject)` i jako argument wskaż **nowe pole**, szczególnie jeśli kopiujesz istniejący InputField.
- Zakres: **0–5**. Możesz wykorzystać istniejący `InputFieldFilter` z `_isSkillInput`, który dopuszcza cyfry 0–5.
- Panel gaszenia tworzy się sam podczas gry; nie wymaga dodania obiektu ani referencji w Inspectorze.

## Sprawdzenie

Cały `Assembly-CSharp.csproj` został skompilowany przez MSBuild. Przeprowadzono 102 sprawdzenia logiki na kodzie produkcyjnym, uruchomionym w pomocniczym programie z atrapami zależności Unity. Sprawdzono też referencje, nazwy przycisków źródłowego panelu w `BattleScene.unity`, zgodność nowych pól z zapisem i reset limitu w rundzie. Program pomocniczy znajduje się lokalnie w ignorowanym `Temp/RulesRegression`; można go odtworzyć poleceniami `python Temp/RulesRegression/generate.py` i `dotnet run --project Temp/RulesRegression/RulesRegression.csproj`.

Przegląd wersji sprzed napraw znajduje się w [AUDYT_BLEDOW.md](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/AUDYT_BLEDOW.md). Wszystkie 12 wskazanych problemów, w tym lukę Podpalenia przy turach poza kolejnością, naprawiono i opisano w [NAPRAWY_BLEDOW.md](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/NAPRAWY_BLEDOW.md). Aktualne 198 sprawdzeń można odtworzyć poleceniami `python Tests/RulesRegression/generate.py` i `dotnet run --project Tests/RulesRegression/RulesRegression.csproj --verbosity quiet`.

Testy pomocnicze nie uruchamiają sceny Unity. Do obejrzenia w **Play Mode**:

1. Podpal jednostkę gracza przed jej turą: najpierw ubywa zdrowia, potem pokazuje się decyzja. „Nie gaś” pozostawia akcję; „Gaś” ją zużywa. Udany wynik 14+ usuwa ogień, ale pozostawia już zadane obrażenia.
2. Sprawdź ten panel z długą nazwą postaci, na obu ekranach i przy ręcznych oraz automatycznych kościach. Escape powinien działać jak odmowa gaszenia.
3. Daj jednostce odporność na ogień: ogień nadal trwa, a wyrzucone 5 zadaje 3 obrażenia. Zatruta broń poziomu 3 nadaje odpornemu celowi poziom 2, także w następnych turach.
4. Wymuś przegrany dublet przy parowaniu uzbrojonego i opancerzonego obrońcy: uszkodzona zostaje broń do parowania. Przy Uniku ucierpi pancerz na trafionej lokalizacji. Daj obrońcy wyłącznie pancerz tułowia i traf w głowę: pojawi się Trafienie Krytyczne, a pancerz tułowia pozostanie sprawny. Powtórz z automatycznym wyborem obrony.
5. Oddal dwóch przeciwników od tej samej jednostki w jednej rundzie: zaatakuje tylko pierwszego. W następnej rundzie może zaatakować ponownie. Zapisz i wczytaj grę po wykorzystaniu tego ataku: drugi atak w tej rundzie pozostaje niedostępny.
6. Ustaw Zmęczenie 3 i porównaj te same kości w ataku, obronie, rzucaniu zaklęcia i inicjatywie. Wczytany zapis powinien zachować poziom 3.
7. Użyj przeciw Bezrozumnemu zaklęcia z rzutem obronnym na SW/Int/Ch i dużą trudnością: nie powinno otworzyć panelu obrony ani nałożyć efektu lub obrażeń.
