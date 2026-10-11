# Terra Paralela — Registro de Decisões: Pivô 2D do Criador de Personagem (Aparência)

> **Natureza deste documento.** Não é um companion doc novo — é um **registro de decisões** que corrige um desalinhamento de trilha e fecha as pendências técnicas do módulo de aparência dentro do que **D-08** permite (moratória: nenhum companion doc novo até o steering passar no harness). Mesma disciplina já aplicada em *Registro de Decisões — Camada de Autoria de Mundo*; a numeração de decisões continua a partir de **D-30** para manter um único índice no ledger.
>
> **Cross-ref:** `Terra_Paralela_Criacao_Personagem_Modular.md` (arquivado por este registro) · `Research_Prompt_Pipeline_Personagem_Modular.md` (reancorado por este registro) · **Geração de Identidade de Clube v2** (precedente direto: `palette`/paleta, `CrestGenerator`) · GDD §1 (perspectivas side-on/isométrica, resoluções de sprite) · GDD §5 (LOD).
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, stream isolado) — não muda com o pivô.

---

## 1. Supersessão formal — a via 3D do criador de personagem está arquivada

**D-30 — O módulo de aparência segue a trilha canônica 2D (D-01), não 3D.** `Terra_Paralela_Criacao_Personagem_Modular.md` — o companion doc produzido em sessão anterior assumindo Opção B (3D em tempo real, rig único, blend shapes, `SubViewport` 3D) — está **arquivado**, mesma disciplina do repositório `testingclaude`: preservado como registro histórico, sem edição retroativa, não é backup vivo. Nenhum artefato ativo do projeto depende dele.

**Por que isto não é reabrir D-01.** D-01 já travava a trilha; o desalinhamento não foi uma decisão de arquitetura tomada de novo — foi um trabalho conduzido numa sessão que não checou a trava existente contra o ledger antes de desenhar. Este registro é a correção, não uma nova deliberação sobre 2D vs. 3D.

**D-31 — O artefato produzido em sessão anterior também violou D-08.** Um companion doc completo foi escrito antes do steering passar no harness. Consequência prática: nenhuma edição adicional naquele arquivo — ele permanece congelado como está, e este registro assume o papel de veículo de decisão até a moratória ser levantada.

---

## 2. O que sobrevive do trabalho anterior — sem retrabalho

Nem tudo desde o zero: a parte do desenho anterior que era **agnóstica de renderização** permanece válida, porque nunca dependeu de 3D.

| Peça | Sobrevive? | Nota |
| :--- | :--- | :--- |
| `AppearanceProfile` como contrato de dados único (slots + cores) | **Sim, intacto** | Struct nunca dependeu de mesh 3D — é índice + enum. |
| Três portas de entrada (montar / randomizar / preset) | **Sim, intacto** | Arquitetura de fluxo, não de renderização. |
| Regra "preset copia, nunca referencia" | **Sim, intacto** | |
| `AppearanceGenerator` compartilhado entre jogador e NPCs | **Sim, intacto** | |
| Envelhecimento como parâmetro derivado, não editado manualmente | **Sim, princípio intacto** | Mecanismo exato muda (§4). |
| `AppearanceAssembler` (mesh 3D, rig, blend shapes) | **Não** | Todo o miolo de montagem é reescrito para 2D (§3). |
| `PartCatalog` como conceito (partes por slot) | **Sim, conceito** | Conteúdo dos slots muda de malha 3D para sprite em camadas (§3). |
| Retrato via `SubViewport` + câmera no busto 3D | **Não** | Substituído por composição 2D direta (§3). |
| Blocos A–D do research prompt (rig, export Blender→glTF, troca de mesh skinnada) | **Não** | Arquivados dentro do próprio arquivo de pesquisa (§5). |
| Blocos F (distribuição por nacionalidade) e G (envelhecimento) do research prompt | **Sim, intactos** | Renderer-agnósticos por natureza — a pergunta é sobre dados demográficos e curva de idade, não sobre pixels vs. polígonos. |

Isso confirma o valor do desenho por camadas: o dado (`AppearanceProfile`) e a lógica de fluxo sobrevivem exatamente porque foram mantidos separados da técnica de renderização — o mesmo princípio já aplicado em todo o projeto (*derived state must never be stored as truth*; *render e simulação são ortogonais*).

---

## 3. Decisões de pivô técnico (2D)

**D-32 — Recoloração via paleta indexada + troca de paleta em runtime.** Pele, cabelo e olhos deixam de ser parâmetro de shader de material 3D e passam a ser **paleta indexada com swap em tempo de execução** — a técnica clássica de jogos 16-bit para recolorir sprite sem redesenhar arte (o mesmo princípio, aliás, que qualquer jogo de futebol retrô usa para variar cor de uniforme sobre um único sprite-base). Implementação exata (shader `CanvasItem` lendo textura de paleta) é dependência de pesquisa — ver Bloco H do research prompt reancorado (§5).

