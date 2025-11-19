using System;
using System.Collections.Generic;

public class Ammo
{
    public string Name; // Nazwa amunicji

    // Lista efektów, które ta amunicja zmienia
    public float? AttackRange = null;
    public float? AttackRangeMultiplier = null; // jeśli nie null, modyfikuje zasięg mnożąc go przez podaną wartość
    public int? ReloadTime = null; // Czas przeładowania
    public bool? Penetrating = null; // Przebijający
    public bool? Pummel = null; // Ogłuszający

    // Konstruktor pozwalający na ustawienie efektów
    public Ammo(string name, int? damage = null, float? attackRange = null, int? reloadTime = null, bool? penetrating = null, bool? pummel = null, float? attackRangeMultiplier = null)
    {
        Name = name;
        AttackRange = attackRange;
        AttackRangeMultiplier = attackRangeMultiplier;
        ReloadTime = reloadTime;
        Penetrating = penetrating;
        Pummel = pummel;
    }

    public static readonly Dictionary<string, Ammo> Ammos = new Dictionary<string, Ammo>
    {
        { "Brak", new Ammo("Brak") },
        { "Bełt", new Ammo("Bełt") },
        { "Improwizowany pocisk", new Ammo("Zaostrzony patyk", damage: -2,  attackRangeMultiplier: 0.5f) },
        { "Pocisk i proch", new Ammo("Pocisk i proch") },
        { "Pocisk kamienny", new Ammo("Pocisk kamienny")},
        { "Strzała", new Ammo("Strzała") },
        { "Strzała przebijająca", new Ammo("Strzała", penetrating: true)},
        { "Strzałka", new Ammo("Strzała") }
    };

    internal static bool TryGetValue(string ammoType, out Ammo effect)
    {
        return Ammos.TryGetValue(ammoType, out effect);
    }
}
