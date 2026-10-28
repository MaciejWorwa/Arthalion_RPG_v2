# Audyt uczenia AI w AutoCombat

Data analizy: 28 października 2026 r.

Raport historyczny: opisuje system sprzed przebudowy. Instrukcja wdrożonego trenera PPO i nowych benchmarków znajduje się w [Tools/AI/README.md](Tools/AI/README.md).

## Wniosek

Uczenie faktycznie aktualizuje i zapisuje Q-values. Nie ma jednak podstaw, aby uznać obecny wynik za optymalną politykę walki. Największe problemy to niewykonalne akcje dopuszczane przez maskę, niespójność wyboru akcji i celu aktualizacji Q, niedostateczna obserwacja oraz rozliczanie operacji asynchronicznych. Ponadto zwykły AutoCombat nie używa wyuczonej polityki.

Samo zwiększenie liczby walk lub zmiana Alpha/Epsilon nie naprawi tych problemów. Najpierw trzeba zapewnić poprawne doświadczenia, następnie poprawić reprezentację problemu i szybkość symulacji.

Zakres: analiza aktualnych skryptów, sceny BattleScene, lokalnego zapisu treningowego i trzech ostatnich znalezionych plików steps.csv. Nie uruchamiano Unity ani nowego treningu. Historyczne logi z marca 2026 r. nie dowodzą identycznego zachowania każdej ścieżki aktualnego kodu; opisany problem zmiany broni występuje jednak również w obecnej implementacji. Nie zmieniono skryptów, scen ani zapisanych modeli.

## 1. Co potwierdzają rzeczywiste dane

Odczytano Application.persistentDataPath odpowiadające katalogowi:

`C:\Users\Maciej Worwa\AppData\LocalLow\Maciej Worwa\Arthalion_RPG`

Ostatni znaleziony przebieg: `training_runs/20260325_133554/steps.csv`.

| Pomiar | Wynik |
| --- | ---: |
| Wiersze rozliczenia transition + terminal | 95 541 |
| Wiersze TRAIN | 64 307 |
| Wiersze EVAL | 31 234 |
| Zakończone epizody z wierszami terminal | 801 |
| Zakończone epizody treningowe / ewaluacyjne | 581 / 220 |
| Zalogowane zmiany old_q → new_q w EVAL | 0 |
| Wiersze timeout / fallback | 0 / 0 |
| Przejścia transition z nagrodą dokładnie -0,2 | 63 951 / 86 837, czyli 73,6% |
| Przejścia EVAL action=41 | 4 090 |
| Z tych przejść: nadal brak broni dystansowej w next_state | 4 090 / 4 090 |

Wszystkie 4 090 prób action=41 wykonała rasa Troll leśny, jednostki 4, 5 i 6. W odpowiadającym lokalnym zapisie AIlearning każda ma tylko pałkę typu melee. Maska pozwala więc wybrać zmianę na broń, której jednostka nie posiada. ChangeWeapon kończy się wtedy bez efektu. Przykład z EVAL, epizod 50: jednostka 4, stan 259, akcja 41, następny stan 307, nagroda -0,2; taki wpis powtarza się.

Stan 259 i stan 307 mają wyłączony bit HasRangedWeapon. Różnica tych stanów dotyczy informacji o celu, a nie pomyślnej zmiany broni. Jest to szczególnie istotne: stan przed akcją bez celu opisuje null, a przy rozliczeniu następny stan opisuje najbliższego przeciwnika.

W znalezionym q_tables.json są trzy rasy: Morgras (300 stanów), Goblin (302) i Troll leśny (80). Wszystkie wiersze mają 43 wartości, stany mieszczą się w 11 bitach, wartości są skończone. Format znalezionego pliku jest spójny z aktualną liczbą stanów i akcji. To potwierdza zapis danych, nie poprawność strategii.

Także dwa wcześniejsze przeanalizowane przebiegi nie mają zalogowanych zmian Q w EVAL. Wyłączenie aktualizacji podczas ewaluacji jest więc dobrym, działającym elementem.

