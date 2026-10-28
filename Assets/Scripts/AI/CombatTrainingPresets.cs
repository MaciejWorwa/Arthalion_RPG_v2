using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Arthalion.AI
{
    public static class CombatTrainingPresets
    {
        public static readonly string[] Races = { "Człowiek", "Goblin", "Morgras", "Drimhar", "Hasai" };
        private static readonly string[] Variants = { "melee", "ranged", "mixed", "walls", "team" };
        public static string[] Names => Races.SelectMany((race, r) => Variants.Select(v => $"AI-v1-starter-{r}-{v}")).ToArray();

        // Build saved fixtures using the game's own unit/stat/equipment constructors.
        // Existing saves are never overwritten. @starter is an explicit sandbox option.
        public static IEnumerator EnsureCreated()
        {
            if (Names.All(n => Directory.Exists(Path.Combine(Application.persistentDataPath, n)))) yield break;
            StatsData[] races = JsonHelper.FromJson<StatsData>(Resources.Load<TextAsset>("units").text);
            WeaponData[] weapons = JsonHelper.FromJson<WeaponData>(Resources.Load<TextAsset>("weapons").text);
            bool savedList = UnitsManager.Instance.IsSavedUnitsManaging;
            UnitsManager.Instance.IsSavedUnitsManaging = false;
            UnityEngine.Random.InitState(76321);
            foreach (string name in Names)
            {
                if (Directory.Exists(Path.Combine(Application.persistentDataPath, name))) continue;
                int r = int.Parse(name.Split('-')[3]);
                string variant = name.Split('-')[4];
                RoundsManager.Instance.ResetRoundFlowState(true);
                Unit.SelectedUnit = Unit.LastSelectedUnit = null;
                foreach (Unit old in UnitsManager.Instance.AllUnits.Where(u => u != null).ToArray()) UnityEngine.Object.Destroy(old.gameObject);
                UnitsManager.Instance.AllUnits.Clear(); InitiativeQueueManager.Instance.InitiativeQueue.Clear();
                if (MapEditor.Instance != null)
                    foreach (GameObject element in MapEditor.Instance.AllElements.Where(e => e != null).ToArray()) UnityEngine.Object.Destroy(element);
                if (MapEditor.Instance != null) MapEditor.Instance.AllElements.Clear();
                GridManager.Width = 8; GridManager.Height = 8; GridManager.Instance.GenerateGrid();
                yield return null;
                if (variant == "walls") for (int y = 1; y < 7; y++) if (y != 3) GridManager.Instance.SetTileState(4, y, false);
                int perTeam = variant == "team" ? 2 : 1;
                SaveAndLoadManager.Instance.IsLoading = true;
                for (int side = 0; side < 2; side++) for (int i = 0; i < perTeam; i++)
                {
                    string race = Races[(r + i) % Races.Length];
                    int raceId = Array.FindIndex(races, data => data.Race == race) + 1;
                    if (raceId == 0) throw new InvalidOperationException("Starter race missing: " + race);
                    Vector2 position = (Vector2)GridManager.Instance.transform.position + new Vector2(side == 0 ? 1 : 6, 2 + i * 3);
                    GameObject go = UnitsManager.Instance.CreateUnit(raceId, "AI starter", position);
                    if (go == null) throw new InvalidOperationException("Could not create starter unit.");
                    Unit unit = go.GetComponent<Unit>(); unit.Stats = go.GetComponent<Stats>();
                    Unit.SelectedUnit = go;
                    go.tag = side == 0 ? "PlayerUnit" : "EnemyUnit";
                    unit.Stats.Name = $"AI {race} {side} {i}";
                    unit.Stats.SetBaseStats(); unit.Stats.TempHealth = unit.Stats.MaxHealth;
                    unit.Stats.Initiative = DiceRollManager.Instance.TestSkill(unit.Stats, "inicjatywę", null, "Reflex")[2];
                    unit.Stats.ChangeTokenSize((int)unit.Stats.Size);
                    var equipment = new List<string>();
                    if (variant != "ranged") equipment.AddRange(new[] { "Miecz", "Włócznia" });
                    if (variant != "melee") equipment.AddRange(new[] { "Łuk", "Kusza" });
                    foreach (string weaponName in equipment)
                    {
                        int id = Array.FindIndex(weapons, data => data.Name == weaponName);
                        if (id < 0) throw new InvalidOperationException("Starter weapon missing: " + weaponName);
                        WeaponData data = JsonUtility.FromJson<WeaponData>(JsonUtility.ToJson(weapons[id]));
                        data.Id = id + 1;
                        if (data.Type.Contains("ranged")) data.AmmoType = weaponName == "Kusza" ? "Bełt" : "Strzała";
                        InventoryManager.Instance.AddWeaponToInventory(data, go, false, false);
                    }
                    Weapon first = unit.GetComponent<Inventory>().AllWeapons.FirstOrDefault(w => CombatActionRules.CanEquip(unit, w));
                    if (first != null) InventoryManager.Instance.EquipForAI(unit, first, 0, false);
                    InventoryManager.Instance.CheckForEquippedWeapons(unit, false);
                }
                SaveAndLoadManager.Instance.IsLoading = false;
                yield return null;
                GridManager.Instance.CheckTileOccupancy();
                SaveAndLoadManager.Instance.SaveGame(name);
            }
            UnitsManager.Instance.IsSavedUnitsManaging = savedList;
            SaveAndLoadManager.Instance.IsLoading = false;
        }
    }
}
