# Terra Paralela — Registro de Decisões: Módulo SimCore (Geração de Jogadores)

> **Natureza deste documento.** Registro de decisões que fecha pendências do módulo `SimCore` (geração procedural de jogadores) e prepara sua reconciliação com o repositório canônico. Segue o formato do *Registro de Decisões: Camada de Autoria de Mundo*.
> **Cross-ref:** todos os arquivos `SimCore.*.cs`, `CalibrationHarness.cs`, `Prompt_Implementacao_Workbench_Fatia1.md`, `Research_Prompt_Arquetipos_Ancoras_Jogadores_v2.md`, relatório *Sistema de Prestígio de Competições*.
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo coberto:** domínio de atributos, geração procedural, identidade (traits/familiaridade), calibração numérica, PRNG. **Fora:** match engine, Tier 1.5, life-sim, arbitragem, Workbench de autoria (coberto no prompt de implementação já emitido).

---

## 1. Reconciliação de nomenclatura — `SimCore` vs. repositório canônico

**Situação.** O módulo `SimCore` (domínio de 12 atributos, `PlayerGenerator`, identidade, harness) foi desenvolvido em sessões de chat, sem visibilidade do estado atual do repositório canônico (`github.com/Monkius-Maximus/SoccerDreamGame`, `src/SoccerSim.Core/`). Não há como, a partir destas conversas, confirmar se `SoccerSim.Core` já possui um modelo de atributos/jogador concorrente.

**Resolução — tratamento como módulo de staging, não como fonte de verdade definitiva.** Aplicando o mesmo padrão já usado para o pacote P0–P8 (D-12/D-13 do registro da camada de autoria de mundo: nomenclatura greenfield reancorada ao repositório real, sem duplicar trabalho):

**D-30 — `SimCore` é staging até reconciliação explícita.** Antes de qualquer handoff para Claude Code atuar diretamente no repositório, é obrigatório auditar `src/SoccerSim.Core/` e responder: (a) já existe enum de atributos/posições? Se sim, qual cardinalidade e nomes? (b) já existe `PlayerGenerator` ou geração procedural? (c) já existe schema de trait/perk? A ausência ou presença determina se este módulo vira *criação* ou *extensão/delta* — mesma distinção que D-13 já aplicou ao P0–P2.

**D-31 — Mapa de reancoragem provisório** (aplicar a mesma disciplina da D-12), a confirmar/corrigir na auditoria:

| Neste módulo | Repositório canônico (hipótese) |
| :--- | :--- |
| `SimCore.cs`, `SimCore.Identity.cs`, `SimCore.Presentation.cs`, `SimCore.Archetypes.cs`, `SimCore.Random.cs` | `src/SoccerSim.Core/Generation/` (ou pasta equivalente a criar) |
| `namespace SimCore` | `namespace SoccerSim.Core.Generation` (ajustar ao padrão real do repo) |
| `CalibrationHarness.cs` | Novo projeto de console, irmão de `tests/SoccerSim.Core.Tests/` — não dentro dele (harness não é suíte de asserts, é instrumento de medição) |

**D-32 — Se `SoccerSim.Core` já tiver enum de atributos concorrente:** este módulo cede. A regra "uma fonte de verdade" (já travada para `AttributeId`, D-27 do registro de autoria de mundo) vale igual aqui — não haverá dois enums de atributo no projeto. Neste caso, o trabalho de calibração (§3 abaixo) permanece válido como *metodologia* (a régua do harness), mesmo que os nomes dos atributos mudem.

---

## 2. Migração de PRNG — `System.Random` → SplitMix64 (concluída neste módulo)

**Situação anterior.** `PlayerGenerator`, `RegionalNameSource`, `IdentityGeneration.RollTraits/RollFamiliarity` usavam `System.Random` com estado mutável compartilhado entre chamadas sequenciais, e o `Gaussian()` interno usava Box-Muller (`Math.Log`, `Math.Cos`).

