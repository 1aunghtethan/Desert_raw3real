using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GinjaGaming.FinalCharacterController
{
    [DefaultExecutionOrder(-2)]
    public class PlayerActionsInput : MonoBehaviour, PlayerControls.IPlayerActionsMapActions
    {
        #region Class Variables
        private PlayerLocomotionInput _playerLocomotionInput;
        private PlayerState _playerState;
        private Coroutine _gatherRoutine;
        public bool GatherPressed { get; private set; }
        public bool AttackPressed { get; private set; }
        #endregion

        #region Startup
        private void Awake()
        {
            _playerLocomotionInput = GetComponent<PlayerLocomotionInput>();
            _playerState = GetComponent<PlayerState>();
        }
        private void OnEnable()
        {
            if (PlayerInputManager.Instance?.PlayerControls == null)
            {
                Debug.LogError("Player controls is not initialized - cannot enable");
                return;
            }

            PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.Enable();
            PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.SetCallbacks(this);
        }

        private void OnDisable()
        {
            if (PlayerInputManager.Instance?.PlayerControls == null)
            {
                Debug.LogError("Player controls is not initialized - cannot disable");
                return;
            }

            PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.Disable();
            PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.RemoveCallbacks(this);
        }
        #endregion

        #region Update
        private void Update()
        {
            if (_gatherRoutine != null)
                return;

            if (_playerLocomotionInput.MovementInput != Vector2.zero ||
                _playerState.CurrentPlayerMovementState == PlayerMovementState.Jumping ||
                _playerState.CurrentPlayerMovementState == PlayerMovementState.Falling)
            {
                GatherPressed = false;
            }
        }

        public void SetGatherPressedFalse()
        {
            GatherPressed = false;
        }

        public void TriggerGathering(float duration)
        {
            if (_gatherRoutine != null)
                StopCoroutine(_gatherRoutine);

            _gatherRoutine = StartCoroutine(GatheringRoutine(duration));
        }

        public void SetAttackPressedFalse() 
        { 
            AttackPressed = false;
        }
        #endregion

        #region Input Callbacks
        public void OnGathering(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            TriggerGathering(0.8f);
        }

        public void OnAttacking(InputAction.CallbackContext context)
        {
            if (!context.performed)
                return;

            AttackPressed = true;
        }
        #endregion

        private IEnumerator GatheringRoutine(float duration)
        {
            GatherPressed = true;
            yield return new WaitForSeconds(duration);
            GatherPressed = false;
            _gatherRoutine = null;
        }
    }
}
