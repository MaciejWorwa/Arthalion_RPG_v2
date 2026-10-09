# Przegląd błędów — 9 października 2026

**Status po naprawach:** wszystkie 12 opisanych problemów ma poprawki w kodzie. Aktualne zachowanie, miejsca zmian i wyniki testów opisuje [NAPRAWY_BLEDOW.md](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/NAPRAWY_BLEDOW.md). Poniżej zachowany jest opis wersji sprzed napraw.

Wprowadzone poprawki zasad opisuje [ZMIANY_ZASAD.md](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/ZMIANY_ZASAD.md). Doprecyzowanie WPecha przy Uniku jest wdrożone: brak noszonego pancerza na trafionej lokalizacji powoduje Trafienie Krytyczne. Pancerz innej lokalizacji nie jest wybierany. Zmiana znajduje się w [CombatManager.cs — GetArmorForDodgeFumble](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/CombatManager.cs:1056) i komunikacie w gałęzi Pecha przy Uniku.

Na etapie samego audytu poniższych problemów nie naprawiałem. Następnie, na Twoją prośbę, przygotowałem poprawki opisane powyżej. P1 oznacza problem, który może zgubić rozliczenie albo uszkodzić stan postaci; P2 oznacza błąd reguł lub zachowania gry. Przegląd obejmuje kod walki, rzutów, stanów, tur, zaklęć, ruchu oraz powiązane fragmenty AI i ekwipunku. Nie jest pełnym testem wszystkich funkcji gry.

## Znalezione problemy

1. **[P1] Odparte zaklęcie zostawia efekt czasowy i później zmienia postać.**

   `HandleSpellEffect` dodaje efekt do `ActiveSpellEffects` przed wykonaniem rzutu obronnego. Udana obrona przerywa nakładanie zaklęcia, ale nie usuwa tego wpisu. `UpdateSpellEffects` później cofa modyfikatory, których nigdy nie zastosowano. Odtworzony przykład: odparte zaklęcie z Siłą −2 pozostawia Siłę 4, a po wygaśnięciu zwiększa ją do 6. Dla zaklęcia z aktualnej biblioteki: odparta **Petryfikacja** zapisuje efekt, który po trzech rundach usuwa wcześniejszą Nieprzytomność celu. Ponowne rzucenie już istniejącego efektu odnawia jego czas przed rzutem obronnym i w ogóle pomija obronę.

   Miejsca: [MagicManager.cs:290](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:290), [MagicManager.cs:300](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:300), [Stats.cs:447](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Stats.cs:447). **Odtworzone testem kodu produkcyjnego.**

2. **[P1] Dwa równoczesne ręczne rzuty mogą zgubić wynik i wywołać niewłaściwy callback.**

   `WaitForRollValue` blokuje kolejny rzut tylko wtedy, gdy panel jest widoczny. `OnSubmitRoll` chowa panel, zanim korutyna pierwszego rzutu odbierze wynik i zwolni wspólne pola `_pending…`. Drugi oczekujący rzut może w tym momencie nadpisać wynik oraz callback pierwszego. Odtworzona kolejność: zatwierdzenie A → wznowienie B → zatwierdzenie B → wznowienie A. Callback A nie zostaje wywołany; korutyna A wywołuje callback B. Sytuację mogą wywołać np. równoczesne testy Strachu kilku jednostek albo Krwawienie i Zatrucie rozliczane na końcu tury. Skutkiem może być pominięcie testu lub dalsze rozliczanie akcji bez wyniku.

   Miejsca: [DiceRollManager.cs:83](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:83), [DiceRollManager.cs:160](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:160), [DiceRollManager.cs:236](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:236). **Odtworzone przez przeplatanie dwóch produkcyjnych korutyn.**

3. **[P2] Nową obsługę Podpalenia można ominąć, grając poza kolejnością inicjatywy.**

   Początek tury rozliczany jest dla `ActiveUnit` z kolejki. Ręczny wybór innej jednostki odświeża kolejkę, ale nie rozpoczyna tury wybranej jednostki. Atak i ruch nie wymagają uprzedniego `BeginTurn`. Jeżeli płonąca jednostka o niższej inicjatywie wykona i zakończy turę przed jednostką aktywną, nie otrzyma w tej rundzie obrażeń od Podpalenia ani pytania o gaszenie. `BeginTurn` pomija jednostki z `IsTurnFinished`. Ten problem dotyczy właśnie wprowadzonej aktualizacji zasad.

   Sprawdzenie w scenie: pozostaw A jako aktywną, podpal B z niższą inicjatywą, ręcznie wybierz B, wykonaj akcję i zakończ jej turę przed A. Miejsca: [InitiativeQueueManager.cs:165](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/InitiativeQueueManager.cs:165), [Unit.cs:131](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Unit.cs:131), [RoundsManager.cs:46](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/RoundsManager.cs:46). **Potwierdzone analizą wywołań; scenariusza nie uruchamiałem w Play Mode.**