Ważne ograniczenia interpretacji:

- Nagroda -0,2 nie dowodzi bezczynności: prawidłowy ruch przygotowujący atak również może tyle otrzymać.
- Brak fallback nie dowodzi poprawności akcji: FinishTurn jest zawsze dopuszczone, a licznik nie mierzy odrzuceń w wykonawcy.
- steps.csv nie zapisuje jawnego wyniku epizodu ani czasu każdego kroku. Nie można z niego rzetelnie odtworzyć krzywej win rate ani kroków na sekundę. Nagroda terminalna zawiera też składniki inne niż wynik walki.
- Liczba identyfikatorów epizodów TRAIN wynosi 582, ale ostatni epizod jest niepełny; liczba zakończonych wynosi 581.

## 2. Błędy i ograniczenia obecnej implementacji

### A. Wyuczona polityka nie steruje zwykłym AutoCombat — priorytet krytyczny

RoundsManager.cs:302–345: przy IsLearning=true jednostki EnemyUnit wywołują SimulateUnit, a PlayerUnit używają AutoCombatManager.Act. Przy IsLearning=false wszystkie jednostki automatyczne używają AutoCombatManager.Act. Nie ma ścieżki wykonania wyuczonej polityki w normalnej rozgrywce. Ładowanie Q jest również uruchamiane przez rozpoczęcie treningu.

Wprowadzić osobne tryby polityki: Heuristic, Train, Evaluate, Inference. Inference ładuje model, wybiera legalną akcję bez eksploracji i nigdy nie aktualizuje parametrów. Train/Evaluate/Inference muszą korzystać z tego samego generatora akcji i wykonawcy. IsLearning nie powinno jednocześnie określać źródła decyzji, eksploracji, zapisu, animacji i restartowania walk.

Obecnie wspólna nagroda końcowa dla zapisanych jednostek nie jest błędem pomieszania stron: Q-learning zbiera decyzje tylko EnemyUnit. Przy przyszłym self-play wynik trzeba obliczać względem strony każdego kontrolera.

### B. Maska nie opisuje rzeczywistej wykonalności — priorytet krytyczny

ReinforcementLearningManager.cs:561–607 i 1054–1073:

- ChangeWeaponToRanged sprawdza tylko canDoAction && !hasRanged. Nie sprawdza istnienia nieuszkodzonej, możliwej do wyposażenia broni dystansowej.
- ChangeWeaponToMelee ma analogiczny problem.
- Ruch i ucieczka nie sprawdzają istnienia ścieżki i faktycznej zmiany pozycji.
- Bieg nie uwzględnia wszystkich ograniczeń wykonywanych przez normalny interfejs Run.
- Maska strzału pomija m.in. typ amunicji i oślepienie.
- IsTargetBehindObstacle blokuje strzał przez dowolną jednostkę lub MapElement, podczas gdy ValidateRangedAttack blokuje wyłączone pola i wysokie przeszkody. Agent ma zatem inny zbiór legalnych strzałów niż silnik walki.
- Szybki test szarży liczy drogę do środka zajętego pola celu, a rzeczywista szarża do wolnego pola sąsiedniego. Progi i bieżąca szybkość również mogą się różnić.

Potrzebne jest jedno API zasad: GetLegalActions(state, activeUnit). Z niego korzystają trening, gra, ewaluacja i obliczanie kolejnego maksimum Q. Walidacja nie powinna mieć efektów ubocznych ani generować logu na każdą sprawdzaną możliwość.

Maskować wyłącznie ruchy niemożliwe według reguł. Legalny, ale ryzykowny szaleńczy atak powinien być dostępny: jego opłacalności ma nauczyć się model. Nietrafiony, prawidłowo wykonany atak jest prawidłową próbką treningową.

### C. Cel aktualizacji Q nie odpowiada regule wyboru akcji — priorytet krytyczny

