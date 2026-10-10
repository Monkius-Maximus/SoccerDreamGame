# Legacy decision identifiers

Before ADR-0016, decisions were recorded in pt-BR registers that each numbered their own
decisions, so `D-30` alone is ambiguous. Cite a legacy decision as `<PREFIX>-D<NN>` (for example
`GEO-D53`). Never renumber a legacy decision, and never edit a legacy register. A change to one
of its decisions is a new ADR that names the legacy id it supersedes.

| Prefix | Register (pt-BR title) | Range | File |
| --- | --- | --- | --- |
| `CORE` | Ledger canônico (reconstruído) | D-01–D-09 | `docs/decisions/` (patch F0-A) |
| `WBH` | Handoff da Ferramenta de Mundo — `ROADMAP.md` | D-01–D-04 | `docs/legacy/design_handoff_ferramenta_de_mundo/` |
| `WORLD` | Registro de Decisões — Camada de Autoria de Mundo | D-10–D-29 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Camada_Autoria_Mundo.md` |
| `SIM` | Registro de Decisões — SimCore | D-30–D-39 | `docs/legacy/Terra_Paralela_Registro_Decisoes_SimCore.md` |
| `REF` | Registro de Decisões — Arbitragem | D-30–D-38 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Arbitragem.md` |
| `PHYS` | Física Ambiental — Etapa 2 | D-30–D-45 | `docs/legacy/Terra_Paralela_Fisica_Ambiental_Etapa2.md` |
| `PLOT` | Registro de Decisões — PlotEditor | D-30–D-49 | `docs/legacy/Terra_Paralela_Registro_Decisoes_PlotEditor.md` |
| `APPR` | Registro de Decisões — Pivô 2D do Criador de Personagem | D-30–D-39 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Pivo2D_Personagem.md` |
| `SMR` | Registro de Decisões — Structured Match Resolver (Tier 1.5) | D-30–D-45 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Tier_1_5.md` |
| `ARCH` | Registro de Decisões — Arquétipos, Coesão e Vida de Carreira | D-30–D-62 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Arquetipos_Coesao_Vida.md` |
| `CAL` | Registro de Decisões — Calibração e Autoria de Mundo | D-38–D-52 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Calibracao_Autoria.md` |
| `GEO` | Registro de Decisões — Geografia e Escopo de Competições | D-30–D-37, D-53–D-57 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Geografia_Competicoes.md` |
| `RC` | Registro de Decisões — Regras e Competições | RC-01–RC-07 | `docs/legacy/Terra_Paralela_Registro_Decisoes_Regras_e_Competicoes.md` |
| `T` | Criação de Personagem e Ciclo de Vida | T-01–T-14 | `docs/legacy/Terra_Paralela_Criacao_Personagem_e_Ciclo_de_Vida.md` |
| `RND`, `LED` | Direção de arte / gramática de maturidade | RND-01–06, LED-01–05 | `docs/decisions/` (patch F0-A) |

`RC`, `T`, `RND` and `LED` were already unique and are cited without a further prefix.

Archived material:
- `docs/legacy/versao-b/` holds the VersaoB 3D-hybrid variant, until it gets a repository of its
  own.
- `docs/legacy/archived/` holds the 3D migration plan and its locked decisions.
