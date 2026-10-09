# Regresje zasad i 12 naprawionych błędów

Uruchom z katalogu głównego repozytorium; wymagane Python 3 i .NET SDK 8:

```powershell
python Tests/RulesRegression/generate.py
dotnet run --project Tests/RulesRegression/RulesRegression.csproj --verbosity quiet
```

Generator pobiera aktualny `DiceRollManager`, `SpellEffect` i pełne metody pozostałych menedżerów bezpośrednio ze skryptów gry. Nie przechowujemy drugiej implementacji reguł. `Stubs.cs` zastępuje zależności Unity, UI i wywołania spoza zakresu sprawdzenia. Korutyny są wznawiane ręcznie, co pozwala odtworzyć kolizje ręcznych rzutów. Generator sprawdza też powiązania źródłowego panelu decyzji w scenie i pola zapisu nowych stanów.

`Program.cs` sprawdza odporności, Zatrucie, Bezrozumnego, Zmęczenie, Podpalenie i Pecha oraz wszystkie 12 problemów audytu. Testy zapisu korzystają z rzeczywistego `SpellEffectData`; serializacja pól jest wykonywana przez `System.Text.Json`. Testy ruchu podstawiają wynik wyszukiwania ścieżki, a testy zaklęć podstawiają collidery zwrócone przez fizykę. Nie testują renderowania, fizyki ani harmonogramu Unity w Play Mode.

Plik projektu `.csproj` i kopie metod są generowane lokalnie. Kod testów leży poza `Assets`, więc Unity nie dołącza go do gry. Pełny projekt gry należy dodatkowo skompilować w Unity albo przez MSBuild.