ReinforcementLearningManager.cs:498–523, 629–658 i 802–816.

Wybór akcji porównuje Q z różnych wierszy, ponieważ dla każdej akcji najpierw wybiera jej cel i osobno wyznacza stan. Rozliczenie bierze natomiast jeden next_state: względem poprzedniego celu albo najbliższego przeciwnika. GetMaxQNextMasked czyta wszystkie akcje tylko z tego jednego wiersza i maskuje je przybliżeniem boolów bez rzeczywistych celów.

To nie jest ta sama funkcja wartości przyszłej decyzji. Nie wystarczy dołożyć bardziej dokładnego testu HP.

Minimalna naprawa bez sieci: jedna wspólna funkcja enumeruje legalne kandydatury (akcja, cel, reprezentacja stanu) i jest używana zarówno przez wybór akcji, jak i bootstrap. To naprawia niespójność obliczeń, ale nadal pozostawia utratę informacji wskutek uproszczonego stanu.

Docelowo observation opisuje całą istotną sytuację, a konkretne action wskazuje typ, cel i ewentualnie pole. Wtedy cel Q-learning jest spójny:

`target = reward + gamma * max(Q(next_observation, a) dla a w legal_actions(next_observation))`

Dla prawdziwego końca epizodu składnik przyszłej wartości jest zerowy. Jeśli przejście obejmuje różną liczbę jednostek czasu, należy ustalić semantykę dyskonta i ewentualnie używać gamma^k, zamiast przypadkowo traktować każdy odstęp jako identyczny.

### D. Rozliczenie może wyprzedzać koniec akcji — priorytet krytyczny

RoundsManager.cs:421–449 czeka na IsMoving i następnie dwie klatki. Nie czeka tam na IsAttackSequenceRunning. Dalsze czekanie po pętli jednostki jest za późne, ponieważ SimulateUnit może już wywołać kolejny krok i rozliczyć poprzedni.

CombatManager.cs:2233–2244 uruchamia opóźnione uderzenie szarży przez osobną korutynę z WaitForSeconds(path.Count * 0.25). To opóźnione uderzenie wywołuje AttackCoroutine bez otaczającego AttackCoroutineTracked. Sam licznik aktywnych ataków nie obejmuje więc całej operacji szarży.

W treningu MoveWithDelay pomija interpolację, ale opóźnienie szarży pozostaje. Dwie klatki nie są warunkiem zakończenia walki.

Potrzebny jest uchwyt wykonania jednej komendy obejmujący ruch, uderzenie, kontratak, ataki okazyjne, obrażenia, stany i usunięcie jednostek. Następna decyzja i odczyt obserwacji mogą nastąpić dopiero po jego zakończeniu. Błąd lub timeout techniczny powinien przerwać/odrzucić epizod i być osobną metryką, a nie zwykłą porażką taktyczną. Trzeba też zagwarantować jednego właściciela przejścia do kolejnej rundy; obecnie istnieją ścieżki zarówno w AutoCombat, jak i w AdvanceRoundWhenReady oraz po wczytaniu zapisu.

### E. Jednostka widzi za mało, aby rozróżnić dobre decyzje

DetermineStates, ReinforcementLearningManager.cs:729–788: 11 boolów, czyli maksymalnie 2 048 stanów na rasę. Sama tabela nie jest dziś ogromna: 2 048 * 43 = 88 064 wartości na rasę. Problemem jest zlewanie różnych sytuacji do tego samego klucza.

Przykład: zdrowi wojownicy tej samej rasy, jeden zręczny, drugi silny, mogą mieć identyczny stan. Różne obrażenia, pancerze, atuty broni i parametry ataku nie są w nim zapisane. Jeden bit IsStrongerThanTarget oparty na HP * Overall tego nie zastępuje. Model nie może nauczyć się dwóch różnych decyzji dla identycznego wejścia i tej samej akcji.

