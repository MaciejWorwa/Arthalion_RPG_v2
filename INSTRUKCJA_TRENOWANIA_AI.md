# Trening AI, testowe walki i wyniki — instrukcja

Projekt zawiera uczącą się sieć PPO oraz niezależne testy przeciw zachowaniom zapisanym w `AutoCombatManager`. Jedna sieć uczy się decyzji dla różnych jednostek i ras. Nie trzeba trenować osobnego modelu dla każdej rasy.

W repozytorium są dwa gotowe artefakty:

- `Assets/AI/Models/CombatV1_Starter.onnx` z metadanymi: model do oglądania i testowania walk bez uczenia.
- `Tools/AI/checkpoints/StarterV1.zip`: pełny checkpoint po **5 192 decyzjach**, wraz ze stanem optymalizatora, wynikami treningu i historią TensorBoard. Pozwala kontynuować naukę na innym komputerze.

To krótki trening sprawdzający działanie systemu. W wykonanym teście 50 walk model miał 25 wygranych i 25 przegranych. **Nie wykazano jeszcze przewagi nad AutoCombat.** Szczegóły i surowe wyniki tego pomiaru: [weryfikacja](Tools/AI/WERYFIKACJA.md) i [CSV](Tools/AI/benchmarks/starter-v1/episodes.csv).

## 1. Przygotowanie drugiego komputera

Poniższe skrypty są przygotowane dla **Windows 10/11 x64**. Potrzebujesz Git, Unity Hub, edytora **Unity 6000.2.7f2** z obsługą budowania Windows oraz działającego polecenia `python` z `pip`. Wersja Unity jest zapisana w `ProjectSettings/ProjectVersion.txt`. Przy pierwszej instalacji potrzebny jest internet. Osobna karta graficzna do treningu nie jest wymagana; domyślna konfiguracja używa CPU.

W PowerShell:

```powershell
git clone https://github.com/MaciejWorwa/Arthalion_RPG_v2.git
cd Arthalion_RPG_v2
```

Jeżeli masz już kopię, najpierw zakończ trening, zachowaj własne zmiany i pobierz aktualizację przez `git pull`. Wszystkie dalsze polecenia wykonuj z głównego katalogu projektu, gdzie znajdują się `Assets`, `Packages` i `Tools`.

1. Dodaj ten katalog do Unity Hub i otwórz go w Unity **6000.2.7f2**. Zaczekaj na import zasobów i pakietów.
2. W Unity wybierz **Arthalion → AI → Build training player**. Poczekaj na ukończenie. Powstanie `Builds/AI/ArthalionAI.exe`. Zbuduj go ponownie po aktualizacji kodu mechaniki lub AI.
3. W PowerShell przygotuj środowisko:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\Tools\AI\setup.ps1
   ```

Skrypt pobierze Python **3.10.12** i przypięte zależności do lokalnego środowiska `Tools/AI/.venv`. Nie trzeba ręcznie instalować PyTorch ani zmieniać domyślnego Pythona. Końcowy komunikat powinien zawierać `Python/trainer ready; torch=2.2.2+cpu`. `CUDA=False` jest prawidłowe dla tej konfiguracji.

Jeżeli PowerShell blokuje dalsze skrypty `.ps1`, wykonaj w tym oknie `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`. Ustawienie dotyczy tylko tej sesji terminala.

Build, środowisko Python i generowane wyniki nie są przesyłane przez Git. Odtwarza się je powyższymi poleceniami. Gotowy checkpoint startowy jest częścią repozytorium.

## 2. Zacznij lub kontynuuj trening

**Kontynuowanie dołączonego modelu** — zalecany pierwszy krok:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\checkpoint.py import `
  --bundle .\Tools\AI\checkpoints\StarterV1.zip --run-id moje-ai-01

