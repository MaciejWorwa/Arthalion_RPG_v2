using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spell : MonoBehaviour
{
    public int Id;
    public string Name;
    public string Arcane;
    public string[] Type; // np. offensive, buff, armor-ignoring, no-damage
    public int CastingNumber; // trudnoœæ zaklêcia
    public float Range; // zasiêg
    public int[] Strength; // si³a zaklêcia
    public int AreaSize; // obszar dzia³ania
    public int Duration; // czas trwania zaklêcia
    public int Targets; // iloœæ celów

    //public bool SaveTestRequiring; // okreœla, czy zaklêcie powoduje koniecznoœæ wykonania testu obronnego

    public string DamageType; // Rodzaj obra¿eñ, np. ice, poison, physical
    public string SaveAttribute; // Cecha, która jest testowana u celu zaklêcia, aby siê przed nim obroniæ
    public string SaveSkill; // Umiejêtnoœæ, która jest testowana u celu zaklêcia, aby siê przed nim obroniæ
    public int SaveDifficulty; // Trudnoœæ testu obronnego

    //public int AttributeValue; // okreœla o ile s¹ zmieniane cechy opisane w tabeli Attribute
    //public string[] Attribute; // okreœla cechê, jaka jest testowana podczas próby oparcia siê zaklêciu lub cechê na któr¹ wp³ywa zaklêcie (np. podnosi j¹ lub obni¿a). Czasami jest to wiêcej cech, np. Pancerz Etery wp³ywa na ka¿d¹ z lokalizacji
    //public Dictionary<string, int> Attributes = new(); // <-- zamiast Attribute + AttributeValue

    public List<AttributePair> Attributes;  // U¿ywamy List<AttributePair>, nie s³ownika.


    public bool ArmourIgnoring; // ignoruj¹cy zbrojê
    public bool MetalArmourIgnoring; // ignoruj¹cy zbrojê
    //public bool Stunning;  // og³uszaj¹cy
    //public bool Paralyzing; // wprowadzaj¹cy w stan bezbronnoœci
}