**D-33 — Um profile, dois compositores, por resolução de consumo.** O GDD §1 já define duas perspectivas com orçamentos de resolução distintos: sprite de partida (16×16/32×32, side-on) e sprite de vida-sim (isométrico, resolução maior). Em vez de um `AppearanceAssembler` único, o módulo passa a ter **dois compositores** consumindo o mesmo `AppearanceProfile`:
- `MatchSpriteComposer` — resolução baixa, provavelmente com detalhe reduzido (ver D-34).
- `LifeSimSpriteComposer` (dobra como gerador de retrato de UI) — resolução mais alta, detalhe completo.

Isto estende o princípio já travado de "render e simulação são ortogonais" para "render e aparência são ortogonais" — um dado, múltiplos consumidores de apresentação, nenhum deles é fonte de verdade.

**D-34 — Detalhe de rosto/cabelo reduzido no sprite de partida, mantido no de vida-sim/retrato.** A 16×16/32×32 pixels, distinção fina de `Head`/`FacialHair` é imperceptível — mesma lógica que já orientava a priorização de MVP no doc arquivado (rosto pesa mais no retrato do que em campo), agora aplicada à própria composição, não só à ordem de implementação. O `MatchSpriteComposer` pode 1ler apenas `skinTone` + `hairColor` (silhueta), ignorando `headShape`/`hairStyleId`/`facialHairId`; o `LifeSimSpriteComposer` lê o profile completo. **Consequência de design, não de pesquisa** — não precisa esperar o Bloco H para ser assumida como direção.

**D-35 — `PartCatalog` guarda duas variantes de asset por parte, um índice compartilhado.** Cada entrada do catálogo (ex.: `hairStyleId = 3`) referencia um sprite-sheet de partida **e** um de vida-sim — nunca dois catálogos paralelos. Evita a duplicação de fonte de verdade que a v2 da Identidade de Clube já identificou e resolveu como padrão para outros dados do projeto (chaves únicas, uma árvore, não duas).

**D-36 — Retrato deixa de precisar de câmera.** Sem modelo 3D, não há "renderizar o busto": o retrato de UI **é** a saída do `LifeSimSpriteComposer`, cacheada como PNG (mesma disciplina de cache do `CrestGenerator` — regenerar só quando o profile muda ou a idade cruza um limiar de envelhecimento). `SubViewport` sai do desenho deste módulo especificamente; permanece em uso no `CrestGenerator`, que é caso à parte.

**D-37 — Envelhecimento vira troca discreta de faixa, não parâmetro contínuo.** A versão 3D previa `DeriveAgedColor` como interpolação contínua (fazia sentido com shader/blend shape). Em paleta indexada, a operação natural é **trocar o índice de `HairColorArchetype` ao cruzar um limiar de idade** (ex.: `Brown` → `Grey` aos X anos), não interpolar. Mecanismo travado; os limiares exatos continuam abertos (§6 — dependem do Bloco G do research prompt, que segue ativo e não muda com o pivô).

**D-38 — A pergunta de performance de "22 instâncias simultâneas" deixa de ser item de pesquisa.** Era um Eixo/Bloco inteiro do research prompt sob 3D (custo de skeleton + blend shape em GPU). Composição de sprite 2D em camadas, pré-cacheada por profile, não tem custo relevante nessa escala — não há necessidade de investigação dedicada. Removido do escopo ativo de pesquisa (refletido em §5).

---

## 4. Reancoragem do research prompt

**D-39 — `Research_Prompt_Pipeline_Personagem_Modular.md` permanece o único artefato de pesquisa deste módulo — não bifurca em um arquivo novo.** Já foi estendido duas vezes nesta linha de sessões (Blocos F/G, depois este pivô); continua sendo o mesmo arquivo, evitando duas fontes de verdade sobre o mesmo tema.

Reancoragem aplicada diretamente no arquivo:

