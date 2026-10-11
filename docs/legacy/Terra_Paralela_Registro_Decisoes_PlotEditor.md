# Terra Paralela — Registro de Decisões: Editor de Lote e Propriedades (`PlotEditor`)

> **Natureza deste documento.** Não é companion doc novo — é um **registro de decisões**, no mesmo formato do *Registro de Decisões: Camada de Autoria de Mundo*. Escrito assim deliberadamente para respeitar **D-08** (moratória de design: nenhum companion doc novo até o steering passar no harness).
>
> **Cross-ref:** *Sistema de Edição de Propriedades* · *Melhorias e Relações na Gameplay* · *Próximos Passos: Prompts* · *Geração de Identidade de Clube v2* · *Registro de Decisões: Camada de Autoria de Mundo* · GDD §2 (Tempo), §3 (Eventos), §4 (Character State), §5 (LOD).
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, stream isolado).
>
> **Escopo:** o sistema único de edição de espaços — estádio/instalações do clube, apartamento do atleta e lote público. **Fora:** match engine, Tier 1.5, arbitragem, sistema de patrocínio/economia (tocado só na fronteira).
>
> **Fecha as seis decisões em aberto** (`C-arte`, `C-cidade`, `C-tile`, `C-granularidade`, `C-cosmético`, `C-naming`) e corrige três erros de arquitetura dos documentos anteriores desta linha. Nada aqui reabre `D-01`…`D-29`.

---

## 1. Reconciliação com o ledger — três correções obrigatórias

Os três documentos anteriores desta linha foram escritos sem visão do ledger travado. A reconciliação encontrou três conflitos reais. Registrados aqui como **correção formal**, não como convivência silenciosa.

### 1.1 `C-arte` era um gate espúrio — **eliminado**

O doc *Próximos Passos* travou a especificação de UX do editor atrás de uma decisão "2D duplo vs. híbrido 3D". **`D-01` já trava a trilha canônica como 2D**; o plano de migração 3D e o repo `testingclaude` estão arquivados. O gate não existia. A UX está destravada e é especificada na §6 deste registro.

### 1.2 O Effect Resolver **não pode gravar** em `ClubFacilities` / `Stadium.Capacity`

Os docs anteriores diziam que melhorar uma instalação "recalcula e grava `ClubFacilities.TrainingFacilities`" e que arquibancadas "gravam `Stadium.Capacity`". Isso viola frontalmente o princípio central do projeto — **estado derivado nunca é armazenado como verdade**, o mesmo que proíbe armazenar OVR, valor de mercado e salário. Capacidade e ratings de instalação são exatamente estado derivado: função dos níveis construídos.

Correção na §3. É o item mais importante deste registro.

### 1.3 "Atmosfera → vantagem de casa" ia abrir um **segundo caminho**

Os docs anteriores propuseram um modificador de atmosfera derivado do estádio construído, alimentando um bônus de mando. Mas **`D-18` / v2 §9.5 já travam** que `AtmosphereArchetype` mapeia para **um** parâmetro nomeado `homeAdvantageModifier`, ordinal, com **um** consumidor (o resolvedor de partida), e que `D-06` proíbe que o arquétipo toque a física. Criar um segundo canal de atmosfera duplicaria a fonte de verdade do mando.

Correção na §4.3: o estádio construído alimenta **o mesmo** `homeAdvantageModifier`, nunca um paralelo.

---

## 2. A decisão estrutural — **travada**

**D-30 — Sistema único: `PlotEditor` + módulos de contexto.** Estádio/instalações, apartamento do atleta e lote público são o **mesmo** sistema. Três sistemas separados triplicariam grade, posicionamento, validação e render. Cada módulo de contexto fornece exatamente quatro peças — **Catálogo, Ruleset, EffectResolver, SaveScope** — e nenhuma linha de lógica de grade.

**D-31 — Construção incremental, abstração diferida.** Ordem: `StadiumContext` → (extrair o núcleo) → `ApartmentContext` → `PublicLotContext`. A generalização acontece no **segundo** caso de uso real, guiada por duplicação observada. Nada de motor genérico especulativo no primeiro. Mesma disciplina que colocou a extração de `MatchRuleset` depois da duplicação real.