.\Tools\AI\start-training.ps1 -RunId moje-ai-01 -Resume -Minutes 60 -Environments 1
```

Import wykonaj tylko raz dla danej nazwy. Import nigdy nie nadpisuje istniejącej sesji. Następnego dnia wystarczy drugie polecenie z `-Resume` i tą samą nazwą.

**Uczenie całkowicie od zera** — nowa nazwa i bez importu oraz `-Resume`:

```powershell
.\Tools\AI\start-training.ps1 -RunId od-zera-01 -Minutes 60 -Environments 1
```

Domyślny zestaw `@starter` automatycznie tworzy na każdym komputerze **25 scenariuszy**: pięć ras × walka wręcz, dystansowa, mieszana, przeszkoda z przejściem i walka drużynowa. Rasy: **Człowiek, Goblin, Morgras, Drimhar, Hasai**. Nie musisz kopiować zapisów z pierwszego komputera, aby użyć tego zestawu. Istniejące scenariusze o tych nazwach nie są nadpisywane.

Trening działa bez okna gry. Co 10 minut runner próbuje przetestować najnowszy gotowy eksport modelu na 40 walkach przeciw AutoCombat. Do pierwszego ukończonego eksportu nie ma jeszcze czego oceniać.

Sesja zatrzyma się po upływie czasu albo osiągnięciu limitu decyzji. Domyślny limit to **500 000 decyzji łącznie od początku danej sesji**, również przy wznowieniu. Gdy go osiągniesz, podnieś go, np.:

```powershell
.\Tools\AI\start-training.ps1 -RunId moje-ai-01 -Resume -Minutes 60 -Steps 1000000
```

Aby zakończyć wcześniej, naciśnij **Ctrl+C w terminalu trenera** i poczekaj na zapis oraz powrót znaku zachęty. Nie zamykaj gwałtownie procesu podczas zapisywania checkpointu. Wznowienie przywraca sieć i optymalizator; bitwa przerwana w połowie zaczyna się ponownie.

## 3. Podgląd wyników podczas nauki

Otwórz drugi terminal w katalogu projektu:

```powershell
.\Tools\AI\.venv\Scripts\tensorboard.exe --logdir .\Tools\AI\results --port 6006
```

Otwórz [TensorBoard na swoim komputerze](http://localhost:6006). Wybierz sesję `moje-ai-01/ArthalionCombatV1` i zakładkę **Scalars**. Wykres `Environment/Cumulative Reward` pokazuje nagrodę treningową; wykresy `Policy/*` i `Losses/*` pomagają rozpoznać, czy trener aktualizuje sieć. Raportowanie następuje co 5 000 decyzji, więc pierwsze wykresy nie pojawiają się od razu. Sam spadek loss lub wzrost nagrody nie dowodzi przewagi w walce.

Czytelny raport wyników walk:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\summarize.py .\Tools\AI\metrics\moje-ai-01
```

Raport rozdziela trening i testy zamrożonych modeli, scenariusze oraz checkpointy. Pokazuje W/L/D, odsetek wygranych, przedział ufności, błędy techniczne i decyzje/s. Aby zapisać pełniejsze zestawienie:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\summarize.py `
  .\Tools\AI\metrics\moje-ai-01 --json .\Tools\AI\metrics\moje-ai-01\raport.json
```

Pliki na dysku:

| Plik lub katalog | Co zawiera |
| --- | --- |
| `Tools/AI/results/moje-ai-01/ArthalionCombatV1/checkpoint.pt` | Wagi, stan optymalizatora i licznik kroków do wznowienia |
| `Tools/AI/results/moje-ai-01/ArthalionCombatV1.onnx` | Ostatni wyeksportowany model do rozgrywania walk |
| Ten sam katalog, `ArthalionCombatV1.metadata.json` | Identyfikatory schematu, reguł i sumę kontrolną modelu |
| `Tools/AI/metrics/moje-ai-01/worker_*/episodes.csv` | Wynik każdej zakończonej walki, seed, scenariusz, strona, czas, błędy |
| `Tools/AI/metrics/moje-ai-01/evaluation_*/` | Oddzielne okresowe testy zamrożonych modeli |
| `Tools/AI/results/moje-ai-01/run_logs/` | Log trenera i procesu gry |

CSV można otworzyć w Excelu; separatorem jest średnik. Dla szczegółowej analizy decyzji w oknie Unity włącz `TraceCommands` — powstaje `commands.csv`. Śledzenie wszystkich komend jest domyślnie wyłączone, aby przyspieszyć trening. Wyniki sesji uruchamianych w edytorze znajdziesz przyciskiem **Open CSV metrics**.

## 4. Sprawdź, czy model wygrywa z AutoCombat

Po zakończeniu godziny treningu uruchom osobny test bez uczenia:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\evaluate.py `
  --env .\Builds\AI\ArthalionAI.exe `
  --model .\Tools\AI\results\moje-ai-01\ArthalionCombatV1.onnx `
  --scenarios '@starter' --episodes 400 --seed 87654321 `
  --output .\Tools\AI\metrics\moje-ai-01-test400

.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\summarize.py .\Tools\AI\metrics\moje-ai-01-test400
```

Za każdym razem użyj nowej nazwy katalogu `--output`, np. `moje-ai-01-test400-po2h`. Test porównuje zamrożony model z AutoCombat, zamienia strony i nie aktualizuje wag. Obok ONNX musi leżeć odpowiadający mu plik `.metadata.json`. Ewaluator sprawdza zgodność modelu z buildem.

Jak oceniać postęp:

