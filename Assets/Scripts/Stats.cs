using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public enum SizeCategory
{
    Little = 0,    // drobny
    Small = 1,         // mały
    Average = 2,       // średni
    Big = 3,         // duży
    Large = 4       // wielki
}

public class Stats : MonoBehaviour
{
    public int Id;
    public int Overall; // Łączna wartość bojowa jednostki
    public int Exp; // Punkty doświadczenia

    [Header("Imię")]
    public string Name;

    [Header("Rasa")]
    public string Race;

    [Header("Type")]
    public string Type;

    [Header("Rozmiar")]
    public SizeCategory Size; // Rozmiar

    [Header("Nazwy początkowych broni")]
    public List<string> PrimaryWeaponNames = new List<string>();
    public List<PairString> PrimaryWeaponAttributes = new List<PairString>();

    [Header("Cechy")]
    public int S;
    public int K;
    public int Zw;
    public int Zr;
    public int Int;
    public int P;
    public int Ch;
    public int SW;

    [Header("Cechy drugorzędowe")]
    public int Sz;
    public int TempSz;
    public int MaxHealth;
    public int TempHealth;
    public int CriticalWounds; // Ilość Ran Krytycznych
    public int SinPoints; // Punkty Grzechu (istotne dla kapłanów)
    public int TempPL; // Punkty losu aktualne
    public int MaxPL; // Punkty Losu Maksymalne
    public int PB; // Punkty Bohatera
    public int ExtraPoints; // Dodatkowe punkty do rozdania między PL a PB
    public int Initiative; // Inicjatywa
    public int CurrentEncumbrance; // Aktualne obciążenie ekwipunkiem
    public int MaxEncumbrance; // Maksymalny udźwig
    public int ExtraEncumbrance; // Dodatkowe obciążenie za przedmioty niebędące uzbrojeniem

    [Header("Zbroja")]
    public int Armor_head;
    public int Armor_arms;
    public int Armor_torso;
    public int Armor_legs;

    public int ArmorPenaltyZw; // bieżąca kara z pancerza zastosowana do Zw
    public int ArmorPenaltyP;  // bieżąca kara z pancerza zastosowana do P


    [Header("Umiejętności")]
    public int Athletics; // Atletyka
    public int Cool; // Opanowanie
    public int Dodge; // Unik
    public int Endurance; // Odporność
    public int MeleeCombat; // Walka Wręcz
    public int RangedCombat; // Walka Dystansowa
    public int Reflex; // Refleks
    public int Spellcasting; // Rzucanie zaklęć

    public int Pray; // Modlitwa
    public int Channeling; // Splatanie magii
    public int MagicLanguage; // Język magiczny


    [Header("Talenty")]
    public bool AccurateShot; // Celny strzał
    public bool Chosen; //Wybraniec Boży
    public bool CombatMaster; // Wojownik
    public bool Fast; // Szybki
    public bool Fencing; // Szermierka
    public bool Hardy; // Twardziel
    public int Pitiless; // Bezlitosny
    public int Religious; // Pobożny
    public bool Sharpshooter; // Strzelec wyborowy
    public int SurvivalInstinct; // Instynkt Przetrwania

    public string[] Magic = new string[6]; // ścieżki magii ----------------------------- Do wprowadzenia
    public string[] Resistance = new string[5]; // Odporny (50% obrażeń) np. ["Fizyczne", "Ogień"]
    public string[] Slayer = new string[3];
    public string[] Specialist = new string[3]; // null/"" = pusty slot
    public string[] Unaffected = new string[5]; // Niewrażliwy np. ["Fizyczne", "Ogień"]


    [Header("Cechy stworzeń")]
    public bool BlackMagic; // Czarna Magia ----------------------------- Do wprowadzenia
    public int Flight; // Latający
    public bool Hungry; // Żarłoczny
    public int NaturalArmor;
    public int Scary; // Straszny
    public bool Slow; // Powolny
    public bool Stink; // Smród
    public bool Tough; // Wytrzymały
    public bool Undead; // Nieumarły
    public bool Unmeaning; // Bezrozumny

