using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using Code.Canvas;
using Code.NPCs;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Code.CameraTool.NotableObjectService;

namespace Code.CameraTool
{
	/// <summary>
	/// Manages the camera tool functionality, including activation, deactivation, and interactions with notable objects.
	/// </summary>
	public class CameraToolService : MonoBehaviour
	{
		#region Fields

		// Constants
		private readonly int defaultCameraFieldOfView = 60;
		private readonly bool isRaycastVersionInsteadOfThickBox_ = true;

		// Serialized fields
		[Header("References")]
		[Tooltip("The root transform of the player.")]
		[SerializeField] private Transform playerRoot;

		[Tooltip("Global post-processing volume for camera effects.")]
		[SerializeField] private Volume globalPPFXVolume;

		[Tooltip("Mouse sensitivity for camera rotation.")]
		[SerializeField] private float cameraMouseSensitivity = 15f;

		[Tooltip("Reference to the camera tripod.")]
		[SerializeField] private CameraTripod cameraTripod_;

		[Tooltip("Audio source for the camera click sound effect.")]
		[SerializeField] private AudioSource cameraClickSFX;

		[Tooltip("Player input system for handling input actions.")]
		[SerializeField] private PlayerInput playerInput;

		[Tooltip("UI component for the camera tool.")]
		[SerializeField] private CameraToolUI cameraToolUI;

		[Tooltip("Service for managing notable objects.")]
		[SerializeField] private NotableObjectService notableObjectService;

		[Tooltip("Virtual camera for the camera tool mode.")]
		[SerializeField] private CinemachineVirtualCamera cameraModeVCam;

		[Tooltip("Virtual camera for third-person player view.")]
		[SerializeField] private CinemachineVirtualCamera playerThirdPersonVCam;

		[Header("Camera Rotation Settings")]
		[Tooltip("Minimum X rotation (upwards).")]
		[SerializeField] private float minX = -40f;

		[Tooltip("Maximum X rotation (downwards).")]
		[SerializeField] private float maxX = 70f;

		[Tooltip("Third Person Offset")]
		[SerializeField] private Vector3 cameraOffset = new Vector3(0, 1.5f, 0);

		// Private fields

		private bool usingCameraTool;
		private bool checkedIfPhotoFileExistsAndIfNotCreateIt;
		private RenderTexture renderTexture_;
		private NotableObject reticleDeterminedMostNotableObject_;
		private bool takePictureInLateUpdate_;
		private Vector2 lookVector;

		private float _cameraYaw;
		private float _cameraPitch;

		#endregion

		#region Properties

		/// <summary>
		/// Gets a value indicating whether the camera mode is toggled.
		/// </summary>
		public bool IsCameraModeActive => usingCameraTool;

		#endregion

		#region Events

		/// <summary>
		/// Event triggered when a notable object is in the camera's sight.
		/// </summary>
		public event NotableObjectInReticleAction NotableObjectInCameraSight;

		#endregion

		#region Unity Methods

		private void Start()
		{
			// Bind input actions
			playerInput.actions["CameraTool"].started += ToggleCameraTool;
			playerInput.actions["TakePicture"].started += TakePictureInputHandler;
			playerInput.actions["Look"].performed += LookVectorListener;

			Cursor.lockState = CursorLockMode.Locked;
		}

		private void Update()
		{
			UpdateFocusObjectWhenActive();

			// Handle rotation regardless of camera tool state
			HandleCameraRotation();

			// Set the local position offset
			playerThirdPersonVCam.transform.localPosition = cameraOffset;
			playerThirdPersonVCam.transform.parent.rotation = playerRoot.rotation;
		}

		private void LateUpdate()
		{
			ThirdPersonCameraFollowTarget();
		}

		private void OnDestroy()
		{
			if (playerInput == null) return;

			// Unbind input actions
			playerInput.actions["TakePicture"].started -= TakePictureInputHandler;
			playerInput.actions["CameraTool"].started -= ToggleCameraTool;
			playerInput.actions["Look"].performed -= LookVectorListener;
		}

		#endregion

		#region Camera Tool Management

		/// <summary>
		/// Toggles the camera tool on or off.
		/// </summary>
		/// <param name="obj">Input action callback context.</param>
		protected void ToggleCameraTool(InputAction.CallbackContext obj)
		{
			if (!usingCameraTool)
			{
				ActivateCameraTool();
			}
			else
			{
				DeactivateCameraTool();
			}
		}

		/// <summary>
		/// Activates the camera tool.
		/// </summary>
		public bool ActivateCameraTool()
		{
			if (cameraTripod_ != null && cameraTripod_.TripodDeployed)
			{
				playerThirdPersonVCam.Priority = 0;
			}
			else
			{
				playerThirdPersonVCam.Priority = 0;
				cameraModeVCam.Priority = 10;
			}

			Cursor.lockState = CursorLockMode.Locked;
			usingCameraTool = true;
			cameraToolUI.RequestEnableUI();
			return true;
		}

