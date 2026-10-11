# Terra Paralela — Criação de Personagem e Ciclo de Vida do Atleta

> **v1.0 — consolidado.** Substitui e encerra `Character_Creation_Design_Doc.md` e `Player_Lifecycle_Development_Design_Doc.md` (dois docs viram um; *one way*). `VersaoB_Abordagem_3D_PZ_Design_Doc.md` fica **arquivado** — ver §0.2.
>
> Companion doc do GDD. Cross-ref: GDD §3 (Eventos), §4 (Character State), §5 (LOD) · **Tactics → Behavior** (Personality Vector, atributos) · **Prestígio de Competição** (curva de fit, idade-pico por posição) · **Geração de Identidade de Clube** (padrão de determinismo) · **Match Engine**.
> Convenção: prosa PT-BR, **identificadores em inglês**. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo:** como um atleta é criado, o que ele carrega, e como ele muda dos 15 aos 46 anos. Camada de *personagem*, não de partida.

---

## 0. Estado desta rodada

### 0.1 Nota de sequenciamento (D-07 / D-08)

Este documento é **design**, não implementação. Nada aqui entra em código antes de a sequência obrigatória D-07 fechar (SplitMix64 → física headless → steering mínimo → primeiro sprite). Registrado explicitamente para o ledger: o consolidado **reduz** a contagem de docs (2 → 1) em vez de abrir escopo novo, mas se a moratória D-08 for lida de forma estrita, o veredito de promoção é seu.

### 0.2 Decisões tomadas nesta rodada

| # | Decisão | Base |
| :--- | :--- | :--- |
| **T-01** | **Trilha 2D confirmada. Versão B (3D híbrido PZ) ARQUIVADA.** | D-01 já trava a trilha canônica em 2D e arquiva a migração 3D. Manter a Versão B viva criaria um caminho paralelo — proibido (*no fallbacks*). |
| **T-02** | Câmera de partida = **side-on ortográfica** (GDD §1, intocado). A dúvida ISS Deluxe / FIFA-diagonal está **encerrada** por D-01. | GDD §1 + D-01 |
| **T-03** | Traços de criação vivem em namespace **`LifeTrait`**, separado dos `PlayerTrait` on-pitch já existentes no ledger. Resolve a colisão `Maestro`. | §2.3 |
| **T-04** | Atributos: o doc **não define lista própria**. Consome o **schema canônico de 12 atributos** do ledger. | §2.1 |
| **T-05** | `capProfile`, `weight` e OVR **saem do registro persistido** — são derivados, computados em leitura. | §2.2 |
| **T-06** | Desenvolvimento = **curva de fit**, não rampa por tier. Ganho satura em ~25 partidas/temporada. | Prestígio §C.5, B.1 |
| **T-07** | Idade-pico **modulada por posição** via `peakOffset` (ST/W −1, meio 0, def +1, GK +2). | Prestígio §C.3 |
| **T-08** | Fases de idade **15→46**, breakpoints 15/21/27/33/39. | Sessão anterior, mantido |
| **T-09** | Evolução de `LifeTrait` habilitada, governada por **LOD × fase**. Tier 1.5 = mesmo regime do Tier 1. | §10 |
| **T-10** | Declínio técnico/mental leve a partir de **44+** (fecha a questão em aberto §11.4 do doc antigo). | §9.4 |
| **T-11** | Snapshots automáticos e transitórios nas idades fixas; **commit permanente é escolha do jogador** na aposentadoria. Aposentadoria antecipada só libera fases efetivamente vividas. | §12 |
| **T-12** | **Mentoria entre atletas da Pool: fora da v1.** | *Don't overengineer* |
| **T-13** | `BodyCondition` renderiza **apenas no retrato 64×64**. Sprites de partida e de vida leem só `buildType`. | §8.4 |
| **T-14** | Streams SplitMix64 próprios para criação, desenvolvimento, evolução de traço e lesão. | §13 |

---

## 1. Fronteira do sistema e princípios

| | |
| :--- | :--- |
| **Entrada** | Escolhas do jogador no wizard (§4) **ou** parâmetros de geração (regen/NPC). |
| **Saída** | `CharacterRecord` — objeto consumido por Match Engine, sistema de eventos (GDD §3), economia e calendário. |
| **LOD** | O atleta criado pelo jogador é sempre **Tier 1/1.5**. A *evolução* obedece ao tier da liga onde ele joga (§10). |

**Princípios aplicados:**
- **Estado derivado nunca é verdade** — OVR, atributos efetivos, `capProfile`, `weight`: computados na leitura, nunca persistidos.
- **One way** — um único pipeline serve criação do jogador, edição de atleta existente e geração de regen. Sem caminho especial para NPC.
- **Fail-fast** — toda violação de precondição lança (§14). Nada de auto-correção silenciosa.
- **Research before thresholds** — todo número marcado **(tunável)** é ponto de partida sem ancoragem empírica; os ancorados citam a fonte.

---