    [Header("Statystyki")]
    public int HighestDamageDealt; // Największe zadane obrażenia
    public int TotalDamageDealt; // Suma zadanych obrażeń
    public int HighestDamageTaken; // Największe otrzymane obrażenia
    public int TotalDamageTaken; // Suma otrzymanych obrażeń
    public int OpponentsKilled; // Zabici przeciwnicy
    public string StrongestDefeatedOpponent; // Najsilniejszy pokonany przeciwnik
    public int StrongestDefeatedOpponentOverall; // Overall najsilniejszego pokonanego przeciwnika
    public int RoundsPlayed; // Suma rozegranych rund
    public int FortunateEvents; // Ilość "Szczęść"
    public int UnfortunateEvents; // Ilość "Pechów"

    public string Notebook; // Notatka

    public List<SpellEffect> ActiveSpellEffects = new List<SpellEffect>();

    public void SetBaseStats()
    {
        CalculateMaxHealth();

        // Rozdzielanie punktów ExtraPoints losowo pomiędzy PP i Resilience
        for (int i = 0; i < ExtraPoints; i++)
        {
            if (UnityEngine.Random.value < 0.5f)
                MaxPL++;
            else
                PB++;
        }
        ExtraPoints = 0;

        TempPL = MaxPL;

        if (Fast) Sz += 2;

        // Aktualizuje udźwig
        MaxEncumbrance = Math.Max(1, 6 + S);

        //Overall = CalculateOverall();
    }

    public void CalculateMaxHealth(bool isSizeChange = false)
    {
        int previousMaxHealth = MaxHealth;

        if (Size == SizeCategory.Little)
            MaxHealth = 1;
        else if (Size == SizeCategory.Small)
            MaxHealth = 10 + S + K;
        else if (Size == SizeCategory.Average)
            MaxHealth = 12 + S + K;
        else if (Size == SizeCategory.Big)
            MaxHealth = 18 + S + K;
        else if (Size == SizeCategory.Large)
            MaxHealth = 2 * (12 + S + K);

        // Uwzględnienie cechy specjalnej Wytrzymały
        if (Tough == true) MaxHealth *= 2;

        if (MaxHealth < 1) MaxHealth = 1;

        if (isSizeChange)
        {
            TempHealth = MaxHealth;
        }
        else
        {
            TempHealth += MaxHealth - previousMaxHealth;
        }

        if (GetComponent<Unit>().Stats != null)
        {
            GetComponent<Unit>().DisplayUnitHealthPoints();
        }
    }

    public void ChangeUnitSize(int newSize)
    {
        if (!Enum.IsDefined(typeof(SizeCategory), newSize)) return; // Sprawdzenie poprawności wartości

        SizeCategory previousSize = Size;
        SizeCategory newSizeCategory = (SizeCategory)newSize; // Konwersja int -> SizeCategory

        if (newSizeCategory == previousSize) return;

        int sizeDifference = newSize - (int)previousSize;

        // Aktualizacja rozmiaru
        Size = newSizeCategory;

        ChangeTokenSize((int)Size);

        // Przeliczenie zdrowia
        CalculateMaxHealth(true);
    }

    public void ChangeTokenSize(int size)
    {
        if (size > 2)
        {
            float tokenSizeModifier = 1f + (size - 2) * 0.25f;
            transform.localScale = new Vector3(tokenSizeModifier, tokenSizeModifier, 1f);
        }
        else if (size < 1)
        {
            transform.localScale = new Vector3(0.8f, 0.8f, 1f);
        }
        else
        {
            transform.localScale = new Vector3(1f, 1f, 1f);
        }
    }

