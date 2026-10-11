# Legacy design documents (frozen, pt-BR)

These are the design documents and decision registers written before ADR-0016, in the owner's
Claude project, between June and August 2026. They are kept here **exactly as they were**, in
Brazilian Portuguese, and are read-only (ADR-0016 §3). Do not edit them.

- **Citing a decision:** use its register prefix (`GEO-D53`, `CAL-D38`); see
  `docs/decisions/LEGACY-IDS.md`.
- **Changing a decision:** write a new ADR that names the legacy id it supersedes.
- **Names:** these documents use the old vocabulary ("banda", "tier", `Regen`, "edição"…). The
  current name of every concept is in `docs/TERMS.csv`.
- **Copy:** copied verbatim on 2026-10-11. The only change is in four file names, given ASCII
  names (marked † below).

## Decision registers

| File | Prefix | Status |
| --- | --- | --- |
| `Terra_Paralela_Registro_Decisoes_Camada_Autoria_Mundo.md` | `WORLD` | Living register. `WORLD-D20` is superseded by ADR-0017 |
| `Terra_Paralela_Registro_Decisoes_SimCore.md` | `SIM` | Living register. Its "SimCore" is a staging library that is not in this repository (`docs/TERMS.csv`, `SimCore`) |
| `Terra_Paralela_Registro_Decisoes_Arbitragem.md` | `REF` | Living register. Design only, behind `CORE-D07` |
| `Terra_Paralela_Fisica_Ambiental_Etapa2.md` | `PHYS` | Living register. Design only, behind `CORE-D07` |
| `Terra_Paralela_Registro_Decisoes_PlotEditor.md` | `PLOT` | Living register. `PLOT-D36` proposes a supersession of `WORLD-D17` that was never ratified |
| `Terra_Paralela_Registro_Decisoes_Pivo2D_Personagem.md` | `APPR` | Living register |
| `Terra_Paralela_Registro_Decisoes_Tier_1_5.md` | `SMR` | Living register. Waits for the situation-taxonomy research |
| `Terra_Paralela_Registro_Decisoes_Arquetipos_Coesao_Vida.md` | `ARCH` | Living register |
| `Terra_Paralela_Registro_Decisoes_Calibracao_Autoria.md` | `CAL` | Living register. `CAL-D38` is amended by ADR-0015 §4 (band → reputation); `CAL-D39`'s reference is amended by ADR-0018 |
| `Terra_Paralela_Registro_Decisoes_Geografia_Competicoes.md` | `GEO` | Living register |
| `Terra_Paralela_Registro_Decisoes_Regras_e_Competicoes.md` | `RC` | Living register. `RC-03` (competition reputation) is deferred by ADR-0015 §4 |
| `Terra_Paralela_Criacao_Personagem_e_Ciclo_de_Vida.md` | `T` | Living register and design document |

## Design documents

| File | Status |
| --- | --- |
| `Game_Design_Document_2D_Soccer_Life_Simulator.md` † | Founding GDD. §5 (static LOD tiers) is amended by `GEO-D32`/`GEO-D33` |
| `Terra_Paralela_Geracao_Identidade_Clube_v2.md` | Current club-identity design. Its §9.1 (`displayCode` unique worldwide) won over `WORLD-D20` (ADR-0017) |
| `Sistema_de_Prestigio_de_Competicoes.md` † | Research report. Its league classes B1–B6 are reference sets for calibration, not a field (ADR-0014, ADR-0015) |
| `Terra_Paralela_Tactics_to_Behavior.md` | Design for Tier 1 player decisions (steering + utility AI). The first code is in `src/SoccerSim.Core/Ai/` and `src/SoccerSim.Core/Pitch/`. Its header still reads "D1 = Opção B"; the 2D track applies (`CORE-D01`, refined by `RND-01`–`06`) |
| `Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md` | Research prompt for Tier 1.5. Amended by `SMR` §7 |
| `Research_Prompt_Pipeline_Personagem_Modular.md` | Research prompt. Blocks A–E archived, F–H active (`APPR-D39`) |
| `Impl_Prompt_Restart_State_Machine.md` | Implementation prompt (match restarts, stage 1). No restart state machine exists in the code at `7ec9d3e` |
| `Terra_Paralela_Lote_Autoria_Liga_Brasileira.md` | Production log of the pilot league (20 clubs, 688 players) |

## Archived (`archived/`, `versao-b/`)

| File | Why archived |
| --- | --- |
| `archived/Terra_Paralela_Geracao_Identidade_Clube.md` | Club identity v1, superseded by v2 |
| `archived/Terra_Paralela_Criacao_Personagem_Modular.md` | 3D appearance design, archived by `APPR-D30` |
| `archived/Terra_Paralela_Plano_Migracao_Repo_3D.md` | 3D repository migration plan. Never carried out |
| `archived/Decisoes_Travadas_Migracao_2D_para_3D.md` † | Locked decisions of the 3D migration plan |
| `versao-b/VersaoB_Abordagem_3D_PZ_Design_Doc.md` | 3D-hybrid variant in the style of Project Zomboid. Its banner says archived (`T-01`); a later art-direction decision (`RND-06`, not in the repository) rescoped it as a reference for world and asset authoring. It waits here for a repository of its own |

## Still outside the repository

- **`design_handoff_ferramenta_de_mundo/`** (`WBH`): the World Builder handoff (`ROADMAP.md`,
  `DATA_CONTRACT.md`, `ALGORITHMS.md`).
- **Patch F0-A** (`CORE`, `RND`, `LED`): the reconstructed ledger.
- **Staging code and authoring pipeline**, in the owner's Claude project:
  - `SimCore*.cs` and `CalibrationHarness.cs`;
  - `tp_*.py`.

  They are not design documents, and whether they belong to this project is open
  (`docs/TERMS.csv`, `SimCore`).
- **Compilation of everything above:** *Terra_Paralela_Documento_Consolidado*. It is deliberately
  not copied, because it would be a second copy of each document.
- **Duplicate uploads:** the Claude project holds two uploads named
  `Terra_Paralela_Registro_Decisoes_Camada_Autoria_Mundo.md` and two named
  `Terra_Paralela_Geracao_Identidade_Clube_v2.md`. The copies here are the versions the project
  returns for those names.
