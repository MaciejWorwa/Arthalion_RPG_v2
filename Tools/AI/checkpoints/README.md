# Przenośny checkpoint startowy

`StarterV1.zip` jest eksportem sesji `integration-final` z 28 października 2026 r. Zawiera ostatni checkpoint PPO **5 192**, stan optymalizatora, model ONNX, metadane, konfigurację, 99 wyników treningowych i zdarzenia TensorBoard. Starsze checkpointy i zainstalowane biblioteki nie są dołączane.

SHA-256 paczki: `024dfe4af2053826fe19984729d00249d73062e643999a7773f7786d918d4fd2`.

```powershell
python Tools/AI/checkpoint.py import --bundle Tools/AI/checkpoints/StarterV1.zip --run-id moje-ai-01
.\Tools\AI\start-training.ps1 -RunId moje-ai-01 -Resume -Minutes 60
```

Przed treningiem przygotuj Unity, player i środowisko Python zgodnie z [instrukcją](../../../INSTRUKCJA_TRENOWANIA_AI.md). Paczka nie oznacza zakończonego treningu: model osiągnął 25 wygranych na 50 walk przeciw AutoCombat w teście integracyjnym. Nie wykazano przewagi taktycznej. Weryfikację przenoszenia i wyniki opisano w [WERYFIKACJA.md](../WERYFIKACJA.md).
