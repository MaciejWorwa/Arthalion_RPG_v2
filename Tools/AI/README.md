# Trenowanie i sprawdzanie AI walki

Nowy system używa jednej wspólnej sieci PPO dla wszystkich jednostek. Każda decyzja zależy od aktywnej jednostki, pozostałych postaci, wyposażenia, stanów i planszy. Kontroler drużyny zachowuje rzeczywistą kolejkę inicjatywy. Rasy nie mają osobnych tabel Q: podobne sytuacje dostarczają wspólnego doświadczenia.

Nie ma gwarancji globalnego optimum ani stałego czasu potrzebnego do osiągnięcia przewagi. Celem pomiaru jest przewaga nad AutoCombat na odłożonych scenariuszach, a nie sam wzrost nagrody treningowej.

Wyniki wykonanych testów wdrożenia i benchmarku opisano w [WERYFIKACJA.md](WERYFIKACJA.md).

Pełna instrukcja użytkownika, w tym instalacja na drugim komputerze i przenoszenie sesji: [INSTRUKCJA_TRENOWANIA_AI.md](../../INSTRUKCJA_TRENOWANIA_AI.md).

## Szybki start na Windows

1. Otwórz projekt w Unity **6000.2.7f2** i zaczekaj na import pakietów. Dodano `com.unity.ml-agents` 4.0.0; Pythonowy trener ma wersję 1.1.0. Zależności są przygotowywane w `Tools/AI/.venv`, bez zmiany domyślnego Pythona systemowego.
2. Wybierz **Arthalion → AI → Build training player**. Powstaje `Builds/AI/ArthalionAI.exe`. Ten osobny build pomija nieużywane shadery obliczeń sieci na GPU; modele w nim mogą działać na CPU, a trening wykonuje Python. Normalny build gry nie korzysta z tego wycinania shaderów.
3. Z katalogu repozytorium uruchom:

   ```powershell
   .\Tools\AI\start-training.ps1 -RunId starter-01 -Minutes 60 -Environments 1
   ```

   Przy pierwszym uruchomieniu `setup.ps1` pobierze lokalne narzędzia i Python 3.10.12 oraz zainstaluje zależności. Na tym komputerze zostało to wykonane podczas wdrożenia.
4. Domyślne `@starter` tworzy **25 nowych zapisów treningowych**: pięć ras × walka wręcz, dystansowa, mieszana, ściana z przejściem i drużynowa. Plansze mają 8×8 pól. Rasy: **Człowiek, Goblin, Morgras, Drimhar i Hasai**. Scenariusze są symetryczne, inicjatywa pochodzi z mechaniki gry, bronie i statystyki powstają przez istniejące konstruktory. Istniejących zapisów nie nadpisuje się.
5. Ustawiony budżet kończy trening po 60 minutach lub 500 000 decyzji, zależnie od tego, co nastąpi wcześniej. Zatrzymanie i Ctrl+C zapisują model. Wznowienie:

   ```powershell
   .\Tools\AI\start-training.ps1 -RunId starter-01 -Minutes 60 -Resume
   ```

Start od pięciu ras ułatwia ocenę wyników. Architektura nie wymaga pięciu osobnych treningów. Później wystarczy dostarczyć scenariusze innych ras i kontynuować zgodny model; nie należy zmieniać schematu wejść ani wymiarów w połowie eksperymentu.

## Własne bitwy

Przygotuj jednostki obu stron w aplikacji, ustaw teren i ekwipunek, wybierz amunicję i zapisz bitwę przed pierwszą rundą. Potem podaj nazwę zapisu:

```powershell
.\Tools\AI\start-training.ps1 -RunId moje-bitwy-01 -Scenarios 'AIlearning' -Minutes 60
```

Możesz podać kilka nazw rozdzielonych przecinkiem. Program zmienia scenariusze i odtwarza pozycje/wyposażenie z kopii w pamięci pomiędzy powtórzeniami tego samego scenariusza. Przy zmianie scenariusza wczytuje jego zapis wraz z mapą.

