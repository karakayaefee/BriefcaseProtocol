# Briefcase Protocol

2v2, host-authoritative social deception and bomb-defusal prototype built with Unity 6.

## Current implementation

- Four-player private UGS Multiplayer Session with Relay and local host/client fallback.
- Red/Blue teams with unique Operator and Support lobby slots.
- Server-timed two-round match flow and automatic side swapping.
- First-person network player, role-based interaction permissions and non-blocking player collisions.
- Real/fake briefcases, deterministic color/serial lock rules, three-digit keypad penalties and five-strike limit.
- Wire Logic and Sequence Button modules.
- Separate 100-point bomb and decoy budgets.
- Remote sound lure and safe controlled-door trap.
- English/Turkish manual and prototype HUD.
- Vivox global push-to-talk adapter and speaking-state integration point.
- EditMode/PlayMode tests, prototype scene generator and Windows build commands.

## First-time setup

1. Open `Bomba` with Unity `6000.5.4f1`.
2. Allow Package Manager to install Multiplayer Services, Netcode for GameObjects and Vivox.
3. Run **Briefcase Protocol > Setup Prototype Project**.
4. Open `Assets/Game/Generated/Scenes/Bootstrap.unity` and enter Play Mode.
5. Use **Local Host (Dev)** for an offline development instance, or link the project in Unity Dashboard and use the private UGS lobby buttons.

The same setup can run headlessly:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe' -batchmode -quit -projectPath "$PWD\Bomba" -executeMethod BriefcaseProtocol.Editor.ProjectScaffolder.SetupPrototypeProject
```

## Tests and builds

Run tests from Unity Test Runner, or in batch mode:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe' -batchmode -projectPath "$PWD\Bomba" -runTests -testPlatform EditMode -testResults "$PWD\TestResults-EditMode.xml" -quit
& 'C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe' -batchmode -projectPath "$PWD\Bomba" -runTests -testPlatform PlayMode -testResults "$PWD\TestResults-PlayMode.xml" -quit
```

Windows builds are available under **Briefcase Protocol > Build**, or through `BriefcaseProtocol.Editor.BuildTools.BuildWindowsDevelopmentBatch` and `BuildWindowsReleaseBatch`.

## Required external setup

- Link the Unity project to a Cloud Project ID.
- Enable Authentication, Multiplayer Services/Relay and Vivox in Unity Dashboard.
- Complete the DSA notification and privacy flow before public testing.
- Complete the Steamworks checklist in [STEAM_RELEASE_CHECKLIST.md](Docs/STEAM_RELEASE_CHECKLIST.md).

The generated IMGUI is intentionally a functional development interface. Final visual UI, environment art and audio should replace it without changing the underlying service and gameplay APIs.
