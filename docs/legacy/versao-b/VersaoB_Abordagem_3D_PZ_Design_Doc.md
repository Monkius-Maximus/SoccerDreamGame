# Design Document — VERSÃO B: Abordagem 3D Híbrida (Project Zomboid)
### Variante exploratória — overlay sobre a Versão A (docs de Criação + Ciclo de Vida)

> # ⛔ ARQUIVADO — NÃO IMPLEMENTAR
>
> **Status: rejeitado (T-01).** A trilha canônica do projeto é **2D** (D-01, que já arquivou o plano de migração 3D e o repo `testingclaude`). Manter esta variante viva criaria um segundo caminho de render — proibido pelo princípio *no fallbacks*.
>
> **Documento canônico substituto:** `Terra_Paralela_Criacao_Personagem_e_Ciclo_de_Vida.md`.
>
> Preservado apenas como **registro de raciocínio de design**: a análise de por que o morph corporal contínuo (estilo GTA San Andreas) é inviável em pixel art e barato em 3D — que motivou a solução 2D adotada (`BodyCondition` discreto no retrato, §8.4 do doc canônico) — vive nas §4 e §7 abaixo. Nenhum contrato de dados daqui é válido.
>
> ---
>
> **Contexto original (histórico):** variante paralela ("e se?") para avaliar viabilidade e tamanho de investimento. Convenção: prosa PT-BR, identificadores em inglês.

---

## 0. Premissa e leitura deste documento

**Premissa:** personagens **3D low-poly** renderizados dentro de um **mundo 2D isométrico em tiles** — o modelo do Project Zomboid (Build 41+). Um único modelo de personagem riggado serve todas as cenas; o mundo de vida permanece em tiles 2D isométricos; o campo da partida é um plano simples.

**Como ler:** este doc registra **apenas o que muda** em relação à Versão A. Tudo que não está listado aqui é **herdado sem alteração** (a lista explícita do que permanece igual está na §9). As camadas mecânicas — CPP, traços, atributos, Personality Vector, fases de idade, evolução de traços — **não mudam**: a abordagem 3D é uma decisão de *apresentação e corpo*, não de regra de jogo.

---

## 1. Arquitetura de render (o coração da Versão B)

A mudança estrutural: o **pipeline duplo de personagem desaparece**. Em vez de uma base de sprite lateral + uma base de sprite isométrica, há **um modelo 3D** renderizado por câmeras diferentes em cada cena.

| Cena | Câmera | Mundo | Personagem |
| :---- | :---- | :---- | :---- |
| **Partida** | Livre (lateral / diagonal / broadcast) — agora é escolha de design, não de custo de arte | Gramado: plano simples (2D ou 3D plano) | Mesmo modelo 3D |
| **Vida / mundo aberto** | Ortográfica no ângulo isométrico | Tiles 2D isométricos | Mesmo modelo 3D composto na cena iso |
| **Menus** | Câmera de retrato no modelo | — | Render do mesmo modelo (ou retrato dedicado) |

**Implementação em Godot:** o personagem 3D vive num **SubViewport** renderizado dentro da cena 2D — exatamente a técnica que você já usa para os escudos. Câmera ortográfica casada com o ângulo iso compõe o modelo 3D sobre os tiles. Nenhum caminho novo de engine além do que o projeto já exercita.

**Relação com o GDD §1:** a *Separated scene architecture* permanece (cenas separadas), mas o **asset do personagem passa a ser único** — é isso que colapsa o pipeline duplo. Isto implica um reframe do §1 ("16-bit sprite logic" → low-poly 3D + mundo iso 2D); o GDD não é editado aqui (é a fonte canônica da Versão A), apenas se registra a implicação.

**Resoluções (reframe da Versão A §8 / GDD §1):** as resoluções deixam de ser de sprite autorado. O retrato 64×64 vira um **render do modelo**; os "sprites" de partida e de vida são **render 3D em tempo real**, não sprite sheets pré-desenhados. A combinatória de sprite × direção × animação some.

---

## 2. Modelo e rig do personagem

