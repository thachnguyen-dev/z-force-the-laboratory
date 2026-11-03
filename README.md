# Z-Force: The Laboratory

**A top-down zombie shooter for Android and iOS, with a mission campaign, an endless mode and online 2-player co-op.**

[<img src="https://play.google.com/intl/en_us/badges/static/images/badges/en_badge_web_generic.png" height="60">](https://play.google.com/store/apps/details?id=com.thachnguyen.zombieproto)

<p>
<img src="https://play-lh.googleusercontent.com/kyKafRrIiRs-ivBHYCA4XYDnYYUitwjJJZoy7OlzoLpEYawk_1nY-FHra0aHI9WYVjBw_Itup3OxndUBElyQ40s=w526-h296" width="49%">
<img src="https://play-lh.googleusercontent.com/ysW-V6WB0QMEB57cOnnu4Zt-N8njQx4g8d3VvajI8QYDNA67TANEKP8lvvIzegBFFBBmeXqnlgMYxiXDEz6TLA=w526-h296" width="49%">
<img src="https://play-lh.googleusercontent.com/D44LnxXA9ZFOsHCEvBv59iXHUgF1WBO0pcIBB4w8oHmFzPqAsdB3Y2uFXgFhkyXBGVJ50s4feaBWlO2othrduQ=w526-h296" width="49%">
<img src="https://play-lh.googleusercontent.com/VdJdDTT8cVw-gKoHJMCC74L2i5Tv85wf6REGXdWdS26Xjp9BFCnfy8vkWUKJSYF9_GbWpcFzNsWu79LtFTlg=w526-h296" width="49%">
</p>

A virus has escaped from a secret lab and a whole town is sealed off. Your special forces team flies in to rescue the
trapped scientists and recover the vaccine. Scavenge weapons, manage a limited backpack, and fight through six
missions, an endless survival mode, or play online with a friend.

A solo project in **Unity 6 and C#** (URP), about 40,000 lines of code.

> This repository holds the **source code only** (runtime, editor tools and tests). Models, textures, sounds and
> scenes are not included, so the project does not open as a playable game. To play it, install it from Google Play.

## Highlights

**Online co-op.** Two players connect through Netcode for GameObjects and a Unity Relay join code. The host runs the
simulation and the client only shows it (host-authoritative). Player actions travel as small commands, game events
come back from the host, and the teammate's movement is smoothed because network updates arrive unevenly. If the
online teammate drops, their character stays in the world.

**One character pipeline for everyone.** A touch player, a keyboard player, the online teammate and the AI teammate
all produce the same per-frame *intent*. They all go through the same `Survivor` code, so there is no separate
combat system for the AI and no special case for network players.

**Zombie AI.** Zombies and bosses see in an 80° cone blocked by walls, hear gunfire (less in the rain), and move with
NavMesh around obstacles. New zombies only spawn where no player's camera can see, and populations are streamed in
rather than placed in the scene up front.

**Missions and maps.** Six missions (checkpoint boss, escort, plant C4 on a hive, hunting in a wildlife reserve,
night rescue, a race for the vaccine) plus an endless wave mode with day, rain and night. Stages are tile-based maps
saved as light JSON files. A generator builds two layouts per mission so replays feel different.

**Procedural animation.** Characters and zombies are posed in code every frame from their state: a walk and run
gait based on real leg movement, and weapon holds where the whole body turns so no arm clips into the torso. No
animation clips are needed, and local, remote and AI characters animate identically.

**Mobile performance.** The camera never rotates, so map meshes keep only the faces that can be seen. Map pieces are
merged into one mesh per 16 m chunk. Sight lines and gameplay collision use the stage grid instead of physics
queries. Shadows are simple blob shadows.

**Release work.** LevelPlay ads with EU/UK consent, iOS App Tracking Transparency, a one-time purchase (Remove Ads +
Co-op) with Android backup rules so it restores correctly, atomic and versioned settings files, and headless
batch-mode builds: a signed Android App Bundle and an Xcode project export for iOS.

## Editor tools

- **Map Editor window:** paint cells, place loot, spawns and props, and save each stage to JSON.
- **Scene Stage Editor:** add, rotate and mirror map objects directly in the Unity scene.
- **Character tools:** import and check rigged FBX characters (size, pivot, skeleton, weights), bake them for the
  game, and pose full-body weapon grips.
- **Asset pipelines:** obstacle pack colouring, sound and portrait import, procedural ground textures.
- **Build commands:** project setup, Android and iOS builds from the command line.

## Code layout

```
Assets/
  Runtime/
    Game/      game flow and menus, campaign, missions, endless mode, tutorial, monetization, settings
    Net/       session (Netcode + Relay), host, client, messages, remote motion smoothing
    World/     survivors, zombies, projectiles, stage map and runtime, procedural animation, atmosphere
    Control/   touch and PC controls, AI teammate, hostage behaviour
    Logic/     pure C# rules: inventory, firearms, shot maths, zombie decisions, match rules
    Art/       map pieces and effects built as meshes in code, post-processing
    UI/, Audio/, Data/
  Editor/      29 editor tools
  Tests/       EditMode and PlayMode (Unity Test Framework)
```

**About 230 automated tests** (87 edit-mode, 141 play-mode) cover gameplay rules, networking and purchases.

## Tech

Unity 6 · C# · URP · Netcode for GameObjects · Unity Relay · NavMesh · Unity IAP · LevelPlay · Unity Test Framework

## About

Made by **Nguyen Ngoc Thach**, Unity developer and game development student at VTC Academy. I designed the game,
made the character models, directed the art and tested every build on real phones. I wrote the code with Claude
Code as an AI pair programmer; much of the map art is generated in code.
More: [github.com/thachnguyen-dev](https://github.com/thachnguyen-dev) ·
[my games on Google Play](https://play.google.com/store/apps/developer?id=the%20stone)