| Bloco | Status pós-pivô |
| :--- | :--- |
| A — Autoria no Blender (rig) | **Arquivado.** Marcado no arquivo com banner explícito; não disparar. |
| B — Exportação glTF | **Arquivado.** |
| C — Materiais/shader 3D | **Arquivado.** |
| D — Performance de skeleton/blend shape | **Arquivado** (também superado por D-38 — nem seria mais relevante). |
| E — Autoria incremental/ferramentas | **Arquivado** (falava de addons Blender/rig; sem aplicação em 2D). |
| F — Distribuição por nacionalidade | **Ativo, sem alteração.** |
| G — Envelhecimento visual derivado | **Ativo, sem alteração** (mecanismo de consumo muda por D-37, mas a pergunta de pesquisa — curva/precedentes — é a mesma). |
| H — Composição em camadas + palette swap (**novo**) | **Ativo, bloco prioritário.** Cobre: padrão de compositing 2D em Godot 4, implementação de palette swap via shader `CanvasItem`, convenção de arquivo para as duas resoluções de sprite (D-33/D-35), pipeline de autoria (Aseprite→Godot), e confirmação de que pré-composição em cache é a escolha certa (formaliza D-38 com fonte, não só dedução). |

---

## 5. Consumidores a jusante — atualizado

| Sistema | Lê | Mudou com o pivô? |
| :--- | :--- | :--- |
| `MatchSpriteComposer` | `AppearanceProfile` (subconjunto: `skinTone`, `hairColor`) | **Novo** (substitui parte do `AppearanceAssembler` 3D) |
| `LifeSimSpriteComposer` / retrato | `AppearanceProfile` (completo) | **Novo** (substitui `AppearanceAssembler` 3D + pipeline de retrato `SubViewport`) |
| `WorldBuilder` (P6/P7, pacote de geração de clube/jogador) | `AppearanceProfile` gerado via `AppearanceGenerator` | Sem mudança — consumia o profile, não o assembler. |
| `ClubIdentity.kits`/`palette` | Fonte de cor de kit no momento da composição | Sem mudança de papel; possível compartilhamento de mecanismo de paleta com D-32 — **pendente Bloco H**. |

---

## 6. O que permanece deliberadamente em aberto

1. **Implementação exata do palette swap em Godot 4** (shader, formato de textura de paleta) — Bloco H, pesquisa não rodada ainda.
2. **Se o shader de paleta de pele/cabelo é o mesmo mecanismo do shader de paleta de kit, ou dois shaders por convenção** — Bloco H.
3. **Estrutura da distribuição por nacionalidade** (granularidade país vs. macro-região) — Bloco F, inalterado pelo pivô, ainda sem pesquisa rodada.
4. **Limiares exatos de idade para troca de `HairColorArchetype`** — Bloco G, mecanismo travado (D-37), curva não.
5. **Tamanho final do catálogo MVP por slot, por resolução** — decisão de produção/conteúdo, não de pesquisa.
6. **Pipeline de autoria de pixel art** (Aseprite ou equivalente → Godot) — Bloco H.

Nenhum destes bloqueia o entendimento do pivô; bloqueiam apenas a implementação, que já estava atrás de D-07 (SplitMix64 → física headless → steering → primeiro sprite) de qualquer forma.

---

## 7. O que isto destrava

O ledger deixa de carregar uma contradição silenciosa entre um companion doc 3D "pronto" e a trilha 2D travada — a contradição está resolvida e registrada, não escondida atrás de um artefato desatualizado. O research prompt continua como o único documento vivo de pesquisa do módulo, agora apontando na direção certa. Quando D-08 for levantada (steering passa no harness), o companion doc 2D definitivo pode ser escrito diretamente a partir deste registro, sem precisar reconstituir o raciocínio do zero — e sem herdar nenhuma decisão da via 3D por engano.

**A prioridade de implementação do projeto não muda:** `System.Random` → SplitMix64 → física headless → steering → primeiro sprite (D-07). Este módulo permanece na janela de sprints de apresentação, atrás da espinha jogável.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | Módulo de aparência segue trilha 2D (D-01); companion doc 3D arquivado |
| D-31 | Companion doc anterior também violou D-08; congelado, sem edição |
| D-32 | Recoloração via paleta indexada + swap em runtime, não shader de material 3D |
| D-33 | Um profile, dois compositores (`MatchSpriteComposer`, `LifeSimSpriteComposer`) por resolução |
| D-34 | Sprite de partida usa subconjunto reduzido do profile (silhueta); vida-sim/retrato usa completo |
| D-35 | `PartCatalog` guarda duas variantes de asset por parte sob um único índice — sem catálogos paralelos |
| D-36 | Retrato é saída direta do `LifeSimSpriteComposer`, cacheada — sem câmera/`SubViewport` |
| D-37 | Envelhecimento vira troca discreta de faixa de `HairColorArchetype`, não interpolação contínua |
| D-38 | Pergunta de performance de 22 instâncias simultâneas removida do escopo de pesquisa (não se aplica em 2D) |
| D-39 | Research prompt permanece arquivo único; Blocos A–E arquivados, F/G ativos, H novo e prioritário |