**D-32 — Dois verbos, dois caminhos de validação.** `Customize` (layout/cosmético, reversível) e `Upgrade` (nível, com gate, irreversível) são fluxos distintos. Misturá-los produz o emaranhado clássico onde "mover um objeto" e "melhorar um objeto" disputam o mesmo estado.

---

## 3. Arquitetura de dados — a correção central

### 3.1 A separação que resolve tudo

O erro dos docs anteriores foi tratar "a grade" como a coisa que a simulação lê. A grade não é isso. Separação travada:

| Camada | O que é | Quem tem | Papel |
| :--- | :--- | :--- | :--- |
| **`FacilityLevels`** | Verdade armazenada: mapa `ObjectTypeId → level`. ~8 inteiros por clube. | **Todos** os clubes, em todos os tiers | Única fonte de verdade |
| **`PlotLayout`** | Arranjo espacial: posição, rotação, variante de cada objeto | **Só** lotes abertos pelo jogador (Tier 1) | Cosmético e espacial |
| **`EffectiveFacilities.Read()`** | Função pura `FacilityLevels → (capacity, trainingRating, …)` | — | **Único ponto de acesso** |

**D-33 — `FacilityLevels` é a única verdade armazenada; `capacity` e os ratings de instalação são derivados em tempo de leitura via `EffectiveFacilities.Read()`.** Espelha exatamente `EffectiveAttributes.Read()`. Nenhum sistema lê capacidade de um campo persistido; nenhum sistema escreve capacidade.

Consequências diretas:
- **Um caminho para todo mundo.** O clube da IA no Tier 3 e o clube do jogador no Tier 1 consomem a mesma função. A IA "melhora" incrementando um nível em `FacilityLevels` — não existe caminho de simulação paralelo.
- **LOD honrado sem fork.** `PlotLayout` só é instanciado para o lote que o jogador está editando; uma grade carregada por vez. Mas isso é uma otimização de front-end, **não** um segundo modelo de dados.
- **Sem débito de calibração.** Ajustar a curva capacidade↔nível é mudar uma função, não migrar dados de 640 clubes.

**D-34 — `PlotLayout` é ortogonal ao efeito.** Nenhuma posição, rotação ou variante entra em `EffectiveFacilities.Read()`. Mover a megastore não muda receita. Isso é o que torna o layout puramente expressivo e o que permite que clubes sem layout sejam simulados idênticos aos com layout.

### 3.2 Contrato de dados

```
PlotState
  plotId: string
  contextType: ContextType        // Stadium | Apartment | PublicLot
  ownerRef: string                // clubId | athleteId | cityId
  facilityLevels: Map<ObjectTypeId, int>   // VERDADE
  layout: PlacedObject[] | null            // COSMÉTICO, null fora do Tier 1

PlacedObject
  objectTypeId: string
  gridX, gridY: int
  rotation: Rotation
  variantId: string

ObjectType                        // catálogo, data-driven, por contexto
  objectTypeId: string
  contextType: ContextType
  footprint: (w, h)
  isAnchored: bool                // slot fixo (arquibancadas) vs. livre
  levels: LevelSpec[]             // custo, buildTime, sprite, effectPayload
  unlockCondition: UnlockSpec
```

**D-35 — `isAnchored` resolve slot-fixo vs. grade-livre com um flag, não com dois modelos.** Arquibancada é objeto ancorado (posição travada, só o nível varia); mobília de apartamento é livre. Um modelo de grade, um caminho de validação, uma propriedade que difere.

### 3.3 Reconciliação com D-17 — **supersessão proposta, a confirmar no ledger**

`D-17` e v2 §9.6 travam que `stadium.capacity` é amostrado por Gaussiana truncada dentro da faixa da banda de prestígio, nunca escrito à mão, nunca derivado em runtime.

O mecanismo sobrevive; **o alvo da amostragem muda**. Com `D-33`, `capacity` deixa de ser campo armazenável — então não pode ser o que se amostra.