- **Base mesh** humanoide low-poly, proporção naturalista **~7 cabeças** (estilo PZ, não chibi — ver §proporções da Versão A discutida).
- **Um único esqueleto/rig** compartilhado por todos os atletas → **animações autoradas uma vez e reaproveitadas** em todos os personagens. Este é o ganho central que justifica o 3D.
- `buildType` (Lean / Balanced / Athletic / Stocky) como **blendshapes** sobre a base mesh (ou variantes de malha), **mais escala** derivada de `height` (160–205 cm).
- Substitui a abordagem da Versão A de "sprite de frame × variante de build × paleta".

---

## 3. Aparência e customização — **SUBSTITUI a §8 da Versão A**

A camada cosmética inteira muda de técnica. O *intent* é o mesmo (modularidade, recolor barato); a implementação vira 3D.

| Elemento | Versão A (sprite/paleta) | **Versão B (3D)** |
| :---- | :---- | :---- |
| Pele | recolor de paleta indexada | **tint de material** na textura |
| Corpo/build | sprite por `buildType` | **blendshape** sobre base mesh + escala |
| Rosto | `facePreset` (sprite) | head mesh low-poly + **blendshapes** de feição (ou conjunto de head meshes) |
| Olhos | overlay × paleta | **material/textura** |
| Cabelo / barba | sprite overlay × paleta | **mesh 3D acoplada ao rig** + tint de material |
| **Uniforme / roupa** | sprite de kit | **mesh 3D acoplada que deforma com a animação** (o grande ganho PZ) + tint de cor de time |
| Chuteira / luva / acessório | recolor / flag | **attachments 3D** + tint |
| Número | sprite na camisa | textura/decal na malha do uniforme |

**Schema de aparência revisto (`static.appearance`):**
```json
"appearance": {
  "baseMeshId": "humanoid_a",
  "buildBlendshapes": { "athletic": 0.6, "stocky": 0.2, "lean": 0.0 },
  "heightScale": 1.02,
  "skinMaterial": { "tint": "#C9A07E" },
  "headMeshId": "head_07",
  "faceBlendshapes": { "jawWidth": 0.3, "noseBridge": -0.1 },
  "eyeMaterial": { "tint": "#5B3A29" },
  "hairMeshId": "hair_11", "hairTint": "#1A1A1A",
  "facialHairMeshId": "beard_03", "facialHairTint": "#1A1A1A",
  "kitMeshId": "kit_team_driven", "bootMeshId": "boot_02", "bootTint": "#0A66C2",
  "accessories": ["headband"],
  "shirtNumber": 9
}
```

---

## 4. Corpo dinâmico — **SUBSTITUI o `conditionTier` discreto; revive o morph estilo GTA San Andreas**

Em 3D, o morph contínuo de corpo (que era inviável em pixel art) volta a ser viável e barato. O `conditionTier` de ~3 níveis discretos da Versão A vira um **morph contínuo por blendshape**.

- **`frame` (estático):** baseline de `buildBlendshapes` + escala — define o Cap Profile (lógica **inalterada** da Versão A §5.4). É o esqueleto: não muda com treino.
- **`condition` (dinâmico):** dois pesos de blendshape contínuos, **derivados do estado que já existe** (agregado dos atributos físicos + fitness + fase de idade):
  - `muscle` (0–1): musculatura construída sobre o frame.
  - `soft` (0–1): "fora de forma" — sobe com `PARTY_ANIMAL`/`PRO` baixo/condicionamento negligenciado.
- O corpo é um **readout** do estado (não re-modifica nada — sem dupla contagem), igual ao princípio da Versão A; só que **contínuo** em vez de em degraus.

**`dynamic` (acréscimo):**
```json
"dynamic": {
  "conditionMorph": { "muscle": 0.72, "soft": 0.0 }
}
```

Este é o upside prometido: a Versão B torna o feedback corporal **mais suave de animar e mais rico** que a Versão A, em vez de mais caro.

---

## 5. Impacto no Ciclo de Vida — overlay no doc de Ciclo de Vida

Mudança **única e visual**: a transformação do corpo ao longo das fases (Prospect magro → Prime no pico → Twilight diminuído, doc de Ciclo de Vida §3/§9) passa a ser uma **animação contínua dos pesos de `conditionMorph`**, não troca de tier discreto. O atacante do exemplo §9 morfa suave de magro a encorpado a "seco de área" ao longo de 15→46.

`EVERGREEN` (Ciclo de Vida §4) agora também **desacelera visivelmente** o avanço do morph rumo ao "diminuído" no Twilight, além do efeito numérico no PhysDecay.