    public int CalculateOverall()
    {
        // --- 1. Ustalenie broni, której jednostka REALNIE użyje ---

        Weapon weapon = null;

        if (InventoryManager.Instance != null)
        {
            weapon = InventoryManager.Instance.ChooseWeaponToAttack(this.gameObject);
        }

        if (weapon == null)
        {
            // Fallback na naturalną broń z komponentu Weapon
            weapon = GetComponent<Weapon>();
        }

        bool isRangedWeapon = weapon != null && (weapon.Type.Contains("ranged") || weapon.Type.Contains("throwing"));
        bool isMeleeWeapon = weapon != null && weapon.Type.Contains("melee");

        // --- 2. Modyfikatory ofensywne (atak) ---

        int meleeMod = Zr + MeleeCombat;
        int rangedMod = Zr + RangedCombat;

        float meleeAttackScore = 0f;
        float rangedAttackScore = 0f;

        if (isMeleeWeapon)
        {
            // 1 punkt różnicy to dużo – mnożnik 8
            meleeAttackScore = meleeMod * 8f;
        }

        if (isRangedWeapon)
        {
            rangedAttackScore = rangedMod * 8f;
        }

        // Jeśli jednostka ma i sensowną walkę wręcz, i dystansową:
        // bierzemy 100% lepszej i 50% gorszej opcji.
        float attackScore = Mathf.Max(meleeAttackScore, rangedAttackScore);
        float secondaryAttack = Mathf.Min(meleeAttackScore, rangedAttackScore);
        attackScore += secondaryAttack * 0.5f;

        // --- 3. Obrażenia z broni (średnia z kości) ---

        float weaponDamageScore = 0f;

        if (weapon != null && weapon.Damage != null && weapon.Damage.Count > 0)
        {
            foreach (int sides in weapon.Damage)
            {
                if (sides <= 0) continue;

                // Średnia z kości kX to (X+1)/2
                float avg = 0.5f * (sides + 1);
                weaponDamageScore += avg;
            }


            // Przybliżenie dodatkowych kości za ROZMIAR + SIŁĘ
            if (weapon.Type.Contains("melee") && Size > SizeCategory.Average && S > 0)
            {
                int s = S;
                if (s == 1 || s == 2) weaponDamageScore += 2.5f;  // k4
                else if (s == 3) weaponDamageScore += 3.5f;  // k6
                else if (s == 4) weaponDamageScore += 4.5f;  // k8
                else if (s == 5) weaponDamageScore += 5.5f;  // k10
                else if (s == 6 || s == 7) weaponDamageScore += 6.5f;  // k12
                else if (s >= 8) weaponDamageScore += 9f;    // 2k8
            }

            // Mnożnik – obrażenia są bardzo istotne, ale nie bardziej niż sam test trafienia
            weaponDamageScore *= 5f;
        }

        // --- 4. Obrona: umiejętności, pancerz i punkty zdrowia ---

        int parryBase = Zr + MeleeCombat + weapon.Defensive; 
        int dodgeBase = Zw + Dodge; 

        // Unik jest trochę lepszy (działa vs dystans, brak kary za rozmiar), więc dajemy mu mały „stały” bonus:
        dodgeBase += 1;

        // Przeliczamy oba na „siłę obrony”
        float parryScore = parryBase * 6f;
        float dodgeScore = dodgeBase * 6f;

        // Finalna obrona: 100% lepszej opcji + 50% gorszej
        float defenseScore = Mathf.Max(parryScore, dodgeScore) + Mathf.Min(parryScore, dodgeScore) * 0.5f;


        int totalArmor =
            Armor_head +
            Armor_torso +
            Armor_arms +
            Armor_legs +
            (NaturalArmor * 4);

        // Każdy punkt pancerza to redukcja obrażeń z wielu trafień – dajemy solidny mnożnik
        float armorScore = totalArmor * 5f;

        // Punkty Zdrowia bazują na S, K i Rozmiarze, więc same w sobie już niosą sporą informację
        float healthScore = MaxHealth * 2f;

        // --- 5. Rozmiar ---

        float sizeScore = 0f;

        switch (Size)
        {
            case SizeCategory.Little:   // drobny
                sizeScore -= 25f;
                break;
            case SizeCategory.Small:    // mały
                sizeScore -= 10f;
                break;
            case SizeCategory.Average:  // średni
                break;
            case SizeCategory.Big:      // duży
                sizeScore += 10f;
                break;
            case SizeCategory.Large:    // wielki
                sizeScore += 25f;  
                break;
        }

        // --- 6. Magia ofensywna (Rzucanie Zaklęć + SW) ---

        float magicScore = 0f;

        if (Spellcasting > 0)
        {
            int magicMod = SW + Spellcasting;
            magicScore = magicMod * 7f;
        }

        // --- 7. Talenty, umiejetności i cechy specjalne istot ---

        float talentScore = 0f;

        // Talenty bojowe:
        if (CombatMaster) talentScore += 30f; // Wojownik
        if (Sharpshooter) talentScore += 30f; // Strzelec wyborowy
        if (AccurateShot) talentScore += 10f; // Celny Strzał
        if (Fencing) talentScore += 10f; // Celny Strzał
        if (Fast) talentScore += 10f; // Szybki                       
        if (Pitiless > 0) talentScore += Pitiless * 6f; // Bezlitosny
        if (SurvivalInstinct > 0) talentScore += SurvivalInstinct * 6f; // Instynkt Przetrwania

        // Twardziel / Wytrzymały mocno wzmacniają przeżywalność
        if (Hardy) healthScore *= 1.25f;  // Twardziel – lepiej znosi krytyki
        if (Tough) healthScore *= 1.5f;   // Wytrzymały – już podwaja PZ, ale jeszcze trochę premiujemy

        //// Nieumarły: odporności, brak wielu stanów – bardzo mocny defensywnie
        //if (Undead)
        //{
        //    defenseScore *= 1.2f;
        //    talentScore += 10f;
        //}

        // Odporność – szczególnie fizyczna
        if (Resistance != null)
        {
            foreach (var res in Resistance)
            {
                if (string.IsNullOrEmpty(res)) continue;

                string r = res.ToLowerInvariant();

                if (r.Contains("fizyczne"))
                {
                    // Prawie całkowita odporność na fizyczne obrażenia – to jest game changer
                    defenseScore *= 1.5f;
                }
                else
                {
                    // Ogień, zimno itd. – wciąż znaczące, ale nie aż tak jak fizyczne
                    talentScore += 5f;
                }
            }
        }

        // Niewrażliwość – szczególnie fizyczna
        if (Unaffected != null)
        {
            foreach (var res in Unaffected)
            {
                if (string.IsNullOrEmpty(res)) continue;

                string r = res.ToLowerInvariant();

                if (r.Contains("fizyczne"))
                {
                    // Prawie całkowita odporność na fizyczne obrażenia – to jest game changer
                    defenseScore *= 2.5f;
                }
                else
                {
                    // Ogień, zimno itd. – wciąż znaczące, ale nie aż tak jak fizyczne
                    talentScore += 10f;
                }
            }
        }

        // Umiejętności
        talentScore += Endurance * 5f; // testy trucizn, wytrzymałość organizmu
        talentScore += Cool * 5f;      // testy strachu, paniki
        talentScore += Reflex * 5f;  // inicjatywa, szybkie reakcje


        if (Scary > 0)
        {
            // typowe wartości: 12, 14, 16… im wyższy próg, tym częściej wrogowie będą spanikowani
            talentScore += Mathf.Max(0, Scary - 8) * 3f;
        }

        // --- 8. Mobilność ---

        // Szybkość daje przewagę pozycyjną, ucieczkę, gonienie – ale to bonus, nie core
        float speedScore = Mathf.Max(Sz, Flight) * 3f;

        // --- 9. Złożenie wszystkiego w jedną wartość ---

        float overallFloat =
            attackScore +
            weaponDamageScore +
            defenseScore +
            armorScore +
            healthScore +
            magicScore +
            sizeScore +
            speedScore +
            talentScore;

        int overall = Mathf.Max(1, Mathf.RoundToInt(overallFloat));

        //Debug.Log($"Overall {Name} = {overall} (Atk {attackScore:F1}, Dmg {weaponDamageScore:F1}, Def {defenseScore:F1}, Armor {armorScore:F1}, HP {healthScore:F1}, Magic {magicScore:F1}, Size {sizeScore:F1}, Speed {speedScore:F1}, Talents and Skills {talentScore:F1})");

        return overall;
    }