**Por que isso violava princípio já travado do projeto.** A prioridade de implementação vigente (`Prompt_Implementacao_SplitMix64.md`) exige SplitMix64 com streams isolados por entidade e proíbe funções transcendentais no caminho de amostragem, justamente para garantir replay bit-a-bit entre plataformas. `System.Random` não é especificado bit-a-bit entre versões do .NET; `Math.Log`/`Math.Cos` têm precisão dependente de `libm`/plataforma.

**D-33 — SplitMix64 implementado e aplicado a todo o módulo.** `SimCore.Random.cs` (novo arquivo) contém:
- `SplitMix64`: gerador de Vigna, só bitshift/multiplicação/XOR.
- `SeedDerivation.Derive(masterSeed, stream, entityId)`: **FNV-1a sobre UTF-8** — não `object.GetHashCode()`/`string.GetHashCode()`, que são aleatorizados por processo desde o .NET Core e quebrariam replay entre execuções mesmo com a mesma seed.
- `Streams`: constantes de nome de stream (`player.attributes`, `player.name`, `player.traits`, `player.familiarity`).

**D-34 — Amostra Gaussiana trocada para aproximação de Irwin-Hall** (soma de 12 uniformes − 6 ≈ N(0,1)). Zero função transcendental no caminho de amostragem — só `NextDouble()` e soma.

**D-35 — `PlayerGenerator.Generate` agora exige `entityId`.** Cada chamada deriva seu próprio stream (`hash(masterSeed, "player.attributes", entityId)`), eliminando o estado mutável compartilhado entre jogadores gerados em sequência. Consequência prática: **qualquer jogador pode ser regenerado isoladamente** sem reproduzir o lote inteiro — pré-requisito para a Fase C do Workbench (revisão individual de âncora) e para regeneração seletiva no editor.

**Verificação empírica.** Duas execuções independentes do harness com a mesma seed produziram saída **byte-idêntica** (`diff` vazio). Determinismo confirmado, não apenas presumido.

---

## 3. Recalibração — teto de OVR e limiares de trait

**Diagnóstico do harness (execução anterior, pré-recalibração).** No tier 1.00 (elite): OVR médio 58–62, máximo 79 — sem craques de verdade numa liga que deveria ter atletas 85+. Limiares de trait (`≥78` para a maioria) operavam a mais de dois desvios-padrão de uma população centrada em ~60: `Maestro` 0,2%, `Sangue frio` 0,3% — raridade por acidente de calibração, não por design.

**D-36 — Decisão de sequenciamento: população primeiro, limiares depois.** Os limiares de trait são definidos relativos à população; recalibrar threshold sobre uma base que vai mudar de novo seria trabalho duplicado.

**D-37 — Médias dos 4 arquétipos elevadas uniformemente (+10 a +12 por atributo), covariância intocada.** Mudança cirúrgica: só o vetor `Means` de `PoacherST`, `BoxToBoxCM`, `StopperCB`, `ClassicGK` mudou; a matriz de covariância — a peça que produz os trade-offs (Strength × Pace antagônicos no zagueiro, etc.) — permanece exatamente como estava, porque nada no diagnóstico apontava problema de *forma* da distribuição, só de *centro*.

**D-38 — Limiares de trait recentrados contra a nova população**, mantendo a lógica de "gatilho ~0.3–0.75 desvio-padrão acima da média do atributo relevante":

| Trait | Limiar anterior | Limiar atual |
| :--- | :--- | :--- |
| Velocista | Pace ≥ 78 | Pace ≥ 78 (já estava correto) |
| Maestro | Vision ≥ 78 | Vision ≥ 76 |
| Trombador | Strength ≥ 78 | Strength ≥ 84 |
| Cão de caça | Tackling ≥ 74 & Stamina ≥ 65 | inalterado |
| Bombardeiro | Finishing ≥ 72 | Finishing ≥ 80 |
| Sangue frio | Composure ≥ 78 | Composure ≥ 78 (já estava correto) |
| Maratonista | Stamina ≥ 78 | Stamina ≥ 82 |
| Cobrador de elite | Composure ≥ 70 & Finishing ≥ 70 | Composure ≥ 76 & Finishing ≥ 76 |
| Muralha | Reflexes ≥ 78 | Reflexes ≥ 82 |