4. **[P2] Jednostka może ponownie działać po ręcznym zakończeniu tury.**

   `FinishTurn` ustawia `IsTurnFinished`, pozostawiając niewykorzystane `CanDoAction` i `CanMove`. Atak i ruch sprawdzają dostępność akcji/ruchu, ale nie sprawdzają zakończenia tury. Dopóki pozostaje ta sama runda, można ponownie wybrać taką jednostkę i wykorzystać pozostawioną akcję lub ruch. Jej stany końca tury były już wcześniej rozliczone, a kolejne `FinishTurn` nie rozliczy ich ponownie.

   Sprawdzenie: zakończ turę A z niewykorzystaną akcją, gdy B nadal ma swoją turę; wybierz A i zaatakuj. Miejsca: [RoundsManager.cs:549](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/RoundsManager.cs:549), [CombatManager.cs:317](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/CombatManager.cs:317), [MovementManager.cs:66](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MovementManager.cs:66). **Test potwierdza uruchomienie ataku przy `IsTurnFinished = true`.**

5. **[P2] Twardziel zmienia kości, ale nie zwiększa wyniku testu Kondycji; może też tworzyć fałszywe dublety.**

   `finalScore` jest obliczany przed podwojeniem niższej kości i nie jest przeliczany po użyciu talentu. Dla K = 3 oraz kości 2 i 7 zwracane są kości 4 i 7, lecz wynik nadal wynosi 12 zamiast 14. Dodatkowo wywołujący kod dostaje już zmienione kości: rzut 2 i 4 wraca jako 4 i 4. Rozliczanie Szczęścia/Pechu np. przy Krwawieniu sprawdzi ten sztuczny dublet, mimo że na kościach nie było dubletu.

   Miejsca: [DiceRollManager.cs:360](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:360), [DiceRollManager.cs:393](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:393), [DiceRollManager.cs:455](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:455). **Oba zachowania odtworzone testami.**

6. **[P2] Specjalista po przerzucie pomija limit 10 premii z cechy i umiejętności.**

   Zwykłe obliczenie stosuje `Math.Min(skillValue + attributeValue, 10)`, ale przeliczenie po przerzucie dodaje pełną sumę. Dla Zr = 8, Walki Wręcz = 5 oraz kości po przerzucie 8 i 7 wynik wynosi 28 zamiast 25. Talent daje więc dodatkową, nieopisaną premię +3. Im większe cecha i umiejętność, tym większa różnica.

   Miejsce: [DiceRollManager.cs:388](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/DiceRollManager.cs:388). **Odtworzone testem.**

7. **[P2] Krytyczne zwiększenie parametrów zaklęcia kumuluje się przy kolejnych celach.**

   Zasięg i obszar mnożone są przez 2 wewnątrz pętli po celach. Czas trwania mnożony jest przez 2 przy każdym `HandleSpellEffect`, również dla kolejnych jednostek w jednym obszarze. Przykład z biblioteki: **Zimowy Krąg**, bazowo 6 rund, przy krytycznym zwiększeniu czasu daje pierwszej jednostce 12 rund, drugiej 24, następnej 48. Wynik zależy więc od liczby i kolejności objętych jednostek, zamiast jednokrotnie podwoić parametr.

   Miejsca: [MagicManager.cs:208](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:208), [MagicManager.cs:218](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:218), [MagicManager.cs:271](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:271). **Czas trwania odtworzony testem; zasięg i obszar potwierdzone analizą pętli.**

8. **[P2] Leczenie przekraczające maksimum zdrowia nie leczy wcale.**

   Zmiana `TempHealth` wykonywana jest tylko wtedy, gdy cała wartość leczenia mieści się pod `MaxHealth`. Cel z 18/20 zdrowia, leczony **Kojącym Chłodem** za 4, zostaje z 18/20 zamiast otrzymać brakujące 2 punkty do 20/20.

   Miejsce: [MagicManager.cs:386](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MagicManager.cs:386). **Odtworzone testem ze strukturą zaklęcia z biblioteki.**

9. **[P2] Automatyczny wybór obrony dwukrotnie liczy modyfikatory.**

   `parryValue` i `dodgeValue` już zawierają odpowiednie modyfikatory. `AutoDefense` dodaje je ponownie przy porównywaniu obron. Przykład: Zr = 5, premia do parowania +2, Zw = 8, obie umiejętności = 0. Faktyczne premie do testów wynoszą 7 dla parowania i 8 dla Uniku. AI porównuje jednak 9 z 8 i wybiera słabsze parowanie.

   Miejsca: [CombatManager.cs:637](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/CombatManager.cs:637), [CombatManager.cs:1162](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/CombatManager.cs:1162). **Odtworzone testem produkcyjnej funkcji wyboru.**

