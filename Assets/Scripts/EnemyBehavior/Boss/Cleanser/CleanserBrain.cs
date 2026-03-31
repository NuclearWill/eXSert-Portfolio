// CleanserBrain.cs
// Purpose: Main AI controller for the Cleanser boss.
// Works with: CleanserComboSystem, CleanserDualWieldSystem, CleanserPlatformController, CleanserProjectile
// Structure mirrors BossRoombaBrain.cs for consistency.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using Utilities.Combat;
using Managers.TimeLord; // For pause event handling

namespace EnemyBehavior.Boss.Cleanser
{
    /// <summary>
    /// Main brain/AI controller for the Cleanser boss.
    /// Manages combat state, attack execution, and ultimate mechanics.
    /// </summary>
    [HelpURL("https://docs.google.com/document/d/18pi24ZJ65GG307F6SvKpSoHPs0izxSb6yZ6cfjvYqMQ/edit?tab=t.0#bookmark=id.pru60xl52koz")]
    [RequireComponent(typeof(NavMeshAgent))]
    public class CleanserBrain : MonoBehaviour, IQueuedAttacker, IHealthSystem
    {
        [Header("Component Help")]
        [SerializeField, TextArea(3, 6)] private string inspectorHelp =
            "CleanserBrain: Main AI controller for the Cleanser boss.\n" +
            "Manages combo execution, dual-wield mechanics, and ultimate attacks.\n" +
            "Wing attacks can be guarded but not parried.\n" +
            "Halberd attacks can be parried but guard won't help.";

[Header("Beta Testing Mode")]
        [Tooltip("When enabled, Cleanser becomes invulnerable and only follows the player. No attacks, no aggression system, no ultimate. For beta testing purposes.")]
        [SerializeField] private bool betaDummyMode = false;
        [Tooltip("Distance at which the Cleanser stops following the player in dummy mode.")]
        [SerializeField] private float dummyModeStoppingDistance = 3f;

        [Header("Profile")]
        [Tooltip("Behavior profile containing movement settings. If null, uses fallback values below.")]
        [SerializeField] private EnemyBehaviorProfile profile;

        [Header("Health")]
        [SerializeField] private float maxHealth = 2000f;
        [SerializeField] private float currentHealth = 2000f;
        
        [Header("Health Bar UI")]
        [Tooltip("Reference to the boss health bar script on the canvas.")]
        [SerializeField] private HealthBar healthBar;
        [Tooltip("How quickly the health bar animates to the current value.")]
        [SerializeField] private float healthBarLerpSpeed = 8f;
        private float displayedHealth;
        
        [Header("Attack Queue Settings")]
        [Tooltip("If true, this boss is exempt from the EnemyAttackQueueManager and attacks freely.")]
        [SerializeField] private bool exemptFromAttackQueue = true;
        
        [Header("Basic Attack Configurations")]
        [Tooltip("Basic melee attack descriptor: Lunge.")]
        public CleanserAttackDescriptor LungeAttack;
        [Tooltip("Basic melee attack descriptor: Lunge with blocking windup.")]
        public CleanserAttackDescriptor LungeBlockAttack;
        [Tooltip("Basic melee attack descriptor: Overhead cleave.")]
        public CleanserAttackDescriptor OverheadCleaveAttack;
        [Tooltip("Basic melee attack descriptor: Cleave.")]
        public CleanserAttackDescriptor CleaveAttack;
        [Tooltip("Basic melee attack descriptor: Advancing cleave.")]
        public CleanserAttackDescriptor CleaveAdvanceAttack;
        [Tooltip("Basic melee attack descriptor: Pommel strike.")]
        public CleanserAttackDescriptor PommelStrikeAttack;
        [Tooltip("Basic melee attack descriptor: Diagonal upward slash.")]
        public CleanserAttackDescriptor DiagUpwardSlashAttack;
        [Tooltip("Basic melee attack descriptor: Wing bash.")]
        public CleanserAttackDescriptor WingBashAttack;
        [Tooltip("Basic mixed attack descriptor: Slash into slap.")]
        public CleanserAttackDescriptor SlashIntoSlapAttack;
        [Tooltip("Basic mixed attack descriptor: Rake into spin slash.")]
        public CleanserAttackDescriptor RakeIntoSpinSlashAttack;
        [Tooltip("Basic melee attack descriptor: Leg sweep.")]
        public CleanserAttackDescriptor LegSweepAttack;
        [Tooltip("Configuration for the knockback basic move that pushes player away.")]
        public KnockbackAttackConfig KnockbackSettings = new KnockbackAttackConfig();

        [Header("Strong Attack Configurations")]
        [Tooltip("Strong finisher descriptor: High Dive.")]
        public CleanserAttackDescriptor HighDiveAttack;
        [Tooltip("Detailed settings for the Anime Dash Slash strong finisher.")]
        public AnimeDashSlashConfig AnimeDashSettings = new AnimeDashSlashConfig();
        [Tooltip("Configuration for the SpinDash strong finisher (uses JumpSpinAttack animation clips/states).")]
        [FormerlySerializedAs("JumpSpinSettings")]
        public JumpSpinAttackConfig SpinDashSettings = new JumpSpinAttackConfig();
        [Tooltip("Configuration for Whirlwind strong attack.")]
        public WhirlwindConfig WhirlwindSettings = new WhirlwindConfig();

        [Header("Ultimate Attack Configuration")]
        [Tooltip("Main ultimate settings block for Double Maximum Sweep.")]
        public DoubleMaximumSweepConfig UltimateSettings = new DoubleMaximumSweepConfig();
        [Tooltip("Positions where Cleanser can jump to perform the double sweep setup.")]
        public List<Transform> DoubleSweepPositions = new List<Transform>();
        [Tooltip("Duration of stun when ultimate is canceled by aerial plunge finisher.")]
        public float AerialFinisherStunDuration = 3f;
        [Tooltip("Health percentage at which ultimate becomes available.")]
        [Range(0f, 1f)] public float UltimateHealthThreshold = 0.5f;
        [Tooltip("Minimum attacks between ultimate uses when using attack-count trigger mode.")]
        public int MinAttacksBetweenUltimates = 15;
        [Tooltip("If true, ultimate triggers by health threshold. If false, triggers by attacks since last ultimate.")]
        public bool UltimateTriggeredByHealth = true;
        [Tooltip("Animator state used during dedicated ultimate hover phase.")]
        [SerializeField] private string stateUltimateHover = "JumpArcHold";

        [Header("Crescent Wave Projectile Configuration")]
        [Tooltip("Shared crescent-wave projectile settings used by DiagUpwardSlash and ultimate sweeps.")]
        public CrescentArcProjectileConfig CrescentWaveProjectileConfiguration = new CrescentArcProjectileConfig();
        [Tooltip("Legacy fallback projectile prefab used if per-attack crescent prefab is not assigned.")]
        public GameObject CrescentWavePrefab;

        [Header("Spare Toss Configuration")]
        [Tooltip("Behavior settings for spare toss projectiles.")]
        public SpareTossConfig SpareTossSettings = new SpareTossConfig();
        [Tooltip("Prefab for the thrown weapon projectile (visual only - the actual spare weapon returns magnetically).")]
        public GameObject ProjectilePrefab;
        [Tooltip("Transform where spare toss projectiles spawn from (typically the hand).")]
        public Transform ProjectileSpawnPoint;

        [Header("Movement Configuration")]
        [Tooltip("Configuration for the gap-closing dash (movement only, no hitbox).")]
        public GapClosingDashConfig GapCloseDashSettings = new GapClosingDashConfig();

        [Header("Movement (Fallback if no Profile)")]
        [Tooltip("Base movement speed (used if profile is null).")]
        public float FallbackSpeed = 8f;
        [Tooltip("Angular speed for turning (used if profile is null).")]
        public float FallbackAngularSpeed = 120f;
        [Tooltip("Acceleration (used if profile is null).")]
        public float FallbackAcceleration = 8f;
        [Tooltip("Stopping distance from target (used if profile is null).")]
        public float FallbackStoppingDistance = 2f;

        [Header("Misc Configuration")]

        [Header("Player Stagger")]
        [Tooltip("If enabled, Cleanser halberd melee hit windows force-stagger the player.")]
        [SerializeField] private bool staggerPlayerOnHalberdMeleeHit = true;
        [Tooltip("If enabled, Cleanser wing melee hit windows force-stagger the player.")]
        [SerializeField] private bool staggerPlayerOnWingMeleeHit = true;
        [Tooltip("Forced stagger duration for Cleanser melee hits.")]
        [SerializeField, Range(0.05f, 2f)] private float meleeHitStaggerDuration = 0.4f;
        [Tooltip("If enabled, player combo is reset when Cleanser melee stagger is applied.")]
        [SerializeField] private bool resetPlayerComboOnMeleeStagger = true;

        [Header("Windup Damage Reduction")]
        [Tooltip("If true, check for animation events to enable/disable damage reduction.")]
        public bool UseDamageReductionEvents = true;
        private bool isDamageReductionActive = false;
        private float currentDamageReduction = 1f;

        [Header("References")]
        [Tooltip("Combo system component reference.")]
        [SerializeField] private CleanserComboSystem comboSystem;
        [Tooltip("Dual-wield system component reference.")]
        [SerializeField] private CleanserDualWieldSystem dualWieldSystem;
        [Tooltip("Platform controller component reference.")]
        [SerializeField] private CleanserPlatformController platformController;
        [Tooltip("Aggression system component reference.")]
        [SerializeField] private CleanserAggressionSystem aggressionSystem;
        [Tooltip("Whirlwind suction effect component reference.")]
        [SerializeField] private VacuumSuctionEffect suctionEffect;
        [Tooltip("Animation controller wrapper. Auto-found on Awake if left empty.")]
        [SerializeField] private CleanserAnimController animController;
        [Tooltip("Animator component used for fallback parameter/trigger control. Auto-found on Awake if left empty.")]
        [SerializeField] private Animator animator;
        [Tooltip("Audio source for boss SFX. Auto-falls back to SoundManager if left empty.")]
        [SerializeField] private AudioSource sfxSource;

        [Header("Hitbox References")]
        [Tooltip("Optional additional halberd colliders (for example pole capsule + blade sphere). Used together to derive halberd hit range.")]
        [SerializeField] private List<Collider> halberdHitboxColliders = new List<Collider>();
        [Tooltip("Collider that represents the wing hit volume. Used to derive hit range during wing hit windows.")]
        [SerializeField] private Collider wingHitboxCollider;
        [Tooltip("Collider used during SpinDash hold phase (typically a capsule around the whole body).")]
        [SerializeField] private Collider spinDashHitboxCollider;
        [Tooltip("Delay before re-enabling SpinDash hitbox after a successful hit, preventing per-frame damage ticks.")]
        [SerializeField] private float spinDashHitboxRearmDelay = 0.08f;

        [Header("Animator Parameters")]
        [Tooltip("Animator float parameter for movement speed magnitude.")]
        [SerializeField] private string paramMoveSpeed = "MoveSpeed";
        [Tooltip("Animator bool parameter indicating moving/not moving state.")]
        [SerializeField] private string paramIsMoving = "IsMoving";
        [Tooltip("Animator bool parameter indicating whether a player target is available.")]
        [SerializeField] private string paramIsPlayerHere = "IsPlayerHere";
        [Tooltip("Animator trigger parameter for stunned reaction.")]
        [SerializeField] private string triggerStunned = "Stunned";
        [Tooltip("Animator trigger parameter for death.")]
        [SerializeField] private string triggerDeath = "Death";

        [Header("Attack Indicator VFX")]
        [Tooltip("VFX prefab to spawn before an attack to warn the player. Leave empty to disable.")]
        [SerializeField] private GameObject attackIndicatorPrefab;
        [Tooltip("Position offset from the boss's transform where the indicator spawns (local space).")]
        [SerializeField] private Vector3 attackIndicatorOffset = new Vector3(0f, 0.5f, 2f);
        [Tooltip("Seconds before the attack lands that the indicator appears.")]
        [SerializeField] private float attackIndicatorLeadTime = 0.4f;
        [Tooltip("How long the indicator stays visible. Set to 0 to auto-hide when attack starts.")]
        [SerializeField] private float attackIndicatorDuration = 0f;
        [Tooltip("If true, indicator follows the boss's position/rotation.")]
        [SerializeField] private bool attackIndicatorFollowsBoss = true;
        [Tooltip("Scale multiplier for the indicator VFX.")]
        [SerializeField] private float attackIndicatorScale = 1f;
        