**Resultado medido (harness, pós-recalibração, tier 1.00, 2.500 jogadores/arquétipo):**

| Arquétipo | OVR médio | Mediana | p90 | Máximo |
| :--- | :--- | :--- | :--- | :--- |
| Atacante de área (ST) | 71,7 | 72 | 79 | 89 |
| Meia box-to-box (CM) | 69,5 | 70 | 76 | 86 |
| Zagueiro raiz (CB) | 70,3 | 70 | 77 | 87 |
| Goleiro clássico (GK) | 70,4 | 70 | 78 | 92 |

Traits, população total (elegíveis / possuem, tier 1.00): Velocista 8,4%/4,1% · Maestro 6,1%/3,0% · Trombador 4,9%/2,3% · Cão de caça 3,8%/1,8% · Bombardeiro 7,7%/3,8% · Sangue frio 8,0%/3,9% · Maratonista 4,3%/1,9% · Cobrador de elite 4,0%/2,0% · Muralha 3,9%/2,2%. Nenhum trait abaixo de 1,8% possuem; nenhum acima de 4,1% — faixa de "diferencial notável", nem universal nem invisível.

**Confirmação da propriedade "tier como raridade natural" (tier 0.85):** todos os traits caem para 0,0–0,5% de posse, sem nenhum código de raridade — a mesma covariância e os mesmos limiares, aplicados a uma população mais fraca, produzem escassez de talento diferencial automaticamente.

**D-39 — Estes números permanecem provisórios**, marcados explicitamente como tais. A fonte definitiva é o `Research_Prompt_Arquetipos_Ancoras_Jogadores_v2.md` (Eixos D/E, já emitido) e o relatório de *Sistema de Prestígio de Competições* (já retornado) — quando ambos alimentarem o design doc de calibração, médias/variâncias/limiares serão revisados contra dado real, não contra "parece razoável".

---

## 4. O que isto destrava

- O gerador produz uma pirâmide de OVR plausível para liga de elite (craques de 85+ presentes, sem deixar de ter distribuição normal de base) e traits que funcionam como diferencial real de jogador, não como decoração matematicamente inacessível.
- Determinismo verificado empiricamente, não apenas presumido — pré-requisito para qualquer save game e para a Fase C (testes xUnit de determinismo) do `Prompt_Implementacao_Workbench_Fatia1.md`.
- `entityId` obrigatório no gerador desbloqueia regeneração seletiva — necessário para a tela de revisão individual do Workbench.
- A pendência de nomenclatura (§1) está registrada como decisão explícita de tratamento, não como ambiguidade silenciosa — a auditoria de `src/SoccerSim.Core/` vira a próxima ação necessária antes de qualquer handoff de implementação real neste módulo.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | `SimCore` tratado como staging até auditoria de `src/SoccerSim.Core/` |
| D-31 | Mapa de reancoragem provisório (namespace, localização de arquivos) |
| D-32 | Enum de atributo concorrente no repo real vence; `SimCore` cede se houver conflito |
| D-33 | SplitMix64 + `SeedDerivation` (FNV-1a) implementados e aplicados a todo o módulo |
| D-34 | Amostra Gaussiana via Irwin-Hall — zero função transcendental |
| D-35 | `Generate()` exige `entityId`; stream isolado por entidade, sem estado mutável compartilhado |
| D-36 | Sequência de recalibração: população primeiro, limiares de trait depois |
| D-37 | Médias dos 4 arquétipos elevadas +10/+12 uniformemente; covariância intocada |
| D-38 | Limiares de trait recentrados contra a nova população (tabela completa acima) |
| D-39 | Números de calibração continuam provisórios até ancoragem em dado real (research v2 + prestígio) |