**D-36 (⚠️ SUSPEITA de supersessão de D-17 — verificar no ledger) — a banda de prestígio semeia `FacilityLevels` iniciais por Gaussiana truncada; `capacity` emerge daí.** O gerador de identidade amostra o **nível inicial das arquibancadas** (e do gramado, e das instalações) na faixa da banda; `EffectiveFacilities.Read()` produz a capacidade. Preserva-se tudo que D-17 protegia (nunca escrito à mão, amostrado por banda, disciplina de Gaussiana truncada) e elimina-se o campo derivado persistido.

**Impacto no contrato `ClubIdentity` (v2 §3):** `stadium.capacity` sai do JSON; entra `stadium.initialFacilityLevels`. `stadium.pitchSurface` idem — vira o nível inicial do objeto `Pitch`, e o `PitchSurface` corrente passa a ser derivado do nível (o clube que investe no gramado muda de `Worn` para `Pristine`, que é o comportamento desejado e que a v2 não contemplava). `stadium.name` e `stadium.atmosphereArchetype` permanecem como estão.

> Esta é a única alteração que este registro propõe a uma decisão já travada. Marcada explicitamente para ratificação, não aplicada por conta própria.

---

## 4. Decisões dos contextos

### 4.1 `StadiumContext`

**D-37 — Arquibancadas por seções fixas (`North`, `South`, `East`, `West`), cada uma com níveis.** Colocação livre de blocos foi rejeitada: em pixel-art 2D isométrico ela produz silhuetas incoerentes, explode o número de sprites necessários e não compra profundidade de decisão — a decisão interessante é *quanto* investir e *em qual setor*, não *onde encostar o bloco*. Quatro seções ancoradas dão exatamente essa decisão com uma fração do custo de arte.

**Catálogo v1** (data-driven; adicionar entrada não exige código): `StandNorth`, `StandSouth`, `StandEast`, `StandWest`, `Pitch`, `TrainingCentre`, `YouthAcademy`, `MedicalCentre`, `Megastore`, `Floodlights`, `Roof`, `PressRoom`, `Parking`.

**Ruleset:** orçamento do clube, aprovação de diretoria, e requisito de capacidade mínima por divisão. O requisito de divisão é o que transforma o estádio de luxo opcional em objetivo com prazo — e amarra estádio, calendário e diretoria num só laço.

**`EffectResolver`:** nenhum. Este contexto **não tem** resolver próprio — ele apenas escreve níveis em `FacilityLevels`, e `EffectiveFacilities.Read()` faz o resto. (A ausência de resolver é o sintoma de que `D-33` está correto.)

### 4.2 `ApartmentContext`

**Catálogo v1:** `Bed`, `HomeGym`, `Kitchen`, `Entertainment`, `SocialSpace`, `Decor`, `Security`. Grade livre (`isAnchored = false`).

**Ruleset:** dinheiro **pessoal** do atleta (nunca o orçamento do clube — são economias separadas) e `propertyTier`, que destrava tamanho de grade.

**`EffectResolver`:** este contexto **tem** resolver, porque escreve na camada dinâmica de vida (recuperação, humor), que não é derivada de nível de instalação e sim modulada por ela. Em `arcadeMode`, o resolver dinâmico é inerte, conforme GDD §4.

**D-38 — `DistrictArchetype` permanece sem consumidor na v1, mesmo com o `ApartmentContext` existindo.** `D-19` trava o domínio (vida/carreira) e v2 §9.4 trava "campo emitido, zero efeito". O apartamento é o **consumidor candidato declarado**, mas especificar hoje qual variável o distrito modula seria escolher a interface de um simulador de vida que ainda não está desenhado. *Deferred abstraction* aplicada: generalizar no segundo caso real, não no primeiro imaginado.

### 4.3 Atmosfera — um parâmetro, um consumidor

**D-39 — O estado construído do estádio alimenta o **mesmo** `homeAdvantageModifier` de `D-18`, nunca um canal paralelo.** A função é `homeAdvantageModifier = f(atmosphereArchetype, occupancyRatio, roofLevel)` — uma função, um parâmetro de saída, um consumidor (o resolvedor de partida). Reafirma `D-06`: nada disso toca física; o modificador é lido pela camada de decisão/resolução.