        // Runtime state for attack indicator
        private GameObject attackIndicatorInstance;
        private Coroutine attackIndicatorCoroutine;

        // Runtime state
        private NavMeshAgent agent;
        private Transform player;
        private PlayerMovement playerMovement;
        private bool isDefeated;
        private bool isStunned;
        private bool isExecutingUltimate;
        private bool isExecutingAttack;
        private bool isInUltimateHoverPhase;
        private bool ultimateCanceledByAerial;
        private int attacksSinceUltimate;
        private bool hasUsedUltimate;
        private int aerialHitsReceived;
        private float ultimateHoverPauseTimer;
        private Coroutine mainLoopCoroutine;
        private Coroutine currentAttackCoroutine;
        private Dictionary<string, float> attackCooldowns = new Dictionary<string, float>();
        private AttackCategory currentAttackCategory = AttackCategory.Halberd;
        private bool pickedUpWeaponThisCombo;
        private float baseAgentSpeed;
        private bool waitingForUltimateLowSweepEvent;
        private bool waitingForUltimateMidSweepEvent;
        private Vector3 pendingUltimateSweepTargetPos;
        private bool waitingForSpareTossReleaseEvent;
        private bool spareTossReleaseEventReceived;
        private bool spinDashHitboxPhaseActive;
        private bool spinDashHitboxArmed;
        private Coroutine spinDashHitboxRearmCoroutine;
        private int spinDashRemainingHits;
        private float spinDashTriggerDamage;
        private bool animeDashTriggerArmed;
        private float animeDashTriggerDamage;
        private float defaultAnimatorSpeed = 1f;

        #region IQueuedAttacker Implementation
        
        public bool IsBoss => true;
        public bool IsAlive => currentHealth > 0f && !isDefeated;
        public GameObject AttackerGameObject => gameObject;
        
        public bool CanAttackFromQueue()
        {
            if (exemptFromAttackQueue) return true;
            if (EnemyAttackQueueManager.Instance == null) return true;
            return EnemyAttackQueueManager.Instance.CanAttack(this);
        }
        
        public void NotifyAttackBegin()
        {
            if (exemptFromAttackQueue) return;
            EnemyAttackQueueManager.Instance?.BeginAttack(this);
        }
        
        public void NotifyAttackEnd()
        {
            if (exemptFromAttackQueue) return;
            EnemyAttackQueueManager.Instance?.FinishAttack(this);
        }
        
        public void RegisterWithAttackQueue()
        {
            if (exemptFromAttackQueue) return;
            EnemyAttackQueueManager.Instance?.Register(this);
        }
        
        public void UnregisterFromAttackQueue()
        {
            if (exemptFromAttackQueue) return;
            EnemyAttackQueueManager.Instance?.Unregister(this);
        }
        
        #endregion

        #region IHealthSystem Implementation
        
        public float currentHP => currentHealth;
        public float maxHP => maxHealth;
        
        public void HealHP(float hp)
        {
            currentHealth = Mathf.Min(currentHealth + hp, maxHealth);
        }
        
        public void LoseHP(float damage)
        {
            if (isDefeated) return;
            
            // Invulnerable in dummy mode
            if (betaDummyMode) return;
            
            float finalDamage = damage;
            if (isDamageReductionActive)
            {
                finalDamage *= currentDamageReduction;
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Damage reduced: {damage} -> {finalDamage} (reduction: {currentDamageReduction})");
#endif
            }
            
            if (comboSystem != null && comboSystem.IsInRecovery)
            {
                finalDamage *= comboSystem.GetDamageMultiplier();
            }
            
            currentHealth -= finalDamage;

            // During dedicated ultimate hover phase, player damage pauses the resolution timer briefly.
            if (isInUltimateHoverPhase && finalDamage > 0f)
            {
                ultimateHoverPauseTimer = Mathf.Max(ultimateHoverPauseTimer, UltimateSettings.HoverTimerPauseOnDamage);
            }

            // Notify aggression system that player hit the boss
            if (aggressionSystem != null)
            {
                aggressionSystem.OnPlayerHitsBoss();
            }
            
            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                OnDefeated();
            }
        }
        
        #endregion

        #region Attack Indicator VFX
        /// <summary>
        /// Shows the attack indicator VFX before an attack.
        /// </summary>
        /// <param name="customOffset">Optional: Override the default offset for this specific attack.</param>
        /// <param name="customDuration">Optional: Override the default duration for this specific attack.</param>
        public void ShowAttackIndicator(Vector3? customOffset = null, float? customDuration = null)
        {
            if (attackIndicatorPrefab == null) return;

            HideAttackIndicator();

            Vector3 offset = customOffset ?? attackIndicatorOffset;
            Vector3 spawnPos = transform.TransformPoint(offset);
            Quaternion spawnRot = transform.rotation;

            attackIndicatorInstance = Instantiate(attackIndicatorPrefab, spawnPos, spawnRot);
            
            if (attackIndicatorScale != 1f)
            {
                attackIndicatorInstance.transform.localScale *= attackIndicatorScale;
            }

            if (attackIndicatorFollowsBoss)
            {
                attackIndicatorInstance.transform.SetParent(transform);
                attackIndicatorInstance.transform.localPosition = offset;
                attackIndicatorInstance.transform.localRotation = Quaternion.identity;
            }

            float duration = customDuration ?? attackIndicatorDuration;
            if (duration > 0f)
            {
                attackIndicatorCoroutine = StartCoroutine(HideIndicatorAfterDelay(duration));
            }

#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Attack indicator shown at offset {offset}");
#endif
        }

        /// <summary>
        /// Hides/destroys the attack indicator VFX.
        /// </summary>
        public void HideAttackIndicator()
        {
            if (attackIndicatorCoroutine != null)
            {
                StopCoroutine(attackIndicatorCoroutine);
                attackIndicatorCoroutine = null;
            }

            if (attackIndicatorInstance != null)
            {
                Destroy(attackIndicatorInstance);
                attackIndicatorInstance = null;
            }
        }

        /// <summary>
        /// Animation Event receiver: Shows the attack indicator.
        /// </summary>
        public void AttackIndicatorStart()
        {
            ShowAttackIndicator();
        }

        /// <summary>
        /// Animation Event receiver: Hides the attack indicator.
        /// </summary>
        public void AttackIndicatorEnd()
        {
            HideAttackIndicator();
        }

        private IEnumerator HideIndicatorAfterDelay(float delay)
        {
            yield return WaitForSecondsCache.Get(delay);
            HideAttackIndicator();
        }
        #endregion

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            comboSystem = comboSystem ?? GetComponent<CleanserComboSystem>();
            dualWieldSystem = dualWieldSystem ?? GetComponent<CleanserDualWieldSystem>();
            platformController = platformController ?? GetComponent<CleanserPlatformController>();
            aggressionSystem = aggressionSystem ?? GetComponent<CleanserAggressionSystem>();
            animController = animController ?? GetComponent<CleanserAnimController>() ?? GetComponentInChildren<CleanserAnimController>();
            animator = animator ?? GetComponentInChildren<Animator>();
            defaultAnimatorSpeed = animator != null ? Mathf.Max(0.01f, animator.speed) : 1f;
            
            ApplyMovementSettings();
            InitializeHealthBar();
            CachePlayerReference();
            InitializeAttackDescriptors();
            EnsurePlayerSlideOffSurface();

