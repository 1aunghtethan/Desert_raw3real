# Item Placement & Ghost View System Documentation

This document explains the implementation of the item placement logic and the "Ghost View" preview system.

## 📄 Core Script
- **Script Name**: `ItemPlacer.cs`
- **File Location**: `Assets/Scripts/Items/ItemPlacer.cs`
- **Attached To**: **Player** GameObject (Instance ID: 45666).

---

## 🏗️ Ghost View (Preview) Logic
The Ghost View provides a visual preview of where an item will be placed before you commit to the action.

### 1. Detection
The system uses a **Raycast** starting from the player's active camera, pointing straight ahead. It performs a `Physics.RaycastAll` to safely penetrate and **ignore** the player's own physics colliders and the ghost's colliders.
It measures the validity distance from the **Player's Position** (not the camera), which ensures perfect accuracy even when playing in **Third-Person Mode** where the camera is set further back.

### 2. Reconstruction
When you select an item in your hotbar, `RebuildGhost()`:
- Instantiates a clone of the item's **DropPrefab** (or **Prefab** if no drop prefab exists).
- **Strips functionality**: It removes rigidbodies, scripts, and non-essential components to ensure the ghost doesn't rotate, fall, or trigger game logic.
- **Material Swap**: All renderers are switched to a special translucent Material (`_ghostMaterial`) that supports URP transparency.
- **Layer Isolation**: The ghost is placed on the **Ignore Raycast** layer so it cannot interfere with placement raycasts, interaction raycasts, or weapon raycasts.

### 3. Visual Feedback
- **Color Coding**: The ghost turns **Green** when in valid placement range and **Red** if it's too far away.
- **Scale Sync**: The ghost's size is automatically synchronized with the `ItemDropper` settings (default `0.4f` for weapons without drop prefabs via `ItemDropper.DefaultDropScale`), ensuring the preview matches the final result.
- **Surface Alignment**: The system uses `Quaternion.FromToRotation` to align the ghost with the ground's **Surface Normal**, making it sit flat on slopes.

### 4. Performance
- The **InventoryPanelUI** reference is cached on `Start()` and only re-fetched if the reference becomes null (e.g., after a scene reload). This avoids calling `FindFirstObjectByType` every frame.
- Ghost cleanup is handled in both `OnDisable()` and `OnDestroy()` to prevent leaked GameObjects.

---

## 🛠️ Placement Logic
The actual placement happens when you confirm the preview.

### 1. Interaction
- **Enable/Disable Mode**: Press **B** to enter or exit placement mode. Only while this mode is active will the ghost appear.
- **Place Item**: Left-Click will instantly place the item, respecting strict distance checks to prevent placing an item if the ghost preview is Red.
- **Rotation**: Press **R** to rotate the preview in 90-degree steps.

### 2. Execution
When `TryPlaceItem()` is called:
1. It verifies the ghost is active and in range.
2. It calls `ItemDropper.CreateWorldPickup()` to spawn the real world object.
3. It **applies the ghost's rotation** to the new object so it matches the preview exactly.
4. For heavy items (where `PickupsSpinAndBob = false`), it sets `isKinematic = true`, `useGravity = false`, and freezes all constraints to lock the object in place.
5. It removes **1** item from the currently selected inventory slot.

---

## ⚙️ Configuration (Inspector)
You can fine-tune the system on the **Player** GameObject:
- **Place Distance**: How far away you can place items.
- **Place Height Offset**: Small gap above the ground to prevent "z-fighting" with the terrain.
- **Ground Layers**: Which layers count as valid placement surfaces.
- **Rotation Step**: How many degrees the item turns when you press **R**.
