using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Arthalion.AI;
using TMPro;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Retains the existing scene component/GUID and UI event methods. Legacy Q tables
// are deliberately never loaded into the new, incompatible observation schema.
public class ReinforcementLearningManager : MonoBehaviour
{
    public static ReinforcementLearningManager Instance { get; private set; }
    [Header("Policy and experiment")]
    public LearningMode Mode = LearningMode.Disabled;
    public LearningOpponent Opponent = LearningOpponent.AutoCombat;
    public LearningSide ControlledSide = LearningSide.Enemy;
    public ModelAsset Model;
    public string ModelLabel = "unassigned";
    public bool FastSimulation = true;
    public bool AutoAdvanceRounds = true;
    [Tooltip("Explicit smoke test only: uniformly random LEGAL actions, never a trained AI.")]
    public bool RandomPolicySmokeTest;
    [Header("Episodes")]
    public int Seed = 12345;
    [Min(1)] public int MaxRounds = 50;
    [Min(0)] public int EpisodeLimit;
    public bool SwapControlledSide = true;
    public bool RandomizePositions = true;
    public bool RandomizeEquipment = true;
    [Tooltip("Optional saved scenarios, e.g. AIlearning. Empty = capture the current battle.")]
    public string[] ScenarioNames = Array.Empty<string>();
    public bool PotentialShaping = true;
    [Min(1)] public int MaxCommandsPerUnitTurn = 64;
    [Min(1)] public float ResolutionTimeoutSeconds = 20;
    public bool TraceCommands;
    [SerializeField] private TMP_Text _trainingDebugDisplay;
    [SerializeField] private TMP_Text _teamWinsDisplay;
    public SimpleGraph simpleGraph;
    [HideInInspector] public bool IsLearning; // compatibility for old Inspector bindings