            SetAllMeleeHitboxesEnabled(false);
            if (spinDashHitboxCollider != null)
            {
                spinDashHitboxCollider.enabled = false;

                var relay = spinDashHitboxCollider.GetComponent<CleanserSpinDashHitboxRelay>();
                if (relay == null)
                    relay = spinDashHitboxCollider.gameObject.AddComponent<CleanserSpinDashHitboxRelay>();
                relay.Owner = this;
            }
        }

        private void ApplyMovementSettings()
        {
            if (agent == null) return;
            
            
            if (profile != null)
            {
                // Use profile settings
                float speed = Random.Range(profile.SpeedRange.x, profile.SpeedRange.y);
                agent.speed = speed;
                baseAgentSpeed = speed;
                agent.angularSpeed = profile.AngularSpeed;
                agent.acceleration = profile.Acceleration;
                agent.stoppingDistance = profile.StoppingDistance;
                agent.avoidancePriority = profile.AvoidancePriority;
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Applied profile settings: speed={speed}, angularSpeed={profile.AngularSpeed}");
#endif
            }
            else
            {
                // Use fallback settings
                agent.speed = FallbackSpeed;
                baseAgentSpeed = FallbackSpeed;
                agent.angularSpeed = FallbackAngularSpeed;
                agent.acceleration = FallbackAcceleration;
                agent.stoppingDistance = FallbackStoppingDistance;
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.LogWarning(nameof(CleanserBrain), "[Cleanser] No profile assigned, using fallback movement settings.");
#endif
            }
        }

        private void InitializeHealthBar()
        {
            displayedHealth = maxHealth;
            if (healthBar != null)
            {
                healthBar.SetHealth(currentHealth, maxHealth);
            }
        }

        /// <summary>
        /// Ensures a PlayerSlideOffSurface component exists to prevent the player from getting stuck on top of this boss.
        /// </summary>
        private void EnsurePlayerSlideOffSurface()
        {
            if (GetComponent<PlayerSlideOffSurface>() == null)
            {
                gameObject.AddComponent<PlayerSlideOffSurface>();
            }
        }

        private void OnEnable()
        {
            RegisterWithAttackQueue();
            SetAllMeleeHitboxesEnabled(false);
            if (spinDashHitboxCollider != null)
                spinDashHitboxCollider.enabled = false;
            
            // Subscribe to pause events for audio handling
            PauseCoordinator.OnPaused += OnGamePaused;
            PauseCoordinator.OnResumed += OnGameResumed;
            
            if (mainLoopCoroutine != null)
                StopCoroutine(mainLoopCoroutine);
            mainLoopCoroutine = StartCoroutine(MainCombatLoop());
        }

        private void OnDisable()
        {
            UnregisterFromAttackQueue();
            
            // Unsubscribe from pause events
            PauseCoordinator.OnPaused -= OnGamePaused;
            PauseCoordinator.OnResumed -= OnGameResumed;
            
            if (mainLoopCoroutine != null)
            {
                StopCoroutine(mainLoopCoroutine);
                mainLoopCoroutine = null;
            }
            
            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }

            EndSpinDashHitboxPhase();
            SetAllMeleeHitboxesEnabled(false);
            ResetAnimationSpeed();
        }

        private void Update()
        {
            UpdateAnimatorParameters();
            UpdateHealthBar();
            
            // Skip aggression updates in dummy mode
            if (!betaDummyMode)
            {
                UpdateAggressionBasedSpeed();
                UpdatePlayerGuardingAggression();
            }
        }
        
        /// <summary>
        /// Called when the game is paused. Pauses audio sources.
        /// </summary>
        private void OnGamePaused()
        {
            if (sfxSource != null && sfxSource.isPlaying)
            {
                sfxSource.Pause();
            }
        }
        
        /// <summary>
        /// Called when the game is resumed. Resumes audio sources.
        /// </summary>
        private void OnGameResumed()
        {
            if (sfxSource != null)
            {
                sfxSource.UnPause();
            }
        }

        private void UpdateAnimatorParameters()
        {
            if (agent == null) return;

            if (animator != null && !string.IsNullOrEmpty(paramIsPlayerHere))
            {
                animator.SetBool(paramIsPlayerHere, player != null);
            }
            
            float speed = agent.velocity.magnitude;
            float normalizedSpeed = agent.speed > 0f ? speed / agent.speed : 0f;

            // Prevent locomotion updates from interrupting active attack/ultimate/stun animations.
            if (isExecutingAttack || isExecutingUltimate || isStunned)
                return;
            
            // Use animation controller if available, otherwise fall back to direct animator
            if (animController != null)
            {
                animController.PlayLocomotion(normalizedSpeed);
            }
            else if (animator != null)
            {
                animator.SetFloat(paramMoveSpeed, speed);
                animator.SetBool(paramIsMoving, speed > 0.1f);
            }
        }

        private void UpdateHealthBar()
        {
            if (healthBar == null) return;
            displayedHealth = Mathf.MoveTowards(displayedHealth, currentHealth, healthBarLerpSpeed * Time.deltaTime * maxHealth);
            healthBar.SetHealth(displayedHealth, maxHealth);
        }

        private void UpdateAggressionBasedSpeed()
        {
            if (agent == null || aggressionSystem == null) return;
            
            float speedMultiplier = aggressionSystem.GetSpeedMultiplier();
            agent.speed = baseAgentSpeed * speedMultiplier;
        }

        private void UpdatePlayerGuardingAggression()
        {
            // Guard detection is now handled internally by CleanserAggressionSystem
            // when detectPlayerGuarding is enabled (default: true).
            // This method is kept for backwards compatibility but does nothing.
            // If you need to manually control guard detection, disable detectPlayerGuarding
            // on the CleanserAggressionSystem and call aggressionSystem.OnPlayerGuarding(Time.deltaTime) here.
        }

        private void CachePlayerReference()
        {
            if (PlayerPresenceManager.IsPlayerPresent)
            {
                player = PlayerPresenceManager.PlayerTransform;
            }
            else
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                    player = playerObj.transform;
            }

            if (player != null)
            {
                playerMovement = player.GetComponent<PlayerMovement>()
                    ?? player.GetComponentInParent<PlayerMovement>()
                    ?? player.GetComponentInChildren<PlayerMovement>();
                    
                if (playerMovement != null)
                    player = playerMovement.transform;
            }
        }

        private void InitializeAttackDescriptors()
        {
            // Only initialize with defaults if not configured in inspector (ID is empty)
            // This allows designer overrides via inspector while providing sensible defaults
            
            if (string.IsNullOrEmpty(LungeAttack?.ID))
            {
                LungeAttack = new CleanserAttackDescriptor
                {
                    ID = "Lunge",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 20f,
                    Cooldown = 2f,
                    RangeMin = 3f,
                    RangeMax = 8f,
                    AnimationTrigger = "Attack_Lunge",
                    HasWindupDamageReduction = true,
                    WindupDamageReduction = 0.5f,
                    IncludesMovement = true,
                    MovementDistance = 5f
                };
            }

            if (string.IsNullOrEmpty(LungeBlockAttack?.ID))
            {
                LungeBlockAttack = new CleanserAttackDescriptor
                {
                    ID = "LungeBlock",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 18f,
                    Cooldown = 2.5f,
                    RangeMin = 2f,
                    RangeMax = 8f,
                    AnimationTrigger = "Attack_LungeBlock",
                    HasWindupDamageReduction = true,
                    WindupDamageReduction = 0.5f,
                    IncludesMovement = true,
                    MovementDistance = 4f
                };
            }

            if (string.IsNullOrEmpty(OverheadCleaveAttack?.ID))
            {
                OverheadCleaveAttack = new CleanserAttackDescriptor
                {
                    ID = "OverheadCleave",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 25f,
                    Cooldown = 2.5f,
                    RangeMin = 0f,
                    RangeMax = 4f,
                    AnimationTrigger = "Attack_OverheadCleave"
                };
            }

            if (string.IsNullOrEmpty(CleaveAttack?.ID))
            {
                CleaveAttack = new CleanserAttackDescriptor
                {
                    ID = "Cleave",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 20f,
                    Cooldown = 2.2f,
                    RangeMin = 0f,
                    RangeMax = 4f,
                    AnimationTrigger = "Attack_Cleave"
                };
            }

            if (string.IsNullOrEmpty(CleaveAdvanceAttack?.ID))
            {
                CleaveAdvanceAttack = new CleanserAttackDescriptor
                {
                    ID = "CleaveAdvance",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 22f,
                    Cooldown = 2.6f,
                    RangeMin = 1f,
                    RangeMax = 7f,
                    AnimationTrigger = "Attack_CleaveAdvance",
                    IncludesMovement = true,
                    MovementDistance = 3.5f
                };
            }

            if (string.IsNullOrEmpty(PommelStrikeAttack?.ID))
            {
                PommelStrikeAttack = new CleanserAttackDescriptor
                {
                    ID = "PommelStrike",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 16f,
                    Cooldown = 2f,
                    RangeMin = 0f,
                    RangeMax = 3f,
                    AnimationTrigger = "Attack_PommelStrike"
                };
            }

            if (string.IsNullOrEmpty(DiagUpwardSlashAttack?.ID))
            {
                DiagUpwardSlashAttack = new CleanserAttackDescriptor
                {
                    ID = "DiagUpwardSlash",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 17f,
                    Cooldown = 2f,
                    RangeMin = 0f,
                    RangeMax = 4f,
                    AnimationTrigger = "Attack_DiagUpwardSlash"
                };
            }

            if (string.IsNullOrEmpty(WingBashAttack?.ID))
            {
                WingBashAttack = new CleanserAttackDescriptor
                {
                    ID = "WingBash",
                    Category = AttackCategory.Wing,
                    BaseDamage = 14f,
                    Cooldown = 2f,
                    RangeMin = 0f,
                    RangeMax = 3f,
                    AnimationTrigger = "Attack_WingBash"
                };
            }

            if (string.IsNullOrEmpty(SlashIntoSlapAttack?.ID))
            {
                SlashIntoSlapAttack = new CleanserAttackDescriptor
                {
                    ID = "SlashIntoSlap",
                    Category = AttackCategory.Mixed,
                    BaseDamage = 15f,
                    Cooldown = 3f,
                    RangeMin = 0f,
                    RangeMax = 5f,
                    AnimationTrigger = "Attack_SlashSlap",
                    IsMultiPart = true,
                    PartCategories = new[] { AttackCategory.Halberd, AttackCategory.Wing },
                    IncludesMovement = true,
                    MovementDistance = 3f
                };
            }

            if (string.IsNullOrEmpty(RakeIntoSpinSlashAttack?.ID))
            {
                RakeIntoSpinSlashAttack = new CleanserAttackDescriptor
                {
                    ID = "RakeIntoSpinSlash",
                    Category = AttackCategory.Mixed,
                    BaseDamage = 18f,
                    Cooldown = 3f,
                    RangeMin = 0f,
                    RangeMax = 4f,
                    AnimationTrigger = "Attack_RakeSpin",
                    IsMultiPart = true,
                    PartCategories = new[] { AttackCategory.Wing, AttackCategory.Halberd }
                };
            }

            if (string.IsNullOrEmpty(LegSweepAttack?.ID))
            {
                LegSweepAttack = new CleanserAttackDescriptor
                {
                    ID = "LegSweep",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 15f,
                    Cooldown = 4f,
                    RangeMin = 0f,
                    RangeMax = 5f,
                    AnimationTrigger = "Attack_LegSweep",
                    CanStunPlayer = true
                };
            }

            if (string.IsNullOrEmpty(HighDiveAttack?.ID))
            {
                HighDiveAttack = new CleanserAttackDescriptor
                {
                    ID = "HighDive",
                    Category = AttackCategory.Halberd,
                    BaseDamage = 40f,
                    Cooldown = 8f,
                    RangeMin = 0f,
                    RangeMax = 10f,
                    AnimationTrigger = "Attack_HighDive"
                };
            }

            if (AnimeDashSettings == null)
                AnimeDashSettings = new AnimeDashSlashConfig();
        }

        #region Main Combat Loop

        private IEnumerator MainCombatLoop()
        {
            yield return new WaitForSeconds(0.5f);
            
            while (!isDefeated)
            {
                // Beta dummy mode: just follow the player, no attacks
                if (betaDummyMode)
                {
                    yield return ExecuteDummyModeFollow();
                    continue;
                }
                
                while (isStunned)
                {
                    yield return null;
                }

                // Check if aggression system is countering
                if (aggressionSystem != null && aggressionSystem.IsCountering)
                {
                    yield return null;
                    continue;
                }
                
                if (ShouldTriggerUltimate())
                {
                    yield return ExecuteUltimateAttack();
                    continue;
                }
                
                if (comboSystem != null && comboSystem.IsInRecovery)
                {
                    yield return null;
                    continue;
                }
                
                if (comboSystem != null && !comboSystem.IsExecutingCombo)
                {
                    float dist = player != null ? Vector3.Distance(transform.position, player.position) : 0f;
                    var combo = comboSystem.SelectCombo(dist);
                    
                    if (combo != null)
                    {
                        yield return ExecuteCombo(combo);
                    }
                    else
                    {
                        // Use aggression-based movement when no combo available
                        yield return ExecuteAggressionBasedMovement(0.3f);
                    }
                }
                else
                {
                    yield return null;
                }
            }
        }

        /// <summary>
        /// Executes movement behavior based on current aggression level.
        /// </summary>
        private IEnumerator ExecuteAggressionBasedMovement(float duration)
        {
            if (player == null) yield break;

            // Check if we should use gap-closing dash at high aggression
            if (aggressionSystem != null && aggressionSystem.CanUseDash())
            {
                float dist = Vector3.Distance(transform.position, player.position);
                if (dist >= GapCloseDashSettings.MinDistanceToUse)
                {
                    yield return ExecuteGapClosingDash();
                    yield break;
                }
            }

            // Get movement behavior from aggression system
            AggressionMovementConfig movementConfig = aggressionSystem?.GetCurrentMovementConfig();
            
            if (movementConfig != null && movementConfig.AggressivelyClosesDistance)
            {
                // Aggressively close distance
                yield return MoveTowardPlayer(duration);
            }
            else if (movementConfig != null)
            {
                // More passive/observant movement - strafe or maintain distance
                float dist = Vector3.Distance(transform.position, player.position);
                float preferredDist = movementConfig.PreferredDistance;

                if (dist < preferredDist - 1f)
                {
                    // Too close, back off slightly
                    yield return MoveAwayFromPlayer(duration * 0.5f);
                }
                else if (dist > preferredDist + 2f)
                {
                    // Too far, approach slowly
                    yield return MoveTowardPlayer(duration * 0.5f);
                }
                else
                {
                    // In range - strafe or idle based on strafe chance
                    if (Random.value < movementConfig.StrafeChance)
                    {
                        yield return StrafeAroundPlayer(duration);
                    }
                    else
                    {
                        // Face player and wait (observant behavior)
                        yield return FaceTarget(player, duration);
                    }
                }
            }
            else
            {
                // Fallback to basic movement
                yield return MoveTowardPlayer(duration);
            }
        }

        /// <summary>
        /// Simple follow behavior for beta dummy mode. 
        /// Follows the player and stops at a reasonable distance. No attacks.
        /// </summary>
        private IEnumerator ExecuteDummyModeFollow()
        {
            if (player == null) yield break;

            float dist = Vector3.Distance(transform.position, player.position);
            
            if (dist > dummyModeStoppingDistance)
            {
                // Move toward player
                agent.SetDestination(player.position);
                agent.stoppingDistance = dummyModeStoppingDistance;
            }
            else
            {
                // Stop and face player
                agent.ResetPath();
                yield return FaceTarget(player, 0.1f);
            }
            
            yield return null;
        }

        private IEnumerator ExecuteCombo(CleanserCombo combo)
        {
            comboSystem.StartCombo(combo);
            pickedUpWeaponThisCombo = false;
            
            while (comboSystem.IsExecutingCombo)
            {
                var step = comboSystem.GetCurrentStep();
                if (step == null)
                    break;

                // Step-directed pickup (designer controlled).
                if (step.PickupSpareWeaponBefore && dualWieldSystem != null
                    && !dualWieldSystem.IsHoldingSpareWeapon
                    && dualWieldSystem.AvailableSpareWeaponCount > 0)
                {
                    dualWieldSystem.PickupSpareWeapon();
                    yield return new WaitUntil(() => !dualWieldSystem.IsPickingUp);
                    pickedUpWeaponThisCombo = true;
                }
                
                if (step.PreDelay > 0f)
                {
                    yield return new WaitForSeconds(step.PreDelay);
                }
                
                if (step.IsFinisher)
                {
                    yield return ExecuteStrongAttack(step.StrongAttack);
                }
                else
                {
                    yield return ExecuteBasicAttack(step.BasicAttack);
                }
                
                attacksSinceUltimate++;
                
                if (!comboSystem.AdvanceStep())
                    break;
                
                yield return null;
            }
            
            // Clean up stockpiled/lodged spare weapons at END of combo.
            if (dualWieldSystem != null)
            {
                while (dualWieldSystem.IsHoldingSpareWeapon)
                {
                    dualWieldSystem.ReleaseCurrentWeapon();
                }

                if (dualWieldSystem.LodgedWeaponCount > 0)
                {
                    dualWieldSystem.ReturnAllLodgedWeaponsToRest();
                }
            }
        }

        private IEnumerator ExecuteBasicAttack(CleanserBasicAttack attackType)
        {
            NotifyAttackBegin();
            isExecutingAttack = true;
            
            switch (attackType)
            {
                case CleanserBasicAttack.Lunge:
                    yield return ExecuteAttackWithAnimationEvents(LungeAttack);
                    break;
                case CleanserBasicAttack.LungeBlock:
                    yield return ExecuteAttackWithAnimationEvents(LungeBlockAttack);
                    break;
                case CleanserBasicAttack.OverheadCleave:
                    yield return ExecuteAttackWithAnimationEvents(OverheadCleaveAttack);
                    break;
                case CleanserBasicAttack.Cleave:
                    yield return ExecuteAttackWithAnimationEvents(CleaveAttack);
                    break;
                case CleanserBasicAttack.CleaveAdvance:
                    yield return ExecuteAttackWithAnimationEvents(CleaveAdvanceAttack);
                    break;
                case CleanserBasicAttack.PommelStrike:
                    yield return ExecuteAttackWithAnimationEvents(PommelStrikeAttack);
                    break;
                case CleanserBasicAttack.DiagUpwardSlash:
                    yield return ExecuteAttackWithAnimationEvents(DiagUpwardSlashAttack);
                    break;
                case CleanserBasicAttack.WingBash:
                    yield return ExecuteAttackWithAnimationEvents(WingBashAttack);
                    break;
                case CleanserBasicAttack.SlashIntoSlap:
                    yield return ExecuteAttackWithAnimationEvents(SlashIntoSlapAttack);
                    break;
                case CleanserBasicAttack.RakeIntoSpinSlash:
                    yield return ExecuteAttackWithAnimationEvents(RakeIntoSpinSlashAttack);
                    break;
                case CleanserBasicAttack.SpareToss:
                    yield return ExecuteSpareToss();
                    break;
                case CleanserBasicAttack.LegSweep:
                    yield return ExecuteAttackWithAnimationEvents(LegSweepAttack);
                    break;
                case CleanserBasicAttack.Knockback:
                    yield return ExecuteKnockbackAttack();
                    break;
                case (CleanserBasicAttack)9:
                    // Legacy serialized value: MiniCrescentWave now routes to DiagUpwardSlash behavior.
                    yield return ExecuteAttackWithAnimationEvents(DiagUpwardSlashAttack);
                    break;
                case (CleanserBasicAttack)16:
                    // Legacy serialized value: Basic SpinDash now routes to SpinDash strong behavior.
                    yield return ExecuteSpinDash();
                    break;
            }
            
            isExecutingAttack = false;
            NotifyAttackEnd();
        }

        private IEnumerator ExecuteStrongAttack(CleanserStrongAttack attackType)
        {
            NotifyAttackBegin();
            isExecutingAttack = true;
            
            switch (attackType)
            {
                case CleanserStrongAttack.HighDive:
                    yield return ExecuteHighDive();
                    break;
                case CleanserStrongAttack.AnimeDashSlash:
                    yield return ExecuteAnimeDashSlash();
                    break;
                case CleanserStrongAttack.Whirlwind:
                    yield return ExecuteWhirlwind();
                    break;
                case CleanserStrongAttack.SpinDash:
                    yield return ExecuteSpinDash();
                    break;
            }
            
            isExecutingAttack = false;
            NotifyAttackEnd();
        }

        #endregion

        #region Animation-Event-Driven Attack Execution

        /// <summary>
        /// Executes an attack by triggering the animation and waiting for animation events to drive timing.
        /// Uses Start/End hitbox events like boxer enemies and base crawlers.
        /// </summary>
        private IEnumerator ExecuteAttackWithAnimationEvents(CleanserAttackDescriptor attack)
        {
            if (attack == null) yield break;

            ApplyAnimationSpeedMultiplier(attack.AnimationSpeedMultiplier);
            
            currentAttack = attack;
            currentAttackCategory = attack.Category;
            
            // Face player before attack
            yield return FaceTarget(player, 0.15f);
            
            // Show attack indicator at start of attack
            ShowAttackIndicator();
            
            // Setup damage reduction if applicable
            if (attack.HasWindupDamageReduction)
            {
                SetDamageReduction(true, attack.WindupDamageReduction);
            }
            
            // Play SFX
            PlaySFX(attack.AttackSFX);
            
            // Spawn VFX
            if (attack.AttackVFX != null)
            {
                Instantiate(attack.AttackVFX, transform.position, transform.rotation);
            }
            
            // Trigger the single animation clip
            TriggerAnimation(attack.AnimationTrigger);
            
            // Wait for animation to complete via animation event calling OnAttackAnimationComplete()
            // For testing without animations, use a fallback timeout
            float timeout = 3f;
            float elapsed = 0f;
            attackAnimationComplete = false;
            
            while (!attackAnimationComplete && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                
                // Continuous hit checking while hitbox is active
                if (isHitboxActive)
                {
                    CheckMeleeHit(currentAttack.BaseDamage, currentAttackCategory);
                }
                
                yield return null;
            }
            
            // Ensure hitbox is disabled
            isHitboxActive = false;
            
            // Hide attack indicator when attack completes
            HideAttackIndicator();
            
            // Disable damage reduction
            SetDamageReduction(false, 1f);

            ResetAnimationSpeed();
            
            // If we timed out (no animation), do a basic hit check
            if (elapsed >= timeout)
            {
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.LogWarning(nameof(CleanserBrain), $"[Cleanser] Attack '{attack.ID}' timed out waiting for animation event. Using fallback.");
#endif
                CheckMeleeHit(attack.BaseDamage, attack.Category);
            }
            
            currentAttack = null;
        }

        // Runtime state for animation events
        private bool attackAnimationComplete = false;
        private bool isHitboxActive = false;
        private bool hasAppliedDamageThisHitboxWindow = false;
        private CleanserAttackDescriptor currentAttack;

        /// <summary>
        /// Animation Event: Called when the attack animation is complete.
        /// </summary>
        public void OnAttackAnimationComplete()
        {
            attackAnimationComplete = true;
            isHitboxActive = false;
        }

        /// <summary>
        /// Animation Event: Called to enable the hitbox at the start of the active frames.
        /// </summary>
        public void OnAttackHitboxStart()
        {
            if (!isExecutingAttack) return;
            isHitboxActive = true;
            hasAppliedDamageThisHitboxWindow = false;

            SetAllMeleeHitboxesEnabled(false);
            if (currentAttackCategory == AttackCategory.Wing)
            {
                SetWingHitboxEnabled(true);
            }
            else
            {
                SetHalberdHitboxesEnabled(true);
            }
            
            // Hide attack indicator when hitbox becomes active (if duration was 0)
            if (attackIndicatorDuration <= 0f)
            {
                HideAttackIndicator();
            }
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Hitbox enabled.");
#endif
        }

        /// <summary>
        /// Animation Event: Called to disable the hitbox at the end of the active frames.
        /// </summary>
        public void OnAttackHitboxEnd()
        {
            isHitboxActive = false;
            hasAppliedDamageThisHitboxWindow = false;
            SetAllMeleeHitboxesEnabled(false);
            
            // Play impact SFX/VFX when hitbox ends (attack follow-through)
            if (currentAttack != null)
            {
                PlaySFX(currentAttack.ImpactSFX);
                if (currentAttack.ImpactVFX != null)
                {
                    Instantiate(currentAttack.ImpactVFX, transform.position + transform.forward, transform.rotation);
                }
            }
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Hitbox disabled.");
#endif
        }

        /// <summary>
        /// Animation Event: Halberd-specific hitbox start.
        /// </summary>
        public void OnHalberdHitboxStart()
        {
            currentAttackCategory = AttackCategory.Halberd;
            OnAttackHitboxStart();
        }

        /// <summary>
        /// Animation Event: Halberd-specific hitbox end.
        /// </summary>
        public void OnHalberdHitboxEnd()
        {
            OnAttackHitboxEnd();
        }

        /// <summary>
        /// Animation Event: Wing-specific hitbox start.
        /// </summary>
        public void OnWingHitboxStart()
        {
            currentAttackCategory = AttackCategory.Wing;
            OnAttackHitboxStart();
        }

        /// <summary>
        /// Animation Event: Wing-specific hitbox end.
        /// </summary>
        public void OnWingHitboxEnd()
        {
            OnAttackHitboxEnd();
        }

        /// <summary>
        /// Animation Event: Called for multi-part attacks to switch category mid-attack.
        /// </summary>
        public void OnSwitchToWingCategory()
        {
            currentAttackCategory = AttackCategory.Wing;
            if (isHitboxActive)
            {
                SetHalberdHitboxesEnabled(false);
                SetWingHitboxEnabled(true);
            }
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Switched to Wing category.");
#endif
        }

        /// <summary>
        /// Animation Event: Called for multi-part attacks to switch category mid-attack.
        /// </summary>
        public void OnSwitchToHalberdCategory()
        {
            currentAttackCategory = AttackCategory.Halberd;
            if (isHitboxActive)
            {
                SetWingHitboxEnabled(false);
                SetHalberdHitboxesEnabled(true);
            }
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Switched to Halberd category.");
#endif
        }

        /// <summary>
        /// Animation Event: Called to trigger movement during an attack.
        /// </summary>
        public void OnAttackMovementStart()
        {
            if (!isExecutingAttack || player == null) return;
            StartCoroutine(DoAttackMovement());
        }

        /// <summary>
        /// Animation Event: Spawns DiagUpwardSlash projectile(s) while the halberd hitbox attack is active.
        /// </summary>
        public void OnDiagUpwardSlashProjectile()
        {
            if (!isExecutingAttack || player == null)
                return;

            SpawnCrescentArcProjectiles(CrescentWaveProjectileConfiguration, player.position, 0f);
        }

        /// <summary>
        /// Animation Event: Launches stockpiled spare weapons for SpareToss timing.
        /// </summary>
        public void OnSpareTossRelease()
        {
            if (!isExecutingAttack)
                return;

            spareTossReleaseEventReceived = true;
            waitingForSpareTossReleaseEvent = false;
        }

        /// <summary>
        /// Animation Event: Spawns the ultimate low sweep projectile(s).
        /// </summary>
        public void OnUltimateLowSweepProjectile()
        {
            if (!isExecutingUltimate)
                return;

            waitingForUltimateLowSweepEvent = false;
            SpawnCrescentWave(UltimateSettings.LowSweepHeight, pendingUltimateSweepTargetPos);
            PlaySFX(UltimateSettings.SweepSFX);
        }

        /// <summary>
        /// Animation Event: Spawns the ultimate mid sweep projectile(s).
        /// </summary>
        public void OnUltimateMidSweepProjectile()
        {
            if (!isExecutingUltimate)
                return;

            waitingForUltimateMidSweepEvent = false;
            SpawnCrescentWave(UltimateSettings.MidSweepHeight, pendingUltimateSweepTargetPos);
            PlaySFX(UltimateSettings.SweepSFX);
        }

        private IEnumerator DoAttackMovement()
        {
            float moveDist = currentAttack?.MovementDistance ?? 3f;
            if (moveDist <= 0f) yield break;
            
            Vector3 dir = (player.position - transform.position).normalized;
            float moveDuration = 0.3f;
            float moveSpeed = moveDist / moveDuration;
            float elapsed = 0f;
            
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                agent.Move(dir * moveSpeed * Time.deltaTime);
                yield return null;
            }
        }

        #endregion

        #region Special Attack Implementations

        private IEnumerator ExecuteSpareToss()
        {
            if (dualWieldSystem == null)
            {
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.LogWarning(nameof(CleanserBrain), "[Cleanser] SpareToss failed: DualWield/stockpile system missing.");
#endif
                yield break;
            }

            yield return FaceTarget(player, 0.3f);

            ApplyAnimationSpeedMultiplier(SpareTossSettings.AnimationSpeedMultiplier);

            // Use animation trigger from config
            TriggerAnimation(SpareTossSettings.AnimationTrigger);
            PlaySFX(SpareTossSettings.ThrowSFX);

            waitingForSpareTossReleaseEvent = true;
            spareTossReleaseEventReceived = false;

            const float releaseFallbackTimeout = 1.2f;
            float elapsed = 0f;
            while (!spareTossReleaseEventReceived && elapsed < releaseFallbackTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            waitingForSpareTossReleaseEvent = false;

            if (!spareTossReleaseEventReceived)
            {
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.LogWarning(nameof(CleanserBrain), "[Cleanser] SpareToss release event not received. Using fallback release timing.");
#endif
            }

            Vector3 tossCenter = player != null ? player.position : transform.position + transform.forward * 6f;
            yield return dualWieldSystem.LaunchStockpiledWeaponsToGround(tossCenter);
            
            yield return new WaitForSeconds(0.15f);
            ResetAnimationSpeed();
        }

        private IEnumerator ExecuteSpinDash()
        {
            if (player == null) yield break;

            yield return FaceTarget(player, 0.1f);
            ShowAttackIndicator();

            ApplyAnimationSpeedMultiplier(SpinDashSettings.WindupAnimSpeedMultiplier);
            TriggerAnimation(SpinDashSettings.WindupTrigger);
            PlaySFX(SpinDashSettings.WindupSFX);
            yield return WaitForAnimationStateToFinish(SpinDashSettings.WindupTrigger, 0.8f);

            if (!string.IsNullOrEmpty(SpinDashSettings.HPWindupTrigger))
            {
                ApplyAnimationSpeedMultiplier(SpinDashSettings.HPWindupAnimSpeedMultiplier);
                TriggerAnimation(SpinDashSettings.HPWindupTrigger);
                yield return WaitForAnimationStateToFinish(SpinDashSettings.HPWindupTrigger, 0.6f);
            }

            ApplyAnimationSpeedMultiplier(SpinDashSettings.HoldPoseAnimSpeedMultiplier);
            TriggerAnimation(SpinDashSettings.HoldPoseTrigger);
            PlaySFX(SpinDashSettings.HoldSFX);

            GameObject spinVfxInstance = null;
            if (SpinDashSettings.SpinVFX != null)
            {
                spinVfxInstance = Instantiate(SpinDashSettings.SpinVFX, transform.position, transform.rotation, transform);
            }

            var dashPoints = dualWieldSystem != null ? dualWieldSystem.GetLodgedWeaponPositions() : new List<Vector3>();
            if (dashPoints.Count == 0 && player != null)
            {
                dashPoints.Add(player.position);
            }

            agent.enabled = false;
            BeginSpinDashHitboxPhase();
            for (int i = 0; i < dashPoints.Count; i++)
            {
                Vector3 target = dashPoints[i];
                target.y = transform.position.y;

                Vector3 start = transform.position;
                float distance = Vector3.Distance(start, target);
                float dashDuration = GetSpinDashSegmentDuration(distance);

                float elapsed = 0f;
                while (elapsed < dashDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / dashDuration;
                    transform.position = Vector3.Lerp(start, target, t);

                    Vector3 moveDir = (target - transform.position).normalized;
                    moveDir.y = 0f;
                    if (moveDir.sqrMagnitude > 0.001f)
                    {
                        transform.forward = Vector3.Slerp(transform.forward, moveDir, Time.deltaTime * 14f);
                    }

                    yield return null;
                }

                yield return new WaitForSeconds(0.05f);
            }

            // Final dash ends at the player.
            if (player != null)
            {
                Vector3 finalTarget = player.position;
                finalTarget.y = transform.position.y;

                Vector3 start = transform.position;
                float distance = Vector3.Distance(start, finalTarget);
                float dashDuration = GetSpinDashSegmentDuration(distance);
                float elapsed = 0f;

                while (elapsed < dashDuration)
                {
                    elapsed += Time.deltaTime;
                    if (SpinDashSettings.TrackPlayerDuringHold)
                    {
                        finalTarget = player.position;
                        finalTarget.y = transform.position.y;
                    }
                    float t = elapsed / dashDuration;
                    transform.position = Vector3.Lerp(start, finalTarget, t);
                    yield return null;
                }
            }

            EndSpinDashHitboxPhase();

            agent.enabled = true;
            agent.Warp(transform.position);

            if (dualWieldSystem != null && dualWieldSystem.LodgedWeaponCount > 0)
            {
                dualWieldSystem.ReturnAllLodgedWeaponsToRest();
            }

            if (spinVfxInstance != null)
            {
                Destroy(spinVfxInstance);
            }

            ApplyAnimationSpeedMultiplier(SpinDashSettings.WindDownAnimSpeedMultiplier);
            TriggerAnimation(SpinDashSettings.WindDownTrigger);
            PlaySFX(SpinDashSettings.WindDownSFX);
            yield return WaitForAnimationStateToFinish(SpinDashSettings.WindDownTrigger, 0.7f);
            ResetAnimationSpeed();

            HideAttackIndicator();
        }

        private IEnumerator ExecuteHighDive()
        {
            if (player == null) yield break;

            ApplyAnimationSpeedMultiplier(HighDiveAttack.AnimationSpeedMultiplier);
            TriggerAnimation("Attack_HighDive");
            PlaySFX(HighDiveAttack.AttackSFX);
            
            Vector3 startPos = transform.position;
            Vector3 peakPos = startPos + Vector3.up * 8f;
            
            agent.enabled = false;
            
            // Jump up
            float elapsed = 0f;
            while (elapsed < 0.6f)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(startPos, peakPos, elapsed / 0.6f);
                yield return null;
            }
            
            // Slam down
            Vector3 targetPos = player.position;
            targetPos.y = startPos.y;
            elapsed = 0f;
            
            while (elapsed < 0.4f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 0.4f;
                Vector3 pos = Vector3.Lerp(peakPos, targetPos, t);
                pos.y = Mathf.Lerp(peakPos.y, startPos.y, t);
                transform.position = pos;
                yield return null;
            }
            
            agent.enabled = true;
            agent.Warp(transform.position);
            
            // Impact VFX/SFX
            if (HighDiveAttack.ImpactVFX != null)
            {
                Instantiate(HighDiveAttack.ImpactVFX, transform.position, Quaternion.identity);
            }
            PlaySFX(HighDiveAttack.ImpactSFX);
            
            CheckAoEHit(HighDiveAttack.BaseDamage, 4f, AttackCategory.Halberd);
            
            yield return new WaitForSeconds(0.8f);
            ResetAnimationSpeed();
        }

        private IEnumerator ExecuteAnimeDashSlash()
        {
            if (player == null) yield break;

            var settings = AnimeDashSettings ?? new AnimeDashSlashConfig();

            ApplyAnimationSpeedMultiplier(settings.AnimationSpeedMultiplier);
            TriggerAnimation(settings.AnimationTrigger);
            PlaySFX(settings.AttackSFX);

            if (settings.PreDashDelay > 0f)
                yield return new WaitForSeconds(settings.PreDashDelay);

            Vector3 centerPos = settings.UsePlayerPositionAsCenterAtStart ? player.position : transform.position;
            centerPos.y = transform.position.y;

            int dashCount = Mathf.Max(1, settings.DashTargetCount);
            float radius = Mathf.Max(0.1f, settings.DashTargetRadius);
            float dashDuration = Mathf.Max(0.01f, settings.DashTravelDuration);
            float pauseDuration = Mathf.Max(0f, settings.PauseAtTargetDuration);
            float turnSpeed = Mathf.Max(0f, settings.TurnSpeed);
            float hitWindowStart = Mathf.Clamp01(settings.HitWindowStart);
            float hitWindowEnd = Mathf.Clamp01(settings.HitWindowEnd);
            if (hitWindowEnd < hitWindowStart)
                hitWindowEnd = hitWindowStart;

            bool useSpinDashColliderForAnimeDash = settings.UseSpinDashColliderForHitRange && spinDashHitboxCollider != null;
            bool animeDashEnabledSpinDashCollider = false;
            if (useSpinDashColliderForAnimeDash && !spinDashHitboxCollider.enabled)
            {
                spinDashHitboxCollider.enabled = true;
                animeDashEnabledSpinDashCollider = true;
            }

            agent.enabled = false;

            List<Vector3> points = new List<Vector3>(dashCount);
            for (int i = 0; i < dashCount; i++)
            {
                float angle = (i * settings.DashAngleStepDegrees) * Mathf.Deg2Rad;
                Vector3 point = centerPos + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius
                );
                point.y = transform.position.y;
                points.Add(point);
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 targetPoint = points[i];
                Vector3 startPoint = transform.position;
                bool isFinalDash = i == points.Count - 1;

                Vector3 dashDir = targetPoint - startPoint;
                dashDir.y = 0f;
                if (dashDir.sqrMagnitude > 0.001f && turnSpeed > 0f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dashDir.normalized);
                    while (Quaternion.Angle(transform.rotation, targetRot) > 1f)
                    {
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                        yield return null;
                    }

                    transform.rotation = targetRot;
                }

                float elapsed = 0f;
                bool hitAppliedThisDash = false;
                float previousT = 0f;
                animeDashTriggerArmed = false;
                bool animeDashWindowOpenedThisDash = false;
                while (elapsed < dashDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / dashDuration);
                    transform.position = Vector3.Lerp(startPoint, targetPoint, t);

                    if (!hitAppliedThisDash)
                    {
                        bool isInsideWindow = t >= hitWindowStart && t <= hitWindowEnd;
                        bool crossedWindowStartThisFrame = previousT < hitWindowStart && t >= hitWindowStart;
                        bool jumpedPastEntireWindowThisFrame = previousT < hitWindowStart && t > hitWindowEnd;

                        if (isInsideWindow || crossedWindowStartThisFrame || jumpedPastEntireWindowThisFrame)
                        {
                            if (useSpinDashColliderForAnimeDash)
                            {
                                if (!animeDashWindowOpenedThisDash)
                                {
                                    animeDashTriggerDamage = settings.DamagePerHit;
                                    animeDashTriggerArmed = true;
                                    animeDashWindowOpenedThisDash = true;
                                    if (spinDashHitboxCollider != null)
                                        spinDashHitboxCollider.enabled = true;
                                }
                            }
                            else
                            {
                                float animeDashRange = Mathf.Max(0.1f, settings.HitRange);
                                hitAppliedThisDash = CheckMeleeHit(settings.DamagePerHit, AttackCategory.Halberd, animeDashRange);
                            }
                        }
                    }

                    previousT = t;

                    yield return null;
                }

                animeDashTriggerArmed = false;
                if (useSpinDashColliderForAnimeDash && spinDashHitboxCollider != null)
                {
                    spinDashHitboxCollider.enabled = !isFinalDash;
                }

                if (pauseDuration > 0f)
                    yield return new WaitForSeconds(pauseDuration);
            }

            agent.enabled = true;
            agent.Warp(transform.position);

            // Exit the AnimeDash pose immediately after the final destination is reached.
            animController?.PlayIdle(0.05f);

            if (animeDashEnabledSpinDashCollider && spinDashHitboxCollider != null)
                spinDashHitboxCollider.enabled = false;

            if (settings.PostDashDelay > 0f)
                yield return new WaitForSeconds(settings.PostDashDelay);

            ResetAnimationSpeed();
        }

        private IEnumerator ExecuteWhirlwind()
        {
            ApplyAnimationSpeedMultiplier(WhirlwindSettings.AnimationSpeedMultiplier);
            // Use animation trigger from config
            TriggerAnimation(WhirlwindSettings.AnimationTrigger);
            PlaySFX(WhirlwindSettings.SpinSFX);
            
            if (WhirlwindSettings.SpinVFX != null)
            {
                Instantiate(WhirlwindSettings.SpinVFX, transform.position, Quaternion.identity, transform);
            }
            
            yield return new WaitForSeconds(0.4f);
            
            if (suctionEffect != null)
            {
                suctionEffect.BasePullStrength = WhirlwindSettings.SuctionStrength;
                suctionEffect.MaxPullStrength = WhirlwindSettings.MaxSuctionStrength;
                suctionEffect.EffectiveRadius = WhirlwindSettings.SuctionRadius;
                suctionEffect.SetPlayerReferences(player, playerMovement);
                suctionEffect.StartSuction(WhirlwindSettings.SuctionDuration);
            }
            
            float suctionElapsed = 0f;
            float lastDamageTick = 0f;
            
            while (suctionElapsed < WhirlwindSettings.SuctionDuration)
            {
                suctionElapsed += Time.deltaTime;
                
                if (player != null)
                {
                    Vector3 dir = (player.position - transform.position).normalized;
                    agent.Move(dir * 2f * Time.deltaTime);
                }
                
                if (suctionElapsed - lastDamageTick >= WhirlwindSettings.DamageTickInterval)
                {
                    lastDamageTick = suctionElapsed;
                    CheckAoEHit(WhirlwindSettings.DamagePerTick, 3f, AttackCategory.Wing);
                }
                
                yield return null;
            }
            
            // Leap slam
            if (player != null)
            {
                Vector3 leapDir = (player.position - transform.position).normalized;
                Vector3 leapTarget = transform.position + leapDir * WhirlwindSettings.LeapDistance;
                Vector3 startPos = transform.position;
                Vector3 peakPos = (startPos + leapTarget) * 0.5f + Vector3.up * 4f;
                
                agent.enabled = false;
                
                float leapElapsed = 0f;
                while (leapElapsed < WhirlwindSettings.LeapDuration)
                {
                    leapElapsed += Time.deltaTime;
                    float t = leapElapsed / WhirlwindSettings.LeapDuration;
                    Vector3 pos = (1 - t) * (1 - t) * startPos + 2 * (1 - t) * t * peakPos + t * t * leapTarget;
                    transform.position = pos;
                    yield return null;
                }
                
                agent.enabled = true;
                agent.Warp(transform.position);
            }
            
            // Impact
            if (WhirlwindSettings.SlamVFX != null)
            {
                Instantiate(WhirlwindSettings.SlamVFX, transform.position, Quaternion.identity);
            }
            PlaySFX(WhirlwindSettings.SlamSFX);
            
            CheckAoEHit(WhirlwindSettings.SlamDamage, WhirlwindSettings.SlamAoERadius, AttackCategory.Halberd);
            
            yield return new WaitForSeconds(0.8f);
            ResetAnimationSpeed();
        }

        /// <summary>
        /// Executes the knockback attack that pushes player away using external force.
        /// </summary>
        private IEnumerator ExecuteKnockbackAttack()
        {
            if (player == null || playerMovement == null) yield break;

            yield return FaceTarget(player, 0.15f);

            ApplyAnimationSpeedMultiplier(KnockbackSettings.AnimationSpeedMultiplier);
            TriggerAnimation(KnockbackSettings.AnimationTrigger);
            PlaySFX(KnockbackSettings.AttackSFX);

            // Wait for animation wind-up
            yield return new WaitForSeconds(0.3f);

            float dist = Vector3.Distance(transform.position, player.position);
            if (dist <= KnockbackSettings.KnockbackRadius)
            {
                // Calculate knockback direction (away from Cleanser)
                Vector3 knockbackDir = (player.position - transform.position).normalized;
                knockbackDir.y = 0;
                
                // Apply knockback using external force system
                Vector3 knockbackImpulse = knockbackDir * KnockbackSettings.KnockbackForce;
                knockbackImpulse.y = KnockbackSettings.VerticalForce;
                
                playerMovement.ApplyKnockback(knockbackImpulse);

                // Deal damage
                if (player.TryGetComponent<IHealthSystem>(out var health))
                {
                    health.LoseHP(KnockbackSettings.Damage);
                }

                // Notify aggression system that player blocked (if they did)
                if (CombatManager.isGuarding && aggressionSystem != null)
                {
                    aggressionSystem.OnPlayerBlocks();
                }

                // Spawn impact VFX
                if (KnockbackSettings.ImpactVFX != null)
                {
                    Instantiate(KnockbackSettings.ImpactVFX, player.position, Quaternion.identity);
                }
                PlaySFX(KnockbackSettings.ImpactSFX);

#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Knockback applied: {knockbackImpulse}");
#endif
            }

            yield return new WaitForSeconds(0.5f);
            ResetAnimationSpeed();
        }

        /// <summary>
        /// Executes the gap-closing dash (no hitbox, pure movement).
        /// </summary>
        private IEnumerator ExecuteGapClosingDash()
        {
            if (player == null) yield break;

            float dist = Vector3.Distance(transform.position, player.position);
            if (dist < GapCloseDashSettings.MinDistanceToUse) yield break;

            yield return FaceTarget(player, 0.1f);

            ApplyAnimationSpeedMultiplier(GapCloseDashSettings.AnimationSpeedMultiplier);
            TriggerAnimation(GapCloseDashSettings.AnimationTrigger);
            PlaySFX(GapCloseDashSettings.DashSFX);

            if (GapCloseDashSettings.DashVFX != null)
            {
                Instantiate(GapCloseDashSettings.DashVFX, transform.position, transform.rotation, transform);
            }

            // Calculate target position (stop at TargetStopDistance from player)
            Vector3 dirToPlayer = (player.position - transform.position).normalized;
            float dashDistance = Mathf.Min(dist - GapCloseDashSettings.TargetStopDistance, 
                                           GapCloseDashSettings.DashSpeed * GapCloseDashSettings.DashDuration);
            Vector3 targetPos = transform.position + dirToPlayer * dashDistance;

            // Execute dash movement (no hitbox)
            float elapsed = 0f;
            agent.enabled = false;

            while (elapsed < GapCloseDashSettings.DashDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / GapCloseDashSettings.DashDuration;
                
                // Smooth dash curve
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                Vector3 newPos = Vector3.Lerp(transform.position, targetPos, smoothT * Time.deltaTime * 10f);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, GapCloseDashSettings.DashSpeed * Time.deltaTime);
                
                yield return null;
            }

            agent.enabled = true;
            agent.Warp(transform.position);

