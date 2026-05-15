---
name: unity-skills
description: "Unity Editor automation via REST API (Unity-Skills). Triggers: Unity, Unity Editor, automate Unity, create script, build scene, manage assets, GameObjects, materials, prefabs, Cinemachine, UI Toolkit, ProBuilder. Use ONLY when the user wants to control or automate the Unity Editor through the UnitySkills REST server."
---

# Unity Skills

Use this skill when the user wants to automate the Unity Editor through the local UnitySkills REST server.

- **513 REST skills** across 40+ modules
- **14 advisory design modules** for architecture guidance
- Unity baseline: **2022.3+**
- Default timeout: **15 minutes**

## Prerequisites

1. Unity Editor must be running with the UnitySkills package installed
2. REST server started via: **Window > UnitySkills > Start Server**
3. Server endpoint: `http://localhost:8090`

## Python Helper

The Python client is at `.agent/skills/unity-skills/scripts/unity_skills.py` (project-local) or `Unity-Skills-main/SkillsForUnity/unity-skills~/scripts/unity_skills.py`.

```python
import sys; sys.path.insert(0, '.agent/skills/unity-skills/scripts')
from unity_skills import call_skill, is_unity_running, wait_for_unity
```

## Operating Modes

### Semi-Auto (default)
~80 skills active: script, perception, scene, editor, asset, workflow, debug/console, + 14 advisory modules. Code-first approach.

### Full-Auto
All 513 skills active. Direct Unity manipulation (create GameObjects, configure materials/lights/UI, build scenes). Activate when user says: "full auto", "全自动模式", "build the scene for me", "直接操作 Unity".

## Core Rules

1. **Version routing**: If user mentions a Unity version, set it: `call_skill("project_get_version")`
2. **Batch operations**: When touching 2+ objects, prefer `*_batch` skills
3. **Workflow**: For multi-step mutations, wrap in `workflow_context` for rollback safety
4. **Compilation**: Script creation/edits may trigger Domain Reload — server will be temporarily unavailable. Wait and retry.
5. **Tests**: `test_*` skills return a `jobId` — poll with `test_get_result(jobId)`

## HTTP Direct Calls

```bash
curl http://localhost:8090/skills
curl -X POST http://localhost:8090/skill/gameobject_create -H "Content-Type: application/json" -d '{"name":"Cube","primitiveType":"Cube"}'
```

## Full Skill Reference

See `Unity-Skills-main/SkillsForUnity/unity-skills~/SKILL.md` for complete skill definitions.
