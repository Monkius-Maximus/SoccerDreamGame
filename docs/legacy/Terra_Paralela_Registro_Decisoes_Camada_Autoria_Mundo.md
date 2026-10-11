# Terra Paralela — Registro de Decisões: Camada de Autoria de Mundo

> **Natureza deste documento.** Não é um companion doc novo — é um **registro de decisões** que fecha as pendências da camada de autoria de mundo e reconcilia os artefatos produzidos na sessão de pesquisa (geração procedural de clubes/jogadores) com o ledger canônico. Escrito assim deliberadamente para respeitar **D-08** (moratória de design: nenhum companion doc novo até o steering passar no harness).
>
> **Cross-ref:** *Geração de Identidade de Clube* · *Prestígio de Competições* · *Tactics → Behavior* · GDD §5 (LOD) · `docs/ARCHITECTURE.md`.
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo coberto:** identidade de clube, escudo, nomes, geração de jogadores, montagem de mundo. **Fora:** match engine, Tier 1.5, life-sim, arbitragem.

---

## 1. Reconciliação de trilha — a suspeita 2D/3D

A sessão de pesquisa produziu dois relatórios intitulados *"...for a 2D Soccer Life Simulator"*, e a triagem levantou suspeita de conflito com a trilha de renderização.

**Resolução: não há conflito. A trilha canônica é 2D (D-01), e os relatórios estão corretos.** O plano de migração 3D e o repositório `testingclaude` estão arquivados. O que importa registrar é que **nada nesta camada dependia da resposta**: identidade de clube, escudo, nomes e geração de jogadores são 100% `SoccerSim.Core` (engine-agnostic) ou ferramenta offline. A suspeita se dissolve sem custo — nenhum artefato precisa ser reescrito por causa dela.

**Consequência de higiene:** o título dos dois relatórios de pesquisa permanece válido; nenhuma edição necessária.

---

## 2. Supersessão formal — o pipeline ETL de datasets de jogos está **rejeitado**

Este é o item mais importante deste registro.

**O que a sessão de pesquisa propôs:** um pipeline de "data laundering" (prompt **P5**, `TerraParalela.Tools.DistributionEtl`) que ingere CSVs derivados de bases de FIFA/PES/FM, agrega atributos por célula `(posição × tier × nacionalidade × idade)` com média/desvio/percentis/matriz de correlação, aplica k-anonimato N≥30, e emite um `distributions.json` que o `PlayerGenerator` consome.

**Por que está rejeitado.** A abordagem de "derivar números de atributo a partir de bases de jogos comerciais" já foi avaliada e recusada em três frentes — legal (as bases derivam de scraping de dados proprietários; o k-anonimato despersonaliza indivíduos, mas não muda a proveniência do conjunto), metodológica (as escalas de FIFA/PES/FM são artefatos de balanceamento de *outros* jogos, não medidas do futebol real — herdá-las importa o balanceamento alheio junto) e arquitetural (cria uma segunda fonte de verdade para atributos, ao lado das âncoras).

**D-10 — O `DistributionEtl` (P5) não será implementado.** O prompt P5 do pacote de implementação fica marcado como **superado**, não como pendente.

**D-11 — Âncoras carregam apenas `archetype`, `qualityBand` e demografia factual.** Nenhum número de atributo entra por âncora. Os atributos são **gerados** a partir do arquétipo e da banda de qualidade, com os parâmetros de geração calibrados contra os alvos internos do projeto (teto de OVR, distribuição de traits) — não contra bases externas de outros jogos.

**O que sobrevive do P5.** A *forma* do contrato de dados continua útil, esvaziada da procedência problemática: o `PlayerGenerator` ainda consome um conjunto de perfis paramétricos por `(archetype × qualityBand × ageBand)`. A diferença é a origem dos números — **parâmetros de design calibrados**, não agregados de datasets alheios. O `DistributionProfile` sobrevive como *schema*; o ETL que o preenchia, não.