		/// <summary>
		/// Deactivates the camera tool.
		/// </summary>
		public bool DeactivateCameraTool()
		{
			if (cameraTripod_ != null && cameraTripod_.TripodDeployed)
			{
				cameraTripod_.TripodObject.VirtualCamera.enabled = false;
			}

			cameraToolUI.DisableUI();
			usingCameraTool = false;

			playerThirdPersonVCam.Priority = 10;
			cameraModeVCam.Priority = 0;

			if (reticleDeterminedMostNotableObject_ != null)
			{
				reticleDeterminedMostNotableObject_.ExitingCameraView();
				reticleDeterminedMostNotableObject_ = null;
			}

			return true;
		}

		#endregion

		#region Camera Rotation

		/// <summary>
		/// Handles camera rotation based on input.
		/// </summary>
		private void HandleCameraRotation()
		{
			if (playerInput == null)
			{
				return;
			}

			Vector2 lookInput = playerInput.actions["Look"].ReadValue<Vector2>();
			float deltaTimeMultiplier = Time.deltaTime;

			// Update yaw and pitch
			_cameraYaw += lookInput.x * deltaTimeMultiplier * cameraMouseSensitivity;
			_cameraPitch -= lookInput.y * deltaTimeMultiplier * cameraMouseSensitivity;

			// Clamp pitch to prevent over-rotation
			_cameraPitch = Mathf.Clamp(_cameraPitch, minX, maxX);

			if (usingCameraTool)
			{
				// Apply rotation to the camera tool's virtual camera
				cameraModeVCam.transform.localRotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0.0f);
			}
			else
			{
				// Apply rotation to the playerRoot for third-person behavior
				playerRoot.localRotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0.0f);
			}
		}

		private void ThirdPersonCameraFollowTarget()
		{
			// Smoothly interpolate the camera's parent position toward the target position
			playerThirdPersonVCam.transform.parent.position = Vector3.Lerp(
				playerThirdPersonVCam.transform.parent.position, // Current position
				playerRoot.position,                                   // Target position
				Time.deltaTime * 10f                             // Lerp speed (adjust 10f as needed)
			);
		}

		#endregion

		#region Picture Taking

		/// <summary>
		/// Handles the input for taking a picture.
		/// </summary>
		/// <param name="context">Input action callback context.</param>
		private void TakePictureInputHandler(InputAction.CallbackContext context)
		{
			if (cameraToolUI.RootNode.activeSelf)
				TakePicture();
		}

		/// <summary>
		/// Takes a picture and processes notable objects in the frame.
		/// </summary>
		public void TakePicture()
		{
			var notableObjects = DetectWhatsInScreen();
			var mostNotable = reticleDeterminedMostNotableObject_;

			if (mostNotable != null)
			{
				var nameActionText = $"{mostNotable.DisplayName}";
				mostNotable.SendCapturedEvent();
			}
		}

		/// <summary>
		/// Detects notable objects currently visible in the camera's view.
		/// </summary>
		/// <returns>List of notable objects detected.</returns>
		public List<NotableObject> DetectWhatsInScreen()
		{
			var detectedNotableObjects = new List<NotableObject>();
			foreach (var notableObject in notableObjectService.ReturnAllNotableObjectsInPictureOrNull())
			{
				detectedNotableObjects.Add(notableObject);
			}

			return detectedNotableObjects;
		}

		#endregion

		#region Reticle and Camera Movement

		/// <summary>
		/// Listens for look vector input and moves the reticle accordingly.
		/// </summary>
		/// <param name="context">Input action callback context.</param>
		protected void LookVectorListener(InputAction.CallbackContext context)
		{
			if (!cameraToolUI.RootNode.activeSelf) return;

			var lookV = context.ReadValue<Vector2>() * cameraMouseSensitivity;
			MoveReticle(lookV);
		}

		/// <summary>
		/// Moves the reticle based on input and clamps camera rotation.
		/// </summary>
		/// <param name="moveDistance">Distance to move the reticle.</param>
		private void MoveReticle(Vector2 moveDistance)
		{
			if (cameraModeVCam == null) return;

			var currentX = cameraModeVCam.transform.localRotation.eulerAngles.x;
			currentX -= moveDistance.y;

			if (currentX > 180f) currentX -= 360f;
			currentX = Mathf.Clamp(currentX, minX, maxX);

			cameraModeVCam.transform.localRotation = Quaternion.Euler(currentX, 0, 0);
			playerRoot.Rotate(Vector3.up, moveDistance.x);
		}

		#endregion

		#region Notable Object Management

		/// <summary>
		/// Updates the most focused notable object when the camera tool is active.
		/// </summary>
		public void UpdateFocusObjectWhenActive()
		{
			if (!usingCameraTool) return;

			NotableObject mostNotableObj = null;

			if (isRaycastVersionInsteadOfThickBox_)
			{
				mostNotableObj = notableObjectService.ReturnNotableObjectUsingRayCastMethod(
					cameraToolUI.GetReticleScreenPos()
				);
			}

			if (reticleDeterminedMostNotableObject_ != mostNotableObj)
			{
				reticleDeterminedMostNotableObject_?.ExitingCameraView();
			}

			mostNotableObj?.CameraToolOnTarget();
			reticleDeterminedMostNotableObject_ = mostNotableObj;

			NotableObjectInCameraSight?.Invoke(mostNotableObj);
		}

		#endregion
	}
}