## 2. Reconciliação com o ledger (obrigatório ler antes de implementar)

Três pontos onde a versão anterior deste módulo divergia da base canônica. Resolvidos aqui.

### 2.1 Schema de atributos — **consome, não define** (T-04)

A versão anterior inventava ~26 atributos em 4 grupos. **Descartado.** O ledger tem um **schema canônico de 12 atributos**; este doc opera sobre ele, seja ele qual for. Todas as mecânicas abaixo são agnósticas à contagem:

- `AP` distribui sobre os 12.
- `CapProfile` é uma função `(position, height, buildType) → teto por atributo`, aplicada aos 12.
- `PhysDecay` incide sobre o **subconjunto físico** dos 12; `TechDecay` (§9.4) sobre o resto.

**RESOLVIDO.** A partição dos 12 é `Technical` = {Finishing, Passing, Dribbling, Tackling} · `Physical` = {Pace, Strength, Stamina} · `Mental` = {Positioning, Vision, Composure} · `Goalkeeping` = {Reflexes, Handling} — travada no Registro de Decisões de Calibração e Autoria, D-48 (reconhecimento de que já era estrutural) e D-52 (partição final + curva de declínio do grupo `Goalkeeping`, ver §9.4 abaixo). O tratamento de goleiro dentro dos 12 (vetor de OVR) foi fechado em D-49.

### 2.2 Estado derivado (T-05)

`EffectiveAttributes.Read()` é o **ponto de acesso único**. Fora dele, ninguém compõe atributo com forma/fadiga.

```
effective = EffectiveAttributes.Read(player, context)
          // base (currentAttributes) + formMood + fatigue + contexto
```

Removidos do registro persistido: `capProfile` (função pura de posição+físico), `weight` (função de altura+build), OVR e valor de mercado (já eram derivados por regra do ledger).

### 2.3 Colisão `Maestro` — dois namespaces (T-03)

O ledger já tem traços on-pitch gerados por limiar (`Maestro`, `Bombardeiro`) e arquétipos (`MaestroCM`). A versão anterior criou um traço `MAESTRO` próprio — duplicação de fonte de verdade.

| Namespace | O que é | Origem | Afeta |
| :--- | :--- | :--- | :--- |
| **`PlayerTrait`** (existente, canônico) | Traço futebolístico on-pitch | **Gerado** por limiar sobre atributos | Utility AI / execução na partida |
| **`LifeTrait`** (este doc) | Caráter, corpo, vida, carreira | **Escolhido** na criação (CPP) ou adquirido por evento | Personality Vector, eventos, desenvolvimento, lesão |

**Regra:** um `LifeTrait` **nunca** modifica execução on-pitch diretamente — ele age via Personality Vector ou via desenvolvimento. Traços da versão anterior que violavam isso (`MAESTRO`, `CLINICAL`, `ENGINE`, `TECH_PRODIGY`) foram **removidos** do catálogo; o efeito que eles buscavam pertence ao `PlayerTrait` do ledger.

**Compra cruzada permitida:** o CPP pode comprar **um** `PlayerTrait` inicial do catálogo canônico como "talento de partida", em vez de um `LifeTrait`. Custo sugerido: **4 CPP** **(tunável)**. Isso preserva a fantasia de "nasci com um dom" sem duplicar taxonomia.

🟡 **Pendência de ledger:** os limiares de `PlayerTrait` estão com débito de calibração conhecido (`Maestro` ~0,2%, `Bombardeiro` ~12,8% dos ST). A compra cruzada **contorna** o limiar (concede o traço diretamente), então não depende dessa calibração — mas a incidência na população gerada depende.

### 2.4 Banda Living Legend (D-05)

Faixa de trabalho **1–99**; a banda **100–120** é reservada e **bloqueada** para criação e desenvolvimento normais. D-05 já a limita a 10,1 m/s no lado da física. Este doc não abre a banda; apenas garante que o campo comporta o valor.

---

## 3. Contrato de dados (`CharacterRecord`)

Partição `static` / `dynamic` conforme GDD §4.

```json
{
  "id": "uuid",
  "arcadeMode": false,

  "static": {
    "identity": {
      "firstName": "string", "lastName": "string", "shirtName": "string",
      "nationality": "string", "secondNationality": null,
      "dateOfBirth": "2011-04-12"
    },
    "physical": { "height": 183, "buildType": "Athletic" },
    "footballing": {
      "primaryPosition": "ST",
      "secondaryPositions": ["AM"],
      "preferredFoot": "Right",
      "weakFootRating": 3,
      "skillMovesRating": 4
    },
    "baseAttributes":    { "<12 chaves do schema canônico>": 0 },
    "potentialCeiling":  { "<12 chaves do schema canônico>": 0 },
    "personality": {
      "lifeTraits": ["WONDERKID", "AMBITIOUS", "GLASS_ANKLES"],
      "grantedPlayerTraitId": null,
      "personalityVector": {
        "AGG": 50, "PRO": 50, "AMB": 80, "LOY": 50,
        "TMP": 50, "SOC": 50, "RSK": 50, "TEAM": 50
      }
    },
    "appearance": {
      "skinTone": 4, "facePreset": 7, "eyeColor": 2,
      "hairStyle": 11, "hairColor": 1,
      "facialHair": 0, "facialHairColor": 1,
      "bootColor": 9, "accessories": ["headband"], "shirtNumber": 9
    },
    "creationPointsSpent": 12
  },

  "dynamic": {
    "currentAttributes": { "<12 chaves do schema canônico>": 0 },
    "formMood": 0,
    "morale": 60,
    "fitness": 100,
    "conditioningLoad": 0,
    "injuries": [],
    "seasonMinutes": 0
  }
}
```