#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Gap-closing dash completed. Distance closed: {dashDistance:F2}");
#endif

            yield return new WaitForSeconds(0.2f);
            ResetAnimationSpeed();
        }

        #endregion

        #region Ultimate Attack

        private bool ShouldTriggerUltimate()
        {
            if (isExecutingUltimate || isStunned)
                return false;
                
            if (UltimateTriggeredByHealth)
            {
                float healthPercent = currentHealth / maxHealth;
                return healthPercent <= UltimateHealthThreshold && !hasUsedUltimate;
            }
            else
            {
                return attacksSinceUltimate >= MinAttacksBetweenUltimates;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("DEBUG/Run Ultimate Sequence")]
        private void DebugRunUltimateSequence()
        {
            if (!Application.isPlaying || isDefeated || isExecutingUltimate)
                return;

            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }

            EndSpinDashHitboxPhase();

            StartCoroutine(ExecuteUltimateAttack());
        }
#endif

        private IEnumerator ExecuteUltimateAttack()
        {
            ApplyAnimationSpeedMultiplier(UltimateSettings.AnimationSpeedMultiplier);

            isExecutingUltimate = true;
            hasUsedUltimate = true;
            attacksSinceUltimate = 0;
            aerialHitsReceived = 0;
            waitingForUltimateLowSweepEvent = false;
            waitingForUltimateMidSweepEvent = false;
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] ULTIMATE: Double Maximum Sweep initiated!");
#endif

            PlaySFX(UltimateSettings.InitiateSFX);

            if (DoubleSweepPositions.Count > 0)
            {
                TriggerAnimation(UltimateSettings.JumpArcBaseTrigger);
                Transform sweepPos = DoubleSweepPositions[Random.Range(0, DoubleSweepPositions.Count)];
                yield return JumpToPosition(sweepPos.position, 1f);
            }
            
            Vector3 arenaCenter = player != null ? player.position : transform.position + transform.forward * 10f;
            yield return FaceTarget(arenaCenter, 0.3f);
            
            // Sweeps
            if (CanSpawnUltimateSweep(UltimateSettings.LowSweepProjectile))
            {
                pendingUltimateSweepTargetPos = arenaCenter;
                waitingForUltimateLowSweepEvent = true;
                TriggerAnimation(UltimateSettings.UltimateTrigger);
                yield return WaitForUltimateLowSweepEventOrFallback();
            }
            yield return new WaitForSeconds(0.8f);
            
            if (CanSpawnUltimateSweep(UltimateSettings.MidSweepProjectile))
            {
                pendingUltimateSweepTargetPos = arenaCenter;
                waitingForUltimateMidSweepEvent = true;
                TriggerAnimation(UltimateSettings.UltimateTrigger);
                yield return WaitForUltimateMidSweepEventOrFallback();
            }
            yield return new WaitForSeconds(0.5f);
            
            Vector3 floatPos = arenaCenter + Vector3.up * UltimateSettings.PlatformRiseHeight;
            TriggerAnimation(UltimateSettings.JumpArcBaseTrigger);
            yield return JumpToPosition(floatPos, 0.8f);
            
            if (platformController != null)
            {
                platformController.RaisePlatforms();
            }
            
            if (UltimateSettings.PlayCutsceneOnFirstUse)
            {
                yield return new WaitForSeconds(UltimateSettings.CutsceneDuration);
                UltimateSettings.PlayCutsceneOnFirstUse = false;
            }
            
            yield return ExecuteUltimateHoverPhase();
            bool canceled = ultimateCanceledByAerial;
            
            if (platformController != null)
            {
                platformController.LowerPlatforms();
            }
            
            if (!canceled)
            {
                yield return ExecuteMassiveStrike();
                
                // Notify aggression system that ultimate completed
                if (aggressionSystem != null)
                {
                    aggressionSystem.OnUltimateCompleted();
                }
            }
            else
            {
                TriggerAnimation(UltimateSettings.JumpArcCancelTrigger);

                // Notify aggression system that ultimate was canceled
                if (aggressionSystem != null)
                {
                    aggressionSystem.OnUltimateCanceled();
                }
                
                yield return ApplyStun(AerialFinisherStunDuration);
            }
            
            // Reset spare stockpile/lodged state after ultimate.
            if (dualWieldSystem != null)
            {
                while (dualWieldSystem.IsHoldingSpareWeapon)
                {
                    dualWieldSystem.ReleaseCurrentWeapon();
                }

                if (dualWieldSystem.LodgedWeaponCount > 0)
                {
                    dualWieldSystem.ReturnAllLodgedWeaponsToRest();
                }
            }

            ResetAnimationSpeed();
            isExecutingUltimate = false;
        }

        private IEnumerator WaitForUltimateLowSweepEventOrFallback()
        {
            const float fallbackTimeout = 0.35f;
            float elapsed = 0f;
            while (waitingForUltimateLowSweepEvent && elapsed < fallbackTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (waitingForUltimateLowSweepEvent)
            {
                waitingForUltimateLowSweepEvent = false;
                SpawnCrescentWave(UltimateSettings.LowSweepHeight, pendingUltimateSweepTargetPos);
                PlaySFX(UltimateSettings.SweepSFX);
            }
        }

        private IEnumerator WaitForUltimateMidSweepEventOrFallback()
        {
            const float fallbackTimeout = 0.35f;
            float elapsed = 0f;
            while (waitingForUltimateMidSweepEvent && elapsed < fallbackTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (waitingForUltimateMidSweepEvent)
            {
                waitingForUltimateMidSweepEvent = false;
                SpawnCrescentWave(UltimateSettings.MidSweepHeight, pendingUltimateSweepTargetPos);
                PlaySFX(UltimateSettings.SweepSFX);
            }
        }

        private void SpawnCrescentWave(float height, Vector3 targetPos)
        {
            CrescentArcProjectileConfig sourceConfig = height <= (UltimateSettings.LowSweepHeight + 0.01f)
                ? UltimateSettings.LowSweepProjectile
                : UltimateSettings.MidSweepProjectile;

            if (sourceConfig != null)
            {
                var runtimeConfig = new CrescentArcProjectileConfig
                {
                    ProjectilePrefab = sourceConfig.ProjectilePrefab != null
                        ? sourceConfig.ProjectilePrefab
                        : CrescentWaveProjectileConfiguration?.ProjectilePrefab,
                    ProjectileCount = sourceConfig.ProjectileCount,
                    Damage = UltimateSettings.SweepDamage,
                    Speed = sourceConfig.Speed > 0f ? sourceConfig.Speed : UltimateSettings.WaveSpeed,
                    MaxDistance = sourceConfig.MaxDistance,
                    SpawnHeight = sourceConfig.SpawnHeight,
                    SpawnForwardOffset = sourceConfig.SpawnForwardOffset,
                    ScaleRange = sourceConfig.ScaleRange,
                    TiltAngleRange = sourceConfig.TiltAngleRange,
                    SpreadStep = sourceConfig.SpreadStep,
                    DamageCategory = sourceConfig.DamageCategory
                };

                if (runtimeConfig.ProjectilePrefab != null)
                {
                    SpawnCrescentArcProjectiles(runtimeConfig, targetPos, height);
                    return;
                }
            }

            // Legacy fallback to rigidbody-style wave prefab.
            if (CrescentWavePrefab != null)
            {
                Vector3 spawnPos = transform.position;
                spawnPos.y = transform.position.y + height;

                var waveObj = Instantiate(CrescentWavePrefab, spawnPos, Quaternion.identity);
                var rb = waveObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 dir = (targetPos - spawnPos).normalized;
                    dir.y = 0;
                    rb.linearVelocity = dir * UltimateSettings.WaveSpeed;
                }

                var waveProjectile = waveObj.GetComponent<CleanserProjectile>();
                if (waveProjectile != null)
                {
                    waveProjectile.Damage = UltimateSettings.SweepDamage;
                }

                Destroy(waveObj, 5f);
                return;
            }
        }

        private bool CanSpawnUltimateSweep(CrescentArcProjectileConfig config)
        {
            bool hasSharedPrefab = CrescentWaveProjectileConfiguration != null && CrescentWaveProjectileConfiguration.ProjectilePrefab != null;
            return hasSharedPrefab || (config != null && config.ProjectilePrefab != null) || CrescentWavePrefab != null;
        }

        private IEnumerator ExecuteUltimateHoverPhase()
        {
            isInUltimateHoverPhase = true;
            ultimateCanceledByAerial = false;
            aerialHitsReceived = 0;
            ultimateHoverPauseTimer = 0f;

            TriggerAnimation(stateUltimateHover);

            float chargeElapsed = 0f;
            while (chargeElapsed < UltimateSettings.ChargeUpTime)
            {
                if (ultimateCanceledByAerial)
                {
#if UNITY_EDITOR
                    EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Ultimate canceled during hover phase by aerial finisher.");
#endif
                    break;
                }

                if (ultimateHoverPauseTimer > 0f)
                {
                    ultimateHoverPauseTimer -= Time.deltaTime;
                }
                else
                {
                    chargeElapsed += Time.deltaTime;
                }
                yield return null;
            }
            isInUltimateHoverPhase = false;
        }

        private void SpawnCrescentArcProjectiles(CrescentArcProjectileConfig config, Vector3 targetPos, float additionalHeight = 0f)
        {
            if (config == null)
                return;

            Vector3 baseDir = (targetPos - transform.position);
            baseDir.y = 0f;
            if (baseDir.sqrMagnitude < 0.001f)
                baseDir = transform.forward;
            baseDir.Normalize();

            int count = Mathf.Max(1, config.ProjectileCount);
            float centerOffset = (count - 1) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float spreadOffset = (i - centerOffset) * config.SpreadStep;
                float randomOffset = 0f;
                float finalYaw = spreadOffset + randomOffset;
                Vector3 dir = Quaternion.AngleAxis(finalYaw, Vector3.up) * baseDir;

                Vector3 spawnPos = transform.position
                    + transform.forward * config.SpawnForwardOffset
                    + Vector3.up * (config.SpawnHeight + additionalHeight);

                GameObject prefabToSpawn = config.ProjectilePrefab != null
                    ? config.ProjectilePrefab
                    : CrescentWaveProjectileConfiguration?.ProjectilePrefab;

                if (prefabToSpawn == null)
                    continue;

                float tiltAngle = Random.Range(config.TiltAngleRange.x, config.TiltAngleRange.y);
                Quaternion spawnRot = Quaternion.LookRotation(dir) * Quaternion.Euler(tiltAngle, 0f, 0f);
                GameObject projectile = Instantiate(prefabToSpawn, spawnPos, spawnRot);

                float scale = Random.Range(config.ScaleRange.x, config.ScaleRange.y);
                if (Mathf.Abs(scale - 1f) > 0.001f)
                {
                    projectile.transform.localScale *= scale;
                }

                var arcProjectile = projectile.GetComponent<CleanserCrescentArcProjectile>();
                if (arcProjectile != null)
                {
                    arcProjectile.Initialize(
                        dir,
                        config.Damage,
                        config.Speed,
                        config.MaxDistance,
                        config.DamageCategory,
                        config.CanBeParried,
                        config.CanBeGuarded,
                        config.GuardDamageMultiplier);
                    continue;
                }

                var cleanserProjectile = projectile.GetComponent<CleanserProjectile>();
                if (cleanserProjectile != null)
                {
                    cleanserProjectile.Damage = config.Damage;

                    var straightConfig = new SpareTossConfig
                    {
                        UseStraightPath = true,
                        ReturnsOnStraightPath = false,
                        UseCurvedBoomerang = false,
                        ProjectileSpeedRange = new Vector2(config.Speed, config.Speed)
                    };

                    cleanserProjectile.Initialize(player, transform, straightConfig, dualWieldSystem);
                    continue;
                }

                // Fallback path if prefab uses Rigidbody-only motion.
                var rb = projectile.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = dir * config.Speed;
                }

                Destroy(projectile, 5f);
            }
        }

        private IEnumerator ExecuteMassiveStrike()
        {
            PlaySFX(UltimateSettings.MassiveStrikeSFX);
            TriggerAnimation(UltimateSettings.JumpArcResolutionTrigger);
            
            Vector3 startPos = transform.position;
            Vector3 targetPos = startPos;
            targetPos.y = 0f;
            
            agent.enabled = false;
            
            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / 0.3f;
                transform.position = Vector3.Lerp(startPos, targetPos, t * t);
                yield return null;
            }
            
            agent.enabled = true;
            agent.Warp(transform.position);
            
            if (UltimateSettings.MassiveStrikeVFX != null)
            {
                Instantiate(UltimateSettings.MassiveStrikeVFX, transform.position, Quaternion.identity);
            }
            
            CheckMassiveStrikeHit();
            
            yield return new WaitForSeconds(1f);
        }

        private void CheckMassiveStrikeHit()
        {
            if (player == null) return;
            
            float dist = Vector3.Distance(transform.position, player.position);
            if (dist <= UltimateSettings.MassiveStrikeRadius)
            {
                float damage = UltimateSettings.MassiveStrikeDamage;
                
                if (CombatManager.isGuarding)
                {
                    damage *= (1f - UltimateSettings.GuardMitigationCap);
                }
                
                if (player.TryGetComponent<IHealthSystem>(out var health))
                {
                    health.LoseHP(damage);
                }
            }
        }

        public void OnAerialHitReceived()
        {
            if (!isExecutingUltimate || !isInUltimateHoverPhase) return;

            if (!WasHitByFullAerialComboPlungeFinisher())
            {
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Aerial hit during ultimate ignored (requires full aerial combo + plunge finisher).");
#endif
                return;
            }
            
            aerialHitsReceived++;
            if (aerialHitsReceived >= UltimateSettings.RequiredAerialHits)
            {
                ultimateCanceledByAerial = true;
            }
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Aerial hit received during ultimate! ({aerialHitsReceived}/{UltimateSettings.RequiredAerialHits})");
#endif
        }

        private bool WasHitByFullAerialComboPlungeFinisher()
        {
            if (player == null)
                return false;

            AerialComboManager aerialCombo = player.GetComponent<AerialComboManager>()
                ?? player.GetComponentInParent<AerialComboManager>()
                ?? player.GetComponentInChildren<AerialComboManager>();

            if (aerialCombo == null)
                return false;

            // Full aerial combo requirement: player used heavy (plunge) and had built at least 2 aerial fast hits.
            return aerialCombo.HasUsedAerialHeavy && aerialCombo.AerialFastCount >= 2;
        }

        #endregion

        #region Public Counter Interface

        /// <summary>
        /// Called by external systems when the player initiates a counter attack against the Cleanser.
        /// This triggers the Cleanser's chance-based counter response.
        /// </summary>
        /// <param name="playerStacks">Current counter stacks the player has.</param>
        /// <param name="maxStacks">Maximum counter stacks possible.</param>
        /// <param name="hasJustParried">Whether the player just parried (for max counter check).</param>
        public void OnPlayerInitiatesCounter(int playerStacks, int maxStacks, bool hasJustParried)
        {
            if (aggressionSystem == null)
            {
#if UNITY_EDITOR
                EnemyBehaviorDebugLogBools.LogWarning(nameof(CleanserBrain), "[Cleanser] AggressionSystem not found, cannot process counter!");
#endif
                return;
            }

            aggressionSystem.OnPlayerCounter(playerStacks, maxStacks, hasJustParried);
        }

        /// <summary>
        /// Called by external systems when the player attacks near the boss (proximity check, no hit needed).
        /// </summary>
        public void OnPlayerAttackNearby()
        {
            if (aggressionSystem != null)
            {
                aggressionSystem.OnPlayerAttackProximity();
            }
        }

        /// <summary>
        /// Gets the current aggression level of the Cleanser.
        /// </summary>
        public AggressionLevel GetAggressionLevel()
        {
            return aggressionSystem != null ? aggressionSystem.CurrentLevel : AggressionLevel.Level1;
        }

        /// <summary>
        /// Gets the current aggression value (0-100).
        /// </summary>
        public float GetAggressionValue()
        {
            return aggressionSystem != null ? aggressionSystem.AggressionValue : 0f;
        }

        #endregion

        #region Helper Methods

        private IEnumerator MoveTowardPlayer(float duration)
        {
            if (player == null) yield break;
            
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                agent.SetDestination(player.position);
                yield return null;
            }
        }

        private IEnumerator MoveAwayFromPlayer(float duration)
        {
            if (player == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                
                // Calculate position away from player
                Vector3 awayDir = (transform.position - player.position).normalized;
                Vector3 targetPos = transform.position + awayDir * 3f;
                
                agent.SetDestination(targetPos);
                yield return null;
            }
        }

        private IEnumerator StrafeAroundPlayer(float duration)
        {
            if (player == null) yield break;

            float elapsed = 0f;
            // Randomly choose strafe direction
            float strafeDir = Random.value > 0.5f ? 1f : -1f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                
                // Calculate strafe position (perpendicular to player direction)
                Vector3 toPlayer = (player.position - transform.position).normalized;
                Vector3 strafeVector = Vector3.Cross(Vector3.up, toPlayer) * strafeDir;
                Vector3 targetPos = transform.position + strafeVector * 3f;
                
                // Face player while strafing
                transform.LookAt(new Vector3(player.position.x, transform.position.y, player.position.z));
                
                agent.SetDestination(targetPos);
                yield return null;
            }
        }

        private IEnumerator FaceTarget(Transform target, float duration)
        {
            if (target == null) yield break;
            yield return FaceTarget(target.position, duration);
        }

        private IEnumerator FaceTarget(Vector3 targetPos, float duration)
        {
            Vector3 dir = (targetPos - transform.position).normalized;
            dir.y = 0;
            
            if (dir.sqrMagnitude < 0.001f) yield break;
            
            Quaternion startRot = transform.rotation;
            Quaternion targetRot = Quaternion.LookRotation(dir);
            
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / duration);
                yield return null;
            }
            
            transform.rotation = targetRot;
        }

        private IEnumerator JumpToPosition(Vector3 targetPos, float duration)
        {
            Vector3 startPos = transform.position;
            Vector3 peakPos = (startPos + targetPos) * 0.5f + Vector3.up * 5f;
            
            agent.enabled = false;
            
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                Vector3 pos = (1 - t) * (1 - t) * startPos + 2 * (1 - t) * t * peakPos + t * t * targetPos;
                transform.position = pos;
                yield return null;
            }
            
            transform.position = targetPos;
            agent.enabled = true;
            agent.Warp(targetPos);
        }

        private float GetSpinDashSegmentDuration(float distance)
        {
            if (SpinDashSettings.MaxTravelDistance > 0f && distance > SpinDashSettings.MaxTravelDistance)
                return Mathf.Max(0.01f, SpinDashSettings.LongDistanceTravelDuration);

            return Mathf.Max(0.01f, distance / Mathf.Max(0.01f, SpinDashSettings.MoveSpeed));
        }

        private void ApplyAnimationSpeedMultiplier(float speedMultiplier)
        {
            if (animator == null)
                return;

            float clamped = Mathf.Max(0.01f, speedMultiplier);
            animator.speed = defaultAnimatorSpeed * clamped;
        }

        private void ResetAnimationSpeed()
        {
            if (animator != null)
                animator.speed = defaultAnimatorSpeed;
        }

        private IEnumerator WaitForAnimationStateToFinish(string stateName, float fallbackTimeout)
        {
            if (string.IsNullOrEmpty(stateName) || animController == null)
            {
                yield return new WaitForSeconds(fallbackTimeout);
                yield break;
            }

            float elapsed = 0f;
            bool hasStarted = false;
            while (elapsed < fallbackTimeout)
            {
                elapsed += Time.deltaTime;

                if (animController.IsPlaying(stateName, out float normalizedTime))
                {
                    hasStarted = true;
                    if (normalizedTime >= 1f)
                        break;
                }
                else if (hasStarted)
                {
                    break;
                }

                yield return null;
            }
        }

        private bool CheckMeleeHit(float damage, AttackCategory category, float range = 3f)
        {
            if (player == null) return false;
            
            float dist = Vector3.Distance(transform.position, player.position);
            if (dist > range) return false;
            
            return ApplyMeleeHitToPlayer(damage, category);
        }

        private bool ApplyMeleeHitToPlayer(float damage, AttackCategory category)
        {
            if (player == null) return false;

            if (category == AttackCategory.Halberd)
            {
                if (CombatManager.isParrying)
                {
                    CombatManager.ParrySuccessful();
#if UNITY_EDITOR
                    EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Attack parried!");
#endif
                    return false;
                }
            }
            else if (category == AttackCategory.Wing)
            {
                if (CombatManager.isGuarding)
                {
                    damage *= 0.25f;
#if UNITY_EDITOR
                    EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Wing attack guarded, reduced to {damage}.");
#endif
                }
            }
            
            if (player.TryGetComponent<IHealthSystem>(out var health))
            {
                health.LoseHP(damage);

                bool shouldStagger = category switch
                {
                    AttackCategory.Halberd => staggerPlayerOnHalberdMeleeHit,
                    AttackCategory.Wing => staggerPlayerOnWingMeleeHit,
                    _ => false
                };

                if (shouldStagger && health is PlayerHealthBarManager playerHealth)
                    playerHealth.ApplyForcedStagger(meleeHitStaggerDuration, resetPlayerComboOnMeleeStagger);

                return true;
            }
            
            return false;
        }

        private bool TryApplyTriggerHitToPlayer(float damage, AttackCategory category)
        {
            if (player == null)
                return false;

            return ApplyMeleeHitToPlayer(damage, category);
        }

        private float GetActiveHitboxRange(AttackCategory category)
        {
            if (category == AttackCategory.Wing)
            {
                if (wingHitboxCollider != null)
                {
                    Bounds b = wingHitboxCollider.bounds;
                    float extent = Mathf.Max(b.extents.x, b.extents.z);
                    if (extent > 0.01f)
                        return Mathf.Max(1f, extent * 2f);
                }
            }
            else
            {
                float maxExtent = 0f;

                if (halberdHitboxColliders != null && halberdHitboxColliders.Count > 0)
                {
                    for (int i = 0; i < halberdHitboxColliders.Count; i++)
                    {
                        Collider col = halberdHitboxColliders[i];
                        if (col == null)
                            continue;

                        Bounds b = col.bounds;
                        float extent = Mathf.Max(b.extents.x, b.extents.z);
                        if (extent > maxExtent)
                            maxExtent = extent;
                    }
                }

                if (maxExtent > 0.01f)
                    return Mathf.Max(1f, maxExtent * 2f);
            }

            if (currentAttack != null)
                return Mathf.Max(1f, currentAttack.RangeMax);

            return 3f;
        }

        private float GetSpinDashHitboxRange(float fallbackRange)
        {
            if (spinDashHitboxCollider != null)
            {
                Bounds b = spinDashHitboxCollider.bounds;
                float extent = Mathf.Max(b.extents.x, b.extents.z);
                if (extent > 0.01f)
                    return Mathf.Max(1f, extent * 2f);
            }

            return Mathf.Max(1f, fallbackRange);
        }

        private void SetHalberdHitboxesEnabled(bool enabled)
        {
            if (halberdHitboxColliders == null)
                return;

            for (int i = 0; i < halberdHitboxColliders.Count; i++)
            {
                Collider col = halberdHitboxColliders[i];
                if (col != null)
                    col.enabled = enabled;
            }
        }

        private void SetWingHitboxEnabled(bool enabled)
        {
            if (wingHitboxCollider != null)
                wingHitboxCollider.enabled = enabled;
        }

        private void SetAllMeleeHitboxesEnabled(bool enabled)
        {
            SetHalberdHitboxesEnabled(enabled);
            SetWingHitboxEnabled(enabled);
        }

        private bool TrySpinDashHit(float damage, float fallbackRange)
        {
            if (!spinDashHitboxPhaseActive || !spinDashHitboxArmed)
                return false;

            float range = GetSpinDashHitboxRange(fallbackRange);
            if (!CheckMeleeHit(damage, AttackCategory.Halberd, range))
                return false;

            spinDashHitboxArmed = false;
            if (spinDashHitboxCollider != null)
            {
                spinDashHitboxCollider.enabled = false;
            }

            if (spinDashHitboxRearmCoroutine != null)
                StopCoroutine(spinDashHitboxRearmCoroutine);

            spinDashHitboxRearmCoroutine = StartCoroutine(RearmSpinDashHitboxAfterDelay());
            return true;
        }

        private void BeginSpinDashHitboxPhase()
        {
            spinDashHitboxPhaseActive = true;
            spinDashHitboxArmed = true;
            spinDashRemainingHits = Mathf.Max(0, SpinDashSettings.MaxHitCount);
            spinDashTriggerDamage = SpinDashSettings.DamagePerHit;

            if (spinDashHitboxCollider != null)
            {
                spinDashHitboxCollider.enabled = true;
            }
        }

        private void EndSpinDashHitboxPhase()
        {
            spinDashHitboxPhaseActive = false;
            spinDashHitboxArmed = false;
            animeDashTriggerArmed = false;

            if (spinDashHitboxRearmCoroutine != null)
            {
                StopCoroutine(spinDashHitboxRearmCoroutine);
                spinDashHitboxRearmCoroutine = null;
            }

            if (spinDashHitboxCollider != null)
            {
                spinDashHitboxCollider.enabled = false;
            }
        }

        public void HandleSpinDashHitboxTrigger(Collider other)
        {
            if (other == null || player == null)
                return;

            Transform otherTransform = other.transform;
            bool isPlayerCollider = otherTransform == player || otherTransform.IsChildOf(player) || player.IsChildOf(otherTransform);
            if (!isPlayerCollider)
            {
                CharacterController cc = other.GetComponentInParent<CharacterController>();
                isPlayerCollider = cc != null && (cc.transform == player || cc.transform.IsChildOf(player) || player.IsChildOf(cc.transform));
            }

            if (!isPlayerCollider)
                return;

            if (spinDashHitboxPhaseActive && spinDashHitboxArmed && spinDashRemainingHits > 0)
            {
                if (TryApplyTriggerHitToPlayer(spinDashTriggerDamage, AttackCategory.Halberd))
                {
                    spinDashRemainingHits--;
                    spinDashHitboxArmed = false;

                    if (spinDashHitboxCollider != null)
                        spinDashHitboxCollider.enabled = false;

                    if (spinDashHitboxRearmCoroutine != null)
                        StopCoroutine(spinDashHitboxRearmCoroutine);

                    if (spinDashRemainingHits > 0)
                        spinDashHitboxRearmCoroutine = StartCoroutine(RearmSpinDashHitboxAfterDelay());
                }
            }
            else if (animeDashTriggerArmed)
            {
                if (TryApplyTriggerHitToPlayer(animeDashTriggerDamage, AttackCategory.Halberd))
                {
                    animeDashTriggerArmed = false;
                    if (spinDashHitboxCollider != null)
                        spinDashHitboxCollider.enabled = false;
                }
            }
        }

        private IEnumerator RearmSpinDashHitboxAfterDelay()
        {
            float delay = Mathf.Max(0f, spinDashHitboxRearmDelay);
            if (delay > 0f)
                yield return WaitForSecondsCache.Get(delay);

            if (spinDashHitboxPhaseActive)
            {
                spinDashHitboxArmed = true;
                if (spinDashHitboxCollider != null)
                {
                    spinDashHitboxCollider.enabled = true;
                }
            }

            spinDashHitboxRearmCoroutine = null;
        }

        private void CheckAoEHit(float damage, float radius, AttackCategory category)
        {
            if (player == null) return;
            
            float dist = Vector3.Distance(transform.position, player.position);
            if (dist <= radius)
            {
                CheckMeleeHit(damage, category, radius);
            }
        }

        private void TriggerAnimation(string triggerName)
        {
            if (string.IsNullOrEmpty(triggerName)) return;
            
            // Use animation controller if available (preferred)
            if (animController != null)
            {
                animController.PlayAttack(triggerName);
            }
            else if (animator != null)
            {
                // Fallback to direct animator trigger
                animator.SetTrigger(triggerName);
            }
        }

        private void PlaySFX(AudioClip clip)
        {
            if (clip == null) return;
            
            if (sfxSource != null)
            {
                sfxSource.PlayOneShot(clip);
            }
            else if (SoundManager.Instance != null)
            {
                SoundManager.Instance.sfxSource.PlayOneShot(clip);
            }
        }

        private void SetDamageReduction(bool active, float reduction)
        {
            isDamageReductionActive = active;
            currentDamageReduction = reduction;
        }

        private IEnumerator ApplyStun(float duration)
        {
            isStunned = true;
            TriggerAnimation(triggerStunned);
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), $"[Cleanser] Stunned for {duration} seconds!");
#endif
            
            yield return new WaitForSeconds(duration);
            
            isStunned = false;
        }

        private void OnDefeated()
        {
            if (isDefeated) return;
            
            isDefeated = true;
            
            if (mainLoopCoroutine != null)
            {
                StopCoroutine(mainLoopCoroutine);
                mainLoopCoroutine = null;
            }
            
            if (currentAttackCoroutine != null)
            {
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }
            
            if (platformController != null)
            {
                platformController.LowerPlatforms();
            }
            
            if (dualWieldSystem != null)
            {
                dualWieldSystem.DropCurrentWeapon();
            }
            
            TriggerAnimation(triggerDeath);
            ResetAnimationSpeed();
            UnregisterFromAttackQueue();
            
#if UNITY_EDITOR
            EnemyBehaviorDebugLogBools.Log(nameof(CleanserBrain), "[Cleanser] Defeated!");
#endif
        }

        #endregion

        #region Animation Event Receivers

        public void EnableDamageReduction()
        {
            if (!UseDamageReductionEvents) return;
            SetDamageReduction(true, LungeAttack.WindupDamageReduction);
        }

        public void DisableDamageReduction()
        {
            if (!UseDamageReductionEvents) return;
            SetDamageReduction(false, 1f);
        }

        #endregion
    }

}
