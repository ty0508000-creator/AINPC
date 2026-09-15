using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Controller : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    public float MoveSpeed => moveSpeed;
    public Vector2 LastMoveDir { get; private set; } = Vector2.right;

    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Rigidbody2D rb;
    private Vector2 moveInput;
    private Player_Attack playerAttack;
    private ControlManager controlManager;
    private PlayerStats playerStats;

    /// <summary>죽었으면 조작을 받지 않는다.</summary>
    private bool IsDead => playerStats != null && playerStats.IsDead;

    void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();
        playerAttack = GetComponent<Player_Attack>();
        controlManager = GetComponent<ControlManager>();
        playerStats = GetComponent<PlayerStats>();
    }

    void Update()
    {
        bool isAIControlled = controlManager != null && !controlManager.IsPlayerControlled;
        bool blocked =
            IsDead ||
            PauseMenuUI.IsOpen ||
            (playerAttack != null && playerAttack.IsInvincible) ||
            DialogueManager.IsDialogueOpen;

        if (blocked)
        {
            moveInput = Vector2.zero;
            if (rb != null) rb.linearVelocity = Vector2.zero;
        }
        else if (isAIControlled)
        {
            moveInput = Vector2.zero;
        }
        else
        {
            moveInput = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
            if (moveInput != Vector2.zero)
                LastMoveDir = moveInput.normalized;
        }

        float speed = isAIControlled && rb != null
            ? rb.linearVelocity.magnitude
            : moveInput.magnitude;
        animator.SetFloat("Speed", speed);

        if (!isAIControlled && moveInput.x < 0)
            spriteRenderer.flipX = true;
        else if (!isAIControlled && moveInput.x > 0)
            spriteRenderer.flipX = false;
    }

    void FixedUpdate()
    {
        bool isAIControlled = controlManager != null && !controlManager.IsPlayerControlled;
        if (IsDead ||
            PauseMenuUI.IsOpen ||
            (playerAttack != null && playerAttack.IsInvincible) ||
            DialogueManager.IsDialogueOpen)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (isAIControlled)
            return;

        rb.linearVelocity = moveInput.normalized * moveSpeed;
    }
}