**Não persistidos (derivados em leitura):** `capProfile`, `weight`, `age`, OVR, valor de mercado, salário, atributos efetivos.

**Regras:** `static` é gravado no Confirmar e só muda em **marco entre-temporadas** (§10.4). Escrita em `static` durante temporada ativa **lança**. `dynamic.currentAttributes` inicia = `baseAttributes` e nunca ultrapassa `potentialCeiling`.

---

## 4. Fluxo de criação

Sete passos lineares, validação por passo e gate global no Confirmar (§14).

| # | Passo | Define |
| :--- | :--- | :--- |
| 1 | **Origin & Identity** | Nome, nacionalidades, nascimento (idade 15–46) |
| 2 | **Physical Profile** | `height` (160–205), `buildType` (Lean/Balanced/Athletic/Stocky) |
| 3 | **Position & Footedness** | Posição primária, até 3 secundárias, pé, weak foot, skill moves |
| 4 | **Attribute Allocation** | Distribui `AP` sobre os 12 atributos, dentro do `CapProfile` |
| 5 | **Traits** | Compra/devolve `LifeTrait` dentro do CPP; opcionalmente 1 `PlayerTrait` |
| 6 | **Appearance** | Sprites + paletas (§8) |
| 7 | **Summary & Confirm** | Grava o `CharacterRecord` |

**Quick-Create** randomiza 1–6 passando pelo **mesmo** `validate()`. Não é caminho paralelo.

**Reuso (*one way*):** o mesmo wizard edita atleta existente do banco e gera regen. Regens rolam traços e atributos pelas mesmas regras — NPC e jogador-criado obedecem à mesma física. Resgata a liberdade de edição que o eFootball perdeu.

---

## 5. Atributos

### 5.1 Escala
Faixa de trabalho **1–99**; campo com teto de engine **120**; banda 100–120 reservada e bloqueada (§2.4). O `formMood` ±5 do GDD §4 calibra contra 1–99.

### 5.2 Três quantidades independentes
| Quantidade | O que é | Fonte | Muda? |
| :--- | :--- | :--- | :--- |
| **`currentAttributes`** | Habilidade agora | Alocação de `AP` → treino | Sim |
| **`potentialCeiling`** | Teto de carreira | `LifeTrait` de talento + origem | Não |
| **`CapProfile`** | Teto de *partida* por arquétipo físico | Função `(position, height, buildType)` | Não (derivado) |

### 5.3 Alocação
`AP = 1500` **(tunável)**, 100% alocado para confirmar, nenhum atributo acima do seu cap.

### 5.4 `CapProfile` — fórmula-base + override (T-04 da rodada anterior, mantido)
Função pura. Estrutura: `cap = base(position, attribute) × modifier(height, buildType)`, com tabela de override manual por arquétipo quando a fórmula produzir resultado indesejado. Cria a tensão de arquétipo (alto-e-forte = pivô de área; baixo-e-rápido = driblador) sem lógica espalhada.

**RESOLVIDO (D-50).** `base(position, attribute)` usa três degraus ancorados no mesmo agrupamento canônico do lift de OVR (`TOP4_ATTRS`/`POSITION_WEIGHTS`, D-46/D-49): 99 nos 4 atributos-assinatura da posição, 85 no resto do arquétipo, 60 fora dele. `modifier(height, buildType)` só toca `Pace`/`Strength`/`Stamina`, com fatores por `buildType` (nenhum build dominante nos três eixos) e um termo linear de altura sobre `Pace`/`Strength`. Fórmula completa no Registro de Decisões de Calibração e Autoria, §14.1. Ainda não há código — esta camada continua atrás de D-07 — e aterrissar os tetos no lote já gerado exige o refit de `valuePivot` declarado em D-43 §6.2/D-46/D-49.

---

## 6. `LifeTrait` — economia e catálogo

### 6.1 Economia
- **CPP base 12**, com **±3 pela dificuldade**.
- Positivos custam, negativos devolvem. Saldo final **≥ 0**.
- **Máx. 4 traços negativos** (teto anti-exploit; expansível pela loja in-game futura).
- Opcionalmente, 4 CPP compram **1 `PlayerTrait`** canônico (§2.3).

