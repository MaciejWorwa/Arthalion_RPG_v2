# Wyniki modelu startowego

`episodes.csv`: 50 rzeczywistych walk zamrożonego ONNX przeciw AutoCombat, bez uczenia. 25 scenariuszy `@starter`, po dwie strony modelu, seed 87654321. Wynik: 25 wygranych, 25 przegranych, 0 remisów, 0 błędów technicznych.

Model: `Assets/AI/Models/CombatV1_Starter.onnx`, SHA-256 `3011befa919e3e52225584ce0d48a7e50bffc65380bce9b284d904fca3e79659`.

```powershell
python Tools/AI/summarize.py Tools/AI/benchmarks/starter-v1
```

To pomiar po krótkim treningu integracyjnym, który nie wykazuje przewagi nad AutoCombat. Szczegóły: [WERYFIKACJA.md](../../WERYFIKACJA.md).
