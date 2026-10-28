# Wyniki weryfikacji wdrożenia

28 października 2026 r.; Unity 6000.2.7f2, ML-Agents 4.0.0 / Python trainer 1.1.0, Python 3.10.12, PyTorch 2.2.2 CPU.

## Potwierdzone działanie

- Zbudowano `Builds/AI/ArthalionAI.exe` bez błędów kompilacji.
- Przeszło 198 regresji zasad, 211 sprawdzeń kontraktu AI i 9 testów narzędzi raportujących oraz przenoszenia checkpointów.
- Wykonano rzeczywisty trening PPO na pięciu rasach i 25 scenariuszach. Checkpoint osiągnął **5 192 decyzje**; w CSV zapisano **99 zakończonych epizodów**. W tych zakończonych epizodach nie ma TechnicalAbort.
- Potwierdzono rzeczywiste aktualizacje optymalizatora: pierwszy przebieg zawierał 48 wywołań `TorchPPOOptimizer.update`, nie tylko zbieranie danych lub zmianę liczników.
- Potwierdzono eksport ONNX, zapis checkpointu z optymalizatorem oraz wznowienie od zapisanego numeru kroku.
- Poprawkę obserwacji terminalnej dodatkowo sprawdzono w czterech kolejnych rzeczywistych bitwach; proces zakończył się kodem 0 i nie wystąpiły błędy techniczne.
- Model uruchomiony bezpośrednio przez ML-Agents/Inference Engine w Unity rozegrał dwa epizody: 27 komend, 38 decyzji, 0 błędów technicznych. Test zakończył Play Mode i zwolnił zasoby przed zamknięciem edytora.

## Zamrożony model przeciw AutoCombatManager

Niezależny ewaluator ONNX, bez trenera i optymalizatora, wykonał **50 walk we wszystkich 25 scenariuszach**, z zamianą strony modelu:

| Wynik | Liczba |
| --- | ---: |
| Wygrane modelu | 25 |
| Przegrane modelu | 25 |
| Remisy | 0 |
| Błędy techniczne | 0 |

Win rate wynosi 50%. Orientacyjny przedział Wilsona 95%: 36,6–63,4%; pary scenariuszy nie są w pełni niezależnymi próbami. **Ten pomiar nie wykazuje przewagi nad AutoCombatManager.** Sprawdza działanie całego systemu po krótkim treningu. Do oceny jakości należy kontynuować trening i porównywać kolejne zamrożone checkpointy na większej próbie.

Testowany model: SHA-256 `3011befa919e` (początek skrótu; pełny skrót jest w metadanych). Kopia modelu znajduje się w `Assets/AI/Models/CombatV1_Starter.onnx`.

## Artefakty lokalne na tym komputerze

- Checkpoint i model: `Tools/AI/results/integration-final/`.
- Epizody treningowe: `Tools/AI/metrics/integration-final/worker_5015/episodes.csv`.
- Końcowa ewaluacja 50 walk: `Tools/AI/metrics/integration-final-all-races/worker_6016/episodes.csv`.
- Log budowania: `Logs/ai_build.log`.
- Log natywnego użycia modelu: `Logs/ai_native_inference.log`.

Wyniki treningu, build i środowisko Python są lokalnymi artefaktami ignorowanymi przez Git. Skrypty i konfiguracje do ich odtworzenia są częścią repozytorium. Krótki model demonstracyjny jest przechowywany w Assets. Surowe 50 wyników benchmarku skopiowano do `Tools/AI/benchmarks/starter-v1/episodes.csv`, żeby były dostępne po sklonowaniu repozytorium.

## Sprawdzenie przenoszenia na drugi komputer

- `Tools/AI/checkpoints/StarterV1.zip` zawiera checkpoint 5 192 ze stanem optymalizatora, ONNX, metadane, konfigurację, CSV treningowe i zdarzenia TensorBoard.
- Wykonano eksport i import do nowego katalogu sesji. Rzeczywisty trener zgłosił `Resuming training from step 5192`, kontynuował do **6 210** i wyeksportował kolejny ONNX. W dodatkowych **24 zakończonych walkach** nie było TechnicalAbort. Nie jest to nowy benchmark jakości modelu; paczka startowa nadal zawiera oryginalny krok 5 192.
- Po przebudowaniu playera dwie kontrolne walki zamrożonego modelu z repozytorium zakończyły się bez błędów zgodności lub wykonania.
- Identyfikator reguł jest teraz liczony po normalizacji końców linii, więc checkout Git z CRLF i LF nie blokuje tego samego modelu. Przed aktualizacją metadanych dołączonego modelu potwierdzono zgodność oryginalnego skrótu całego kodu gry; zmienił się sposób wyliczania identyfikatora, nie mechanika ani wagi modelu.
- Testy importu sprawdzają zachowanie optymalizatora i metryk, odbudowę ścieżek, ochronę istniejących sesji, uszkodzone archiwa oraz niedozwolone ścieżki. Narzędzie nie odczytuje struktur pickle podczas importu ani eksportu.