1. **Najpierw błędy:** oczekuj `aborts=0`. `TechnicalAbort` to błąd wykonania, a nie porażka taktyczna. Sprawdź kolumnę `error` i log, zanim wydłużysz trening.
2. **Porównuj te same testy:** ten sam zestaw scenariuszy, liczba walk i seed dla modeli po 1h, 2h itd. 40 walk wystarcza do wstępnego podglądu; kilkaset daje lepszy pomiar.
3. **Sprawdź przedział ufności:** wynik 52% po kilkudziesięciu walkach nie dowodzi poprawy. Przy 400 niezależnych walkach wynik około 50% ma orientacyjnie margines ±5 punktów procentowych; pary z zamianą stron nie są w pełni niezależne.
4. **Sprawdź każdą rasę, mapę i obie strony:** wysoka średnia może ukrywać słabość np. przeciw broni dystansowej lub przy przeszkodach. Zwracaj uwagę również na wzrost liczby remisów.
5. **Zostaw osobny test końcowy:** inne mapy i seedy niż używane do wyboru checkpointu. `@starter` nadaje się do kontroli treningu, lecz sama poprawa na nim nie potwierdza radzenia sobie na nowych mapach.

Praktyczny cykl na jednym PC: **godzina treningu → test → obejrzenie słabych walk → kolejna godzina**. Zacznij od jednego procesu i małych bitew. Potem porównaj `-Environments 2` i `4`, mierząc decyzje/s oraz zużycie RAM. Więcej procesów nie zawsze przyspiesza naukę. Nie ma uczciwej gwarancji, że model po konkretnej liczbie godzin będzie wygrywał.

## 5. Oglądaj testowe walki

Najprościej: uruchom dwie widoczne walki gotowego modelu, bez treningu:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\evaluate.py `
  --env .\Builds\AI\ArthalionAI.exe `
  --model .\Assets\AI\Models\CombatV1_Starter.onnx `
  --scenarios '@starter' --episodes 2 --seed 87654321 --visible `
  --output .\Tools\AI\metrics\podglad-starter-01
```

Aby oglądać swój model, zamień ścieżkę `--model` na model z `results/moje-ai-01`. Każde uruchomienie podglądu powinno mieć nowy katalog wyników. Jeśli równolegle trwa automatyczny test treningu, użyj innego wolnego portu, np. `--port 7005`.

W edytorze Unity możesz także przygotować konkretną bitwę:

1. Skopiuj swój ONNX oraz `.metadata.json` do `Assets/AI/Models/` pod własną nazwą. Poczekaj na import.
2. Otwórz `Assets/Scenes/BattleScene.unity`, a potem **Arthalion → AI → Training and benchmarks**.
3. W polu `Model` przypisz ONNX. Wybierz `Mode=Inference` dla jednej walki albo `Evaluation` dla serii. Ustaw `Opponent=AutoCombat`, `ControlledSide=Enemy` lub `Player`. **RandomPolicySmokeTest musi być wyłączone.**
4. Wyłącz `FastSimulation`, aby oglądać ruchy. Wpisz nazwę zapisu w `ScenarioNames`. Pusta lista oznacza aktualną planszę; przygotuj ją w Play Mode przy `Mode=Disabled`, a potem przełącz tryb i uruchom eksperyment. Dla konkretnego ustawienia wyłącz `RandomizePositions` i `RandomizeEquipment`.
5. Ustaw `EpisodeLimit`, np. 10 dla serii. `0` oznacza brak limitu. `SwapControlledSide` zamienia stronę modelu między walkami. Panel pokazuje wygrane, przegrane, remisy i błędy. Przycisk **Stop after this episode** kończy serię po bieżącej walce.

Tryb `Training` wymaga połączonego trenera Python. Tryby `Inference` i `Evaluation` nie uczą modelu. `Disabled` pozostawia standardową grę i dotychczasowy AutoCombat.

## 6. Własne scenariusze, kolejne rasy i uzbrojenie

W grze przygotuj obie drużyny, statystyki, broń, amunicję i przeszkody. Zapisz bitwę przed pierwszą rundą, np. `AI-trening-las`. Przygotuj kilka różnych bitew. Uruchom:

```powershell
.\Tools\AI\start-training.ps1 -RunId moje-ai-01 -Resume -Minutes 60 `
  -Scenarios 'AI-trening-las,AI-trening-lucznicy,AI-trening-magowie'
```

AI może wyposażać tylko broń, którą jednostka faktycznie ma w ekwipunku. Nie otrzymuje całego arsenału automatycznie. Wprowadzaj nowe rasy i sytuacje stopniowo, zachowując także wcześniejsze scenariusze, żeby sprawdzać zapominanie. Parametry ras i wyposażenia nie wymagają osobnych sieci; zmiana wymiarów obserwacji lub akcji wymaga już nowego modelu.