**Ladder de dificuldade** — nomenclatura Amador→Lenda. Dificuldade baixa = mais CPP **e** IA mais fraca; alta = CPP enxuto **e** IA mais competente (desafio duplo no topo).

| Tier | CPP | Competência da IA |
| :--- | :--- | :--- |
| Amador | 15 | Mínima |
| Semiprofissional | 14 | Muito baixa |
| Profissional | 13 | Baixa |
| **Craque** *(base)* | **12** | Média |
| Estrela | 11 | Alta |
| Classe Mundial | 10 | Muito alta |
| Lenda | 9 | Máxima |

Tags: **[H]** High Stakes · **[M]** Medium · **[L]** Low (GDD §3) · **[F]** Form/Mood · **[D]** Desenvolvimento.

### 6.2 Talento e longevidade
| Id | Rótulo | CPP | Efeito | Tags |
| :--- | :--- | :--- | :--- | :--- |
| `WONDERKID` | Joia Rara | 5 | `potentialCeiling` +25% | [D] |
| `NAT_ATHLETE` | Atleta Nato | 3 | Caps físicos +10 | [D] |
| `GAME_BRAIN` | Mente de Jogo | 3 | Caps mentais +10 | [D] |
| `EVERGREEN` | Longevidade | 4 | Declínio do Twilight ×0,4; `TechDecay` ×0,5 | [D][F] |
| `AMBIDEXTROUS` | Ambidestro | 2 | `weakFootRating = 5` | — |
| `ICE_VEINS` | Cabeça Fria | 3 | +20% em mini-games High Stakes; `TMP+25` | [H][F] |

### 6.3 Passivos físicos
| Id | Rótulo | CPP | Efeito | Tags |
| :--- | :--- | :--- | :--- | :--- |
| `GLASS_ANKLES` | Tornozelos de Vidro | −4 | `InjuryMult` ×2,0 | [L] |
| `INJURY_RECUR` | Lesão Crônica | −3 | Região recorrente sob fadiga alta | [L] |
| `WEAK_FOOTED` | Perna Fraca | −2 | `weakFootRating = 1` | — |
| `SLOW_STARTER` | Cai de Produção | −2 | 6 primeiras rodadas: viés de Form −2 | [F] |
| `BIG_GAME_BOTTLER` | Apaga em Jogo Grande | −4 | Jogo High Stakes: Form −3 | [H][F] |
| `PENALTY_NERVES` | Cabeça Quente no Pênalti | −2 | −20% no mini-game de pênalti | [H] |

### 6.4 Caráter
| Id | Rótulo | CPP | Efeito | Tags |
| :--- | :--- | :--- | :--- | :--- |
| `PRO_EXEMPLARY` | Profissional Exemplar | 4 | `PRO+35`; recuperação +30%, ganho de treino +20% | [D][L][F] |
| `BORN_LEADER` | Líder Nato | 4 | Elegível a capitão; moral de vestiário; diálogos de liderança (idade ≥ 21) | [M] |
| `CHARISMATIC` | Carismático | 3 | +15% em diálogos Medium Stakes | [M] |
| `MEDIA_DARLING` | Querido da Mídia | 3 | `SOC+20`; patrocínio facilitado | [M][L] |
| `LOYAL` | Leal | 2 | `LOY+30`; recusa assédio de transferência | [M] |
| `AMBITIOUS` | Ambicioso | 1 | `AMB+30`; exige títulos | [M][H] |
| `MERCENARY` | Mercenário | −2 | `LOY−30, AMB+20`; persegue salário | [H][M] |
| `HOTHEAD_PRESS` | Pavio Curto | −3 | `TMP−30`; força opção ruim em coletiva | [M][F] |
| `PARTY_ANIMAL` | Animal de Festa | −3 | `SOC+25, PRO−20`; volatilidade de Form +50% | [L][M][F] |
| `INTROVERT` | Tímido | −2 | `SOC−25`; bloqueia diálogos sociais | [M] |
| `MAVERICK` | Rebelde | −2 | Atrita com técnicos rígidos; diálogos desafiadores | [M] |

### 6.5 Origem
| Id | Rótulo | CPP | Efeito | Tags |
| :--- | :--- | :--- | :--- | :--- |
| `ACADEMY_GRAD` | Cria da Base | 2 | Vínculo com clube; `LOY+15` | [M] |
| `STREET_BALLER` | Jogador de Rua | 2 | `skillMovesRating = 5`; −8 em caps mentais | [D] |
| `WORKING_CLASS` | Origem Humilde | 2 | `PRO+10`; resiliência em adversidade | [F][M] |
| `LATE_BLOOMER` | Talento Tardio | −2 | `TrainGain` reduzido até 24; `potentialCeiling` +15% | [D][F] |
| `RICH_KID` | Filho de Família Rica | −2 | `AMB−15`; eventos financeiros distintos | [M] |
| `FOREIGN_PROSPECT` | Estrangeiro | −2 | Eventos de adaptação no 1º ano fora | [L][M] |
| `LEGACY` | Filho de Ídolo | 0 | Pressão de mídia; eventos de expectativa | [M][F] |

