using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Player.Command;

// Made by Jasper Fadden
// This is an upgraded draft of Rainbow Man's player controller. The movement code utilizes the ADSR system often seen in music that our professor described in a lecture. This allows smooth slow-down and speed-up physics that make movement feel much less rigid. This controller also implements some rudimentary animation (having the player turn to face the mouse), player statistics like shooting speed and inaccuracy, and more.

// Credits to Josh McCoy for much of the code implementing Attack-Decay-Sustain-Release movement.

public class PlayerController : MonoBehaviour
{
    // Movement speed for the player
    [SerializeField] float speed = 10.0f;
    // This is the prefab the player shoors.
    [SerializeField] public ProjectileController ProjectilePrefab;

    Rigidbody2D rigidbody2d;
    SpriteRenderer sprite;
    // These two variables are used to store what direction the player is moving in. Right/Upwards are positive, Left/Down negative.
    float horizontal;
    float vertical;
    // This is used to temporarily store the player position before translating them.
    Vector3 position;

    // Used to store what angle clockwise the player's mouse is relative to the player.
    float angle;
    Vector3 mousePosition;
    // This variable stores the cooldown between shots for the player. The player can shoot 1 / FireRate shots per second.
    // Set to public for upgrades to access it.
    [SerializeField] public float FireRate = 0.2f;
    // This is a timer that is set to fire rate after shooting but decrements by Time.deltaTime every frame.
    private float FireRateTimer = 0.0f;

    // Not sure whether or not this should be a thing, but it could be fun for upgrades (maybe a minigun one that adds
    // to inaccuracy but gives a huge fire-rate, or one that sets inaccuracy to be 0).
    // Set to public for projectiles and upgrades to access it.
    [SerializeField] static public float inaccuracy = 1.5f;


    // To avoid counter-strike style bunnyhop shennanigans, decay and sustain are basically ignored and set to normal speed.
    [SerializeField] private float AttackDuration = 0.45f;
    [SerializeField] private AnimationCurve Attack;

    [SerializeField] private float DecayDuration = 1.0f;
    [SerializeField] private AnimationCurve Decay;

    [SerializeField] private float SustainDuration = 1.0f;
    [SerializeField] private AnimationCurve Sustain;

    [SerializeField] private float ReleaseDuration = 0.64f;
    [SerializeField] private AnimationCurve Release;

    // Two versions must be made for most variables in order to bring the code from 1D to 2D
    private float HAttackTimer;
    private float HDecayTimer;
    private float HSustainTimer;
    private float HReleaseTimer;

    private float VAttackTimer;
    private float VDecayTimer;
    private float VSustainTimer;
    private float VReleaseTimer;

    // These custom made timers are to prevent the player from sliding when they stop themself by pressing contrasting movement keys
    private float HDualReleaseTimer = 0.0f;
    private float VDualReleaseTimer = 0.0f;
    private float DualReleaseThreshold = 0.1f;

    // There are five possible phases the player can be in, each one modifies speed depending on how long in the phase the player is.
    private enum Phase { Attack, Decay, Sustain, Release, None };
    private Phase CurrentPhaseVertical;
    private Phase CurrentPhaseHorizontal;

    // Start is called before the first frame update.
    void Start()
    {
        // Easily access components with variables.
        rigidbody2d = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        CurrentPhaseVertical = Phase.None;
        CurrentPhaseHorizontal = Phase.None;

        // This set of two property changes slightly enhances collision so the player doesn't clip into walls.
        GetComponent<Rigidbody2D>().collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.Extrapolate;
    }