Własne zapisy są poza repozytorium, domyślnie w:

```powershell
explorer "$env:USERPROFILE\AppData\LocalLow\Maciej Worwa\Arthalion_RPG"
```

Na drugi komputer skopiuj **całe foldery odpowiednich zapisów** do analogicznego katalogu. Zawierają jednostki i mapę. Nie kopiuj tylko jednego pliku jednostki. `@starter` tworzy swoje zapisy samodzielnie; własne scenariusze trzeba przenieść osobno. Przy zmianie Company/Product Name w ustawieniach Unity zmieni się lokalizacja zapisów.

Limity jednej bitwy V1: plansza do 32×32 pól, 32 jednostki, 64 obserwowane bronie, 32 efekty czasowe i 32 znane zaklęcia aktywnej jednostki. Przekroczenie limitu zgłasza błąd. Szczegóły reprezentacji i self-play: [dokumentacja techniczna](Tools/AI/README.md).

## 7. Przenieś dalszą naukę między komputerami

**Na komputerze A:** zakończ trening przez Ctrl+C i poczekaj na zapis. Następnie:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\checkpoint.py export `
  --run-id moje-ai-01 --output "$env:USERPROFILE\Desktop\moje-ai-po1h.zip"
```

**Na komputerze B:** pobierz ten sam stan kodu (`git pull`), zbuduj player, uruchom `setup.ps1` i skopiuj ZIP, np. na Pulpit. Następnie:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\checkpoint.py import `
  --bundle "$env:USERPROFILE\Desktop\moje-ai-po1h.zip" --run-id moje-ai-pc2

.\Tools\AI\start-training.ps1 -RunId moje-ai-pc2 -Resume -Minutes 60
```

Przy własnych scenariuszach przenieś też zapisy i dodaj takie samo `-Scenarios`. Przy self-play kontynuuj z `-SelfPlay`; przenoś przebieg tego samego rodzaju. Paczka zachowuje ostatni checkpoint z optymalizatorem, ONNX, metadane, konfigurację, CSV i zapisane wykresy. Nie przenosi puli dawnych przeciwników self-play z pamięci trenera — po wznowieniu buduje się ona ponownie. Nie zawiera builda ani środowiska Python. Import przelicza ścieżki dla nowego komputera i sprawdza sumy SHA-256. Checkpointy PyTorch importuj wyłącznie z zaufanego źródła.

Sam ONNX wystarczy do testowania walk, ale **nie wystarczy do wznowienia treningu PPO**. `git pull` również nie pobiera Twoich późniejszych sesji, ponieważ `results` i `metrics` są ignorowane. Przenoś je paczką. Jeśli chcesz zachować konkretny checkpoint na GitHubie, dodaj wyeksportowaną paczkę w `Tools/AI/checkpoints/` i wypchnij ją osobnym commitem; nie dodawaj całego środowiska lub wszystkich wyników. Większe archiwa przechowuj poza historią Git, np. jako zasób wydania.

Możesz trenować niezależnie na obu komputerach pod różnymi nazwami. Plików optymalizatora nie można scalać przez Git; do dalszej nauki wybierz jedną sesję i przenieś jej checkpoint. W celu odtworzenia wyników zachowaj też identyfikator commita, konfigurację i scenariusze. Zmiana mechaniki gry wymaga ponownej oceny modelu; błąd zgodności `rules` lub `schema` należy wyjaśnić, a nie omijać ręczną edycją metadanych.

## 8. Najczęstsze problemy

| Objaw | Co zrobić |
| --- | --- |
| Brak `Builds/AI/ArthalionAI.exe` | Otwórz właściwą wersję Unity i wybierz Build training player |
| Błąd importu torch/ML-Agents | Uruchom `powershell -ExecutionPolicy Bypass -File .\Tools\AI\setup.ps1 -Reinstall` |
| Trening od razu kończy się po wznowieniu | Podnieś `-Steps` ponad zapisany łączny krok |
| Import zgłasza istniejący RunId | Wybierz nową nazwę; nie nadpisuj wcześniejszej sesji |
| Brak wykresów | Poczekaj na 5 000 decyzji, sprawdź wybraną sesję i katalog TensorBoard |
| Model/metadata mismatch | Zachowaj parę ONNX + metadata; użyj zgodnej wersji kodu i przebuduj player |
| Nie ma własnego scenariusza | Skopiuj cały jego folder zapisu do LocalLow na drugim komputerze |
| Błąd zajętego portu | Zakończ poprzedniego gracza/trenera lub podaj inny `--port` w ewaluatorze |
| Dużo TechnicalAbort | Sprawdź kolumnę `error`, limity bitwy i logi, zamiast zwiększać czas treningu |