### 6.6 Estilo de vida
| Id | Rótulo | CPP | Efeito | Tags |
| :--- | :--- | :--- | :--- | :--- |
| `GYM_RAT` | Rato de Academia | 2 | Bônus de treino físico; risco de overtraining | [D][L] |
| `HOMEBODY` | Caseiro | 2 | `SOC−10`; volatilidade de Form −30% | [F][M] |
| `FAMILY_FOCUSED` | Homem de Família | 2 | Form estável; relocação mais custosa | [L][M][F] |
| `WORKAHOLIC` | Workaholic | 1 | `PRO+15`; bônus de treino, risco de burnout | [D][F] |
| `NIGHT_OWL` | Coruja | −1 | Treino matinal −10% | [L] |
| `GAMBLER` | Apostador | −2 | `RSK+30`; eventos de aposta e dívida | [H][M] |
| `INFLUENCER` | Influencer | −2 | `SOC+15`; renda de patrocínio vs. distração | [M][L] |

### 6.7 Exclusões e pré-requisitos
Mutuamente exclusivos: `LOYAL`⊗`MERCENARY` · `AMBIDEXTROUS`⊗`WEAK_FOOTED` · `PRO_EXEMPLARY`⊗{`PARTY_ANIMAL`,`HOTHEAD_PRESS`} · `WONDERKID`⊗`LATE_BLOOMER` · `HOMEBODY`⊗{`NIGHT_OWL`,`INFLUENCER`} · `ICE_VEINS`⊗{`BIG_GAME_BOTTLER`,`PENALTY_NERVES`,`HOTHEAD_PRESS`}.
Pré-requisitos: `BORN_LEADER` idade ≥ 21 · `ACADEMY_GRAD` exige clube inicial · `LEGACY` exige pai-jogador no banco.

---

## 7. Personality Vector

Oito dimensões, 0–100, default 50. **Única ponte** entre `LifeTrait` e sistemas: `lifeTraits → personalityVector → consumidores`. Direção única.

| Dim | Nome | Lido por |
| :--- | :--- | :--- |
| `AGG` | Aggression | Utility AI (desarme, cartão); eventos de confronto |
| `PRO` | Professionalism | Ganho de treino, recuperação, resistência a vício |
| `AMB` | Ambition | Contrato, transferência, expectativa |
| `LOY` | Loyalty | Ficar-vs-sair, relação com torcida |
| `TMP` | Temperament | Volatilidade de Form/Mood; imprensa |
| `SOC` | Sociability | Gate de diálogo social, patrocínio |
| `RSK` | RiskAppetite | Utility AI (chute/drible de risco); risco financeiro |
| `TEAM` | TeamPlay | Utility AI (passar vs. chutar); vestiário |

O Match Engine **nunca** lê `LifeTrait`; lê o vetor + atributos. Coerente com *Tactics → Behavior*: personalidade é **modulação de peso**, não lógica separada.

**Exibição:** implícita por padrão (perfil qualitativo no resumo); **toggle** em opções revela tooltip numérico no hover.

---

## 8. Aparência — sprites 2D em camadas

Trilha 2D (T-01). Sem escultura de geometria: **troca de sprite + recolor de paleta indexada**, técnica autêntica da era.

### 8.1 Camadas (z-order)
`BodyBase` (por `buildType`) → `Head` (`facePreset`) → `Eyes` → `Brows` → `FacialHair` → `Hair` → equipamento (`bootColor`, `gkGloves` auto, `headband`, `captainArmband` auto).

### 8.2 Paletas
Rampas indexadas de 4 índices (sombra/base/luz/contorno). Um sprite serve N cores: `skinTone` 0–11, `hairColor` 0–15, `bootColor` 0–15 são troca de rampa.

### 8.3 Contrato de sprite por cena (fecha a lacuna do GDD §1)

| Contexto | Projeção | Resolução | Direções autoradas |
| :--- | :--- | :--- | :--- |
| Retrato UI / menus | — | 64×64 | 1 |
| Partida (campo) | Side-on | **32×32** **(tunável)** | **1** (espelhada → 2) |
| Vida-sim | Isométrica | **32×48** **(tunável)** | **2** (espelhadas → 4) |
| HUD | — | 16×16 / 32×32 | — |

Total: **4 conjuntos autorados** por elemento de corpo (1 retrato + 1 side-on + 2 iso). O espelhamento horizontal corta a combinatória pela metade — é a alavanca que torna o pipeline 2D duplo viável.

### 8.4 `BodyCondition` — corpo dinâmico (T-13)
Enum derivado (nunca persistido) de `(agregado de atributos físicos, fitness, fase de idade)`:

`Soft` · `Athletic` · `Peak`

**Renderiza apenas no retrato 64×64.** Sprites de partida e de vida leem só `buildType`. Motivo: a 32px a diferença é invisível, e renderizar condição nas três vistas multiplicaria a combinatória por 3 sem payoff. O jogador vê o corpo mudar onde o corpo é visível.