    // Update is called once per frame.
    void Update()
    {
        // These timers count down every frame if they are above 0
        // These "dual release timers" are designed to allow the player to stop on the spot if they hit the reverse direction 
        // and release both related movement keys. Otherwise, the player would bounce in a random direction after releasing both.
        if (HDualReleaseTimer > 0)
        {
            HDualReleaseTimer -= Time.deltaTime;
        }
        if (VDualReleaseTimer > 0)
        {
            VDualReleaseTimer -= Time.deltaTime;
        }
        // This timer is designed to enforce the player's fire rate.
        if (FireRateTimer > 0)
        {
            FireRateTimer -= Time.deltaTime;
        }

        // This LONG chain of if statement sets get player input for WASD and arrowkeys, and it helps implement 2D ADSR smoothly.
        // Things get much more advanced when translating things to 2D movement, so this is a long process

        // These are the 4 GetKeyDown Statements for the attack phase.
        // Note that directions and phases aren't updated every frame, only on keydown or keyup mostly.
        // This is to allow time based progression of states in case we decide to use the middle two stages.
        if (Input.GetKeyDown(KeyCode.D))
        {
            horizontal = 1.0f;
            this.ResetTimersHorizontal();
            this.CurrentPhaseHorizontal = Phase.Attack;
        }
        if (Input.GetKeyDown(KeyCode.A))
        {
            horizontal = -1.0f;
            this.ResetTimersHorizontal();
            this.CurrentPhaseHorizontal = Phase.Attack;
        }
        if (Input.GetKeyDown(KeyCode.W))
        {
            vertical = 1.0f;
            this.ResetTimersVertical();
            this.CurrentPhaseVertical = Phase.Attack;
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            vertical = -1.0f;
            this.ResetTimersVertical();
            this.CurrentPhaseVertical = Phase.Attack;
        }

        // These are a set of four, complex release statements for each key. The first is described in detail.
        // This first loop only works if it has been enough time since the other horizontal key was pressed,
        // the player was in attack phase for long enough to speed up to full, and the right key was released.
        if (Input.GetKeyUp(KeyCode.D) && HDualReleaseTimer <= 0 && this.HAttackTimer > this.AttackDuration)
        {
            horizontal = 1.0f;
            this.CurrentPhaseHorizontal = Phase.Release;
        }
        // The second loop doesn't require the the attack phase was finished (as in, the player doesn't have to fully accelerate).
        else if (Input.GetKeyUp(KeyCode.D) && HDualReleaseTimer <= 0)
        {
            // If so, their sliding speed is scaled down, so just tapping the button doesn't result in a huge sliding boost.
            horizontal = this.HAttackTimer / this.AttackDuration;
            this.CurrentPhaseHorizontal = Phase.Release;
        }
        // If the "HDualReleaseTimer," then the other button was recently held. This means the player shouldn't slide at all.
        else if (Input.GetKeyUp(KeyCode.D))
        {
            this.CurrentPhaseHorizontal = Phase.None;
            // This switches the current direction if the player let's go of one of the keys after holding both
            if (Input.GetKey(KeyCode.A))
            {
                horizontal = -1.0f;
                this.CurrentPhaseHorizontal = Phase.Attack;
            }
        }

        if (Input.GetKeyUp(KeyCode.A) && HDualReleaseTimer <= 0 && this.HAttackTimer > this.AttackDuration)
        {
            horizontal = -1.0f;
            this.CurrentPhaseHorizontal = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.A) && HDualReleaseTimer <= 0)
        {
            horizontal = -1.0f * this.HAttackTimer / this.AttackDuration;
            this.CurrentPhaseHorizontal = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.A))
        {
            this.CurrentPhaseHorizontal = Phase.None;

            if (Input.GetKey(KeyCode.D))
            {
                horizontal = 1.0f;
                this.CurrentPhaseHorizontal = Phase.Attack;
            }
        }

        if (Input.GetKeyUp(KeyCode.W) && VDualReleaseTimer <= 0 && this.VAttackTimer > this.AttackDuration)
        {
            vertical = 1.0f;
            this.CurrentPhaseVertical = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.W) && VDualReleaseTimer <= 0)
        {
            vertical = this.VAttackTimer / this.AttackDuration;
            this.CurrentPhaseVertical = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.W))
        {
            this.CurrentPhaseVertical = Phase.None;

            if (Input.GetKey(KeyCode.S))
            {
                vertical = -1.0f;
                this.CurrentPhaseVertical = Phase.Attack;
            }
        }

        if (Input.GetKeyUp(KeyCode.S) && VDualReleaseTimer <= 0 && this.VAttackTimer > this.AttackDuration)
        {
            vertical = -1.0f;
            this.CurrentPhaseVertical = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.S) && VDualReleaseTimer <= 0)
        {
            vertical = -1.0f * this.VAttackTimer / this.AttackDuration;
            this.CurrentPhaseVertical = Phase.Release;
        }
        else if (Input.GetKeyUp(KeyCode.S))
        {
            this.CurrentPhaseVertical = Phase.None;

            if (Input.GetKey(KeyCode.W))
            {
                vertical = 1.0f;
                this.CurrentPhaseVertical = Phase.Attack;
            }
        }


        // These 2 statements scan for no movement. Nested for clarity, as they only set 
        // movement directions to 0 if the player is not currently sliding from the release phase.
        if (this.CurrentPhaseHorizontal == Phase.None)
        {
            if (!(Input.GetKey(KeyCode.A)) && !(Input.GetKey(KeyCode.D)))
            {
                horizontal = 0.0f;
            }
        }
        if (this.CurrentPhaseVertical == Phase.None)
        {
            if (!(Input.GetKey(KeyCode.S)) && !(Input.GetKey(KeyCode.W)))
            {
                vertical = 0.0f;
            }
        }

        // These 2 statements scan for when the player is pressing both keys at once and set the player to 
        // stop and not slide in the release phase (via the DualReleaseTimer system)
        if ((Input.GetKey(KeyCode.A)) && (Input.GetKey(KeyCode.D)))
        {
            horizontal = 0.0f;
            this.ResetTimersHorizontal();
            this.CurrentPhaseHorizontal = Phase.Sustain;
            // Set the dual release timer to a value so that the player doesn't bounce in a random direction after releasing both keys.
            HDualReleaseTimer = DualReleaseThreshold;
        }
        if ((Input.GetKey(KeyCode.S)) && (Input.GetKey(KeyCode.W)))
        {
            vertical = 0.0f;
            this.ResetTimersVertical();
            this.CurrentPhaseVertical = Phase.Sustain;
            VDualReleaseTimer = DualReleaseThreshold;
        }


        // This code is not for movement, but shooting. The player will be able to shoot by
        // holding down either of two buttons, repeating fire every time the FireRateTimer reaches 0.
        if ( (Input.GetButton("Jump") || Input.GetButton("Fire1")) && FireRateTimer <= 0 )
        {
            FireRateTimer = FireRate;
            Instantiate(ProjectilePrefab, new Vector3(gameObject.transform.position.x, gameObject.transform.position.y, gameObject.transform.position.z), transform.rotation);
        }
        // Make clicking a tiny bit faster (again, unsure if this is what we should do. If it feels weird, delete it).
        // IIRC, this is done by many games so it's super unlikely that the player clicks and feels it didn't register.
        else if ((Input.GetButtonDown("Jump") || Input.GetButtonDown("Fire1")) && ( FireRateTimer - (FireRate/2.0f) ) <= 0)
        {
            FireRateTimer = FireRate;
            Instantiate(ProjectilePrefab, new Vector3(gameObject.transform.position.x, gameObject.transform.position.y, gameObject.transform.position.z), transform.rotation);
        }
    }

    // Fixed update is used for better compatibility and physics. 
    // It is important the player's position is changed here for clipping reasons.
    void FixedUpdate()
    {
        // Basic sprite animation via flipping horizontally. It's based on where the player's mouse is, so we have to calculate things.
        // Get the player's mouse position and RainbowMan's current direction.
        mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 direction = mousePosition - transform.position;
        // This is used to detect where the player should be facing.
        angle = Vector2.SignedAngle(Vector2.down, direction) + 270;

        // If on the right side of the unit circle, else on the left side.
        if ((angle) > 90 && (angle) < 270)
        {
            sprite.flipX = true;
        }
        else
        {
            sprite.flipX = false;
        }

        // After animation code, here we move the player by the inputs calculated in the Update() function.
        if (this.CurrentPhaseHorizontal != Phase.None)
        {
            position = this.gameObject.transform.position;
            // Note position.x is changed based on "horizontal." The speed is decreased for diagonal movement.
            if (vertical != 0)
            {
                position.x += horizontal * speed * HorizontalADSREnvelope() * Time.deltaTime * (1.0f / 1.4142f);
            }
            else
            {
                position.x += horizontal * speed * HorizontalADSREnvelope() * Time.deltaTime;
            }
            gameObject.transform.position = position;

        }

        if (this.CurrentPhaseVertical != Phase.None)
        {
            position = this.gameObject.transform.position;
            // Note position.y is changed based on "vertical." Again, speed is decreased for diagonal movement.
            if (vertical != 0)
            {
                position.y += vertical * speed * VerticalADSREnvelope() * Time.deltaTime * (1.0f / 1.4142f);
            }
            else
            {
                position.y += vertical * speed * VerticalADSREnvelope() * Time.deltaTime;
            }
            gameObject.transform.position = position;
        }

        // Floats are often error ridden and slightly off, so this code ensure's the player is always properly stopped when required.
        if (horizontal < 0.01 && horizontal > -0.01)
        {
            horizontal = 0;
        }
        if (vertical < 0.01 && vertical > -0.01)
        {
            vertical = 0;
        }
    }

    // Simple function borrowed from JoshMcCoy, these resets all ADSR timers.
    private void ResetTimersHorizontal()
    {
        this.HAttackTimer = 0.0f;
        this.HDecayTimer = 0.0f;
        this.HSustainTimer = 0.0f;
        this.HReleaseTimer = 0.0f;
    }
    private void ResetTimersVertical()
    {

        this.VAttackTimer = 0.0f;
        this.VDecayTimer = 0.0f;
        this.VSustainTimer = 0.0f;
        this.VReleaseTimer = 0.0f;
    }

    // Code from JoshMcCoy, this essentially allows us to modify the value of movement depending on current state.
    // Two versions exist for horizontal and vertical movement.
    float HorizontalADSREnvelope()
    {
        float velocity = 0.0f;

        if (Phase.Attack == this.CurrentPhaseHorizontal)
        {
            velocity = this.Attack.Evaluate(this.HAttackTimer / this.AttackDuration);
            this.HAttackTimer += Time.deltaTime;
            if (this.HAttackTimer > this.AttackDuration)
            {
                this.CurrentPhaseHorizontal = Phase.Decay;
            }
        }
        else if (Phase.Decay == this.CurrentPhaseHorizontal)
        {
            velocity = this.Decay.Evaluate(this.HDecayTimer / this.DecayDuration);
            this.HDecayTimer += Time.deltaTime;
            if (this.HDecayTimer > this.DecayDuration)
            {
                this.CurrentPhaseHorizontal = Phase.Sustain;
            }
        }
        else if (Phase.Sustain == this.CurrentPhaseHorizontal)
        {
            velocity = this.Sustain.Evaluate(this.HSustainTimer / this.SustainDuration);
            this.HSustainTimer += Time.deltaTime;
        }
        else if (Phase.Release == this.CurrentPhaseHorizontal)
        {
            velocity = this.Release.Evaluate(this.HReleaseTimer / this.ReleaseDuration);
            this.HReleaseTimer += Time.deltaTime;
            if (this.HReleaseTimer > this.ReleaseDuration)
            {
                this.CurrentPhaseHorizontal = Phase.None;
            }
        }
        return velocity;
    }

    // Vertical version.
    float VerticalADSREnvelope()
    {
        float velocity = 0.0f;

        if (Phase.Attack == this.CurrentPhaseVertical)
        {
            velocity = this.Attack.Evaluate(this.VAttackTimer / this.AttackDuration);
            this.VAttackTimer += Time.deltaTime;
            if (this.VAttackTimer > this.AttackDuration)
            {
                this.CurrentPhaseVertical = Phase.Decay;
            }
        }
        else if (Phase.Decay == this.CurrentPhaseVertical)
        {
            velocity = this.Decay.Evaluate(this.VDecayTimer / this.DecayDuration);
            this.VDecayTimer += Time.deltaTime;
            if (this.VDecayTimer > this.DecayDuration)
            {
                this.CurrentPhaseVertical = Phase.Sustain;
            }
        }
        else if (Phase.Sustain == this.CurrentPhaseVertical)
        {
            velocity = this.Sustain.Evaluate(this.VSustainTimer / this.SustainDuration);
            this.VSustainTimer += Time.deltaTime;
        }
        else if (Phase.Release == this.CurrentPhaseVertical)
        {
            velocity = this.Release.Evaluate(this.VReleaseTimer / this.ReleaseDuration);
            this.VReleaseTimer += Time.deltaTime;
            if (this.VReleaseTimer > this.ReleaseDuration)
            {
                this.CurrentPhaseVertical = Phase.None;
            }
        }
        return velocity;
    }
}