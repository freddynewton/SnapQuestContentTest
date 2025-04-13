using UnityEngine;
using UnityEngine.InputSystem;

namespace Code.CameraTool
{
    public class CameraTripod
    {
        public bool TripodHasCameraFocus;
        public float TripodPanHeight;
        public float TripodPanLeftRight;
        public float MaxDistanceFromTripodBeforeRecall;
        public GameObject Tripod;
        public TripodObject TripodObject;
        public bool TripodDeployed;
        public Transform PlayerRoot;
        public CameraToolService cameraToolService_;
        public GameObject TripodPrefab;
        
        public CameraTripod(PlayerInput playerInput, CameraToolService cameraToolService)
        {
            //init to actions
            var deployTripod = playerInput.actions["Tripod"];
            deployTripod.started += DeployTripod;
            cameraToolService_ = cameraToolService;
        }

        private void DeployTripod(InputAction.CallbackContext obj)
        {
            // spawn the 3d object from GameMain prefabs and do place animation, lock movement for a second.
            if (Tripod == null)
            {
                Tripod = GameObject.Instantiate(TripodPrefab);
                TripodObject = Tripod.GetComponent<TripodObject>();
                TripodObject.SetParentScript(this);
            }
            if (TripodDeployed)
            {
                Tripod.gameObject.SetActive(false);
                TripodDeployed = false;
            }
            else if(cameraToolService_.IsCameraModeActive)
            {
                Tripod.gameObject.SetActive(true);
            }

            TripodDeployed = true;

            // reset V-cam angle to current camera angle
            Tripod.transform.position = PlayerRoot.position;
            TripodObject.CameraPivot.transform.localRotation = Quaternion.identity;
            Tripod.transform.rotation = Quaternion.Euler(0, Camera.main.transform.rotation.eulerAngles.y, 0);
            TripodObject.VirtualCamera.transform.rotation = Camera.main.transform.rotation;
            TripodObject.AdjustHeightAndSetPivotAtTopOfStand();
            TripodObject.VirtualCamera.m_Lens.FieldOfView = 60;

            Tripod.SetActive(true);
            // camera can pivot, but not move around (up and down / left and right can move up and down a certain distance for ease of use?)

            cameraToolService_.DeactivateCameraTool();
            // show UI controls for moving camera around the tripod
            // numbers for poses (like in selfie mode) (full body?)
            
            // check for distance and do a call out and return tripod if player moved too far away? Or better to just have recall button with G again.
        }
    }
}