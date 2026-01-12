# 🌱 Pixel-Life – [**Play it on Itch.io**](https://alinlucian.itch.io/pixel-life)
> **A grid-based life simulation where simple rules create complex, emergent ecosystems.**

---

## ⚡ About the Project
**Pixel-Life** is a **cellular life simulation prototype** built in Unity, focused on **emergent behavior**, **rule-based agents**, and **high-performance simulation** using **Unity Jobs + Burst**.

The world is a large 2D grid composed of simple blocks such as **Water, Grass, Dirt, Humans, and Predators**.  
Each cell follows minimal rules — yet together they form a living system where:

- Grass grows from dirt  
- Humans wander, eat grass, reproduce, and die  
- Predators hunt humans and regulate population  
- Terrain and life continuously reshape the world  

The simulation runs on a **fixed-timestep**, ensuring deterministic behavior independent of frame rate.  
This project serves as both a **technical exploration of emergent systems** and a foundation for future experiments in artificial life, ecology, and sandbox simulations.

---

## 🎮 Controls

### 🟧 World Interaction
- **Left Click** — Paint selected block (Grass / Water / Human / Predator)  
- **Mouse Wheel** — Change brush size  
- **1** — Select Grass  
- **2** — Select Water  
- **3** — Select Human  
- **4** — Select Predator  

---

## 🧠 Core Features
- **Large-Scale Grid Simulation** — Thousands of cells updated efficiently  
- **Emergent Life Behavior** — Humans and predators follow simple rules that produce complex dynamics  
- **Ecosystem Loop** — Grass → Humans → Predators → Space → Regrowth  
- **Dirt Regrowth System** — Dirt has a chance to grow back into grass over time  
- **Fixed-Timestep Simulation** — Consistent behavior regardless of FPS  
- **Unity Jobs + Burst** — Parallel, cache-friendly cell updates  
- **Checkerboard Chunk Updates** — Prevents write conflicts in parallel jobs  
- **In-Editor Painting Tool** — Spawn terrain and life interactively  
- **Deterministic Simulation** — Same seed produces the same evolution  

---

## 📜 License

**Creative Commons Attribution–NonCommercial 4.0 International (CC BY-NC 4.0)**  

This work, including all source code, assets, and materials of *“Pixel-Life”*  
by **Alin Lucian Brebulet**, is licensed under CC BY-NC 4.0.

You are free to:  
- **Share** — copy and redistribute the material in any medium or format  
- **Adapt** — remix, transform, and build upon the material  

Under the following terms:  
- **Attribution** — You must give appropriate credit and indicate if changes were made.  
- **NonCommercial** — You may not use the material for commercial purposes.  
- **No additional restrictions** — You may not apply legal terms or technological measures that legally restrict others from doing anything the license permits.

🔗 Full license text:  
https://creativecommons.org/licenses/by-nc/4.0/