    public int GetEffectiveStat(string statName)
    {
        int baseValue = 0;
        // Pobieramy bazową wartość danej statystyki – można odnieść się do właściwego pola
        FieldInfo field = this.GetType().GetField(statName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(int))
        {
            baseValue = (int)field.GetValue(this);
        }

        // Sumujemy wszystkie modyfikatory, które dotyczą danej statystyki
        int modifierSum = ActiveSpellEffects
                            .Where(effect => effect.StatModifiers.ContainsKey(statName))
                            .Sum(effect => effect.StatModifiers[statName]);

        return baseValue + modifierSum;
    }

    public void UpdateSpellEffects()
    {
        for (int i = ActiveSpellEffects.Count - 1; i >= 0; i--)
        {
            SpellEffect effect = ActiveSpellEffects[i];
            effect.RemainingRounds--;

            if (effect.RemainingRounds <= 0)
            {
                // Odwrócenie działania efektu – dla każdej modyfikowanej statystyki odejmujemy wartość buffa.
                foreach (var mod in effect.StatModifiers)
                {
                    Unit affectedUnit = GetComponent<Unit>();

                    // Szukamy pola najpierw w Stats
                    FieldInfo field = this.GetType().GetField(mod.Key, BindingFlags.Public | BindingFlags.Instance);
                    object targetObject = this;

                    // Jeśli nie znaleziono w Stats, próbujemy znaleźć w Unit
                    if (field == null && affectedUnit != null)
                    {
                        field = affectedUnit.GetType().GetField(mod.Key, BindingFlags.Public | BindingFlags.Instance);
                        targetObject = affectedUnit;
                    }

                    if (field == null) continue;

                    if (field.FieldType == typeof(int))
                    {
                        int currentValue = (int)field.GetValue(targetObject);
                        field.SetValue(targetObject, currentValue - mod.Value);
                    }
                    if (field.FieldType == typeof(bool))
                    {
                        // Załóżmy, że w słowniku mod.Value == 1 oznacza, że buff włączył daną cechę
                        // Aby odwrócić, ustawiamy ją na false – oczywiście, jeśli oryginalna wartość była false.
                        // Jeśli mogło być też true – trzeba to odpowiednio przechowywać (np. jako dodatkowe pole w SpellEffect).
                        field.SetValue(targetObject, false);
                    }

                    if (field.Name == "NaturalArmor")
                    {
                        InventoryManager.Instance.CheckForEquippedWeapons();
                    }

                }
                Debug.Log($"Efekt zaklęcia {effect.SpellName} oddziałujący na {Name} zakończył się.");
                ActiveSpellEffects.RemoveAt(i);
            }
        }

        if(Unit.SelectedUnit != null)
        {
            UnitsManager.Instance.UpdateUnitPanel(Unit.SelectedUnit);
        }
    }

    //Sprawdza, czy postać specjalizuje się z danej rzeczy
    public bool HasSpecialist(string skill) => !string.IsNullOrEmpty(skill) && Specialist != null && Specialist.Any(s => s == skill);
    public bool HasSlayer(string skill) => !string.IsNullOrEmpty(skill) && Slayer != null && Slayer.Any(s => s == skill);
    public bool HasMagic(string path) => !string.IsNullOrEmpty(path) && Magic != null && Magic.Any(m => m == path);

    //Zwraca kopię tej klasy
    public Stats Clone()
    {
        return (Stats)this.MemberwiseClone();
    }
}