Potrzebne są m.in. efektywne statystyki używane przez mechanikę, HP i maksimum HP, pancerz i odporności, broń z obrażeniami i cechami, czas pozostały do przeładowania, dostępne wyposażenie, stany z czasem trwania, ruch/akcja, pozycje jednostek, teren i linie strzału, kolejka inicjatywy oraz pozostały limit walki. Informacje wejściowe powinny odpowiadać temu, co AI ma prawo wiedzieć w wybranym trybie gry. Nie podawać stanu generatora losowego ani przyszłych wyników kości jako obserwacji.

Osobne tabele ras dodatkowo rozdzielają doświadczenia, zamiast uczyć wspólnej zależności między statystykami i opłacalnością akcji.

### F. Zbiór akcji zawiera już gotowe decyzje taktyczne

AllActions, ReinforcementLearningManager.cs:1464–1519, i GetTileFarthestFromTarget:1213–1235.

AI wybiera spośród siedmiu rankingów przeciwników i narzuconych sposobów ruchu. Nie wybiera dowolnego celu i pola. Nie odkryje więc np. konkretnej pozycji w osłonie lub pośredniego celu, którego żaden ranking nie wskazuje. Kilka rankingów może wskazywać tego samego przeciwnika, mnożąc równoważne akcje, zniekształcając eksplorację i rozpraszając aktualizacje Q.

Docelowy zbiór powinien zawierać konkretne legalne cele, osiągalne pola i dozwolone tryby ruchu. Zdeduplikować akcje prowadzące do tej samej komendy. Dla pełnego badania mechaniki udostępnić wszystkie badane mechaniki: dziś nie ma osobnych decyzji np. o zaklęciach, podnoszeniu się, doborze konkretnej broni czy lokacji trafienia. Zakres udostępnionych akcji wyznacza zakres wniosków o optymalności.

### G. Nagroda narzuca preferencje inne niż samo wygrywanie

ComputeDelayedReward:921–951 i PerformParameterAction:1005–1008:

- +1 za przeładowanie premiuje sam rodzaj czynności.
- +1,5 za obrażenia, -2 za stracone HP i premia za zabicie określają ręcznie kompromis między agresją, przeżyciem i wynikiem drużyny. Mogą np. karać korzystne poświęcenie jednostki.
- Zmianę HP wybranego celu rozlicza się aż do następnej decyzji tej samej jednostki. W międzyczasie cel mógł zostać trafiony przez sojusznika albo efekt stanu. Jest to sygnał konsekwencji odroczonych, ale nie wiarygodna identyfikacja autora obrażeń/zabójstwa.
- Dla usuniętej jednostki GiveTerminalRewardToAll dodaje tylko ImmediateReward i wynik drużyny. Brakuje danych do spójnego rozliczenia jej końcowych obrażeń. Sam terminalny wynik drużyny jest przy celu drużynowym prawidłowy także dla poległej jednostki.
- ComputeDelayedReward jest pomijane na początku SimulateUnit, gdy HP <= 0; potrzebne jest jawne zakończenie decyzji jednostki i zachowanie danych niezależnych od Unity Object.

Przy badaniu optymalnej gry drużyny zacząć od +1 za zwycięstwo, -1 za porażkę, 0 za remis. Usunąć bonusy za typ akcji. Optymalność wymaga zdefiniowania celu: maksymalizacja szans wygranej, zachowanie armii i minimalizacja liczby tur są różnymi zadaniami.

StepPenalty i gamma < 1 również zmieniają zadanie. Kara za każdą decyzję preferuje krótsze sekwencje, a dyskonto wcześniejsze nagrody i późniejsze straty. Jeśli celem jest wyłącznie prawdopodobieństwo wygranej w epizodzie o ustalonym skończonym limicie, punktem odniesienia powinno być gamma=1 i brak kosztu kroku. Inne warianty można porównać świadomie. Czas pozostały do limitu musi być częścią obserwacji. Przy gamma=0,9 nagroda +100 odległa o 50 przejść waży około 0,515, co utrudnia odkrywanie długich przygotowań.

