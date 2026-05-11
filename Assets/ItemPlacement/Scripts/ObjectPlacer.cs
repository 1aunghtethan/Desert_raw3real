using GinjaGaming.FinalCharacterController;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPlacer : MonoBehaviour
{
    [Header("Placement Parameters")]
    [SerializeField] private GameObject placeableObjectPrefab;
    [SerializeField] private GameObject previewObjectPrefab;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask placementSurfaceLayerMask;

    [Header("Preview Material")]
    [SerializeField] private Material previewMaterial;
    [SerializeField] private Color validColor;
    [SerializeField] private Color invalidColor;

    [Header("Raycast Parameters")]
    [SerializeField] private float objectDistanceFromPlayer;
    [SerializeField] private float raycastStartVerticalOffset;
    [SerializeField] private float raycastDistance;

    [Header("Rotation Parameters")]
    [SerializeField] private float rotationStepDegrees = 90f;

    private GameObject _previewObject = null;
    private Vector3 _currentPlacementPosition = Vector3.zero;
    private float _currentPlacementPitch = 0f;
    private float _currentPlacementYaw = 0f;
    private bool _inPlacementMode = false;
    private bool _validPreviewState = false;

    private void Update()
    {
        UpdateInput();

        if (_inPlacementMode)
        {
            UpdateCurrentPlacementPosition();

            if (CanPlaceObject())
                SetValidPreviewState();
            else
                SetInvalidPreviewState();
        }
    }

    private void UpdateCurrentPlacementPosition()
    {
        Vector3 cameraForward = new Vector3(playerCamera.transform.forward.x, 0f, playerCamera.transform.forward.z);
        cameraForward.Normalize();

        Vector3 startPos = playerCamera.transform.position + (cameraForward * objectDistanceFromPlayer);
        startPos.y += raycastStartVerticalOffset;

        RaycastHit hitInfo;
        if (Physics.Raycast(startPos, Vector3.down, out hitInfo, raycastDistance, placementSurfaceLayerMask))
        {
            _currentPlacementPosition = hitInfo.point;
        }

        _previewObject.transform.position = _currentPlacementPosition;
        _previewObject.transform.rotation = CurrentPlacementRotation();
    }

    private void UpdateInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            EnterPlacementMode();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            ExitPlacementMode();
        }
        else if (Input.GetMouseButtonDown(0))
        {
            PlaceObject();
        }
        else if (Input.GetMouseButtonDown(1))
        {
            RotatePreviewObject();
        }
    }

    private void SetValidPreviewState()
    {
        previewMaterial.color = validColor;
        _validPreviewState = true;
    }

    private void SetInvalidPreviewState()
    {
        previewMaterial.color = invalidColor;
        _validPreviewState = false;
    }

    private bool CanPlaceObject()
    {
        if (_previewObject == null)
            return false;

        return _previewObject.GetComponentInChildren<PreviewObjectValidChecker>().IsValid;
    }

    private void PlaceObject()
    {
        if (!_inPlacementMode || !_validPreviewState)
            return;

        Instantiate(placeableObjectPrefab, _currentPlacementPosition, CurrentPlacementRotation(), transform);

        ExitPlacementMode();
    }

    private void EnterPlacementMode()
    {
        if (_inPlacementMode)
            return;

        PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.Disable();

        _currentPlacementPitch = 0f;
        _currentPlacementYaw = playerCamera.transform.eulerAngles.y;
        _previewObject = Instantiate(previewObjectPrefab, _currentPlacementPosition, CurrentPlacementRotation(), transform);
        _inPlacementMode = true;
    }

    private void RotatePreviewObject()
    {
        if (!_inPlacementMode)
            return;

        _currentPlacementPitch += rotationStepDegrees;
        _currentPlacementPitch = Mathf.Repeat(_currentPlacementPitch, 360f);

        if (_previewObject != null)
            _previewObject.transform.rotation = CurrentPlacementRotation();
    }

    private Quaternion CurrentPlacementRotation()
    {
        return Quaternion.Euler(0f, _currentPlacementYaw, 0f) * Quaternion.Euler(_currentPlacementPitch, 0f, 0f);
    }

    private void ExitPlacementMode()
    {
        PlayerInputManager.Instance.PlayerControls.PlayerActionsMap.Enable();

        Destroy(_previewObject);
        _previewObject = null;
        _inPlacementMode = false;
    }
}