> **Débito de calibração conectado.** As dívidas já conhecidas (teto de OVR baixo demais — máx. ~79 no tier 1.00 contra alvo 86–88 para elencos de elite; limiares de trait altos demais em relação à população gerada — *Maestro* ~0,2%, *Bombardeiro* ~12,8% dos ST) são exatamente o tipo de ajuste que agora se resolve **nos parâmetros de geração**, sem apelar a base externa. Isso reforça a decisão: se o gerador precisa de correção, corrige-se o parâmetro, não se importa a escala de outro jogo.

---

## 3. Reancoragem do pacote P0–P8 no repositório real

O pacote de prompts de implementação foi escrito assumindo uma solução greenfield (`TerraParalela.Core` / `.Game` / `.Tools` / `.Tests`). **O repositório canônico já existe e usa outra nomenclatura.** Sem esta correção, o Claude Code criaria uma árvore paralela.

**D-12 — Mapa de reancoragem obrigatório.** Todo prompt do pacote, antes de ser disparado, tem os nomes substituídos por:

| No pacote P0–P8 | Repositório canônico |
| :--- | :--- |
| `TerraParalela.Core` | `src/SoccerSim.Core/` |
| `TerraParalela.Game` | `game/` (projeto Godot) |
| `TerraParalela.Tests` | `tests/SoccerSim.Core.Tests/` |
| `TerraParalela.Tools` | novo projeto de autoria, fora do build do jogo |
| Persistência SQLite ad-hoc (P2) | `src/SoccerSim.Infrastructure.Sqlite/` + `sql/` (migrations 0001–0005, 17 tabelas) |

**D-13 — P0, P1 e P2 estão majoritariamente satisfeitos e viram tarefas de *extensão*, não de criação.** O `SplitMix64`, o schema SQL e as suítes xUnit já existem. Emitir P0/P1/P2 como escritos duplicaria trabalho e violaria "mudanças cirúrgicas". A ação correta é auditar o que existe e escrever prompts de *delta*.

**D-14 — O pacote inteiro é reposicionado na sequência de implementação.** A sequência travada (**D-07**) é `SplitMix64 → física headless → steering mínimo → primeiro sprite`, e o Godot não é aberto antes do passo 4. A camera de autoria de mundo (`ClubIdentityGenerator`, `CrestGenerator`, `NameProvider`, `PlayerGenerator`, `WorldBuilder`) pertence à **janela de sprints de autoria**, depois da espinha jogável. Portanto:

- **P3 (`CrestGenerator`, parte B em Godot) não pode ser disparado agora** — viola D-07.
- A prioridade de implementação vigente permanece a migração `System.Random` → **SplitMix64**, com `Prompt_Implementacao_SplitMix64.md` já pronto para handoff.

---

## 4. Decisões de licença — travadas

Fecho o débito de "VERIFICAR" com uma regra de decisão em vez de uma lista de dúvidas.

**D-15 — Política de dependência: nenhuma biblioteca de terceiros entra no build do *jogo* nesta camada.** Só entram em ferramenta de autoria (offline) ou como asset. Justificativa: a camada é geradora de dado inerte; toda dependência em runtime é superfície de risco sem contrapartida.

Consequências concretas:

| Recurso | Papel | Decisão |
| :--- | :--- | :--- |
| **Bogus** | Enriquecer datasets de nomes | **Adotado — apenas em ferramenta de autoria.** O runtime lê só os JSONs próprios do projeto. Uma fonte de verdade. |
| **game-icons.net** (~4.170 ícones, CC BY 3.0) | Banco de `centralCharge` | **Adotado como asset**, com atribuição obrigatória em menu de créditos acessível. |
| **GodSVG** (MIT, GDScript) | Editor SVG feito em Godot | **Referência arquitetural apenas.** Não é dependência: é GDScript, e a camada é C#. |
| **`libimagequant`** | Quantização de cor | **Rejeitado** (GPLv3). Quantização será implementação própria (median-cut + Floyd–Steinberg são algoritmos livres). |
| Datasets de nomes de proveniência duvidosa | Base de nomes | **Rejeitados.** Só domínio público / licença explícita, com campo `source` registrado no próprio JSON. |
| Plugins de rasterização SVG em runtime | Escudo em runtime | **Não adotados na v1.** Escudo é composto por camadas e cacheado em PNG; rasterização vetorial em runtime é complexidade sem demanda. |

---

