using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arthalion.AI
{
    public sealed class CombatCommand
    {
        public CombatActionKind Kind;
        public Unit Target;
        public Tile Destination;
        public Weapon Equipment;
        public SpellData Spell;
        public string CriticalChoice = "damage";
        public readonly List<GameObject> SpellTargets = new List<GameObject>();
    }

    // Shared by the policy mask and the command executor. No tactical scores or preferences.
    public static class CombatActionRules
    {
        public static bool IsPresent(Unit unit)
        {
            return unit != null && unit.gameObject.activeInHierarchy && !unit.HasRider;
        }

        public static bool CanAct(Unit unit)
        {
            return IsPresent(unit) && !unit.IsTurnFinished && !unit.Unconscious;
        }

        public static int BaseSpeed(Unit unit)
        {
            int speed = unit.Stats.Sz;
            if (unit.IsMounted && unit.Mount != null)
                speed = unit.Mount.Stats.Flight != 0 ? unit.Mount.Stats.Flight : unit.Mount.Stats.Sz;
            return speed;
        }

        public static bool CanEquip(Unit unit, Weapon weapon)
        {
            return weapon != null && !weapon.Broken && weapon.Armor == 0 &&
                weapon.Type != null && !weapon.Type.Any(t => t == "head" || t == "arms" || t == "torso" || t == "legs") &&
                unit.Stats.S >= weapon.S && unit.Stats.Zr >= weapon.Zr;
        }

        public static bool CanAttack(Unit unit, Unit target, CombatActionKind kind, Dictionary<Vector2, int> paths = null)
        {
            if (!CanAct(unit) || !unit.CanDoAction || !IsPresent(target) || target == unit || target.CompareTag(unit.tag)) return false;
            Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
            float distance = CombatManager.Instance.CalculateDistance(unit.gameObject, target.gameObject);
            if (kind == CombatActionKind.Grapple)
                return !unit.Grappled && unit.GrappledUnitId == 0 && !target.Grappled &&
                    target.Stats.Size - unit.Stats.Size <= 1 && distance <= 1.5f;
            if (unit.Grappled || unit.GrappledUnitId != 0) return false;
            bool melee = weapon.Type.Contains("melee");
            if (kind == CombatActionKind.Charge)
            {
                if (!melee || !unit.CanMove || unit.Prone || unit.Entangled || unit.Stats.Slow || distance <= weapon.AttackRange) return false;
                paths = paths ?? ReachableDistances(unit);
                int best = AdjacentPositions(target.transform.position).Where(paths.ContainsKey)
                    .Select(p => paths[p]).DefaultIfEmpty(int.MaxValue).Min();
                int speed = unit.Stats.Flight != 0 ? unit.Stats.Flight : BaseSpeed(unit) * 2;
                return best >= unit.Stats.Sz / 2f && best <= speed;
            }
            if (kind == CombatActionKind.AllOutAttack && !melee) return false;
            if (distance > weapon.AttackRange) return false;
            if (!melee && weapon.Type.Contains("ranged"))
                return CombatManager.Instance.ValidateRangedAttack(unit, target, weapon, distance, false);
            return true;
        }

        public static IEnumerable<Vector2> AdjacentPositions(Vector2 position)
        {
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                if (x != 0 || y != 0) yield return position + new Vector2(x, y);
        }

        public static Dictionary<Vector2, int> ReachableDistances(Unit unit, bool flying = false)
        {
            return MovementManager.Instance.GetReachableDistances(unit, flying);
        }

        public static Dictionary<int, CombatCommand> BuildParameters(Unit unit, CombatActionKind kind,
            IList<Unit> units, IList<SpellData> spells, HashSet<string> visitedLoadouts)
        {
            var commands = new Dictionary<int, CombatCommand>();
            if (!CanAct(unit)) return commands;
            bool move = unit.CanMove, action = unit.CanDoAction;
            if (kind == CombatActionKind.Move || kind == CombatActionKind.Run || kind == CombatActionKind.Retreat || kind == CombatActionKind.Fly)
            {
                bool run = kind == CombatActionKind.Run, fly = kind == CombatActionKind.Fly;
                if (fly && !move) return commands;
                if ((!move && !run && !fly) || ((run || fly || kind == CombatActionKind.Retreat) && !action)) return commands;
                if ((run || fly) && (unit.Prone || unit.Entangled || unit.Grappled)) return commands;
                if (run && unit.Stats.Slow || fly && unit.Stats.Flight <= 0) return commands;
                int range = fly ? unit.Stats.Flight : BaseSpeed(unit) / (unit.Prone ? 2 : 1);
                foreach (var pair in ReachableDistances(unit, fly))
                {
                    // Run's distance is determined by its skill roll during execution. The same
                    // engine rule as AutoCombat permits a distant destination and clips the path.
                    if (pair.Value <= 0 || (!run && pair.Value > range)) continue;
                    Tile tile = GridManager.Instance.GetTileAtWorldPosition(pair.Key);
                    if (tile == null || tile.IsOccupied || !tile.gameObject.activeInHierarchy) continue;
                    int x = Mathf.RoundToInt(pair.Key.x - GridManager.Instance.transform.position.x);
                    int y = Mathf.RoundToInt(pair.Key.y - GridManager.Instance.transform.position.y);
                    if (x < 0 || y < 0 || x >= CombatLearningContract.BoardWidth || y >= CombatLearningContract.BoardHeight) continue;
                    commands.Add(y * CombatLearningContract.BoardWidth + x,
                        new CombatCommand { Kind = kind, Destination = tile });
                }
                return commands;
            }
            if (kind == CombatActionKind.StandardAttack || kind == CombatActionKind.AllOutAttack || kind == CombatActionKind.Charge || kind == CombatActionKind.Grapple)
            {
                var paths = kind == CombatActionKind.Charge ? ReachableDistances(unit) : null;
                for (int i = 0; i < units.Count; i++) if (CanAttack(unit, units[i], kind, paths))
                    commands.Add(i, new CombatCommand { Kind = kind, Target = units[i] });
                return commands;
            }
            if (kind == CombatActionKind.GrappleStrike || kind == CombatActionKind.ReleaseGrapple ||
                kind == CombatActionKind.EscapeGrapple || kind == CombatActionKind.EscapeEntanglement)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    Unit other = units[i];
                    if (!IsPresent(other)) continue;
                    bool allowedLink = (kind == CombatActionKind.GrappleStrike || kind == CombatActionKind.ReleaseGrapple) && unit.GrappledUnitId == other.UnitId ||
                        kind == CombatActionKind.EscapeGrapple && other.GrappledUnitId == unit.UnitId && unit.Grappled ||
                        kind == CombatActionKind.EscapeEntanglement && other.EntangledUnitId == unit.UnitId && unit.Entangled && !unit.Grappled;
                    if (allowedLink && (action || kind == CombatActionKind.ReleaseGrapple))
                        commands.Add(i, new CombatCommand { Kind = kind, Target = other });
                }
                return commands;
            }
            if (kind == CombatActionKind.EquipMainHand || kind == CombatActionKind.EquipOffHand)
            {
                Inventory inventory = unit.GetComponent<Inventory>();
                int hand = kind == CombatActionKind.EquipMainHand ? 0 : 1;
                for (int i = 0; i < inventory.AllWeapons.Count; i++)
                {
                    Weapon weapon = inventory.AllWeapons[i];
                    if (!CanEquip(unit, weapon) || inventory.EquippedWeapons[hand] == weapon) continue;
                    string key = LoadoutKey(unit, weapon, hand);
                    if (visitedLoadouts.Contains(key)) continue;
                    commands.Add(i, new CombatCommand { Kind = kind, Equipment = weapon });
                }
                return commands;
            }
            if (kind == CombatActionKind.CastSpell)
            {
                if (!action || !unit.CanCastSpell || unit.Stats.Spellcasting <= 0 ||
                    !DiceRollManager.CanPerformTest(unit.Stats, "SW", "Spellcasting")) return commands;
                for (int i = 0; i < spells.Count; i++)
                    if (KnowsSpell(unit, spells[i]) && SpellTargetChoices(unit, spells[i], units, null).Count > 0)
                        commands.Add(i, new CombatCommand { Kind = kind, Spell = spells[i] });
                return commands;
            }
            Weapon current = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
            bool allowed = kind == CombatActionKind.FinishTurn ||
                kind == CombatActionKind.Aim && action && unit.AimingBonus == 0 && unit.Stats.P > 0 ||
                kind == CombatActionKind.Reload && action && current.ReloadLeft > 0 ||
                kind == CombatActionKind.StandUp && move && unit.Prone ||
                kind == CombatActionKind.SelectMainHand && InventoryManager.Instance.SelectedHand != 0 && unit.GetComponent<Inventory>().EquippedWeapons[0] != null && !visitedLoadouts.Contains(LoadoutKey(unit, selectedHand: 0)) ||
                kind == CombatActionKind.SelectOffHand && InventoryManager.Instance.SelectedHand != 1 && unit.GetComponent<Inventory>().EquippedWeapons[1] != null && !visitedLoadouts.Contains(LoadoutKey(unit, selectedHand: 1));
            if (allowed) commands.Add(0, new CombatCommand { Kind = kind });
            return commands;
        }

        public static bool KnowsSpell(Unit unit, SpellData spell)
        {
            string path = spell.Arcane switch {
                "Ścieżka Lodu" => "Ice", "Ścieżka Ognia" => "Fire", "Ścieżka Powietrza" => "Air",
                "Ścieżka Wody" => "Water", "Ścieżka Ziemi" => "Earth", "Ścieżka Śmierci" => "Death",
                _ => spell.Arcane
            };
            return unit.Stats.HasMagic(path) || unit.GetComponent<Spell>()?.Name == spell.Name;
        }

        public static Dictionary<int, GameObject> SpellTargetChoices(Unit unit, SpellData spell, IList<Unit> units, ICollection<GameObject> selected)
        {
            var result = new Dictionary<int, GameObject>();
            for (int i = 0; i < units.Count; i++)
            {
                Unit target = units[i];
                if (!IsPresent(target) || selected != null && selected.Contains(target.gameObject)) continue;
                if (spell.Type.Contains("offensive") && target == unit || spell.Type.Contains("self-only") && target != unit) continue;
                if (Vector2.Distance(unit.transform.position, target.transform.position) > spell.Range) continue;
                result.Add(i + 1, target.gameObject); // 0 means finish target selection.
            }
            return result;
        }

        public static string LoadoutKey(Unit unit, Weapon candidate = null, int hand = -1, int selectedHand = -1)
        {
            var equipped = unit.GetComponent<Inventory>().EquippedWeapons;
            Weapon main = equipped[0], off = equipped[1];
            if (candidate != null)
            {
                if (main != null && main.TwoHanded) main = off = null;
                if (candidate == main) main = null;
                if (candidate == off) off = null;
                if (candidate.TwoHanded) main = off = candidate;
                else if (hand == 0) main = candidate; else off = candidate;
            }
            return (main != null ? main.GetInstanceID() : 0) + ":" + (off != null ? off.GetInstanceID() : 0) + ":" +
                (selectedHand < 0 ? InventoryManager.Instance.SelectedHand : selectedHand);
        }

        public static IEnumerator Execute(Unit unit, CombatCommand command)
        {
            if (unit == null) yield break;
            if (Unit.SelectedUnit != unit.gameObject) unit.SelectUnit();
            switch (command.Kind)
            {
                case CombatActionKind.FinishTurn:
                    RoundsManager.Instance.FinishTurn(unit, false); break;
                case CombatActionKind.Move:
                case CombatActionKind.Run:
                case CombatActionKind.Retreat:
                case CombatActionKind.Fly:
                    if (command.Kind == CombatActionKind.Run) MovementManager.Instance.Run();
                    else if (command.Kind == CombatActionKind.Fly) MovementManager.Instance.Flight();
                    else yield return MovementManager.Instance.UpdateMovementRange(1, unit);
                    if (command.Kind == CombatActionKind.Retreat) MovementManager.Instance.Retreat(true);
                    MovementManager.Instance.MoveSelectedUnit(command.Destination.gameObject, unit.gameObject);
                    break;
                case CombatActionKind.StandardAttack:
                case CombatActionKind.AllOutAttack:
                case CombatActionKind.Charge:
                case CombatActionKind.GrappleStrike:
                    string attack = command.Kind == CombatActionKind.GrappleStrike ? "Grappling" : command.Kind.ToString();
                    CombatManager.Instance.ChangeAttackType(attack);
                    CombatManager.Instance.Attack(unit, command.Target); break;
                case CombatActionKind.Grapple:
                case CombatActionKind.EscapeGrapple:
                case CombatActionKind.ReleaseGrapple:
                    yield return CombatManager.Instance.GrapplingForAI(unit, command.Target,
                        command.Kind == CombatActionKind.EscapeGrapple ? "escape" : command.Kind == CombatActionKind.ReleaseGrapple ? "release" : "");
                    break;
                case CombatActionKind.EscapeEntanglement:
                    yield return CombatManager.Instance.EscapeFromEntanglement(command.Target.Stats, unit.Stats);
                    break;
                case CombatActionKind.Aim: CombatManager.Instance.SetAim(); break;
                case CombatActionKind.Reload: CombatManager.Instance.Reload(); break;
                case CombatActionKind.StandUp: StatesManager.Instance.Prone(unit, false); break;
                case CombatActionKind.EquipMainHand:
                case CombatActionKind.EquipOffHand:
                    InventoryManager.Instance.EquipForAI(unit, command.Equipment, command.Kind == CombatActionKind.EquipMainHand ? 0 : 1);
                    break;
                case CombatActionKind.SelectMainHand: InventoryManager.Instance.SelectedHand = unit.AISelectedHand = 0; break;
                case CombatActionKind.SelectOffHand: InventoryManager.Instance.SelectedHand = unit.AISelectedHand = 1; break;
                case CombatActionKind.CastSpell:
                    yield return MagicManager.Instance.CastSpellForAI(unit, command.Spell, command.SpellTargets, command.CriticalChoice);
                    break;
            }
            Physics2D.SyncTransforms();
        }
    }
}
