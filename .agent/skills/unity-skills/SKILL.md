---
name: unity-skills
description: "Unity Editor automation via REST API. Control GameObjects, components, scenes, materials, prefabs, lights, and more with 100+ professional tools."
---

# Unity Editor Control Skill

You are an expert Unity developer. This skill enables you to directly control Unity Editor through a REST API.
Use the Python helper script in `scripts/unity_skills.py` to execute Unity operations.

## Prerequisites

1. Unity Editor must be running with the UnitySkills package installed
2. REST server must be started: **Window > UnitySkills > Start Server**
3. Server endpoint: `http://localhost:8090`

## Quick Start

```python
# Import the helper from the scripts/ directory
import sys
sys.path.insert(0, 'scripts')  # Adjust path to skill's scripts directory
from unity_skills import call_skill, is_unity_running, wait_for_unity

# Check if Unity is ready
if is_unity_running():
    # Create a cube
    call_skill('gameobject_create', name='MyCube', primitiveType='Cube', x=0, y=1, z=0)
    # Set its color to red
    call_skill('material_set_color', gameObjectName='MyCube', r=1, g=0, b=0)
```

## ⚠️ Important: Script Creation & Domain Reload

When creating C# scripts with `script_create`, Unity recompiles all scripts (Domain Reload).
The server temporarily stops during compilation and auto-restarts.

```python
# After creating a script, wait for Unity to recompile
result = call_skill('script_create', name='MyScript', template='MonoBehaviour')
if result.get('success'):
    wait_for_unity(timeout=10)  # Wait for server to come back
```

## Available Skills

Summary of 431 skills available across various Unity modules (Cinemachine, GameObject, Material, Scene, UI, Physics, Audio, Texture, Model, Script, Package, etc.)

For a full list of skills, call `get_skills()` from `unity_skills.py` or check the `references/` directory.

## Skill Directory Structure

```
unity-skills/
├── SKILL.md          # This file - skill entry point
└── scripts/
    ├── unity_skills.py  # Python helper with call_skill(), is_unity_running(), etc.
    └── agent_config.json # Agent identification
```
