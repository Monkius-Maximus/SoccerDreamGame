> **⚠️ ARQUIVADO — superado pelo pivô 2D.** Este documento assume Opção B (3D em tempo real), incompatível com **D-01** (trilha canônica 2D, já travada). Mantido como registro histórico, sem edição retroativa (mesma disciplina do `testingclaude`: arquivo morto, não backup vivo). Ver `Terra_Paralela_Registro_Decisoes_Pivo2D_Personagem.md` para as decisões vigentes. Este doc também foi produzido em violação de **D-08** (moratória de companion docs novos) — motivo adicional para não tratá-lo como ativo.

# Terra Paralela — Criação de Personagem Modular (Aparência) — [ARQUIVADO]

> Companion doc do GDD. Cross-ref: **Tactics → Behavior** (o modelo consumido em campo, Tier 1) · **Geração de Identidade de Clube** (padrão irmão: dado gerado por regra + editável, mesma disciplina de determinismo) · **Gerador de Escudos** (precedente direto do pipeline `SubViewport` de retrato) · GDD §5 (LOD) · *Research Prompt — Pipeline de Personagem Modular* (pesquisa em andamento; parâmetros empíricos deste doc dependem do retorno).
> Convenção: prosa PT-BR, **identificadores em inglês**. Determinismo obrigatório (SplitMix64, stream isolado).
>
> **Escopo:** como a aparência de um personagem (jogador ou NPC) é representada, gerada, editada e renderizada — corpo, rosto, cabelo, pele. Não cobre kit/uniforme (`ClubIdentity.kits`, já especificado) nem física de partida.

---

## 1. Papel do sistema e fronteira

O criador de personagem **não é uma feature isolada de UI** — é uma de três portas de entrada para o mesmo dado. O mesmo `AppearanceProfile` que o jogador monta à mão na tela de criação é o que preenche milhares de NPCs no mundo. Consequência direta (*separation of concerns*): a lógica de geração, o catálogo de partes e o assembler não sabem se estão sendo chamados por um humano editando um slider ou pelo `WorldBuilder` populando uma liga inteira.

| | |
| :--- | :--- |
| **Entrada** | Uma de três: edição manual por slot (UI), seed (`AppearanceGenerator.Generate`), ou preset autorado (`Resource` copiado). |
| **Saída** | `AppearanceProfile` serializado, consumido por `AppearanceAssembler` (modelo 3D) e pelo pipeline de retrato (`SubViewport`). |
| **LOD** | Modelo 3D completo só instanciado em cena ativa — Tier 1 (partida do próprio clube, ~22 agentes) ou telas de edição/vida. Fora disso, o jogo consome só o profile serializado + retrato em cache (GDD §5). |

**Regra de fonte única.** Não existe segundo caminho de geração de aparência. O botão "aleatorizar" da tela de criação chama literalmente o mesmo gerador que povoa o mundo — uma correção de distribuição vale para os dois, sem sincronizar duas implementações.

---

## 2. Contrato de dados (`AppearanceProfile`)

```json
{
  "bodyType": "BodyBuildArchetype",
  "skinTone": "int (índice em paleta ou parâmetro contínuo — ver §9.1)",
  "face": {
    "headShape": "int (índice no PartCatalog, slot Head)",
    "faceStructureParam": "float (0.0–1.0, se blend shape contínuo — ver §9.1)"
  },
  "hair": {
    "hairStyleId": "int (índice no PartCatalog, slot Hair)",
    "hairColor": "HairColorArchetype",
    "facialHairId": "int (índice no PartCatalog, slot FacialHair; 0 = nenhuma)"
  },
  "eyeColor": "EyeColorArchetype"
}
```

**Notas de contrato:**

- `bodyType`, `skinTone`, `hairColor`, `eyeColor` são enums fechados (§4) na v1 — paleta curada, não slider RGB livre. Slider contínuo é uma evolução possível, não a v1 (ver §9.1, dependente da pesquisa).
- Kit **não** entra neste struct — vem de `ClubIdentity.kits` no momento da montagem (`kitContext` passado ao `AppearanceAssembler`, não persistido no profile do personagem).
- Altura/físico de gameplay (atributos 1–99) **não duplicam** aqui — `bodyType` é a representação visual; se blend-shape contínuo de físico for adotado (§9.1), ele é *derivado* dos atributos físicos existentes no momento da montagem, não armazenado duas vezes.
- `faceStructureParam` só existe se a pesquisa confirmar blend shapes contínuos como viável (Eixo B do research prompt); caso contrário, `headShape` sozinho basta (cabeças discretas).

---

