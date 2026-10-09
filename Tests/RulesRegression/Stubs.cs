using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.Mathematics { }
namespace UnityEngine {
    public class SerializeField : Attribute { }
    public class GraphicsBuffer { }
    public class Coroutine { public IEnumerator Routine; public Coroutine(IEnumerator r) { Routine = r; } }
    public class Component {
        public GameObject gameObject;
        public string name => gameObject.name;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T:class => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool inactive=false) where T:class => gameObject.GetComponent<T>();
        public bool CompareTag(string tag) => gameObject.CompareTag(tag);
    }
    public class MonoBehaviour : Component {
        public int CoroutineCalls;
        public List<Coroutine> Started=new List<Coroutine>();
        public Coroutine StartCoroutine(IEnumerator r) { CoroutineCalls++; var c=new Coroutine(r); Started.Add(c); return c; }
        protected void Destroy(object obj) { }
    }
    public class Transform { public Vector3 position; public void SetAsLastSibling() { } }
    public struct Vector2 {
        public float x,y;
        public Vector2(float x,float y){this.x=x;this.y=y;}
        public static implicit operator Vector2(Vector3 v)=>new Vector2(v.x,v.y);
        public static implicit operator Vector3(Vector2 v)=>new Vector3(v.x,v.y);
        public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x && a.y==b.y;
        public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b);
        public override bool Equals(object o)=>o is Vector2 v && this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y);
    }
    public struct Vector3 {
        public float x,y;
        public Vector3(float x,float y){this.x=x;this.y=y;}
        public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y));
    }
    public class Collider2D:Component {}
    public static class Physics2D {
        public static List<float> Radii=new List<float>();
        public static Dictionary<Vector2,Collider2D[]> Targets=new Dictionary<Vector2,Collider2D[]>();
        public static Collider2D[] OverlapCircleAll(Vector2 position,float radius){Radii.Add(radius);return Targets[position];}
    }
    public class GameObject {
        public string name, tag = "EnemyUnit";
        public bool activeSelf=true, activeInHierarchy=true;
        public Transform transform = new Transform();
        public static List<GameObject> All = new List<GameObject>();
        public Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public GameObject(string name="object") { this.name=name; All.Add(this); }
        public T Add<T>(T component) where T:Component { component.gameObject=this; Components[typeof(T)]=component; return component; }
        public T GetComponent<T>() where T:class => Components.TryGetValue(typeof(T),out var c) ? c as T : null;
        public T GetComponentInChildren<T>(bool inactive=false) where T:class => GetComponent<T>();
        public bool CompareTag(string t) => tag==t;
        public void SetActive(bool b) { activeSelf=activeInHierarchy=b; }
        public static GameObject[] FindGameObjectsWithTag(string t) => All.Where(g=>g.tag==t && g.activeSelf).ToArray();
    }
    public enum KeyCode { Escape,Return,KeypadEnter }
    public struct Color { public static Color green=>new Color(); }
    public static class Input { public static bool GetKeyDown(KeyCode k) => false; }
    public class WaitUntil { public Func<bool> Predicate; public WaitUntil(Func<bool> p) { Predicate=p; } }
    public static class Debug { public static void Log(object x) { } public static void LogError(object x) { } }
    public static class Random { public static int Value=5; public static int Range(int min, int max)=>Math.Clamp(Value,min,max-1); }
    public static class Mathf {
        public static int Clamp(int a,int b,int c)=>Math.Clamp(a,b,c);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static int CeilToInt(float a)=>(int)Math.Ceiling(a);
        public static int RoundToInt(float a)=>(int)Math.Round(a);
    }
}
namespace UnityEngine.UI {
    public class Slider:Component { public float value; public void SetValueWithoutNotify(float f){value=f;} }
    public class Toggle:Component { public bool isOn; }
    public class Image:Component { public Color color; }
    public class Button:Component { public ButtonClickedEvent onClick=new ButtonClickedEvent(); public class ButtonClickedEvent { public Action Action; public void AddListener(Action a){Action+=a;} public void Invoke()=>Action?.Invoke(); } }
}
namespace TMPro {
    public class TMP_Text:Component { public string text; }
    public class TMP_InputField:Component { public string text; public void SetTextWithoutNotify(string t){text=t;} }
}
public class InputFieldFilter:Component { public void SetBool(string n,bool b){} }
public partial class Stats:Component {
    public string Name="Test";
    public int S,K,Zw,Zr,Int,P,Ch,SW,Athletics,Cool,Dodge,Endurance,MeleeCombat,RangedCombat,Reflex,Spellcasting,Pray,Channeling,MagicLanguage;
    public bool Unmeaning,Undead,Hardy,Chosen;
    public int Sz=5,TempSz=5;
    public int TempHealth=20,MaxHealth=20,FortunateEvents,UnfortunateEvents,TotalDamageDealt,HighestDamageDealt,TotalDamageTaken,HighestDamageTaken;
    public string[] Resistance=new string[0],Unaffected=new string[0];
    public bool Specialist;
    public bool HasSpecialist(string s)=>Specialist;
    public bool HasResistance(string t)=>HasDamageType(Resistance,t);
    public bool IsUnaffectedBy(string t)=>HasDamageType(Unaffected,t);
    public List<SpellEffect> ActiveSpellEffects=new List<SpellEffect>();
}
class Unit:Component {
    public static GameObject SelectedUnit;
    public Stats Stats;
    public Unit Mount;
    public bool Scared,Blinded,Ablaze,Unconscious,Petrified,Prone,Entangled,Grappled,CanDoAction=true,CanMove=true,HasUsedOpportunityAttack,IsTurnFinished,IsRunning,IsRetreating,CanCastSpell;
    public int Fatiqued,Poison,TurnStartedInRound,EntangledUnitId,UnitId;
    public Stats LastAttackerStats;
    public void DisplayUnitHealthPoints(){}
}
class Weapon:Component {
    public int Id=1,Poisonous,ReloadLeft;
    public float AttackRange=10;
    public string Name="Sword",Quality="Zwykła";
    public bool Magical,Broken;
    public string[] Type=new string[0];
}
class Inventory:Component {
    public List<Weapon> EquippedArmors=new List<Weapon>();
    public Dictionary<string,List<Weapon>> ArmorByLocation=new Dictionary<string,List<Weapon>>();
}
class UnitsManager {
    public static UnitsManager Instance=new UnitsManager();
    public static Dictionary<string,string> ResistancePlToEn=new Dictionary<string,string>{{"Zatrucie","Poison"},{"Ogień","Fire"},{"Obrażenia fizyczne","Physical"},{"Zimno","Ice"},{"Elektryczność","Electric"}};
    public static Dictionary<string,string> ResistanceEnToPl=ResistancePlToEn.ToDictionary(x=>x.Value,x=>x.Key);
    public void UpdateUnitPanel(GameObject g){}
    public List<Unit> AllUnits=new List<Unit>();
    public void DestroyUnit(GameObject g){}
}
class InventoryManager {
    public static InventoryManager Instance=new InventoryManager();
    public static void RecalculateWeaponValue(Weapon w){}
    public void CheckForEquippedWeapons(){}
    public Weapon ChooseWeaponToAttack(GameObject g)=>g.GetComponent<Weapon>();
}
class AnimationManager {
    public static AnimationManager Instance=new AnimationManager();
    public IEnumerator PlayAnimation(string n,GameObject a,GameObject b,int d=0){yield break;}
}
class GameManager {
    public static GameManager Instance=new GameManager();
    public static bool IsAutoDiceRollingMode=true,IsAutoCombatMode,IsStatsHidingMode,IsHealthPointsHidingMode,IsAutoKillMode,IsGamePaused;
    public void RefreshPauseState(){}
}
class ReinforcementLearningManager { public static ReinforcementLearningManager Instance; public bool IsLearning; }
class SaveAndLoadManager {
    public static SaveAndLoadManager Instance=new SaveAndLoadManager(); public bool IsLoading;
    public void SaveUnits(List<Unit> units,string name){RoundsManager.Instance.Actions++;}
}
class InitiativeQueueManager {
    public static InitiativeQueueManager Instance=new InitiativeQueueManager();
    public Unit ActiveUnit;
    public void UpdateInitiativeQueue(){}
    public void SelectUnitByQueue(){}
}
partial class RoundsManager:MonoBehaviour {
    public static RoundsManager Instance=new RoundsManager();
    public static int RoundNumber=1;
    private bool _isRoundGenerationInProgress;
    private Coroutine _turnStartCoroutine;
    public Unit TurnStartUnit {get;private set;}
    public bool IsResolvingTurnStart {get;private set;}
    public int Actions;
    private bool _isFortunePointSpent;
    public void DisplayActionsLeft(){}
    public void TryAdvanceRoundIfComplete(){}
    public void TryAdvanceRoundAfterManualFinish(){}
}
partial class StatesManager:MonoBehaviour {
    public static StatesManager Instance=new StatesManager();
    private GameObject _extinguishPanel;
    private bool _attemptExtinguish;
    public int EndUpdates;
    public void UpdateUnitStates(Unit u){EndUpdates++;}
    public void Unconscious(Unit u,bool b){u.Unconscious=b;}
    public void Prone(Unit u,bool b){u.Prone=b;}
    public void Entangled(Unit u,bool b){u.Entangled=b;}
}
partial class CombatManager:MonoBehaviour {
    public static CombatManager Instance=new CombatManager();
    public bool IsAttackSequenceRunning;
    public bool IsManualPlayerAttack;
    private readonly HashSet<Unit> _pendingOpportunityAttacks=new HashSet<Unit>();
    public Dictionary<string,bool> AttackTypes=new Dictionary<string,bool>{{"MountAttack",false}};
    public void ChangeAttackType(){}
    public IEnumerator AttackCoroutineTracked(Unit a,Unit b,bool o){yield break;}
    private string NormalizeHitLocation(string h)=>h;
    public string Choice;
    private IEnumerator Parry(Unit u,Stats s,Weapon w,int m){Choice="parry";yield break;}
    private IEnumerator Dodge(Unit u,Stats s,int m){Choice="dodge";yield break;}
    public void CheckForOpportunityAttack(GameObject g,Vector2 v){}
    public void ReleaseEntangledUnit(Unit u,Unit t,Weapon w=null){}
    public bool ValidateRangedAttack(Unit u,Unit t,Weapon w,float d)=>true;
    public void Reload(){}
    public float CalculateDistance(GameObject a,GameObject b)=>Vector3.Distance(a.transform.position,b.transform.position);
    public GameObject CreateExtinguishPanel(out UnityEngine.UI.Button a,out UnityEngine.UI.Button b){
        var panel=new GameObject("Extinguish_Panel");panel.Add(new TMPro.TMP_Text());
        a=new GameObject("attempt").Add(new UnityEngine.UI.Button());
        b=new GameObject("skip").Add(new UnityEngine.UI.Button());return panel;
    }
}
partial class MovementManager:MonoBehaviour {
    public static MovementManager Instance=new MovementManager(); public bool IsMoving;
    public List<Vector2> Path=new List<Vector2>();
    public List<Vector2> FindPath(Vector2 start,Vector2 end)=>Path;
    public void SetCanMoveToggle(bool b){}
    public IEnumerator UpdateMovementRange(int m){yield break;}
    public IEnumerator MoveWithDelay(GameObject g,List<Vector2> p,int range){yield break;}
}
class Tile:Component { public bool IsOccupied; }
partial class GridManager {
    public static GridManager Instance=new GridManager();
    public Tile[,] Tiles;
    public void ResetColorOfTilesInMovementRange(){}
}
partial class AutoCombatManager:MonoBehaviour {
    public static AutoCombatManager Instance=new AutoCombatManager();
    public GameObject Opponent;
    public float Distance;
    public Tile TargetTile;
    public string Choice;
    public (GameObject,float) GetClosestOpponent(GameObject g,bool ranged)=>(Opponent,Distance);
    public IEnumerator ExecuteAttack(Unit u,GameObject t,Weapon w,float d){Choice="attack";yield break;}
    public IEnumerator AttemptToChangeDistanceAndAttack(Unit u,GameObject t,Weapon w){Choice="move";yield break;}
}
class Spell:Component {
    public string Name="Mental",SaveAttribute,SaveSkill,DamageType;
    public int Duration,SaveDifficulty,AreaSize,Targets=1,CastingNumber;
    public float Range=10;
    public int[] Strength;
    public string[] Type=new string[0];
    public List<KeyValuePair<string,int>> Attributes=new List<KeyValuePair<string,int>>();
}
partial class MagicManager:MonoBehaviour {
    public string CriticalCastingString="";
    public string RequestedCritical;
    public static bool IsTargetSelecting;
    public HashSet<GameObject> Targets=new HashSet<GameObject>();
    private List<Stats> _targetsStats=new List<Stats>();
    private float _spellDistance;
    public GameObject _criticalCastingPanel=new GameObject();
    public GameObject _castSpellButton=new GameObject();
    public MagicManager(){_criticalCastingPanel.SetActive(false);_castSpellButton.Add(new UnityEngine.UI.Image());}
    public void ResetSpellCasting(){IsTargetSelecting=false;}
    public void CriticalCasting(Spell s){CriticalCastingString=RequestedCritical;}
    public IEnumerator GodsWrath(Stats s,int n){yield break;}
    public IEnumerator DealMagicDamage(Stats c,Stats t,Spell s,int level,int[] dice){yield break;}
}