Domyślnie pozycje są losowane spośród legalnych wolnych pól. Wyposażenie jest losowane **spośród przedmiotów, które dana jednostka faktycznie posiada i może wyposażyć**. System nie przyznaje automatycznie całego arsenału. Aby ćwiczyć inne bronie, dodaj je do ekwipunku w scenariuszu. Parametry postaci pochodzą z zapisu; różnorodne statystyki trzeba zapewnić różnymi postaciami/scenariuszami.

W automatycznym eksperymencie obie strony używają automatycznych rzutów i automatycznej śmierci przy HP < 0. Strona bazowa korzysta z AutoCombatManager i jego automatycznej obrony; strona ucząca się samodzielnie wybiera obronę. Zmieniane ustawienia gry są przywracane po zakończeniu eksperymentu. HP=0 nie oznacza usunięcia jednostki z walki.

## Podgląd i zwykłe używanie modelu w grze

W `Assets/AI/Models/CombatV1_Starter.onnx` pozostawiono model po krótkim treningu integracyjnym. Można go od razu przypisać w oknie AI i obejrzeć jego decyzje. Jest to punkt startowy do testów, nie docelowo wyszkolony model wszystkich sytuacji. Pełny przenośny checkpoint znajduje się w `Tools/AI/checkpoints/StarterV1.zip`. Zaimportuj go poleceniem `python Tools/AI/checkpoint.py import --bundle Tools/AI/checkpoints/StarterV1.zip --run-id moje-ai-01`, następnie uruchom trening z `-RunId moje-ai-01 -Resume`. Eksport dalszej sesji: `python Tools/AI/checkpoint.py export --run-id moje-ai-01 --output moje-ai.zip` (po zakończeniu treningu).

Otwórz **BattleScene**, następnie **Arthalion → AI → Training and benchmarks**. Okno pokazuje istniejący ReinforcementLearningManager.

- `Disabled`: dotychczasowa gra i AutoCombatManager.
- `Training`: decyzje dostarcza uruchomiony trener Python. Brak połączenia jest błędem, a nie ukrytym przejściem na losowe zachowanie.
- `Evaluation`: powtarzane testy zamrożonego modelu przeciw AutoCombatManager, bez aktualizacji sieci.
- `Inference`: jedna bitwa sterowana modelem, bez uczenia i automatycznego restartu po zakończeniu.

Skopiuj wyeksportowany `ArthalionCombatV1.onnx` z katalogu wyników do `Assets`, zaczekaj na import i przypisz go w polu `Model`. Ustaw `ControlledSide` na Enemy lub Player. W normalnych testach pozostaw `Opponent=AutoCombat`. Wyłącz `FastSimulation`, aby oglądać walkę; prędkość oglądania można ustawić w istniejącym GameSpeedController. Ustaw `ScenarioNames` na pustą listę, aby użyć bitwy przygotowanej na aktualnej planszy, albo wpisz nazwy zapisów.

`RandomPolicySmokeTest` jest wyłącznie testem poprawności wykonania losowych legalnych akcji. Musi być **wyłączone** przy właściwym treningu, benchmarku i używaniu modelu. Losowa polityka ma osobny generator i nie zużywa rzutów kośćmi środowiska.

Panel pokazuje W/L/D, błędy techniczne, przedział ufności win rate, liczbę decyzji i decyzje/s. `Stop after this episode` zatrzymuje eksperyment po zakończeniu bieżącej walki.

## Jak oceniać postęp

Wyniki trafiają do:

- `Tools/AI/results/<RunId>/`: checkpointy PPO, stan optymalizatora, ONNX i TensorBoard.
- `Tools/AI/metrics/<RunId>/worker_<port>/episodes.csv`: jawny wynik każdej walki, seed, scenariusz, strona modelu, liczba decyzji, czas i powód błędu.
- `manifest.json`: schemat obserwacji, wersja zasad, parametry eksperymentu i identyfikator modelu.
- `commands.csv`: opcjonalny szczegółowy ślad, gdy włączono `TraceCommands`; domyślnie wyłączony dla wydajności.