**D-40 — A magnitude não é inventada aqui.** É a **mesma dependência de dados** já declarada em v2 §11 #1 (vantagem de casa por liga, dado público, já exigida pelo harness de monotonicidade ordinal). Não é pesquisa nova; é o mesmo número, agora com um consumidor a mais. Sem número, sem implementação.

### 4.4 `PublicLotContext`

**D-41 — O lote público é uma ferramenta de autoria offline, não progressão in-fiction.** Alinha com a fronteira já estabelecida para o `ClubIdentityGenerator`: roda no editor de mundo, produz dado inerte, e o jogo **apenas lê**. Sem economia, sem gate, sem `EffectResolver`, catálogo cheio. Saída = dados de mundo.

Justificativa: a leitura alternativa ("clube investe na comunidade") exigiria um sistema econômico de cidade que não existe, com efeitos (prestígio urbano, base de torcida) que precisariam de ancoragem empírica própria — custo alto para benefício especulativo. E a leitura de autoria é a que o projeto **já precisa**: as cidades da base de mundo (~32 ligas) têm que vir de algum lugar. Escolher a leitura que resolve um problema existente em vez de inventar um sistema novo é a decisão barata e correta.

**Consequência:** `PublicLotContext` não é feature de jogador na v1. Se um dia a progressão urbana for desejada, ela entra como um **ruleset adicional** sobre o mesmo contexto — que é precisamente o que `D-30` compra.

### 4.5 As duas pendências menores

**D-42 — Customização cosmética é livre e reversível; upgrade custa e é irreversível.** Fronteira limpa e igual ao modelo dos dois verbos (`D-32`): se custa recurso, é `Upgrade`; se não custa, é `Customize`. Cobrar por trocar a cor de um assento cria uma terceira categoria (cosmético pago) que precisaria do seu próprio fluxo de validação, sem comprar decisão interessante.

**D-43 — Naming rights e patrocínio **não** entram no catálogo de objetos.** O lean anterior (objeto com `EffectPayload` financeiro) está **rejeitado**: naming rights não tem footprint, não ocupa tile, não tem nível — forçá-lo no catálogo cria um tipo de objeto fantasma e um caso especial em toda a validação de posicionamento. É um **contrato de patrocínio**, pertence ao sistema econômico do clube, e o estádio apenas **lê** o contrato vigente para renderizar a placa/letreiro. Uma fonte de verdade, no sistema certo.

---

## 5. Validação (fail-fast)

Toda falha bloqueia e reporta a razão exata. Sem auto-correção, sem fallback.

| # | Regra | Erro |
| :--- | :--- | :--- |
| V1 | Footprint dentro dos limites da grade | `OUT_OF_BOUNDS` |
| V2 | Tiles-alvo livres | `TILE_OCCUPIED` |
| V3 | Terreno compatível com o tipo | `INVALID_TERRAIN` |
| V4 | Adjacência satisfeita | `ADJACENCY_VIOLATED` |
| V5 | Recurso suficiente para o custo | `INSUFFICIENT_FUNDS` |
| V6 | Condição de desbloqueio atendida | `OBJECT_LOCKED` |
| V7 | Gate de contexto satisfeito (diretoria, `propertyTier`) | `CONTEXT_GATE_FAILED` |
| V8 | Edição fora de partida ativa | `WRONG_SCENE_STATE` |
| V9 | Remoção não quebra dependência de routing | `BREAKS_DEPENDENCY` |
| V10 | `isAnchored == true` → tentativa de mover é erro | `OBJECT_IS_ANCHORED` |
| V11 | `objectTypeId` fora do enum do contexto | `UNKNOWN_OBJECT_TYPE` |
| V12 | `level` fora da faixa declarada em `levels[]` | `INVALID_LEVEL` |

---

## 6. UX do editor — destravada por `D-01`

Especificável agora porque a trilha é 2D isométrico, sem ambiguidade de modelo de interação.