    public bool IsSessionActive { get; private set; }
    public bool IsRunningEpisodes => IsSessionActive && (Mode == LearningMode.Training || Mode == LearningMode.Evaluation);
    public bool IsFastSimulation => IsSessionActive && FastSimulation;
    public bool IsExecutingCommand { get; private set; }
    public bool IsResetting { get; private set; }
    public bool ExternalPolicy { get; private set; }
    public List<SpellData> Spells { get; private set; } = new List<SpellData>();
    public string RunFolder { get; private set; }
    public int CompletedEpisodes { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Draws { get; private set; }
    public int Aborts { get; private set; }
    public int Commands { get; private set; }
    public long PolicyDecisions { get; private set; }
    public BattleResult LastResult { get; private set; }
    private CombatLearningAgent _player, _enemy;
    private TrainingBattleSnapshot _snapshot;
    private StreamWriter _episodes, _steps;
    private readonly Stopwatch _wallClock = new Stopwatch();
    private readonly float[] _lastPotential = new float[2];
    private readonly bool[] _hasDecision = new bool[2];
    private float _initialPlayerHealth, _initialEnemyHealth;
    private long _episodeStartDecisions;
    private int _episodeCommands, _scenarioIndex, _episodeSeed;
    private double _episodeStartTime;
    private bool _starting, _stopRequested, _ownsAcademy, _previousAutoStep;
    private bool _previousDice, _previousDefense, _previousAutoCombat, _previousKill, _previousStatsHiding;
    private LogType _previousLogType;
    private LearningMode _activeMode;
    private string _failure;
    private bool _settingsCaptured;
    private int _previousFrameRate, _previousVSync;
    private double _nextUiUpdate;
    private bool _startupConfigurationApplied;

    private string _runtimeException;
    private void Awake()
    {
        if (Instance == null) { Instance = this; Application.logMessageReceived += CaptureRuntimeException; }
        else Destroy(gameObject);
    }
    private void CaptureRuntimeException(string condition, string stack, LogType type)
    { if (type == LogType.Exception && (_starting || IsSessionActive)) _runtimeException = condition + " " + stack; }

    private IEnumerator Start()
    {
        yield return null;
        ApplyCommandLine();
        _startupConfigurationApplied = true;
        if (IsLearning && Mode == LearningMode.Disabled) Mode = LearningMode.Training;
        if (Mode != LearningMode.Disabled) yield return StartConfiguredSession();
    }

    private void Update()
    {
        if (!_startupConfigurationApplied) return;
        if (_runtimeException != null)
        {
            string error = _runtimeException; _runtimeException = null;
            if (_starting) StartupFailed(error); else if (IsSessionActive) Abort(error);
        }
        IsLearning = IsSessionActive && Mode == LearningMode.Training;
        if (!_starting && Mode != _activeMode)
        {
            if (Mode == LearningMode.Disabled) StopSession();
            else if (!IsSessionActive) StartCoroutine(StartConfiguredSession());
            else { Debug.LogWarning("[AI] Stop the current experiment before changing its mode."); Mode = _activeMode; }
        }
        if (IsSessionActive && Time.realtimeSinceStartupAsDouble >= _nextUiUpdate)
        { UpdateTrainingUI(); _nextUiUpdate = Time.realtimeSinceStartupAsDouble + 0.5; }
    }

    [ContextMenu("AI/Start configured experiment")]
    public void StartExperiment() { if (!_starting && !IsSessionActive) StartCoroutine(StartConfiguredSession()); }

    public IEnumerator StartConfiguredSession()
    {
        if (_starting || IsSessionActive || Mode == LearningMode.Disabled) yield break;
        _starting = true;
        _settingsCaptured = true;
        _previousFrameRate = Application.targetFrameRate; _previousVSync = QualitySettings.vSyncCount;
        _previousDice = GameManager.IsAutoDiceRollingMode; _previousAutoCombat = GameManager.IsAutoCombatMode;
        _previousDefense = GameManager.IsAutoDefenseMode;
        _previousKill = GameManager.IsAutoKillMode; _previousStatsHiding = GameManager.IsStatsHidingMode;
        _previousLogType = Debug.unityLogger.filterLogType;
        GameManager.IsAutoDiceRollingMode = true; GameManager.IsAutoCombatMode = true;
        GameManager.IsAutoDefenseMode = true;
        GameManager.IsAutoKillMode = true; GameManager.IsStatsHidingMode = false;
        GameManager.Instance?.HideActivePanels();
        if (ScenarioNames.Length == 1 && ScenarioNames[0] == "@starter")
        {
            yield return CombatTrainingPresets.EnsureCreated();
            ScenarioNames = CombatTrainingPresets.Names;
        }
        if (ScenarioNames.Length > 0)
        {
            if (!Directory.Exists(Path.Combine(Application.persistentDataPath, ScenarioNames[0])))
            { StartupFailed("Saved scenario does not exist: " + ScenarioNames[0]); yield break; }
            SaveAndLoadManager.Instance.SetLoadingType("all"); SaveAndLoadManager.Instance.LoadGame(ScenarioNames[0]);
            yield return new WaitUntil(() => !SaveAndLoadManager.Instance.IsLoading);
            yield return null;
        }
        if (!UnitsManager.Instance.AllUnits.Any(u => u != null && u.CompareTag("PlayerUnit")) ||
            !UnitsManager.Instance.AllUnits.Any(u => u != null && u.CompareTag("EnemyUnit")))
        { StartupFailed("Prepare units on BOTH sides before starting AI training."); yield break; }
        TextAsset spells = Resources.Load<TextAsset>("spells");
        if (spells != null) Spells = JsonHelper.FromJson<SpellData>(spells.text).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
        Academy academy = Academy.Instance;
        if (!RandomPolicySmokeTest && (Mode == LearningMode.Training || ExternalPolicy) && !academy.IsCommunicatorOn)
        { StartupFailed("Python policy is not connected. Start Tools/AI/start-training.ps1 or evaluate.py first. No random fallback was used."); yield break; }
        if (!RandomPolicySmokeTest && Mode != LearningMode.Training && !ExternalPolicy && Model == null)
        { StartupFailed("Assign a trained ONNX Model for inference/evaluation, or connect the external evaluator."); yield break; }
        _previousAutoStep = academy.AutomaticSteppingEnabled; academy.AutomaticSteppingEnabled = false; _ownsAcademy = true;
        try
        {
            _snapshot = new TrainingBattleSnapshot();
            _player = CreateAgent(true); _enemy = CreateAgent(false);
            _ = CombatObservations.Board(); // Reject unsupported sizes before accepting training data.
            RunFolder = CommandLine("--ai-output") ?? Path.Combine(Application.persistentDataPath, "ai_training_runs", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            RunFolder = Path.Combine(RunFolder, "worker_" + (CommandLine("--mlagents-port") ?? "editor"));
            Directory.CreateDirectory(RunFolder);
            File.WriteAllText(Path.Combine(RunFolder, "manifest.json"), JsonUtility.ToJson(new RunManifest {
                schema = CombatObservations.SchemaHash(), model = Model != null ? Model.name : ModelLabel,
                mode = Mode.ToString(), opponent = Opponent.ToString(), seed = Seed, shaping = PotentialShaping,
                vectorSize = CombatObservations.VectorSize, unitSize = CombatObservations.UnitSize,
                weaponSize = CombatObservations.WeaponSize, effectSize = CombatObservations.EffectSize,
                spellSize = CombatObservations.SpellSize, scenarios = ScenarioNames, maxRounds = MaxRounds
                , rules = Resources.Load<TextAsset>("ai_rules_version")?.text ?? "editor-unversioned"
            }, true));
            string episodePath = Path.Combine(RunFolder, "episodes.csv");
            bool append = Mode == LearningMode.Training && File.Exists(episodePath) && new FileInfo(episodePath).Length > 0;
            if (append)
            {
                foreach (string line in File.ReadLines(episodePath).Skip(1))
                {
                    string[] row = line.Split(';');
                    if (row.Length < 11 || !Enum.TryParse(row[5], out BattleResult result)) continue;
                    CompletedEpisodes++;
                    if (result == BattleResult.TechnicalAbort) Aborts++;
                    else if (result == BattleResult.Draw) Draws++;
                    else if (CombatLearningContract.OutcomeReward(result, row[4] == "Player") > 0) Wins++; else Losses++;
                }
            }
            _episodes = new StreamWriter(episodePath, append) { AutoFlush = true };
            if (!append) _episodes.WriteLine("episode;mode;scenario;seed;learned_side;result;model;rounds;commands;decisions;seconds;wins;losses;draws;aborts;win_rate;ci_low;ci_high;error");
            if (TraceCommands)
            {
                _steps = new StreamWriter(Path.Combine(RunFolder, "commands.csv"));
                _steps.WriteLine("episode;round;unit;race;action;target;x;y");
            }
        }
        catch (Exception error) { StartupFailed(error.ToString()); yield break; }
        _wallClock.Start(); _activeMode = Mode; IsSessionActive = true; _starting = false; _stopRequested = false;
        IsLearning = Mode == LearningMode.Training;
        if (FastSimulation)
        { Debug.unityLogger.filterLogType = LogType.Error; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
        Debug.LogError("[AI] Experiment started. Metrics: " + RunFolder);
        yield return ResetEpisode(false);
        RoundsManager.Instance.NextRound();
    }

    private CombatLearningAgent CreateAgent(bool player)
    {
        var go = new GameObject(player ? "AI player team" : "AI enemy team"); go.transform.SetParent(transform); go.SetActive(false);
        var behavior = go.AddComponent<BehaviorParameters>();
        behavior.BehaviorName = CombatLearningContract.BehaviorName;
        behavior.TeamId = Opponent == LearningOpponent.SelfPlay ? (player ? 0 : 1) : 0;
        behavior.BrainParameters.VectorObservationSize = CombatObservations.VectorSize;
        behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(CombatLearningContract.ActionOptions);
        behavior.DeterministicInference = true;
        behavior.InferenceDevice = InferenceDevice.Burst;
        behavior.Model = Model;
        behavior.BehaviorType = RandomPolicySmokeTest ? BehaviorType.HeuristicOnly :
            Mode == LearningMode.Training || ExternalPolicy ? BehaviorType.Default : BehaviorType.InferenceOnly;
        BufferSensorComponent units = Buffer(go, "01_units", CombatLearningContract.MaxUnits, CombatObservations.UnitSize);
        BufferSensorComponent weapons = Buffer(go, "02_weapons", CombatLearningContract.MaxWeapons, CombatObservations.WeaponSize);
        BufferSensorComponent effects = Buffer(go, "03_effects", CombatLearningContract.MaxEffects, CombatObservations.EffectSize);
        BufferSensorComponent spells = Buffer(go, "04_spells", CombatLearningContract.MaxSpells, CombatObservations.SpellSize);
        var agent = go.AddComponent<CombatLearningAgent>(); agent.MaxStep = 0;
        agent.Configure(this, player, units, weapons, effects, spells);
        go.SetActive(true);
        return agent;
    }

    private static BufferSensorComponent Buffer(GameObject go, string name, int count, int size)
    { var sensor = go.AddComponent<BufferSensorComponent>(); sensor.SensorName = name; sensor.MaxNumObservables = count; sensor.ObservableSize = size; return sensor; }

    public bool LearnedPlayerSide => CombatLearningContract.ControlledPlayerSide(ControlledSide, CompletedEpisodes, SwapControlledSide && IsRunningEpisodes);
    public bool ControlsUnit(Unit unit) => IsSessionActive && unit != null &&
        (Opponent == LearningOpponent.SelfPlay || unit.CompareTag("PlayerUnit") == LearnedPlayerSide);

    public IEnumerator PlayUnitTurn(Unit unit)
    {
        var visited = new HashSet<string>(); int count = 0;
        InventoryManager.Instance.SelectedHand = unit.AISelectedHand;
        while (CombatActionRules.CanAct(unit) && (unit.CanDoAction || unit.CanMove))
        {
            if (++count > MaxCommandsPerUnitTurn) { Abort("Unit turn command limit exceeded: " + unit.UnitId); yield break; }
            CombatLearningAgent agent = unit.CompareTag("PlayerUnit") ? _player : _enemy;
            visited.Add(CombatActionRules.LoadoutKey(unit));
            CombatCommand command = null;
            try { command = agent.Choose(unit, visited); }
            catch (Exception error) { Abort(error.ToString()); }
            if (command == null || LastResult == BattleResult.TechnicalAbort) yield break;
            bool previousAction = unit.CanDoAction, previousMove = unit.CanMove;
            int previousHp = unit.Stats.TempHealth;
            Vector3 previousPosition = unit.transform.position;
            _steps?.WriteLine(string.Join(";", CompletedEpisodes, RoundsManager.RoundNumber, unit.UnitId, unit.Stats.Race,
                command.Kind, command.Target != null ? command.Target.UnitId : -1,
                command.Destination != null ? command.Destination.transform.position.x.ToString(CultureInfo.InvariantCulture) : "",
                command.Destination != null ? command.Destination.transform.position.y.ToString(CultureInfo.InvariantCulture) : ""));
            IsExecutingCommand = true;
            yield return CombatActionRules.Execute(unit, command);
            yield return WaitForResolution();
            IsExecutingCommand = false;
            Commands++; _episodeCommands++;
            if (unit == null || CheckBattleResult() != BattleResult.Ongoing || LastResult == BattleResult.TechnicalAbort) yield break;
            if (unit.CanDoAction != previousAction || unit.CanMove != previousMove || unit.Stats.TempHealth != previousHp || unit.transform.position != previousPosition)
                visited.Clear();
        }
        if (unit != null && !unit.IsTurnFinished) RoundsManager.Instance.FinishTurn(unit, false);
    }

    public IEnumerator WaitForResolution()
    {
        double start = Time.realtimeSinceStartupAsDouble;
        do
        {
            if (Time.realtimeSinceStartupAsDouble - start > ResolutionTimeoutSeconds)
            { Abort($"Combat resolution timed out: move={MovementManager.Instance.IsMoving}, attack={CombatManager.Instance.IsAttackSequenceRunning}, turnStart={RoundsManager.Instance.IsResolvingTurnStart}, dice={DiceRollManager.Instance.IsWaitingForRoll}, autoDice={GameManager.IsAutoDiceRollingMode}, paused={GameManager.IsGamePaused}, selected={Unit.SelectedUnit?.name}"); yield break; }
            yield return null;
        } while (MovementManager.Instance.IsMoving || CombatManager.Instance.IsAttackSequenceRunning ||
            AutoCombatManager.Instance.IsActing ||
            RoundsManager.Instance.IsResolvingTurnStart || DiceRollManager.Instance.IsWaitingForRoll);
        Physics2D.SyncTransforms();
    }

    public BattleResult CheckBattleResult(bool includeRoundLimit = false)
    {
        if (LastResult == BattleResult.TechnicalAbort) return LastResult;
        bool player = UnitsManager.Instance.AllUnits.Any(u => CombatActionRules.IsPresent(u) && u.CompareTag("PlayerUnit"));
        bool enemy = UnitsManager.Instance.AllUnits.Any(u => CombatActionRules.IsPresent(u) && u.CompareTag("EnemyUnit"));
        if (!player && !enemy) return BattleResult.Draw;
        if (!player) return BattleResult.EnemyWin;
        if (!enemy) return BattleResult.PlayerWin;
        return includeRoundLimit && RoundsManager.RoundNumber >= MaxRounds ? BattleResult.Draw : BattleResult.Ongoing;
    }

    public IEnumerator CompleteEpisode()
    {
        LastResult = CheckBattleResult(true);
        bool abort = LastResult == BattleResult.TechnicalAbort;
        foreach (CombatLearningAgent agent in new[] { _player, _enemy })
        {
            if (agent == null) continue;
            int side = agent.PlayerSide ? 0 : 1;
            if (!abort && _hasDecision[side])
            {
                if (PotentialShaping) agent.AddReward(-_lastPotential[side]); // terminal Phi = 0
                agent.AddReward(CombatLearningContract.OutcomeReward(LastResult, agent.PlayerSide));
            }
            if (abort) agent.EpisodeInterrupted();
            else { agent.ClearTerminalObservation(); agent.EndEpisode(); }
        }
        Academy.Instance.EnvironmentStep();
        float reward = CombatLearningContract.OutcomeReward(LastResult, LearnedPlayerSide);
        if (abort) Aborts++; else if (LastResult == BattleResult.Draw) Draws++; else if (reward > 0) Wins++; else Losses++;
        int games = Wins + Losses + Draws;
        CombatLearningContract.WilsonInterval(Wins, games, out double lower, out double upper);
        _episodes.WriteLine(string.Join(";", CompletedEpisodes, Mode, ScenarioNames.Length == 0 ? "current" : ScenarioNames[_scenarioIndex],
            _episodeSeed, LearnedPlayerSide ? "Player" : "Enemy", LastResult, Model != null ? Model.name : ModelLabel,
            RoundsManager.RoundNumber, _episodeCommands, PolicyDecisions - _episodeStartDecisions,
            (_wallClock.Elapsed.TotalSeconds - _episodeStartTime).ToString("F3", CultureInfo.InvariantCulture), Wins, Losses, Draws, Aborts,
            (games == 0 ? 0 : (double)Wins / games).ToString("F5", CultureInfo.InvariantCulture),
            lower.ToString("F5", CultureInfo.InvariantCulture), upper.ToString("F5", CultureInfo.InvariantCulture), (_failure ?? "").Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ')));
        _steps?.Flush(); if (!FastSimulation) simpleGraph?.AddValue(games > 0 ? (float)Wins / games : 0);
        CompletedEpisodes++;
        if (_stopRequested || Mode == LearningMode.Inference || EpisodeLimit > 0 && CompletedEpisodes >= EpisodeLimit)
        {
            StopSession();
            if (CommandLine("--ai-mode") != null) Application.Quit(abort ? 2 : 0);
            yield break;
        }
        yield return ResetEpisode(true);
    }

    private IEnumerator ResetEpisode(bool next)
    {
        IsResetting = true; LastResult = BattleResult.Ongoing; _failure = null;
        if (next && ScenarioNames.Length > 1 && (!SwapControlledSide || CompletedEpisodes % 2 == 0))
        {
            _scenarioIndex = (CompletedEpisodes / (SwapControlledSide ? 2 : 1)) % ScenarioNames.Length;
            _snapshot.Dispose();
            SaveAndLoadManager.Instance.SetLoadingType("all"); SaveAndLoadManager.Instance.LoadGame(ScenarioNames[_scenarioIndex]);
            yield return new WaitUntil(() => !SaveAndLoadManager.Instance.IsLoading);
            yield return null;
            _snapshot = new TrainingBattleSnapshot();
        }
        _episodeSeed = CombatLearningContract.EpisodeSeed(Seed, CompletedEpisodes, SwapControlledSide);
        yield return _snapshot.Restore(_episodeSeed, RandomizePositions && IsRunningEpisodes, RandomizeEquipment && IsRunningEpisodes);
        UnityEngine.Random.InitState(_episodeSeed);
        _player.SetEpisodeSeed(_episodeSeed); _enemy.SetEpisodeSeed(unchecked(_episodeSeed + 101));
        _initialPlayerHealth = Health(true); _initialEnemyHealth = Health(false);
        _lastPotential[0] = _lastPotential[1] = 0; _hasDecision[0] = _hasDecision[1] = false;
        _episodeCommands = 0; _episodeStartDecisions = PolicyDecisions; _episodeStartTime = _wallClock.Elapsed.TotalSeconds;
        IsResetting = false;
    }

    private static float Health(bool player) => UnitsManager.Instance.AllUnits.Where(u => u != null && u.CompareTag("PlayerUnit") == player)
        .Sum(u => Mathf.Max(0, u.Stats.TempHealth));
    private float Potential(bool player)
    {
        float value = Health(true) / Mathf.Max(1, _initialPlayerHealth) - Health(false) / Mathf.Max(1, _initialEnemyHealth);
        return player ? value : -value;
    }

    public void Abort(string reason) { _failure = reason; LastResult = BattleResult.TechnicalAbort; Debug.LogError("[AI] " + reason); }
    public void RecordPolicyDecision() { PolicyDecisions++; }
    public void PrepareTeamDecision(Unit unit)
    {
        int side = unit.CompareTag("PlayerUnit") ? 0 : 1;
        CombatLearningAgent agent = side == 0 ? _player : _enemy;
        if (PotentialShaping && _hasDecision[side]) agent.AddReward(Potential(side == 0) - _lastPotential[side]);
        _lastPotential[side] = Potential(side == 0); _hasDecision[side] = true;
    }
    public int ChooseReaction(Unit unit, int phase, int[] choices)
    {
        try { return (unit.CompareTag("PlayerUnit") ? _player : _enemy).ChooseReaction(unit, phase, choices); }
        catch (Exception error) { Abort(error.ToString()); return choices[0]; }
    }
    public void RequestStop() { _stopRequested = true; }
    private void StartupFailed(string reason) { Debug.LogError("[AI] " + reason); _starting = false; StopSession(); if (CommandLine("--ai-mode") != null) Application.Quit(2); }

    [ContextMenu("AI/Stop experiment")]
    public void StopSession()
    {
        if (!_settingsCaptured && !IsSessionActive) { Mode = _activeMode = LearningMode.Disabled; return; }
        _episodes?.Dispose(); _episodes = null; _steps?.Dispose(); _steps = null;
        _snapshot?.Dispose(); _snapshot = null;
        if (_player != null) Destroy(_player.gameObject); if (_enemy != null) Destroy(_enemy.gameObject);
        if (_ownsAcademy && Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = _previousAutoStep;
        _ownsAcademy = false;
        GameManager.IsAutoDiceRollingMode = _previousDice; GameManager.IsAutoCombatMode = _previousAutoCombat;
        GameManager.IsAutoDefenseMode = _previousDefense;
        GameManager.IsAutoKillMode = _previousKill; GameManager.IsStatsHidingMode = _previousStatsHiding;
        Debug.unityLogger.filterLogType = _previousLogType;
        Application.targetFrameRate = _previousFrameRate; QualitySettings.vSyncCount = _previousVSync;
        _settingsCaptured = false;
        IsSessionActive = false; IsLearning = false; IsExecutingCommand = false;
        Mode = _activeMode = LearningMode.Disabled;
    }
    private void OnApplicationQuit() { _episodes?.Dispose(); _steps?.Dispose(); }
    private void OnDestroy() { Application.logMessageReceived -= CaptureRuntimeException; if (IsSessionActive) StopSession(); if (Instance == this) Instance = null; }

    private void UpdateTrainingUI()
    {
        int games = Wins + Losses + Draws;
        CombatLearningContract.WilsonInterval(Wins, games, out double lower, out double upper);
        string text = $"{Mode} | {(RandomPolicySmokeTest ? "RANDOM SMOKE TEST" : Opponent.ToString())}\n" +
            $"Episodes: {CompletedEpisodes}  W/L/D: {Wins}/{Losses}/{Draws}  Aborts: {Aborts}\n" +
            $"Win rate: {(games > 0 ? 100f * Wins / games : 0):F1}%  CI95: {lower:P0}–{upper:P0}\n" +
            $"Decisions: {PolicyDecisions} ({PolicyDecisions / Math.Max(1, _wallClock.Elapsed.TotalSeconds):F1}/s)  Commands: {Commands}";
        if (_trainingDebugDisplay != null) _trainingDebugDisplay.text = text;
        if (_teamWinsDisplay != null) _teamWinsDisplay.text = $"Learned AI {Wins} | AutoCombat {Losses} | Draw {Draws}";
    }

    public void ToggleLogs() { Debug.unityLogger.filterLogType = Debug.unityLogger.filterLogType == LogType.Error ? LogType.Log : LogType.Error; }
    public void SaveQTables() { _episodes?.Flush(); _steps?.Flush(); Debug.Log("[AI] PPO checkpoints and ONNX are saved by the Python trainer, not as Q tables."); }
    public void LoadQTables() { Debug.LogWarning("[AI] Legacy Q tables are incompatible. Assign a V1 ONNX model instead."); }
    public void ExportAllData() { SaveQTables(); Debug.Log("[AI] CSV metrics: " + RunFolder); }

    public static string CommandLine(string option)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(option + "=", StringComparison.Ordinal)) return args[i].Substring(option.Length + 1);
            if (args[i] == option && i + 1 < args.Length) return args[i + 1];
        }
        return null;
    }
    private void ApplyCommandLine()
    {
        if (Enum.TryParse(CommandLine("--ai-mode"), true, out LearningMode mode)) Mode = mode;
        if (int.TryParse(CommandLine("--ai-seed"), out int seed)) Seed = seed;
        if (int.TryParse(CommandLine("--ai-episodes"), out int episodes)) EpisodeLimit = episodes;
        if (int.TryParse(CommandLine("--ai-max-rounds"), out int rounds)) MaxRounds = Mathf.Max(1, rounds);
        if (CommandLine("--ai-scenarios") != null) ScenarioNames = CommandLine("--ai-scenarios").Split(',');
        if (bool.TryParse(CommandLine("--ai-fast"), out bool fast)) FastSimulation = fast;
        ExternalPolicy = CommandLine("--ai-external-policy") == "true";
        RandomPolicySmokeTest |= CommandLine("--ai-random-policy") == "true";
        TraceCommands |= CommandLine("--ai-trace") == "true";
        if (CommandLine("--ai-opponent") == "SelfPlay") Opponent = LearningOpponent.SelfPlay;
        if (CommandLine("--ai-model-label") != null) ModelLabel = CommandLine("--ai-model-label");
        if (Mode == LearningMode.Evaluation) { Seed = int.TryParse(CommandLine("--ai-seed"), out seed) ? seed : 87654321; PotentialShaping = false; }
        if (Mode == LearningMode.Training && int.TryParse(CommandLine("--mlagents-port"), out int port)) Seed = unchecked(Seed + port * 9973);
    }

    [Serializable] private sealed class RunManifest
    {
        public int contractVersion = CombatLearningContract.Version;
        public string schema, rules, model, mode, opponent;
        public int seed, vectorSize, unitSize, weaponSize, effectSize, spellSize, maxRounds;
        public bool shaping;
        public string[] scenarios;
    }
}