Podgląd TensorBoard:

```powershell
.\Tools\AI\.venv\Scripts\tensorboard.exe --logdir .\Tools\AI\results
```

Zbiorczy raport wyników:

```powershell
python .\Tools\AI\summarize.py .\Tools\AI\metrics\starter-01
```

Trening automatycznie próbuje co 10 minut uruchomić niezależny test najnowszego gotowego checkpointu na 40 walkach. Model jest kopiowany przed testem: wynik dotyczy zamrożonej sieci, mimo że trening trwa dalej. Ewaluator nie tworzy optymalizatora. Wymaga ukończonego eksportu ONNX; przed pierwszym checkpointem nie ma czego testować.

40 walk pokazuje tylko duże zmiany. Do wyboru modelu użyj co najmniej kilkuset walk, zamiany stron i tego samego zestawu scenariuszy. Końcowy test wykonaj na mapach i seedach nieużywanych do wyboru checkpointu:

```powershell
.\Tools\AI\.venv\Scripts\python.exe .\Tools\AI\evaluate.py `
  --env .\Builds\AI\ArthalionAI.exe `
  --model .\Tools\AI\results\starter-01\ArthalionCombatV1.onnx `
  --scenarios '@starter' --episodes 400 --seed 87654321 `
  --output .\Tools\AI\metrics\starter-01-final
```

Obok ONNX musi znajdować się plik `.metadata.json`, który przygotowuje runner treningu. Ewaluator porównuje schemat i wersję zasad z buildem. `--visible` uruchamia oglądanie testowych bitew. Modele ze starego `q_tables.json` są niezgodne i nie są ładowane ani nadpisywane.

Interpretacja:

- Porównuj **win rate zamrożonych checkpointów** i przedziały ufności. Zwracaj uwagę na wyniki każdej rasy/scenariusza i obu stron, a nie tylko średnią.
- Remisy i błędy techniczne mają osobne kolumny. TechnicalAbort nie jest porażką taktyczną. Przed długim treningiem oczekuj **0 błędów technicznych**.
- Przy 400 niezależnych walkach win rate około 50% ma orientacyjnie margines +/-5 punktów procentowych. Zamieniane parami scenariusze nie są w pełni niezależne; do bardzo małych różnic potrzebna jest większa próba i analiza par.
- W self-play średnia wygranych może krążyć wokół 50% mimo poprawy. Oceniaj taki model przeciw zamrożonemu AutoCombat i wcześniejszym checkpointom.
- Pierwszy działający checkpoint nie oznacza jeszcze skutecznej taktyki. Nie należy wnioskować o przewadze z samego zakończenia treningu lub poprawnego eksportu.

## Wydajność na jednym PC

Zacznij od małych bitew 1v1/2v2 i jednego procesu. Porównaj decyzje/s i czas do osiągnięcia tej samej jakości dla `-Environments 1`, `2` i `4`. Więcej procesów może pogorszyć wynik, jeśli zabraknie RAM lub CPU. Nie uruchamiaj od razu dużych armii wszystkich ras na wielkich mapach.

`FastSimulation` pomija animacje i znaczną część aktualizacji paneli; nie pomija rozliczenia obrażeń, obrony, szarży ani stanów. Trening nie wymaga renderowania i używa parametrów gry zamiast pikseli. Wariant domyślny używa CPU. GPU do optymalizacji PyTorch wymaga zgodnej instalacji CUDA-owego PyTorch; samo podanie `-Device cuda` nie zmieni instalacji CPU na GPU.

