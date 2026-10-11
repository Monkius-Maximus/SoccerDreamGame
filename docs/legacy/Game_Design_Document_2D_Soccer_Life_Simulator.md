### **Game Design Document: 2D Soccer Life Simulator**

**1\. Core Architecture & Visual Perspective**

* **Structure:** Separated scene architecture.  
* **Match Engine:** Side-on orthographic view (horizontal scrolling). Replicates 16-bit physical constraints and sprite logic.  
* **Life Simulation:** Isometric projection. Optimised for housing grids, routing, and complex object interactions.  
* **Asset Resolution Guidelines:**  
  * UI/Menus: 64x64 pixels.  
  * Match HUD: 16x16 or 32x32 pixels.  
  * HD-2D Scaling (if applicable): 512x512 pixels.

**2\. Time Management System**

* **Base:** Real-time clock delta accumulator for the active scene.  
* **Controls:** Pause, fast-forward (2x, 4x) modifying the global time variable.  
* **Task Skipping:** Calculates remaining action time, advances clock, applies stat/resource changes instantly.  
* **Calendar Simulation:** Bypasses rendered scenes. Uses mathematical background calculation for dates/events. Requires mechanical trade-offs (e.g., lower stat yield) compared to manual play.

**3\. Event & Interruption System**

* **Trigger:** RNG rolled against global probability thresholds during macro-simulation.  
* **Resolution Tiers:**  
  * **High Stakes:** Abstract mini-games (e.g., contract negotiations). Skill/luck dictates outcome.  
  * **Medium Stakes:** Dialogue choices (e.g., press conferences). Options gated by personality traits.  
  * **Low Stakes:** Passive notifications (e.g., flight delays). Unavoidable stat modifiers.

**4\. Character State Architecture**

| Component | State | Logic | Database Handling |
| :---- | :---- | :---- | :---- |
| **Personality** | Static | Dictates off-pitch event probability weights and on-pitch AI behavior (e.g., aggression, selfishness). | Assigned at generation. Read-only during season. |
| **Form / Mood** | Dynamic | Applies integer modifiers (-5 to \+5) to base physical/technical stats. | Recalculated post-match or post-event. Resets seasonally. Bypassed in arcade modes. |

**5\. Simulation Level of Detail (LOD)** Limits background processing to maintain performance.

* **Tier 1: High Simulation (Active Human League)**  
  * Full event generation.  
  * Match engine simulates minute-by-minute stats.  
  * Tracks daily form fluctuations and injuries.  
* **Tier 2: Statistical Simulation (Major Foreign Leagues)**  
  * No life simulation events.  
  * Matches resolved via team Elo/tactical aggregate algorithms.  
  * Form updated weekly based strictly on match rating.  
* **Tier 3: Database Simulation (Minor Leagues)**  
  * Pure mathematical resolution at week's end.  
  * Form/Mood ignored. Players perform at base attributes.  
  * Simple SQL inserts for standings and scorers.