## 3. Vocabulários canônicos (enums) — provisórios até a pesquisa retornar

Confirmados como *existência* na sessão de design; os *valores* de cada faixa (quantos tons de pele, quantas cores de cabelo) são conteúdo, crescem livremente sem tocar em código — mas a **técnica** por trás de cada enum (paleta fixa vs. shader paramétrico) é decisão pendente da pesquisa (§9.1).

### `BodyBuildArchetype` — variação de físico sobre o rig único
`Lean` (Magro) · `Athletic` (Atlético) · `Stocky` (Troncudo) · `Heavy` (Pesado)

> Buckets discretos por padrão nesta v1 — premissa §0.3 do research prompt (rig único compartilhado) exige validar se a variação é blend shape contínuo sobre o mesmo rig ou troca de malha de corpo por bucket. Pendente Eixo A/B.

### `HairColorArchetype`
`Black` · `DarkBrown` · `Brown` · `LightBrown` · `Blonde` · `Red` · `Grey` · `White`

### `EyeColorArchetype`
`Brown` · `Hazel` · `Green` · `Blue` · `Grey` · `Amber`

> Envelhecimento (`Grey`/`White` progressivo) é **derivado da idade em runtime**, não escolhido manualmente — ver §6. O enum existe para NPCs/jogadores já idosos na geração inicial do mundo.

> **Fronteira arquitetura × conteúdo** (mesmo princípio do `ClubIdentityGenerator`): os enums acima são arquitetura — cada valor tem consumidor mecânico (paleta do shader, seleção de malha). A lista de itens dentro de cada slot do `PartCatalog` (quantos penteados, quantos formatos de rosto) é conteúdo: cresce sem tocar em código.

---

## 4. `PartCatalog` — a galeria física

Resource registrando as partes disponíveis por slot, com IDs estáveis (compatibilidade de save através de patches de conteúdo).

| Slot | Conteúdo | MVP |
| :--- | :--- | :--- |
| `Head` | Formatos de cabeça/rosto discretos | 4–6 |
| `Hair` | Penteados | 6–10 |
| `FacialHair` | Barba/bigode (inclui "nenhuma") | 4–6 |
| `Body` | Malhas de físico, se `BodyBuildArchetype` for troca discreta (§3) | 4 (= nº de archetypes) |

**Validação fail-fast.** No boot, todo item do catálogo é checado contra o esqueleto compartilhado (mesmos bones esperados, mesma escala de rest pose — ver Eixo C/D do research prompt para o procedimento exato). Parte incompatível lança erro; não há fallback silencioso para uma parte "segura".

**Ordem de prioridade do MVP** (§0.9 do research prompt): rosto pesa mais no retrato de UI do que em campo (onde o jogador ocupa uma fração pequena da tela) — por isso `Head`/`Hair`/`FacialHair` vêm antes de refinar `Body` além dos 4 buckets iniciais.

---

## 5. `AppearanceAssembler` — a única função de montagem

`Assemble(profile: AppearanceProfile, kitContext: KitContext) → CharacterModel`

1. Instancia o esqueleto compartilhado.
2. Para cada slot, troca a malha (`MeshInstance3D.mesh`) pelo item indicado no profile, lido do `PartCatalog`.
3. Aplica cor via parâmetro de shader (pele, cabelo, olhos) — técnica exata (instance uniform vs. máscara de canal) pendente do Eixo C da pesquisa.
4. Aplica `kitContext` (cores/padrão do clube) sobre a malha de torso — dado que vem de `ClubIdentity`, não deste sistema.

Não existe segundo caminho de montagem. Jogador em campo, jogador no vestiário, e o NPC de fundo cujo retrato está sendo renderizado passam pela mesma função.

---

## 6. Envelhecimento visual — parâmetro derivado, não armazenado

Grisalho progressivo é função da idade do personagem no momento da montagem, não uma escolha manual re-editada a cada temporada:

```
effectiveHairColor = DeriveAgedColor(profile.hair.hairColor, age)
```

A curva exata (a partir de que idade o grisalho começa, quão rápido progride) é **decisão de design pendente de calibração** (§9.2) — a existência do mecanismo é premissa fixa (§0.7 do research prompt), a curva não.

---

## 7. Retrato — `SubViewport` + cache

Mesmo padrão já usado no `CrestGenerator`: câmera dedicada enquadrando o busto/rosto do `CharacterModel` montado, renderizada em `SubViewport`, resultado cacheado como PNG. Retrato é regenerado apenas quando o `AppearanceProfile` subjacente muda (edição manual) ou quando a idade cruza um limiar de recálculo de envelhecimento (§6) — nunca a cada frame, nunca a cada acesso de UI.