Praktyczny cykl: godzina treningu → porównanie zamrożonego modelu → sprawdzenie słabych scenariuszy → dodanie ich do kolejnej sesji. Nie ma potrzeby wykonywać od razu miesięcznego treningu. Jeśli jakość stoi w miejscu, najpierw sprawdź błędy techniczne, remisy, różnorodność danych i konfigurację, a dopiero potem zwiększaj budżet.

## Self-play po sprawdzeniu punktu odniesienia

```powershell
.\Tools\AI\start-training.ps1 -RunId selfplay-01 -Scenarios '@starter' -SelfPlay -Minutes 60
```

Konfiguracja `selfplay.yaml` używa archiwum wcześniejszych przeciwników. Obie drużyny mają ten sam schemat zachowania i różne TeamId. Nie mieszaj wznowienia treningu przeciw AutoCombat z inną architekturą lub niezgodnym schematem; do adaptacji istniejącego modelu można skorzystać z mechanizmu `--initialize-from` ML-Agents po świadomym przygotowaniu konfiguracji.

## Zakres i ograniczenia V1

- Wszystkie konkretne cele i osiągalne pola są dostępne, a nie tylko rankingi typu „najbliższy/najsłabszy”. Typ akcji i jej parametr są kolejnymi decyzjami tej samej sieci.
- Dostępne są ruch, bieg, lot, odwrót, ataki zwykłe/szaleńcze/szarże, zapasy i uwalnianie, celowanie, przeładowanie, podnoszenie się, konkretne wyposażenie obu rąk, reakcje obronne i gaszenie. Zaklęcia korzystają z istniejącego wykonawcy efektów; wybierane są zaklęcie, preferowany efekt krytyczny i cele.
- V1 ma jawne pojemności dobrane do szybkiego treningu na PC: plansza do **32×32**, do **32 jednostek**, **64 obserwowanych broni**, **32 efektów czasowych** i **32 znanych zaklęć aktywnej jednostki**. To limit jednej bitwy, nie liczby trenowanych ras. Przekroczenie jest błędem, nie cichym obcięciem wiedzy. Zmiana wymiarów wymaga nowego schematu/modelu.
- Pełne liczby statystyk i cech logicznych są kodowane numerycznie; kategorie tekstowe mają stałe kody. Jest to reprezentacja dla sieci, a nie pełne przeszukiwanie drzewa gry.
- Cel to wynik drużyny: +1/-1/0. Opcjonalne nagrody potencjałowe używają różnicy znormalizowanego HP drużyn, gamma=1 i zerowego potencjału terminalnego. Nie ma bonusu za przeładowanie ani ręcznie ustalanej premii za konkretną taktykę. Zachowaj gamma=1 przy tej konfiguracji shaping.
- Mechaniki, które sama gra opisuje jedynie komunikatem do ręcznego rozstrzygnięcia, nadal nie mają symulowanego skutku. AI nie może wyciągnąć wniosków o niezaimplementowanym efekcie. Nie należy traktować tego trenera jako dowodu optymalności całej mechaniki RPG.

## Testy implementacji

```powershell
python Tests/RulesRegression/generate.py
dotnet run --project Tests/RulesRegression/RulesRegression.csproj --verbosity quiet
dotnet run --project Tests/CombatAI/CombatAI.csproj --verbosity quiet
python -m unittest discover -s Tests/CombatAI -p 'test_*.py'
```

Test rzeczywistej sceny i korutyn bez trenera:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.2.7f2\Editor\Unity.exe' `
  -batchmode -nographics -projectPath $PWD `
  -executeMethod Arthalion.AI.Editor.CombatAIEditor.RunSmokeTest `
  --ai-mode Evaluation --ai-random-policy true --ai-episodes 2 `
  --ai-max-rounds 10 --ai-scenarios '@starter' --ai-fast true `
  -logFile Logs/ai_smoke.log
```

Osobno trzeba sprawdzić rzeczywisty trening: aktualizacje optymalizatora, wznowienie checkpointu, eksport ONNX i ewaluację wyeksportowanego modelu. Smoke test losowej polityki tego nie zastępuje.
