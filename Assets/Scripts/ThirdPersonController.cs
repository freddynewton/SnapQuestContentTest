using Code.CameraTool;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
using UnityEngine.InputSystem;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM && STARTER_ASSETS_PACKAGES_CHECKED
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class ThirdPersonController : MonoBehaviour
	{
		#region Fields and Properties

		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 2.0f;

		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 5.335f;

		[Tooltip("How fast the character turns to face movement direction")]
		[Range(0.0f, 0.3f)]
		public float RotationSmoothTime = 0.12f;

		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		public AudioClip LandingAudioClip;
		public AudioClip[] FootstepAudioClips;
		[Range(0, 1)] public float FootstepAudioVolume = 0.5f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;

		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.50f;

		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;

		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;

		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.28f;

		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		public Transform PlayerMeshTransform;

		public float Sensitivity = 10f;

		public CameraToolService CameraToolService;

		// Player
		private float _speed;
		private float _animationBlend;
		private float _targetRotation = 0.0f;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// Timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		// Animation IDs
		private int _animIDSpeed;
		private int _animIDGrounded;
		private int _animIDJump;
		private int _animIDFreeFall;
		private int _animIDMotionSpeed;

		public PlayerInput PlayerInput;
		private Animator _animator;
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool _hasAnimator;

		#endregion

		#region Unity Methods

		private void Awake()
		{
			InitializeMainCamera();
		}

		private void Start()
		{
			InitializeComponents();
			AssignAnimationIDs();
			ResetTimeouts();
		}

		private void Update()
		{
			_hasAnimator = TryGetComponent(out _animator);

			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			// Camera rotation logic removed
		}

		#endregion

		#region Initialization

		private void InitializeMainCamera()
		{
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void InitializeComponents()
		{
			_hasAnimator = TryGetComponent(out _animator);
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
		}

		private void AssignAnimationIDs()
		{
			_animIDSpeed = Animator.StringToHash("Speed");
			_animIDGrounded = Animator.StringToHash("Grounded");
			_animIDJump = Animator.StringToHash("Jump");
			_animIDFreeFall = Animator.StringToHash("FreeFall");
			_animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
		}

		private void ResetTimeouts()
		{
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
		}

		#endregion

		#region Movement

		private void Move()
		{
			float targetSpeed = CalculateTargetSpeed();
			UpdateSpeed(targetSpeed);
			Vector3 inputDirection = GetInputDirection();

			if (CameraToolService.IsCameraModeActive)
			{
				HandleStrafingMovement(inputDirection);
			}
			else
			{
				HandleDefaultMovement(inputDirection);
			}

			UpdateAnimator(targetSpeed);
		}

		private float CalculateTargetSpeed()
		{
			return _input.move == Vector2.zero ? 0.0f : (_input.sprint ? SprintSpeed : MoveSpeed);
		}

		private void UpdateSpeed(float targetSpeed)
		{
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			_animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
			if (_animationBlend < 0.01f) _animationBlend = 0f;
		}

		private Vector3 GetInputDirection()
		{
			return new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;
		}

		private void HandleStrafingMovement(Vector3 inputDirection)
		{
			// Calculate the camera's Y-axis rotation
			float cameraYaw = _mainCamera.transform.eulerAngles.y;

			// Smoothly interpolate the rotation of the PlayerMeshTransform to match the camera's Y-axis direction
			float rotation = Mathf.SmoothDampAngle(PlayerMeshTransform.eulerAngles.y, cameraYaw, ref _rotationVelocity, RotationSmoothTime);

			// Apply the rotation to the PlayerMeshTransform
			PlayerMeshTransform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);

			// Calculate the strafing direction based on the camera's orientation
			Vector3 strafeDirection = _mainCamera.transform.right * inputDirection.x + _mainCamera.transform.forward * inputDirection.z;
			strafeDirection.y = 0.0f;

			// Move the character in the strafing direction
			_controller.Move(strafeDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		private void HandleDefaultMovement(Vector3 inputDirection)
		{
			if (_input.move != Vector2.zero)
			{
				// Calculate the target rotation based on input direction and camera orientation
				_targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
								  _mainCamera.transform.eulerAngles.y;

				// Smoothly interpolate the rotation
				float rotation = Mathf.SmoothDampAngle(PlayerMeshTransform.eulerAngles.y, _targetRotation, ref _rotationVelocity,
													   RotationSmoothTime);

				// Apply the rotation to the PlayerMeshTransform
				PlayerMeshTransform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
			}

			// Move the character in the target direction
			Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
			_controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
							 new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
		}

		private void UpdateAnimator(float targetSpeed)
		{
			if (_hasAnimator)
			{
				_animator.SetFloat(_animIDSpeed, _animationBlend);
				_animator.SetFloat(_animIDMotionSpeed, _input.analogMovement ? _input.move.magnitude : 1f);
			}
		}

		#endregion

		#region Jump and Gravity

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				HandleGroundedBehavior();
			}
			else
			{
				HandleAirborneBehavior();
			}

			ApplyGravity();
		}

		private void HandleGroundedBehavior()
		{
			_fallTimeoutDelta = FallTimeout;

			if (_hasAnimator)
			{
				_animator.SetBool(_animIDJump, false);
				_animator.SetBool(_animIDFreeFall, false);
			}

			if (_verticalVelocity < 0.0f)
			{
				_verticalVelocity = -2f;
			}

			if (_input.jump && _jumpTimeoutDelta <= 0.0f)
			{
				_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

				if (_hasAnimator)
				{
					_animator.SetBool(_animIDJump, true);
				}
			}

			if (_jumpTimeoutDelta >= 0.0f)
			{
				_jumpTimeoutDelta -= Time.deltaTime;
			}
		}

		private void HandleAirborneBehavior()
		{
			_jumpTimeoutDelta = JumpTimeout;

			if (_fallTimeoutDelta >= 0.0f)
			{
				_fallTimeoutDelta -= Time.deltaTime;
			}
			else if (_hasAnimator)
			{
				_animator.SetBool(_animIDFreeFall, true);
			}

			_input.jump = false;
		}

		private void ApplyGravity()
		{
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		#endregion

		#region Grounded Check

		private void GroundedCheck()
		{
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
				transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

			if (_hasAnimator)
			{
				_animator.SetBool(_animIDGrounded, Grounded);
			}
		}

		/// <summary>
		/// Plays a footstep sound when the player is grounded and moving.
		/// </summary>
		private void OnFootstep()
		{
			// Ensure the player is grounded and moving
			if (!Grounded || _speed <= 0.1f) return;

			// Ensure there are footstep audio clips available
			if (FootstepAudioClips == null || FootstepAudioClips.Length == 0) return;

			// Select a random footstep sound
			int index = UnityEngine.Random.Range(0, FootstepAudioClips.Length);
			AudioClip footstepClip = FootstepAudioClips[index];

			// Play the footstep sound at the player's position
			AudioSource.PlayClipAtPoint(footstepClip, transform.position, FootstepAudioVolume);
		}
		/// <summary>
		/// Plays a landing sound when the player lands after being airborne.
		/// </summary>
		private void OnLand()
		{
			// Ensure there is a landing audio clip available
			if (LandingAudioClip == null) return;

			// Play the landing sound at the player's position
			AudioSource.PlayClipAtPoint(LandingAudioClip, transform.position, FootstepAudioVolume);
		}

		#endregion
	}
}