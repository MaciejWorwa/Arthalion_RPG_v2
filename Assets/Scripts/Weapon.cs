using System.Collections.Generic;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    public int Id;
    public WeaponBaseStats BaseWeaponStats; // Przechowywanie bazowych statystyk broni

    [Header("Nazwa")]
    public string Name;

    [Header("Typ")]
    public string[] Type;
    public bool TwoHanded;
    public bool NaturalWeapon;

    [Header("Jakość")]
    public string Quality;

    [Header("Uszkodzenie")]
    public bool Broken; // Uszkodzenie broni

    [Header("Obrażenia (np. 4 i 6 to k4 + k6) lub uszkodzenie pancerza")]
    public List<int> Damage = new List<int> { 0 };

    [Header("Zasięg")]
    public float AttackRange;

    [Header("Typ amunicji")]
    public string AmmoType = "Brak"; // Rodzaj amunicji

    [Header("Czas przeładowania")]
    public int ReloadTime;
    public int ReloadLeft;

    [Header("Wymagania")]
    public int S;
    public int Zr;

    [Header("Kary")]
    public int Zw;
    public int P;

    [Header("Cechy broni")]
    public int Defensive; // Parująca
    public bool Entangle; //Unieruchamiająca
    public bool Fast; // Szybka
    public bool Penetrating; // Przebijająca
    public bool Pummel; // Ogłuszająca
    public bool Slow; // Powolna
    public bool Magical; // Magiczna
    public int Poisonous; // Zatruta, np. kły jadowe Hasai

    [Header("Cechy pancerza")]
    public int Armor;

    public Dictionary<int, int> WeaponsWithReloadLeft = new Dictionary<int, int>(); // słownik zawierający wszystkie posiadane przez postać bronie wraz z ich ReloadLeft

    // Funkcja pomocnicza do zapisywania bazowych cech broni dystansowych, przed uwzględnieniem typu amunicji
    public void SetBaseWeaponStats()
    {
        // Zapisujemy bazowe statystyki przy uruchomieniu
        BaseWeaponStats = new WeaponBaseStats
        {
            Damage = new List<int>(this.Damage),
            AttackRange = this.AttackRange,
            ReloadTime = this.ReloadTime,
            Penetrating = this.Penetrating,
            Pummel = this.Pummel,
        };
    }

    public void ResetWeapon()
    {
        Id = 0;
        Name = "Pięści";
        Type = new string[] { "melee", "natural-weapon" };
        NaturalWeapon = true;
        TwoHanded = false;
        Quality = "Zwykła";
        Damage = new List<int> { 4 }; ;
        AttackRange = 1.5f;
        AmmoType = "Brak";
        ReloadTime = 0;
        ReloadLeft = 0;
        Broken = false;

        S = 0;
        Zr = 0;
        Zw = 0;
        P = 0;

        Defensive = 0;
        Fast = false;
        Penetrating = false;
        Pummel = false;
    }
}

[System.Serializable]
public class WeaponBaseStats
{
    public List<int> Damage = new List<int> { 0 };
    public float AttackRange;
    public int ReloadTime;
    public bool Penetrating;
    public bool Pummel;
}