É **readout**, não modificador — reflete os atributos físicos, não os altera (nada de dupla contagem). Cobre a intenção do GTA San Andreas (corpo seguindo hábito) ao custo de 3 sprites de retrato por build.

---

## 9. Ciclo de vida — desenvolvimento e declínio

### 9.1 Fases (T-08)
Breakpoints **15 / 21 / 27 / 33 / 39**, teto **46**. Todos os multiplicadores **(tunável)**.

| Fase | Idade | `TrainGain` | `PhysDecay`/temp. | Volatilidade de trait | `InjuryMult` |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Prospect** | 15–20 | ×1,5 | 0 | 0,20 (alta) | ×1,0 |
| **Breakthrough** | 21–26 | ×1,0 | 0 | 0,10 | ×1,0 |
| **Prime** | 27–32 | ×0,5 | −1 | 0,05 | ×1,2 |
| **Veteran** | 33–38 | ×0,2 | −3 | 0,02 | ×1,8 |
| **Twilight** | 39–46 | ×0,0 | −6 | 0 | ×2,5 |

**Prospect** — variância alta nos dois sentidos: boa gestão gera traço positivo e crescimento rápido; negligência gera traço negativo. Viés da fase para traços rebeldes (`MAVERICK`, `HOTHEAD_PRESS`, `PARTY_ANIMAL`). **Breakthrough** — refino de especialidade. **Prime** — platô. **Veteran** — condicionamento obrigatório (§11). **Twilight** — sem crescimento; `EVERGREEN` decide se a carreira continua.

### 9.2 `peakOffset` por posição (T-07)
As fronteiras Prime/Veteran/Twilight deslocam por grupo posicional, ancorado na idade-pico documentada (Prestígio §C.3: atacantes ~25, meias 25–27, zagueiros ~27, GK 27+):

| Grupo | `peakOffset` |
| :--- | :--- |
| ST, W | **−1** |
| AM, CM, DM | **0** |
| CB, FB | **+1** |
| GK | **+2** |

Teto de 46 é **rígido** — o offset move fronteiras internas, não o fim da carreira. Os **snapshots permanecem nas idades fixas** 15/21/27/33/39 (§12), desacoplados do offset, por previsibilidade de UX.

### 9.3 Curva de fit — o modelo de ganho (T-06)

**Não** é `ganho = f(tier)` crescente. Ancorado em Prestígio §C.5 e na engenharia reversa FM (§B.1: reputação de liga isoladamente **não** afeta crescimento):

```
gain = TrainGain(phase, position)
     × fit(currentAbility, leagueTier)
     × minutesFactor(seasonMinutes)
     × performanceFactor(matchRating)
     × personalityFactor(PRO, AMB)
```

- **`fit`** — máximo quando o atleta joga em tier **igual ou ligeiramente acima** do seu nível. Cai se fácil demais (aprende pouco) e se difícil demais (atuações ruins travam a curva). É isto que faz empréstimo para baixo **acelerar** um jovem preso em elenco de elite.
- **`minutesFactor`** — satura em ~**25 partidas/temporada** (~95% do ganho possível; FM-Arena thread 13975). Minutos são o driver primário.
- **`personalityFactor`** — `PRO` alto quase dobra o ganho; `AMB` modula em janela estreita.
- **Papel do `leagueTier`:** define o **alvo de dificuldade** contra o qual o fit é medido — nunca um multiplicador direto de XP.

### 9.4 Declínio (T-10)
- `PhysDecay` incide no subconjunto `Physical` dos 12 atributos (`Pace`, `Strength`, `Stamina`), da fase Prime em diante.
- **`TechDecay`:** `Technical`, `Mental` **e `Goalkeeping`** (`Reflexes`, `Handling` — D-52) resistem até **44**; de 44 a 46, **−1/temporada** **(tunável)**. Fecha a questão em aberto: garante que a aposentadoria chega por atrito natural, sem tornar o veterano inútil de repente. O goleiro entra na curva lenta porque leitura e posicionamento sustentam carreira além da idade em que o físico de linha já caiu — D-52 detalha a justificativa.
- **`EVERGREEN`** multiplica `PhysDecay` do Twilight por **0,4** e `TechDecay` por **0,5**.
- Nada disso toca `formMood`, que continua aplicado em leitura por `EffectiveAttributes.Read()` — o veterano bem condicionado e com moral alta ainda rende acima do base decaído.

### 9.5 Arco de exemplo (ST, com `EVERGREEN`)
| Idade | Fase | Físico | Técnico | Mental | Leitura |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 15 | Prospect | 70 | 62 | 55 | Cru, cresce rápido |
| 21 | Breakthrough | 88 | 80 | 72 | Especializando |
| 27 | Prime | 90 | 90 | 84 | Auge (offset −1: entra e sai antes) |
| 33 | Veteran | 81 | 90 | 88 | Pernas caem, leitura no topo |
| 39 | Twilight | 68 | 88 | 90 | Vira finalizador de área |
| 46 | Twilight | 55 | 85 | 88 | `EVERGREEN` segurou a queda |