Este pipeline é o único consumidor de aparência fora da cena ativa — mundo de fundo (Tiers 2/3), telas de negociação, notícias, tudo lê o PNG cacheado, nunca instancia o modelo 3D (GDD §5).

---

## 8. Três portas de entrada — o mesmo profile

### 8.1 Montar
UI de carrossel por slot (avançar/voltar entre opções do `PartCatalog`), com preview ao vivo do `AppearanceModel` num `SubViewport` de edição. Cada mudança escreve direto no `AppearanceProfile` em edição.

### 8.2 Randomizar
Chama `AppearanceGenerator.Generate(seed, nationality)` — a **mesma** função usada para gerar NPCs do mundo. Distribuição condicionada por nacionalidade (estrutura pendente do Eixo F da pesquisa; mesmo padrão já usado no `NameProvider` por nacionalidade em outro sistema já especificado). Resultado é editável em seguida — randomizar não é definitivo, é ponto de partida.

### 8.3 Preset
Profiles autorados manualmente como `Resource` (não gerados). Selecionar um preset **copia** os valores para o profile em edição — nunca referencia o resource original. Editar depois de escolher um preset não altera o preset-fonte.

**Regra comum às três portas:** todas terminam no mesmo `AppearanceProfile` em edição; a tela de criação não sabe nem precisa saber qual porta populou o estado atual.

---

## 9. Decisões em aberto

1. **Blend shapes contínuos vs. partes discretas** — para rosto (`faceStructureParam`) e para físico (`BodyBuildArchetype` como bucket discreto vs. slider contínuo). Depende do Eixo B da pesquisa (viabilidade técnica + custo de produção). Nenhuma técnica de shader/mesh é fixada até o retorno.
2. **Curva de envelhecimento visual** (§6) — a partir de que idade, que taxa de progressão do grisalho. Decisão de design, mas informada pelo Eixo G da pesquisa (precedentes de FM/Sims).
3. **Estrutura da distribuição por nacionalidade** (§8.2) — granularidade (por país vs. por macro-região), formato de tabela de pesos. Depende do Eixo F da pesquisa; os pesos numéricos em si são calibração posterior, fora deste doc.
4. **Rig e pipeline de export exatos** (Blender → Godot) — bones, `SkeletonProfile`, procedimento de troca de mesh em runtime sem deformação. Depende dos Eixos A–D da pesquisa; este doc assume que a solução existe, não a especifica.
5. **Técnica de recoloração** (instance uniform vs. máscara de canal) — Eixo C da pesquisa, com implicação direta de performance para as 22 instâncias simultâneas do Tier 1.
6. **Tamanho final do catálogo MVP** por slot (quantos penteados, quantas cabeças) — decisão de conteúdo/produção, não de pesquisa; a tabela §4 lista pontos de partida, não valores travados.

---

## 10. Build path incremental

1. **Schema + enums + validador.** Escrever um `AppearanceProfile` à mão, montar via `AppearanceAssembler` num personagem, ver o resultado em cena. Prova o contrato — nenhum gerador ainda.
2. **`PartCatalog` mínimo** (2–3 opções por slot) + validação fail-fast no boot contra o esqueleto compartilhado.
3. **Retrato.** Ligar o `SubViewport` + cache PNG — o primeiro retrato saindo do dado, mesmo padrão do `CrestGenerator`.
4. **`AppearanceGenerator`** — geração por seed, ainda sem condicionamento por nacionalidade (uniforme). Prova o caminho "randomizar".
5. **Condicionamento por nacionalidade** — só depois que o Eixo F da pesquisa retornar com a estrutura de distribuição.
6. **Presets** — 2–3 profiles autorados manualmente, prova o caminho "preset" (copiar, não referenciar).
7. **Envelhecimento** — `DeriveAgedColor` ligado, curva provisória (ajustável depois de calibração).

Abstração (extrair um `PartSelectorWidget` genérico de UI, por exemplo) só depois que os três slots do passo 2 confirmarem o mesmo padrão — não antes.

---

## 11. O que isto destrava

Com o contrato de `AppearanceProfile` fechado, o `WorldBuilder` (já especificado em outro sistema, P6/P7 do pacote de implementação de geração de clubes/jogadores) ganha o campo de aparência que faltava para os NPCs, e a tela de criação de personagem do jogador deixa de ser uma feature isolada — é o mesmo pipeline com uma UI na frente. **A montagem real ainda depende do retorno da pesquisa de pipeline** (rig, export, técnica de recolor) antes que o primeiro modelo 3D possa ser instanciado de fato.
