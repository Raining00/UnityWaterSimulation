# Water System

A Unity URP ocean system combining GPU wave simulation with realistic and stylized rendering.

## Overview

- **FFT simulation:** Multi-cascade wave spectra, GPU inverse FFT, displacement, normals, and wave-breaking foam.
- **Ocean geometry:** Camera-driven quadtree LOD, instanced mesh patches, and an extended horizon.
- **Realistic rendering:** Water absorption and scattering, reflections, depth-based refraction, foam, caustics, and GPU spray particles.
- **Underwater (realistic mode):** RGB absorption, distance fog, FFT waterline transitions, underwater caustics, and water-to-air refraction.
- **Stylized rendering:** Turquoise-to-deep-blue water colors, scrolling detail normals, soft reflections, and depth-based shoreline foam.

Both rendering modes share the same simulation. Appearance settings are controlled through the Ocean component or C#.

## Getting Started

Open the project with **Unity 6000.5.7f1** and **URP 17.5**. Try [DeepOceanDemo](Assets/WaterSystem/Demo/DeepOceanDemo.unity) or [StylizedOceanDemo](Assets/WaterSystem/Demo/StylizedOceanDemo.unity).

To create an ocean, add `OceanRenderer` to a GameObject. Its simulation components are connected automatically. Choose **Physical** or **Stylized** under **Shading Mode**. Enable the camera's depth and opaque textures for refraction and shoreline effects.

Try [UnderwaterDemo](Assets/WaterSystem/Demo/UnderwaterDemo.unity) for an automatic dive preview.

## Realistic Rendering

![Realistic ocean — overview](img/1.gif)

![Realistic ocean — waves](img/2.gif)

![Realistic ocean — surface detail](img/3.gif)

## Stylized Rendering

![Stylized ocean](img/4.gif)