## 5. Resolução das decisões em aberto de *Geração de Identidade de Clube* §9

**D-16 — Política cromática: preservação confirmada como v1 definitiva.** A cor preserva a família cromática da âncora; o desvio se concentra em nome, escudo e símbolo. Encerra o item 1 do §9. Motivo: a cor é o vetor de reconhecimento que faz a paródia funcionar e é o eixo de menor exposição.

**D-17 — `stadium.capacity`: mecanismo travado, números não.** O gerador recebe a **banda de prestígio do clube** (B1–B6) e sorteia dentro da faixa daquela banda por Gaussiana truncada, stream próprio. As faixas numéricas permanecem **abertas** (dependem do modelo de dados de clube fechar) — "research before thresholds" se mantém. O que fica decidido é que `capacity` **nunca é escrito à mão** e **nunca é derivado em runtime**.

**D-18 — `AtmosphereArchetype`: interface definida, magnitude aberta.** Reafirmando **D-06** (nunca toca física), o arquétipo modula **variáveis de contexto de pressão** consumidas pela camada de decisão e pelo `RefereeProfile` — nunca rolagem/quique de bola, nunca atributo. A magnitude continua pendente de ancoragem empírica (vantagem de casa por liga). Sem número, sem implementação.

**D-19 — `DistrictArchetype` toca exclusivamente a camada de vida/carreira.** Não toca partida, não toca atributo, não toca finanças de clube. Isso mantém a separação de responsabilidades e impede que um enum de autoria vaze para a simulação esportiva. Qual variável de vida exatamente ele modula permanece aberto — mas o **domínio** está travado.

**D-20 — `displayCode`: unicidade por país, com desempate determinístico.** Escopo global seria colisão garantida em ~32 ligas e forçaria códigos artificiais. Regra: deriva das iniciais do `officialName`; em colisão dentro do mesmo país, substitui-se o último caractere por ordinal determinístico derivado do `clubId`. Sem fallback, sem laço aleatório.

**D-21 — `foundingYear` é coerente, não livre.** Sorteado dentro de uma janela histórica definida por país no template de mundo (a "era de fundação" daquela nação fictícia). Um clube fundado em 1878 num país cuja liga nasceu em 1950 quebra a ficção; o custo de evitar isso é um campo no template.

**D-22 — Rivalidade admite assimetria.** Cada aresta tem direção e intensidade próprias; rivalidade simétrica é simplesmente um par de arestas. Motivo: o clube pequeno que odeia o grande sem reciprocidade é um fato do futebol e uma fonte barata de narrativa. Assimetria é o caso geral; simetria é o caso particular.

---

## 6. Colisões de nomenclatura e schema — resolvidas

**D-23 — Colisão *Maestro*: o arquétipo é renomeado, o trait permanece.** `MaestroCM` → **`RegistaCM`**. O trait `Maestro` mantém o nome. Motivo: o trait é o conceito mais visível ao jogador e mais caro de renomear; o arquétipo é vocabulário interno de geração.

**D-24 — `CreatorST` e `CreatorAM` colapsam em `CreatorAdvanced`.** A posição já é um campo próprio da entidade; codificá-la também no id do arquétipo duplica a fonte de verdade. Um arquétipo, posição como parâmetro. Alinha com "one way" e com a regra de que estado derivado não se armazena como verdade.

**D-25 — Nenhum terceiro arquétipo de goleiro na v1.** Dois cobrem o espaço até que duplicação real prove o contrário (abstração diferida). Reabrir só com caso de uso concreto.

**D-26 — Goleiros não ganham schema paralelo de atributos.** O esquema de 12 atributos permanece único; a semântica de goleiro emerge da **tabela de pesos por posição**, não de um conjunto separado de campos. `EffectiveAttributes.Read()` continua sendo o único ponto de acesso. Um schema alternativo para GK criaria exatamente o caminho paralelo que o projeto proíbe.

**D-27 — Canonicalização de identificadores de atributo.** Um único enum `AttributeId` em `SoccerSim.Core`, identificadores em inglês, rótulos PT-BR vivendo apenas na camada de UI. Qualquer string de atributo fora desse enum é erro de compilação, não de runtime.

---