**Todo o resto do doc de Ciclo de Vida é inalterado** — fases, TrainGain, PhysDecay, InjuryMult, evolução de traços, snapshots, fim de carreira são *projection-agnostic*. Os snapshots (§8) passam a capturar também `appearance` e `conditionMorph` — apenas números, custo de storage desprezível.

---

## 6. Deltas de validação — overlay na §10 da Versão A

Mudam apenas as validações de aparência: faixas de `baseMeshId`/`headMeshId`/`hairMeshId` válidas, pesos de blendshape ∈ [0,1], `heightScale` derivado coerente. **Todas as demais validações (V1–V12) permanecem** — a lógica de `AP`, CPP, traços, GK, número etc. não tem relação com a técnica de render.

---

## 7. Pipeline de assets e realidade de produção (honesto)

**O que torna o 3D viável aqui:** uma base mesh + um rig + animações compartilhadas + roupas/kits modulares acopláveis. Você modela e anima uma vez; cada atleta é uma combinação de meshes + blendshapes + tints.

**Stack:** Blender (grátis) → Godot; composição 3D→2D via SubViewport (já em uso no projeto).

**Custos honestos (os mesmos que levantamos):**
- **Skill de 3D** (modelar, riggar, skinar, animar) — diferente de pixel art. É o fator decisivo real.
- **Mudança estética**: sai do 16-bit/pixel; entra no low-poly do PZ. Decisão consciente.
- **Footprint** ligeiramente maior que 2D, mas low-poly + mundo iso 2D mantém leve (o PZ não é pesado) — a meta de cliente leve sobrevive.
- **Legibilidade de partida**: deixa de ser problema de sprite minúsculo — é render em tempo real, resolve-se com enquadramento de câmera.

---

## 8. (reservado — sem deltas adicionais)

As demais seções da Versão A (fluxo de criação de 7 passos, sistema de atributos, traços, Personality Vector) **não recebem delta de render** — só o **passo 6 (Appearance)** do fluxo muda de conteúdo (passa a editar meshes/blendshapes/tints em vez de sprites/paletas); o fluxo em si é idêntico.

---

## 9. O que permanece IDÊNTICO (herdado da Versão A — explícito)

Para deixar claro que isto é overlay, não reescrita:

- **Economia de CPP** + ladder de dificuldade Amador→Lenda (Criação §6.1).
- **Todos os ~41 traços** + `EVERGREEN` (Criação §6.2–6.6).
- **Sistema de atributos**: escala 1–99 (+ teto de engine 120), pool `AP`, **Cap Profile** (e `buildType` continua definindo os caps), `potentialCeiling` (Criação §5).
- **Personality Vector** (8 dimensões) e seu consumo por Match AI + Event RNG (Criação §4.5).
- **Personality Vector implícito + toggle de tooltip** (Criação §12.2).
- **Fluxo de criação** de 7 passos (só o conteúdo do passo Appearance muda).
- **Toda a lógica de validação** exceto os campos de aparência (Criação §10).
- **Todo o doc de Ciclo de Vida**: fases 15→46, TrainGain/PhysDecay/InjuryMult, evolução de traços por LOD×Idade, lesões/condicionamento, fim de carreira (treinador / pool), snapshots.
- **Loja in-game** como escopo futuro (Criação §12.5).

---

## 10. O que testar — questões abertas da Versão B

1. **Câmera de partida agora é livre** (o 3D destrava isso). Side-on plano, diagonal estilo FIFA antigo, ou broadcast? Decisão que antes era refém do custo de arte e agora é puramente de design/jogabilidade.
2. **Blendshape vs variantes de malha** para `buildType` e feições — qual dá melhor custo/qualidade no low-poly.
3. **Quanto detalhe facial vale** à distância isométrica? A identidade vem de cabelo/silhueta/cor de kit/número + o retrato 64×64; provavelmente não vale esculpir rosto detalhado.
4. **Validar footprint** no hardware-alvo com N personagens 3D em cena simultaneamente (partida cheia = 22+ modelos).
5. **A decisão macro A vs B.** O fator decisivo não é técnico-estético abstrato — é **se você vai investir num pipeline de personagem 3D**. Se sim, a Versão B colapsa o pipeline duplo e destrava o morph de corpo. Se não houver capacidade de 3D, a Versão A (ou um iso-only 2D enxuto) é o caminho realista.