Jeśli sama końcowa nagroda jest zbyt rzadka, porównać dodatek potencjałowy:

`r_shaped = r_task + gamma * Phi(next_state) - Phi(state)`

Przykładowy potencjał może używać różnicy pozostałego HP drużyn, normalizowanej stałymi wartościami z początku walki. Ustawić Phi=0 na wszystkich prawdziwych stanach terminalnych i używać tego samego dyskonta co trener. Przy założeniach teorii MDP taki dodatek zachowuje optymalną politykę zadania bazowego; nie gwarantuje, że skończony trening sieci ją odnajdzie. Zewnętrzne przerwanie zbierania danych nie jest automatycznie końcem zadania — wymaga właściwego bootstrapu.

Źródło: [Ng, Harada, Russell — Policy invariance under reward transformations](https://wordpress.andrewng.org/index.php/publication/policy-invariance-under-reward-transformations-theory-and-application-to-reward-shaping/).

### H. Definicja końca walki różni się od obsługi obrażeń

GatherTargetsInfo, SimulateUnit, BothTeamsExist i RoundsManager traktują HP <= 0 jako niezdolność do dalszej walki. CombatManager uruchamia obsługę śmierci/ran krytycznych dla HP < 0; postać na 0 HP może nadal istnieć i mieć dostępne akcje.

To potwierdzona niespójność progów. Trzeba ustalić jedno IsDefeated/IsEliminated zgodne z wybranym trybem reguł, a nie kopiować test HP. Limit RoundNumber > 50 jest dziś włączany do didAIWin=false, czyli traktowany jak przegrana EnemyUnit. Wprowadzić jawne Win/Loss/Draw oraz osobne TechnicalAbort; prawdziwy limit będący regułą zadania to terminal, techniczny limit zbierania danych to truncation.

### I. Ewaluacja mierzy tylko wycinek umiejętności

PlayerUnit są sterowane stałym AutoCombatManager.Act, którego ShouldUseAllOutAttack zawiera ręczne reguły i losową decyzję. To przydatny przeciwnik bazowy, ale zwycięstwa nad nim nie dowodzą ogólnej optymalności. Trening może uczyć się wykorzystywać jego ograniczenia.

Reset AIlearning losuje pozycje w SaveAndLoadManager.cs:543–551; ewaluacja też korzysta z tego resetu. Nie ma osobnego, wersjonowanego zestawu seedów i scenariuszy testowych. Przy 20 walkach błąd oszacowania win rate blisko 50% wynosi orientacyjnie +/-22 punkty procentowe dla poziomu 95%. Nie wystarczy to do oceny niewielkiej poprawy.

Potrzebne są zapisane wyniki epizodów z checkpoint_id, opponent_id, scenario_id, seed, wynikiem, liczbą tur, czasem i powodem zakończenia. Porównywać zamrożone modele na tych samych zestawach scenariuszy, z zamianą stron i kontrolą inicjatywy. Zachować osobny zestaw końcowy, nieużywany do wyboru checkpointu.

### J. Zapis nie zachowuje pełnego stanu treningu

SaveQTables:1305–1357 zapisuje tylko race/state/values. Brakuje wersji obserwacji, mapowania akcji, reguł, nagród, liczników, epsilon i stanu RNG. Po uruchomieniu Q jest wznawiane, ale harmonogram eksploracji nie jest odtwarzany z tego pliku. W BattleScene zapisane EpsilonDecayEpochs=100 różni się od domyślnego pola klasy 1000: przy 500 aktualizacjach na epokę to odpowiednio około 50 000 i 500 000 aktualizacji. Decydują wartości sceny/Inspectora.

Wprowadzić wersjonowany checkpoint i atomowy zapis z kopią zapasową oraz walidacją wymiarów. Po zmianie znaczenia stanu/akcji/nagrody nie ładować starego Q jako równoważnego nowego modelu. Zachować je do porównania historycznego.

### K. Obecne wskaźniki nie pokazują przyczyn problemu

_noOpStepCount rośnie dla abs(delayedReward)<0,001, choć pusty krok ze StepPenalty zwraca -0,2. Z kolei prawidłowa akcja może mieć nagrodę zero. No-op należy rozpoznawać na podstawie wyniku wykonania komendy, zmiany zasobów i semantyki akcji.

Fallback przy błędzie używa (int)AttackType.FinishTurn=7, ale identyfikator FinishTurn w AllActions wynosi 42. Obecnie ta ścieżka jest praktycznie osłonięta przez zawsze legalny FinishTurn, ale indeks pozostaje błędny i trzeba go poprawić przed rozszerzaniem maski.

## 3. Docelowe rozwiązanie dla tej gry

### Jeden logiczny kontroler drużyny

Ponieważ walka jest turowa, proponuję rozpocząć od jednego agenta na drużynę. Podejmuje on decyzję dla aktualnie aktywnej jednostki, obserwując planszę i jej parametry. Jedna wspólna sieć obsługuje różne jednostki na podstawie ich cech; rasa może być dodatkową kategorią, a nie kluczem oddzielnego treningu.

Zaletą jest jedna trajektoria decyzji drużyny. Poległa jednostka nie usuwa wiedzy o poprzednich decyzjach, a końcowa nagroda dotyczy zwycięstwa drużyny. Silnik zachowuje kolejkę inicjatywy: kontroler nie wybiera dowolnej jednostki poza jej turą. Po własnej komendzie środowisko przesuwa się do następnej legalnej decyzji kontrolowanej drużyny albo końca walki, rozliczając po drodze wszystkie efekty i decyzje przeciwnika.

Jeśli docelowo każda jednostka ma być osobnym agentem, rozważyć MA-POCA i nagrody grupowe. Unity opisuje obsługę jednostek usuniętych w trakcie epizodu i możliwość połączenia z self-play. Jest to inna architektura niż jeden kontroler drużyny; nie trzeba wdrażać obu naraz. [Dokumentacja współpracy agentów](https://unity-technologies.github.io/ml-agents/Learning-Environment-Design-Agents/#groups-for-cooperative-scenarios).

### Wspólny silnik decyzji i wykonania

Proponowany podział odpowiedzialności:

```text
CombatRules: obserwacja, legalne komendy, koszt i skutek komendy
CombatExecutor: wykonanie całej komendy i jednoznaczne zakończenie
CombatEnvironment: reset(seed, scenario), step(action), wynik i metryki
CombatPolicy: wybór akcji na podstawie obserwacji
TrainingRunner: zbieranie doświadczeń, trening, checkpointy i ewaluacja
```

Początkowo CombatExecutor może wywoływać istniejące menedżery Unity. Nie trzeba od razu przepisywać całej gry. Następnie wydzielać reguły do logiki bez UI, zachowując jedną implementację używaną przez trening i grę. Osobna uproszczona kopia zasad w Pythonie mogłaby nauczyć model wygrywania w innym środowisku.

### Sieć i algorytm

Rekomenduję Unity ML-Agents z PPO jako pierwszy mierzalny punkt odniesienia po naprawieniu środowiska. To wybór praktyczny dla istniejącego projektu Unity, a nie twierdzenie o najszybszym algorytmie dla każdej gry. W repozytorium pakiet ML-Agents nie jest obecnie dodany; trzeba dobrać i przypiąć zgodne wersje pakietu Unity i trenera Python.

Decyzje zamawiać zdarzeniowo, na granicach akcji, zamiast w każdej klatce. Maskować konkretne niewykonalne komendy. Standardowy interfejs dyskretnych akcji ma stałe wymiary: na początek potrzebne jest stabilne mapowanie/padding jednostek i pól oraz maski pustych slotów. Nie wolno po cichu obcinać listy celów do arbitralnego top-k, jeżeli chcemy badać optymalność w pełnym zbiorze akcji. Bardziej elastyczne ocenianie zmiennej listy kandydatów wymaga dodatkowej implementacji modelu/trenera.

[Dokumentacja decyzji, obserwacji i masek ML-Agents](https://unity-technologies.github.io/ml-agents/Learning-Environment-Design-Agents/).

Jeżeli po pomiarach okaże się, że pozyskiwanie doświadczeń jest drogie, kolejnym porównaniem może być Double DQN z buforem powtórek i siecią docelową. To nadal uczenie wartości Q, lecz z generalizacją pomiędzy podobnymi jednostkami. Publikacja uzasadnia ograniczanie zawyżania Q; nie dowodzi, że ten algorytm wygra z PPO w Arthalionie. [Double DQN](https://arxiv.org/abs/1509.06461).

### Przeciwnicy i scenariusze

1. Najpierw testy działania w małych walkach: melee 1v1, ranged 1v1, melee kontra ranged; następnie zespoły, osłony i stany.
2. Nie usuwać z końcowego treningu niewygodnych przypadków. Losować wyposażenie, zgodne z zasadami statystyki, teren, pozycje i inicjatywę w docelowym zakresie gry.
3. Zachować obecny AutoCombat jako zamrożony baseline, dodać przeciwnika losującego legalne akcje oraz archiwalne wersje modeli.
4. Trenować przez self-play przeciw mieszaninie przeciwników; nie wyłącznie przeciw aktualnej kopii siebie. Zamieniać uczącą się stronę i role jednostek.
5. Mierzyć wyniki w całej macierzy przeciwników. Wzajemne kontry mogą tworzyć cykle; pojedynczy wynik lub ranking nie wystarczy.

Oficjalna konfiguracja self-play obejmuje archiwum snapshotów i częstotliwości zmiany przeciwnika: [Training Configuration File](https://github.com/Unity-Technologies/ml-agents/blob/develop/com.unity.ml-agents/Documentation~/Training-Configuration-File.md).

## 4. Jak skrócić czas do dobrego modelu

Najpierw usunąć marnowane doświadczenia, następnie optymalizować symulację:

- Natychmiastowy reset z danych w pamięci zamiast wczytywania wielu plików jednostek po każdej walce.
- Tryb treningowy bez renderowania, animacji, dźwięku, przeliczania UI i autozapisów użytkowych. Nie usuwać efektów mechanicznych ani oczekiwania na ich logiczne zakończenie.
- Buforowane logowanie; obecnie File.AppendAllText otwiera plik przy każdym kroku. Szczegółowe ślady zachowywać wybiórczo, zbiorcze metryki zawsze.
- Jedno zebranie danych jednostki i ścieżek na decyzję; cache z unieważnianiem po zmianie planszy. Obecnie DetermineStates ponawia te same obliczenia dla wielu z 43 akcji, a GetTileAdjacentToTarget uruchamia wyszukiwanie dla kilku pól i używa GameObject.Find.
- Kilka osobnych instancji środowiska, np. pomiar dla 1, 2, 4 i 8 procesów. Obecne singletony i statyczne pola utrudniają niezależne areny w jednym procesie, więc osobne buildy są łatwiejszym początkiem. Dobierać liczbę procesów pomiarem CPU/RAM, nie z góry.
- Time.timeScale nie przyspiesza pętli oczekujących na kolejne klatki ani operacji dyskowych. Sama wartość 100 nie oznacza treningu 100 razy szybszego.

ML-Agents wspiera wiele instancji przez num-envs i konfigurację silnika: [Training Using Concurrent Unity Instances](https://unity-technologies.github.io/ml-agents/Training-ML-Agents/#training-using-concurrent-unity-instances).

Mierzyć osobno kroki/s, epizody/s, czas decyzji i resetu, zużycie zasobów, liczbę doświadczeń do uzyskania danego wyniku oraz czas rzeczywisty do osiągnięcia tego wyniku. Duży wzrost kroków/s nie jest sukcesem, jeśli próbki nadal opisują błędne akcje.

## 5. Kolejność wdrożenia i warunki odbioru

| Etap | Zakres | Warunek odbioru |
| --- | --- | --- |
| 1. Wiarygodne wykonanie | Uchwyty akcji, wspólna legalność, prawidłowy koniec walki, eliminacja pustych zmian broni | Troll z samą pałką nigdy nie dostaje komendy zmiany na ranged; szarża generuje jeden kompletny wynik; brak decyzji w trakcie rozliczenia |
| 2. Wiarygodny punkt odniesienia | Spójny bootstrap Q, osobny inference, jawne wyniki i wersje checkpointów | Gra korzysta z załadowanej polityki; inference/eval nie zmieniają Q; zapis/wczytanie zachowuje decyzje i postęp treningu |
| 3. Właściwe zadanie | Obserwacja parametrów i planszy, konkretne akcje, nagroda za wynik | Różne istotne statystyki są rozróżnialne; legalne ryzykowne akcje są dostępne; osobny remis i błąd techniczny |
| 4. Generalizacja | Wspólna sieć, przeciwnicy archiwalni, scenariusze i seedy | Poprawa na zestawie walidacyjnym i osobnym końcowym; brak załamania w podgrupach uzbrojenia i stanów |
| 5. Wydajność | Reset w pamięci, build bez grafiki, logowanie buforowane, wiele procesów | Wzrost jakości na godzinę przy zachowaniu zgodności reguł |

Przykładowe niezbędne regresje przed długim treningiem: zwykły atak, pudło, szarża z opóźnionym uderzeniem, odwrót z atakiem okazyjnym, obrażenia od stanu, przeładowanie wieloetapowe, brak wymaganej broni/amunicji, granica 0 HP, śmierć autora decyzji, koniec epizodu bez kolejnej tury jednostki oraz zgodność wyników szybkiej i wizualnej symulacji przy kontrolowanym RNG. To są testy przyszłego wdrożenia, nie testy wykonane podczas tego audytu.

Do porównywania checkpointów zacząć od kilkuset walk na tym samym zestawie scenariuszy z zamianą stron; około 400 niezależnych wyników daje przy win rate bliskim 50% orientacyjny margines +/-5 punktów procentowych, nie gwarancję takiej dokładności dla każdej podgrupy. Raportować przedziały ufności, remisy i wyniki według archetypu, a dla małych różnic zwiększać próbę. Powtórzyć trening z kilkoma seedami. Najlepszy checkpoint wybierać na walidacji, końcowy pomiar robić na odłożonym zbiorze.

## 6. Jak interpretować „optymalne” i badać mechanikę

Bez definicji celu nie istnieje jedna optymalna strategia. Po ustaleniu maksymalizacji prawdopodobieństwa wygranej AI może samodzielnie odkrywać, kiedy opłaca się celować, przeładować, wycofać, zmienić broń, szarżować lub poświęcić jednostkę. Generator legalnych akcji określa reguły, nie opłacalność tych decyzji.

Dla małych, ograniczonych mikroaren można dodatkowo policzyć dokładną wartość przez enumerację stanów i probabilistycznych wyników, o ile liczba stanów na to pozwala. To sprawdzi poprawność środowiska i jakość uczenia. Dla pełnej gry RL daje empirycznie silne polityki i coraz lepsze przybliżenie; sama liczba epizodów nie dowodzi globalnego optimum.

Do badania balansu zapisywać macierze zwycięstw archetypów, zestawów ekwipunku i scenariuszy wraz z checkpointami. Spadek wyniku przeciw nowemu przeciwnikowi może oznaczać wykrytą kontrę, a nie błąd mechaniki. Wnioski należy odnosić do sprawdzonych map, zasad, parametrów i puli przeciwników. Zmiana reguł wymaga ponownego treningu lub kontrolowanej adaptacji oraz nowej ewaluacji.