## 7. Escopo de mundo — travado em três estágios

**D-28 — Expansão em três estágios, com portão de benchmark entre eles:**

1. **Piloto (1 liga).** Análogo fictício do Brasileirão Série A, já eleito liga-piloto. Valida o pipeline inteiro ponta a ponta com custo mínimo.
2. **Núcleo (16 ligas).** Só após o piloto rodar limpo. Aqui se mede tempo de geração e footprint.
3. **Completo (32 ligas).** Só após o benchmark do núcleo. A meta de 32 é ambição validada, não premissa.

Isso encerra o "16 vs. 32" da triagem: **os dois, em ordem**. Gerar 32 ligas antes de validar uma é assumir risco sem informação.

**D-29 — Nações reais + clubes fictícios permanece o modelo.** Confirmado como definitivo para a v1. Preserva o reconhecimento das seleções (que a heurística de cobertura depende) e concentra toda a liberdade de paródia no nível do clube.

---

## 8. O que permanece deliberadamente em aberto

Registrado aqui para não virar decisão por omissão:

1. **Faixas numéricas de `capacity` por banda** — bloqueado pelo modelo de dados de clube (D-17).
2. **Magnitude do efeito de `AtmosphereArchetype`** — bloqueado por ancoragem empírica (D-18).
3. **Qual variável de vida o `DistrictArchetype` modula** — domínio travado, alvo não (D-19).
4. **Parâmetros de calibração do `PlayerGenerator`** (curva de idade, μ/σ de PA por banda, pesos por posição) — dependem de fechar as dívidas de teto de OVR e limiares de trait.
5. **Pesquisa 13 (editor de escudos in-game)** — o prompt existe na biblioteca, mas não rodou; e a implementação está atrás de D-07 de qualquer forma.
6. **Escopo do Tier 1.5** (só o botão "simular partida", ou também modo de playback assistível) — fora desta camada, mas pendente.

---

## 9. O que isto destrava

Com este registro anexado, a camada de autoria de mundo tem **decisão fechada em tudo que era decisão de design** — resta apenas o que legitimamente depende de dado empírico ou de outro sistema fechar primeiro. Os artefatos da sessão de pesquisa podem entrar no ledger sem gerar contradição, porque a única contradição real (o ETL do P5) está formalmente superada em vez de conviver silenciosamente com as âncoras.

**A prioridade de implementação não muda:** continua sendo `System.Random` → SplitMix64, seguida da física headless. Esta camada permanece na janela de sprints de autoria, e este documento existe para que, quando essa janela chegar, não haja nenhuma decisão de design pendente bloqueando o handoff.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-10 | `DistributionEtl` (P5) não será implementado — superado |
| D-11 | Âncoras carregam só arquétipo, banda de qualidade e demografia factual |
| D-12 | Mapa de reancoragem do pacote P0–P8 no repositório canônico |
| D-13 | P0/P1/P2 viram tarefas de extensão, não de criação |
| D-14 | Camada de autoria reposicionada atrás de D-07 |
| D-15 | Nenhuma dependência de terceiros no build do jogo nesta camada |
| D-16 | Preservação cromática confirmada como v1 definitiva |
| D-17 | `capacity` sorteado por banda; faixas numéricas abertas |
| D-18 | `AtmosphereArchetype` modula contexto de pressão; magnitude aberta |
| D-19 | `DistrictArchetype` toca exclusivamente vida/carreira |
| D-20 | `displayCode` único por país, desempate determinístico |
| D-21 | `foundingYear` dentro de janela histórica por país |
| D-22 | Rivalidade admite assimetria; simetria é par de arestas |
| D-23 | `MaestroCM` → `RegistaCM`; trait `Maestro` preservado |
| D-24 | `CreatorST` + `CreatorAM` → `CreatorAdvanced` |
| D-25 | Sem terceiro arquétipo de GK na v1 |
| D-26 | Sem schema paralelo de atributos para GK |
| D-27 | Enum único `AttributeId`; rótulos PT-BR só na UI |
| D-28 | Expansão de mundo em três estágios (1 → 16 → 32) com portão de benchmark |
| D-29 | Nações reais + clubes fictícios confirmado para a v1 |