---

## 10. Evolução de `LifeTrait`

### 10.1 Governança LOD × fase (T-09)
| LOD | Evolução de atributo | Evolução de `LifeTrait` |
| :--- | :--- | :--- |
| **Tier 1** (jogada) | Plena | **Plena** |
| **Tier 1.5** (clube do jogador, simulada) | Plena | **Plena** — mesmo regime do Tier 1 |
| **Tier 2** (ligas estrangeiras) | Por rating de partida | **Reduzida** — só eventos maiores |
| **Tier 3** (ligas menores) | Base matemática | **Congelada** |

Tier 1.5 recebe o regime do Tier 1 porque é o **mesmo clube** — apenas simulado em vez de jogado. Divergência entre os dois modos seria sinal de calibração, não comportamento desejado.

### 10.2 Gatilho
A cada checagem, rola contra a volatilidade da fase (§9.1). Disparando, a mudança é **dirigida por comportamento recente**, nunca aleatória: eventos (GDD §3), consistência de treino, marcos de carreira.

### 10.3 Grafo de maturação
| De | Gatilho | Para |
| :--- | :--- | :--- |
| `HOTHEAD_PRESS` | Boas escolhas de imprensa + idade | neutro → `CHARISMATIC` |
| `PARTY_ANIMAL` | Idade + treino consistente | neutro → tendência `PRO_EXEMPLARY` |
| `MAVERICK` | Capitania / liderança | `BORN_LEADER` |
| `AMBITIOUS` | Sucesso + permanência longa | `LOYAL` |
| `AMBITIOUS` | Títulos negados / estagnação | `MERCENARY` |
| `WONDERKID` | Potencial realizado ao entrar no Prime | consumido (removido) |
| `SLOW_STARTER` | Experiência acumulada | resolve (removido) |

Volatilidade é bidirecional e cai com a fase: personalidade **consolida** com a idade.

### 10.4 Janela de commit
Mudanças de traço são **commitadas entre temporadas** (ou em marco de evento explícito), nunca no meio da temporada. Preserva o `static` read-only do GDD §4. O `personalityVector` é re-derivado no commit.

---

## 11. Lesão e condicionamento

- `InjuryMult` cresce por fase (§9.1): a mesma jogada lesiona um veterano mais que um prospect.
- **`conditioningLoad`** (slots de fisioterapia/condicionamento) torna-se obrigatório no Veteran/Twilight. Insuficiente → `PhysDecay` acelera **e** `InjuryMult` sobe outro degrau.
- `GLASS_ANKLES` e `INJURY_RECUR` **compõem** com o multiplicador de idade.
- `EVERGREEN` e `PRO_EXEMPLARY` aliviam o regime.
- Períodos longos no departamento médico realimentam `morale` e `formMood`.

---

## 12. Fim de carreira, snapshots e Pool (T-11)

### 12.1 Saídas
Disparo por idade-teto (46), aposentadoria voluntária ou lesão incapacitante. Caminhos **não exclusivos**: virar treinador (personalidade do atleta vira base do perfil técnico) e/ou salvar na Pool de Personalizados.

### 12.2 Snapshots
Automáticos e **transitórios** nas idades fixas **15 / 21 / 27 / 33 / 39**, presos ao save da carreira. No fim de carreira, o jogador **escolhe quais fases commitar** à Pool permanente — paga storage só pelo que quer guardar. **Aposentadoria antecipada libera apenas as fases efetivamente vividas** (não se guarda uma fase que o atleta nunca alcançou).

```json
{
  "poolEntryId": "uuid",
  "displayName": "string",
  "originCareerId": "uuid",
  "snapshots": [
    { "phase": "Prospect", "age": 15, "record": { } },
    { "phase": "Prime",    "age": 27, "record": { } }
  ]
}
```

Uma entrada da Pool carrega numa carreira nova **na fase escolhida** — e, sendo um `CharacterRecord` padrão, entra pelo mesmo pipeline de criação (§4), sem caminho especial. Resgata a cultura de compartilhamento dos *option files* do PES no eixo do tempo.

---

## 13. Determinismo (T-14)

Todo sorteio consome stream isolado, no padrão de `ClubIdentityGenerator`:

```
seed = hash(masterSeed, StreamName, playerId[, season])
```

Acréscimos ao enum `StreamName`: `CharacterCreation` · `PlayerDevelopment` · `TraitEvolution` · `InjuryRoll`.

Garantia: `(escolhas de criação, seed) → CharacterRecord` e `(CharacterRecord, seed, temporada) → estado pós-temporada` são replayáveis bit a bit. Sem `System.Random`, sem transcendentais (regra do prompt SplitMix64).

---

## 14. Validação (fail-fast)