**D-44 — Tile único no núcleo; contextos declaram `visualScale`.** Um tamanho de tile em `SoccerSim.Core`; a diferença entre a escala de um estádio e a de um apartamento é de renderização, não de modelo. Dois tamanhos de tile no núcleo criariam dois caminhos de conversão grade↔tela.

**D-45 — Dois modos explícitos e mutuamente exclusivos: `ViewMode` e `BuildMode`.** Nunca edição acidental; `ViewMode` é o estado default de qualquer lote. A transição é um toggle único.

**Modelo de interação em `BuildMode`:**
- Painel de catálogo lateral, agrupado por categoria, com custo e estado de desbloqueio visíveis. Item bloqueado aparece **desabilitado com o motivo**, nunca oculto — esconder o alvo remove a progressão do horizonte do jogador.
- Objeto selecionado segue o cursor com snap ao tile; **preview de validade contínuo** (silhueta verde/vermelha) antes do commit, com o código de erro da §5 legível ao passar o mouse.
- Rotação em 4 passos (bloqueada se `isAnchored`).
- Objeto ancorado abre direto o **painel de nível** em vez do fluxo de posicionamento — os dois verbos de `D-32` visíveis na interface.
- Câmera: pan livre, zoom limitado, **sem rotação da câmera** (sprites isométricos 2D só existem numa orientação; rotação de câmera exigiria 4× o custo de arte).
- Confirmação explícita para todo `Upgrade` (irreversível, `D-42`), mostrando custo, `buildTime` e efeito. `Customize` é imediato e reversível, sem diálogo.

**D-46 — Obra em andamento tem sprite intermediário e ocupa o tile.** O tempo precisa ser legível na tela, não só num contador. A obra ocupa espaço enquanto corre — é o que torna o `buildTime` uma restrição sentida e não um número abstrato. Integra ao acumulador de delta do GDD §2; fast-forward e task-skip avançam o relógio e concluem obras pendentes.

**D-47 — O fundo side-on da partida é derivado, read-only.** A cena de partida renderiza as arquibancadas a partir de `FacilityLevels`, sem nenhum caminho de edição. Corte transversal do mesmo dado, não um segundo editor.

---

## 7. Determinismo

Os níveis iniciais amostrados pelo gerador (`D-36`) consomem um stream próprio:

```
seed = hash(masterSeed, "facility_levels", clubId)
```

Isolado dos streams de partida, de identidade de clube e de geração de jogadores. Edições do jogador **não** consomem PRNG — posicionamento e upgrade são ações determinísticas do usuário, não sorteios.

---

## 8. Dependências de dados (não são decisões em aberto)

Nenhum número entra neste registro. Cada um vira tarefa limitada com entregável nomeado.

| # | Número | Origem exigida | Entregável |
| :--- | :--- | :--- | :--- |
| 1 | `homeAdvantageModifier` por `(atmosphereArchetype, occupancyRatio, roofLevel)` | Vantagem de casa por liga — **mesma dependência de v2 §11 #1**, já exigida pelo harness de monotonicidade | Tabela ordinal |
| 2 | Curva `standLevel → capacity` por banda B1–B6 | Capacidades reais de estádio por tier de liga (dado público e abundante) | Tabela de 6 linhas: média, desvio, mín, máx **de nível**, não de capacidade |
| 3 | Curva de custo por nível e `buildTime` | **Não é pesquisa** — é calibração de *game feel* (mesma disciplina de `D-04`) | Fixado no primeiro playtest do loop |

O #1 não é trabalho novo. O #2 substitui o entregável de v2 §11 #2, ajustado por `D-36`.

---

## 9. Posicionamento na sequência de implementação — **atrás de D-07**

**D-48 — O módulo inteiro é reposicionado atrás de `D-07`.** A sequência travada é `SplitMix64 → física headless → steering mínimo → primeiro sprite`, e o Godot não é aberto antes do passo 4. O `PlotEditor` é código de cena Godot 2D isométrica: **não pode ser disparado agora**, exatamente pelo mesmo motivo que reposicionou a camada de autoria de mundo.