10. **[P2] AI wylicza zerowy zasięg broni miotanej przy S od 1 do 9.**

    W `AutoCombatManager.Act` występuje `S / 10`, gdzie obie wartości są całkowite. Wynik dla S = 3 wynosi 0, więc cały wyliczony zasięg również wynosi 0. AI uznaje np. Oszczep za broń, którą nie da się zaatakować nawet bliskiego celu, i wybiera podejście zamiast rzutu. Dla porównania analogiczny fragment ruchu używa `S / 10f`, zachowując ułamek.

    Miejsce: [AutoCombatManager.cs:56](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/AutoCombatManager.cs:56). **Potwierdzone analizą typów i przepływu; nie uruchamiałem walki AI w scenie.**

11. **[P2] Nieudany ruch oznacza pole stojącej jednostki jako wolne.**

    `MoveSelectedUnit` wywołuje `ResetTileOccupancy` przed sprawdzeniem ścieżki i zasięgu. Jeżeli ruch zostanie odrzucony, nie przywraca zajętości pola początkowego, mimo że jednostka nadal na nim stoi. `AvailablePositions` uzna takie pole za wolne; tworzenie jednostki na losowym wolnym polu może umieścić kolejną jednostkę w tym samym miejscu. Błąd utrzymuje się do ponownego przeliczenia zajętości siatki.

    Sprawdzenie: wybierz jednostkę, spróbuj ruchu dalej niż jej zasięg i obejrzyj `Tile.IsOccupied` pod nią. Miejsca: [MovementManager.cs:127](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/MovementManager.cs:127), [GridManager.cs:504](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/GridManager.cs:504), [UnitsManager.cs:141](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Managers/UnitsManager.cs:141). **Potwierdzone analizą ścieżki odrzucenia ruchu; nie uruchamiałem scenariusza w Play Mode.**

12. **[P2] Wygaśnięcie czasowego efektu logicznego zawsze ustawia `false`, zamiast przywrócić poprzedni stan.**

    Efekty nie zapisują wcześniejszej wartości pól logicznych. Nawet poprawnie nałożone zaklęcie po zakończeniu ustawia takie pole na `false`. Przykładowo Petryfikacja może usunąć Nieprzytomność, która występowała już przed zaklęciem z innego powodu. Test na ogólnym mechanizmie atrybutów: postać z trwałym `Hardy = true` dostaje czasowe `Hardy = true`; po wygaśnięciu traci talent. Ten problem występuje również bez błędu rzutu obronnego z punktu 1.

    Miejsce: [Stats.cs:457](C:/Users/macie/Documents/GitHub/Arthalion_RPG_v2/Assets/Scripts/Stats.cs:457). **Mechanizm odtworzony testem na polu logicznym.**

## Weryfikacja i zakres zmian

- Kompilacja całego `Assembly-CSharp.csproj` przez MSBuild zakończyła się powodzeniem. Pozostały trzy ostrzeżenia CS0649 o polach klas opakowujących JSON.
- 102 sprawdzenia aktualizacji zasad przechodzą w `Temp/RulesRegression`; obejmują też przypadek pancerza tułowia i trafienia w nieopancerzoną głowę. Testy sprawdzają logikę produkcyjną z atrapami zależności Unity, bez uruchamiania sceny.
- Dla audytu odtworzono 13 błędnych zachowań dotyczących 9 z opisanych problemów. Pomocniczy program znajduje się lokalnie w ignorowanym `Temp/RulesAudit`. Trzy pozostałe problemy wskazano na podstawie analizy przepływu kodu. Testy nie dowodzą poprawności całej gry ani wyglądu UI.
- Po doprecyzowaniu Pecha zmieniłem w kodzie gry wyłącznie `GetArmorForDodgeFumble` i komunikat w gałęzi Uniku w `CombatManager.cs`. Pozostałe zmiany skryptów w working tree należą do wcześniej przygotowanej aktualizacji zasad. Nie wprowadziłem poprawek żadnego z powyższych 12 problemów.

Odtworzenie lokalnych sprawdzeń: `python Temp/RulesRegression/generate.py`, `dotnet run --project Temp/RulesRegression/RulesRegression.csproj`, `python Temp/RulesAudit/extract_extra.py`, `dotnet run --project Temp/RulesAudit/RulesAudit.csproj`. Program audytu korzysta z kopii produkcyjnych metod przygotowanej w trakcie tego przeglądu; po dalszych zmianach kodu kopię należy odświeżyć.