| # | Regra | Erro |
| :--- | :--- | :--- |
| V1 | Nome não vazio, dentro do limite | `INVALID_NAME` |
| V2 | Idade ∈ [15, 46] | `INVALID_AGE` |
| V3 | Exatamente 1 posição primária | `NO_PRIMARY_POSITION` |
| V4 | ≤ 3 secundárias, ≠ primária | `INVALID_SECONDARY` |
| V5 | `AP` 100% alocado | `AP_NOT_FULLY_SPENT` |
| V6 | Nenhum atributo > `CapProfile` | `ATTRIBUTE_CAP_EXCEEDED` |
| V7 | Nenhum atributo > 99 (banda reservada) | `RESERVED_BAND_VIOLATION` |
| V8 | Saldo de CPP ≥ 0 | `NEGATIVE_CPP_BALANCE` |
| V9 | ≤ 4 `LifeTrait` negativos | `TOO_MANY_NEGATIVE_TRAITS` |
| V10 | Nenhuma exclusão mútua ativa | `MUTUALLY_EXCLUSIVE_TRAITS` |
| V11 | Pré-requisitos satisfeitos | `TRAIT_PREREQ_UNMET` |
| V12 | ≤ 1 `PlayerTrait` concedido | `MULTIPLE_GRANTED_PLAYER_TRAITS` |
| V13 | `shirtNumber` ∈ [1,99] e único no elenco | `INVALID_OR_TAKEN_NUMBER` |
| V14 | Chaves de atributo == schema canônico de 12 | `ATTRIBUTE_SCHEMA_MISMATCH` |
| V15 | Escrita em `static` fora de marco entre-temporadas | `STATIC_WRITE_IN_SEASON` |

---

## 15. Em aberto (honesto)

**Bloqueado por ledger (não é decisão deste doc):**
1. ~~Partição dos 12 atributos em físico/técnico/mental~~ — **RESOLVIDO**, D-52.
2. ~~Tratamento de goleiro dentro dos 12~~ — **RESOLVIDO**, D-49 (vetor de OVR) + D-52 (curva de declínio).
3. Catálogo canônico de `PlayerTrait` elegíveis à compra cruzada (§2.3).
4. `FormMood` segue o modelo antigo −5/+5 com reset sazonal do GDD; a divergência já está registrada no ledger e **não** foi resolvida aqui.

**Sem ancoragem empírica (prompts de pesquisa dispatchados):**
5. Todos os multiplicadores de §9.1 e a mitigação de `EVERGREEN` → prompt #1.
6. ~~Tabelas de `CapProfile` por posição~~ — **RESOLVIDO**, D-50 (fórmula). Falta só código, atrás de D-07.
7. Deltas do Personality Vector por traço → prompt #4.
8. Forma exata da curva de `fit` (íngreme? ganho zero ou negativo no "difícil demais"?) — Prestígio §4.3 já a lista como decisão de design, não de pesquisa.

**Decisão de produto adiada:**
9. Loja in-game (créditos por objetivo → desbloqueios, slots extras de traço) — escopo futuro.
10. Mentoria entre atletas da Pool — fora da v1 (T-12).

**Prompts obsoletos:** #5 (câmera) e #6/#7 (viabilidade e custo de pipeline 3D) do `Pacote_Prompts_Pesquisa_Dirigida.md` ficam **cancelados** por T-01/T-02.

---

## 16. Build path incremental

Respeitando D-07 (nada antes da sequência obrigatória) e abstração só após duplicação real:

1. **Schema + validação.** `CharacterRecord` à mão, `validate()` verde, headless. Prova o contrato.
2. **`CapProfile` como função pura** + alocação de `AP`, testado sobre **duas** posições (a segunda confirma o padrão, não a primeira).
3. **`LifeTrait` + CPP + Personality Vector.** Agregação `traits → vector` determinística, com teste de replay por seed.
4. **Ciclo de uma temporada headless:** `TrainGain × fit × minutes`, `PhysDecay`, sem render. Aqui o harness de monotonicidade já pode checar que jovem em Tier adequado cresce mais que jovem sem minutos.
5. **Evolução de trait** entre temporadas + commit em `static`.
6. **Só então** o wizard e os sprites (§8) — a camada visual é a última, não a primeira.

---

## 17. O que isto destrava

Com este doc fechado, o **contrato de personagem** deixa de ser bloqueio para: o Match Engine (que passa a ter Personality Vector e atributos com origem definida), o sistema de eventos do GDD §3 (que ganha os pesos de RNG por traço), o gerador de regen (mesmo pipeline, sem caminho especial) e o `EffectiveAttributes.Read()` (que passa a ter a lista completa de contribuintes).

**Não destrava** a geração de população: os limiares de `PlayerTrait` continuam abertos no ledger. A partição dos 12 atributos (D-52) e o débito de calibração de OVR (D-46+D-49, teto 88) já foram fechados — ver o Registro de Decisões de Calibração e Autoria — mas isso não muda a prioridade de implementação deste módulo, que segue atrás de D-07.