Consequência prática: os prompts `B1`–`B7` do doc *Próximos Passos* ficam **prontos, não disparáveis**. A prioridade de implementação vigente não muda — continua `System.Random` → SplitMix64, com `Prompt_Implementacao_SplitMix64.md` pronto para handoff.

**D-49 — Os prompts `B3` e `B6` precisam de reescrita antes de disparar.** Ambos instruem o resolver a gravar em `ClubFacilities`/`Stadium`, o que `D-33` agora proíbe. Marcados como **superados na forma atual**, não como pendentes. Os prompts de pesquisa `A1`–`A8` permanecem válidos e disparáveis a qualquer momento (são pesquisa, não implementação).

---

## 10. O que permanece deliberadamente em aberto

Registrado para não virar decisão por omissão:

1. **Qual variável de vida o `DistrictArchetype` modula** — domínio travado (`D-19`), alvo aberto, e o `ApartmentContext` é o consumidor candidato declarado (`D-38`).
2. **Números das três dependências da §8.**
3. **Requisitos de capacidade mínima por divisão** — depende do modelo de competição fechar.
4. **Se o `PublicLotContext` ganha ruleset de progressão numa versão futura** (`D-41` decide a v1, não veta a evolução).
5. **Se `FacilityLevels` da IA evolui por regra de simulação** (orçamento → incremento) ou permanece estático por save — é decisão do módulo econômico, não deste.

---

## 11. Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | Sistema único `PlotEditor` + módulos de contexto (4 peças cada) |
| D-31 | Construção incremental; abstração extraída no 2º caso de uso |
| D-32 | Dois verbos separados: `Customize` ≠ `Upgrade` |
| D-33 | `FacilityLevels` é a única verdade; capacidade/ratings derivados via `EffectiveFacilities.Read()` |
| D-34 | `PlotLayout` é ortogonal ao efeito — posição nunca altera simulação |
| D-35 | `isAnchored` resolve slot-fixo vs. grade-livre com um flag, não dois modelos |
| D-36 | ⚠️ Banda semeia `FacilityLevels` iniciais; `capacity` emerge — **supersessão proposta de D-17, a ratificar** |
| D-37 | Arquibancadas por seções fixas N/S/L/O com níveis |
| D-38 | `DistrictArchetype` segue sem consumidor na v1; apartamento é candidato declarado |
| D-39 | Estádio construído alimenta o **mesmo** `homeAdvantageModifier` de D-18 |
| D-40 | Magnitude do mando = mesma dependência de dados de v2 §11 #1 |
| D-41 | `PublicLotContext` é ferramenta de autoria offline, não progressão in-fiction |
| D-42 | Cosmético livre e reversível; upgrade custa e é irreversível |
| D-43 | Naming rights **fora** do catálogo — é contrato de patrocínio; estádio só lê |
| D-44 | Tile único no núcleo; contexto declara `visualScale` |
| D-45 | `ViewMode` / `BuildMode` mutuamente exclusivos; default é `ViewMode` |
| D-46 | Obra em andamento tem sprite intermediário e ocupa o tile |
| D-47 | Fundo side-on derivado de `FacilityLevels`, read-only |
| D-48 | Módulo reposicionado atrás de D-07 — prompts prontos, não disparáveis |
| D-49 | Prompts `B3`/`B6` superados na forma atual; `A1`–`A8` seguem válidos |

---

## 12. O que isto destrava

O módulo passa de "arquitetura esboçada com seis decisões abertas" para **decisão fechada em tudo que era decisão de design** — resta apenas o que legitimamente depende de dado empírico (§8) ou de outro módulo fechar primeiro (§10).

Três correções de arquitetura foram feitas antes de virarem código: o gate espúrio de direção de arte, o armazenamento de estado derivado, e o segundo canal de vantagem de casa. Cada uma teria custado uma migração ou uma dívida de calibração se descoberta depois.

**A prioridade de implementação não muda:** continua `System.Random` → SplitMix64, seguida da física headless. Este módulo mora na janela de sprints pós-espinha-jogável, e este registro existe para que, quando essa janela chegar, nenhuma decisão de design esteja bloqueando o handoff.
