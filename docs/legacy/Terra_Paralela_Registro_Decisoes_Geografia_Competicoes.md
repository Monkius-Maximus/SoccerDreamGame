# Terra Paralela — Registro de Decisões: Geografia e Escopo de Competições

> **Natureza deste documento.** Não é um companion doc novo — é um **registro de decisões** que fecha as pendências levantadas numa sessão de design (hierarquia geográfica, escopo de campeonato, LOD dinâmico, seeds de simulação) e as reconcilia com o ledger canônico. Escrito assim deliberadamente para respeitar **D-08** (moratória de design: nenhum companion doc novo até o steering passar no harness), no mesmo padrão do *Registro de Decisões — Camada de Autoria de Mundo*.
>
> **Cross-ref:** GDD §5 (LOD) · *Geração de Identidade de Clube v2* (`ClubIdentity.geography.countryId`) · *Tactics → Behavior* · *Registro de Decisões — Camada de Autoria de Mundo* (D-10…D-29) · *Registro de Decisões — Calibração e Autoria de Mundo* (D-38…D-52) · `Prompt_Implementacao_SplitMix64.md` · `tp_gazetteer.py`, `tp_world_data.py`, `tp_import.py`, `tp_geo_import.py`.
>
> **Numeração.** Continua a sequência D-01…D-37 (rodada 1 deste registro). A rodada 2 (§9 em diante) precisou pular para **D-53**, não D-38: o Registro de Calibração e Autoria já ocupou D-38…D-52 entre a rodada 1 deste documento e a rodada 2. **Ressalva de segurança, mantida:** se D-53+ já estiver ocupado no ledger real por uma sessão não vista aqui, renumerar na reconciliação.
>
> **Escopo coberto (rodada 1):** modelo de dados geográfico, escopo de competição, LOD dinâmico por distância competitiva, seeds de simulação. **Escopo coberto (rodada 2, §9-§13):** correção da árvore geográfica (separação geografia física × confederação de futebol), classificação de cidade (`CityProfile`), padrão Gazetteer/ActiveWorld, e o ajuste do pipeline de terceirização pra não deixar cada IA inventar geografia própria. **Fora:** algoritmo/pesos da distância competitiva (número, não arquitetura — §7), reescrita do GDD §5 em si, escopo do Tier 1.5 assistível.

---

> **Nota de rodada (2026-08-18, rodada 2).** Antes de continuar produzindo ligas com múltiplas IAs em paralelo, uma revisão externa (mesma disciplina de "segunda opinião" já usada no projeto) identificou um erro de modelagem real em D-30: a árvore geográfica usava `Confederation` como **pai direto** de `Country`, misturando geografia física com organização esportiva. É um erro que só dói depois — hoje só o Brasil está populado, é a janela mais barata para corrigir. §9 fecha isso (D-53) e as decisões que dependiam dela (§10-§13). D-30, D-31, D-32...D-37 da rodada 1 **permanecem válidas** — nenhuma foi reaberta, só D-30 foi corrigida na sua modelagem de nível "confederação" (o resto — `SubRegion`, `Country`, `Region`, `City`, contenção pura — estava certo e continua).

---

## 1. Papel do sistema e fronteira

Este registro fecha duas perguntas de arquitetura que ficaram soltas depois da sessão de brainstorm: **como representar a geografia do mundo** e **como um campeonato se relaciona com ela sem ficar preso a um único nível da árvore**. Nenhuma das duas toca o match engine, o steering ou a física — é camada de dados + orquestração de LOD, consumida por `SoccerSim.Core`.

| | |
| :--- | :--- |
| **Entrada** | Nada em runtime muda; o modelo é consumido pelo `WorldBuilder` (autoria) e pelo orquestrador de LOD (runtime, leitura apenas). |
| **Saída** | `GeoNode` (árvore, majoritariamente conteúdo real) + `Competition`/`CompetitionEdition` (dados de mundo). |
| **LOD** | É o próprio assunto deste documento — ver §6. |

---

## 2. `GeoNode` — árvore de contenção geográfica

**D-30 — A geografia do mundo espelha a estrutura real (países, sub-regiões, cidades) como árvore de contenção pura.** Isto estende **D-29** ("nações reais + clubes fictícios") do nível cidade/clube para o nível país/sub-região: a árvore inteira é dado factual, não gerado. Motivo: D-29 já assumiu o reconhecimento do mundo real como ativo de design; não faria sentido preservar países reais e inventar continentes fictícios por cima — seria uma segunda fonte de verdade geográfica sem necessidade.

> **Emenda (D-53, §9).** O `GeoNodeKind` original desta seção incluía `Confederation` como nível da árvore, entre `World` e `Country`. **Isso foi corrigido.** A tabela e o bloco `GeoNode` abaixo já refletem a versão corrigida (`Continent` no lugar de `Confederation`); o raciocínio completo da correção — por que estava errado e o que isso quebraria se não fosse pego agora — está em §9.

```
GeoNode {
  geoNodeId: string
  kind: GeoNodeKind        // enum fechado — arquitetura
  parentId: string | null  // null apenas na raiz (World)
  displayName: string      // conteúdo — cresce livremente
}
```

### `GeoNodeKind` (enum fechado, revisado por D-53)

`World` · `Continent` · `SubRegion` · `Country` · `Region` · `City`

**Fronteira arquitetura × conteúdo** (mesma disciplina de `Geração de Identidade de Clube v2` §4): os *valores* do enum são arquitetura — adicionar um nível de hierarquia exige código em qualquer sistema que percorra a árvore. As *instâncias* (quais países, quais sub-regiões) são conteúdo, carregadas de um dataset, sem tocar em código.

`SubRegion` é o nível que absorve os agrupamentos que não são unidade administrativa nem continente inteiro. Ele é **reusável em duas posições estruturalmente análogas** (§9.2): entre `Continent` e `Country` (agrupamentos multi-país, como o Golfo ou o Sudeste Asiático) e entre `Country` e `Region` (agrupamentos domésticos sem governo próprio, como as 5 macro-regiões do Brasil). As duas posições compartilham o mesmo significado semântico — "nó de agrupamento sem governo próprio" — por isso reaproveitam o mesmo valor de enum em vez de duplicar (`WorldSubRegion` vs `DomesticSubRegion` teria sido a alternativa, descartada por criar dois nomes pra uma ideia só).

**Ponto de integração já existente.** `ClubIdentity.geography.countryId` (contrato travado em *Geração de Identidade de Clube v2* §3) passa a referenciar `GeoNode.geoNodeId` onde `kind = Country`. Não é campo novo — é a amarração do campo que já existia à árvore que faltava.

---

## 3. `Competition` — escopo como conjunto, não como nível fixo

**D-31 — `Competition` carrega um `scope` e um `memberSet` gerado por predicado em tempo de autoria e congelado por edição.** Uma competição nunca é amarrada a um único nó da árvore; ela referencia um `anchorGeoNodeId` (o nó contra o qual o predicado é avaliado) e materializa a lista de membros **uma vez**, no rollover da temporada.

```
Competition {
  competitionId: string
  name: string
  scope: CompetitionScope   // enum fechado — §4
  anchorGeoNodeId: string
  memberPredicateId: string // referência à regra de autoria, não executa em runtime
}

CompetitionEdition {
  editionId: string
  competitionId: string
  season: string
  memberClubIds: string[]   // congelado — sem recálculo em runtime
}
```

**Por que congelado, não predicado vivo.** A alternativa (recalcular o `memberSet` a cada leitura) foi descartada: um campeonato regional real não perde nem ganha participante no meio da edição porque metadado geográfico de um clube mudou. Congelar no rollover é o comportamento correto **e** o caminho mais simples — zero recomputação em runtime, replay determinístico, sem casos de borda de "o clube saiu da região no meio do campeonato". *One way*: o predicado roda uma vez, gera o snapshot, o snapshot é a fonte de verdade daquela edição.

**Nota (D-53, §9.3).** `memberPredicateId` já era, desde a rodada 1, avaliado por **predicado sobre propriedades**, não por "todos os filhos deste nó da árvore" — por isso uma competição que reúne clubes através de duas confederações (ex.: uma copa que junta países árabes de CAF e AFC) já era representável sem forçar um nó de árvore artificial cruzando confederações. Isto não mudou com D-53; só ficou mais claro por que não precisava mudar.

---

## 4. `CompetitionScope` — taxonomia de 5 níveis

Enum fechado, validado contra os exemplos da sessão anterior (Copa do Nordeste, Copa do Golfo, ligas nacionais):

| Valor | Exemplo real | `anchorGeoNodeId.kind` típico |
| :--- | :--- | :--- |
| `SubNational` | Copa do Nordeste | `Region` ou `SubRegion` |
| `National` | Liga Brasileira | `Country` |
| `SubContinentalZonal` | Copa do Golfo | `SubRegion` |
| `Continental` | Libertadores, Champions | `Country` (avaliado por `memberPredicateId` = confederação) |
| `Intercontinental` | Mundial de Clubes | `World` |

Fail-fast: `scope` fora deste conjunto é erro de compilação, não de runtime — mesma disciplina de todo enum fechado do projeto (ex.: `AttributeId`, D-27).

**Nota (D-53).** A coluna `Continental` mudou de `Confederation` pra `Country` porque `Confederation` deixou de ser `GeoNodeKind`. O `anchorGeoNodeId` de uma competição continental (Libertadores) agora aponta pra qualquer `Country` de referência dentro da confederação (ou, mais simples, o predicado filtra direto por `footballConfederation`, sem precisar de um nó-âncora geográfico no sentido estrito) — o predicado, não a árvore, é quem sabe o que "continental" significa em futebol.

---

## 5. LOD dinâmico — distância competitiva substitui Tier estático

**D-32 — A atribuição de Tier por entidade é automática, computada por um único caminho (distância competitiva relativa ao clube-âncora do jogador), não escolha manual do jogador.** A alternativa de deixar o jogador selecionar manualmente quais ligas rodar em detalhe (padrão Football Manager) foi descartada por violar *one way* — introduziria um segundo eixo de configuração competindo com o mecanismo automático, sem ganho claro de design para este projeto.

**Distinção importante que não deve ser confundida:** isto é ortogonal a **D-28** (expansão do mundo em três estágios — 1 → 16 → 32 ligas). D-28 decide **quantas** entidades existem no mundo simulável; esta decisão define **qual Tier** cada entidade recebe dado que existe. Um resolve footprint total, o outro resolve profundidade de simulação por entidade — dois eixos, sem sobreposição.

**A distância não é geográfica, é competitiva.** Um adversário de Libertadores em outro continente está mais "perto" (Tier mais alto) do que um clube da quarta divisão da mesma cidade do jogador, que nunca vai enfrentar. A distância é medida sobre o grafo de `Competition`/`CompetitionEdition` que conecta o clube do jogador aos demais, não sobre `GeoNode`.

**D-33 — Granularidade da promoção dinâmica de Tier é por partida (fixture), não por torneio nem por temporada.** Motivo: promover um torneio ou temporada inteira gastaria ciclo de simulação em partidas que o jogador nunca vai ver — exatamente o desperdício que o LOD existe para evitar. Uma entidade é promovida no momento em que uma partida específica dela entra na janela de resolução do jogador (a partida de Libertadores contra o adversário estrangeiro), e retorna ao Tier de base assim que a partida é resolvida.

### 5.1 Emenda declarada ao GDD §5

O GDD §5 (LOD) descreve os três tiers como bandas estáticas ("Active Human League" / "Major Foreign Leagues" / "Minor Leagues"). Esta decisão **não invalida os três tiers** — a divisão High/Statistical/Database Simulation continua correta. O que muda é o **mecanismo de atribuição**: de rótulo fixo por liga para cálculo dinâmico por distância competitiva, com granularidade de partida. **A reescrita do texto do GDD §5 fica sinalizada, não executada aqui** — mesmo tratamento que o GDD §1 recebeu no plano de migração 3D (flagado, reescrito depois, em documento próprio).

---

## 6. Seeds — dois conceitos distintos, mesma disciplina

**D-35 — "Seed de geração" e "seed de simulação" são conceitos distintos no vocabulário do projeto, ambos sob a disciplina SplitMix64/`StreamName`, mas com formas diferentes.**

| | Seed de geração (já em uso) | Seed de simulação (nova) |
| :--- | :--- | :--- |
| Natureza | Estática — determina um artefato uma vez | Fluxo indexado no tempo |
| Exemplo já existente | `seed = hash(masterSeed, "club_identity", clubId)` | — |
| Consumidor | `ClubIdentityGenerator`, `CrestGenerator`, geração de nomes | Resolução de partidas Tier 2/3 |

**D-36 — Fórmula de derivação da seed de simulação:** `seed = hash(masterSeed, "world_sim", entityId, season, round)`, seguindo exatamente o padrão já estabelecido para `club_identity`. Isolada em stream próprio — não compartilha fluxo com física de partida (Tier 1), geração de jogadores, nem geração de identidade de clube. **A composição exata da chave (formato, anti-colisão) não é reespecificada aqui** — é responsabilidade do `Prompt_Implementacao_SplitMix64.md`, que já define o `StreamName` enum; este documento apenas declara que `world_sim` precisa entrar como uma categoria nova nesse enum quando o prompt for revisitado.

**D-34 — Armazenamento híbrido: Tier 1 grava estado; Tier 2/3 derivam da seed e gravam apenas deltas esparsos.** Rejeitado o extremo "tudo derivado da seed" (todo acesso de leitura viraria recálculo, complexidade sem ganho onde o estado já é pequeno) e o extremo "tudo gravado" (padrão Football Manager — save pesado, sem necessidade aqui). O ponto ótimo: o clube do jogador é pequeno e mutado o tempo todo (vale gravar); o resto do mundo é grande e raramente tocado (vale derivar).

**D-37 — Ações do jogador são deltas que sobrepõem a projeção determinística da seed; a seed nunca é mutada.** A seed mestre produz uma linha de tempo "não perturbada" para o fundo do mundo. Quando uma ação do jogador afeta uma entidade de Tier 2/3 (ex.: um jogador emprestado para um clube estrangeiro), o resultado é gravado como delta esparso *sobre* aquele nó — a derivação da seed para os demais nós permanece intocada e replayável.

---

## 7. O que fica deliberadamente em aberto (rodada 1)

Registrado para não virar decisão por omissão — mesma disciplina do §8 do *Registro de Decisões — Camada de Autoria de Mundo*:

1. **Função e pesos da distância competitiva** — arquitetura travada (grafo de competições, não geografia), mas a fórmula exata e os limiares são número, não design. "Research before thresholds": não entram aqui sem dado.
2. **Reescrita do texto do GDD §5** — sinalizada em §5.1, não executada.
3. **Escopo do Tier 1.5 assistível** — fora deste documento; pertence à linha do *Structured Match Resolver* já em pesquisa (`Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md`), não à geografia.
4. **Formato exato da chave de hashing e proteção anti-colisão do stream `world_sim`** — deferido ao `Prompt_Implementacao_SplitMix64.md`, para não duplicar a fonte de verdade.
5. **Se `GeoNode` vira tabela SQL nova ou se aproveita alguma das 17 tabelas já existentes** — precisa de auditoria antes de propor migration nova (mesma disciplina de D-13: auditar o que existe antes de criar).

---

## 8. O que isto destrava (rodada 1)

Com `GeoNode`, `Competition`/`CompetitionEdition` e a distinção de seeds fechados como arquitetura, o `WorldBuilder` ganha o esqueleto que faltava para registrar campeonatos de qualquer escopo sem gambiarra — e o LOD deixa de ser rótulo estático para virar cálculo, o que a Copa do Golfo e a Copa do Nordeste exercitam como casos de teste naturais (escopos `SubContinentalZonal` e `SubNational` sobre a mesma árvore).

**A prioridade de implementação não muda.** Este módulo, como a camada de autoria de clubes, pertence à janela de sprints de autoria — atrás de **D-07** (`SplitMix64 → física headless → steering mínimo → primeiro sprite`). A sequência ativa continua sendo a migração `System.Random` → SplitMix64.

---

## 9. D-53 — Geografia física e confederação de futebol são árvores separadas

### 9.1 O problema

D-30 (§2) modelava `Confederation` como pai direto de `Country` na árvore de `GeoNode`. Isso mistura duas coisas que não têm a mesma natureza: geografia é fato físico estável; confederação é organização esportiva, com histórico de mudanças por decisão administrativa.

**Casos reais que quebram a premissa "confederação = pai geográfico"** (verificados por pesquisa dirigida, 2026-08-18 — fontes: Wikipedia "List of FIFA member associations", páginas de cada confederação, CAFOnline.com):

| País | Continente físico | Confederação de futebol | Motivo da divergência |
| :--- | :--- | :--- | :--- |
| Israel | Ásia | UEFA | Excluído de facto da AFC em 1974 (boicote de membros árabes); nômade sem confederação nos anos 80-90; admitido na UEFA em 1994. |
| Cazaquistão | Ásia | UEFA | Saiu da AFC, entrou na UEFA em 2002, por vínculos competitivos/econômicos. |
| Austrália | Oceania | AFC | Saiu da OFC, entrou na AFC em 2006, buscando competição mais forte. |
| Guiana, Suriname | América do Sul | CONCACAF | Zona Caribenha da CONCACAF, por histórico colonial. |
| Guiana Francesa | América do Sul | CONCACAF | Território francês; assento FIFA é da França, joga na zona caribenha. |
| Turquia, Chipre, Armênia, Geórgia, Azerbaijão | Ásia | UEFA | Ásia Ocidental/Cáucaso pelo geoscheme da ONU; UEFA por decisão histórica/institucional. |
| Guam, Ilhas Marianas do Norte | Oceania (Micronésia) | AFC | Território geograficamente oceânico, joga na zona da Ásia Oriental da AFC. |

**Caso-limite documentado, não corrigido:** a Rússia é classificada aqui como `Europe` (convenção estatística/política padrão, ONU M49), embora ~75-77% do território (Sibéria, Extremo Oriente) seja fisicamente Ásia. Isto é **ambíguo por convenção**, não um mismatch limpo como os demais — registrado explicitamente para não ser "descoberto" depois como inconsistência de dado.

**Por que isto importa além de estar "mais certo":** o projeto já tem sistemas que vão precisar de geografia física real e independente de futebol — viagem/distância (já mencionado em D-42 §5.1, coeficiente de gol por km percorrido), clima, fuso horário, e o próprio `WorldSubRegion`/agrupamento cultural que a produção de conteúdo vai usar. Se `Country.parent = Confederation`, qualquer sistema desses herdaria a distorção (a Austrália "geograficamente ao lado" do Japão porque os dois são AFC) sem ninguém decidir isso de propósito.

### 9.2 A decisão

**D-53 — A árvore de `GeoNode` é geografia física pura: `World → Continent → SubRegion (opcional) → Country → Region → City`. `Confederation` sai do `GeoNodeKind`. `Country` carrega a confederação de futebol como campo escalar (`footballConfederation`, um dos 6 valores fechados: `AFC`/`CAF`/`CONCACAF`/`CONMEBOL`/`OFC`/`UEFA`), nunca como nó pai.**

```
Country.footballConfederation: FootballConfederation   // enum fechado, 6 valores
                                                          // NÃO é GeoNodeKind, NÃO é pai
```

Justificativa: exatamente o padrão que `Club.prestigeBand` já usa desde D-38 (Registro de Calibração) — um fato que **descreve** a entidade sem ser posição na árvore de contenção. Duas árvores que compartilham um nó (`Country`) através de um campo escalar, em vez de uma árvore só fingindo caber duas semânticas.

**Consequência que não estava prevista.** `CompetitionScope.Continental` (§4) não pode mais apontar `anchorGeoNodeId` para um nó `Confederation` — porque esse nível não existe mais. O predicado de uma competição continental passa a filtrar por `footballConfederation` diretamente (ex.: `país.footballConfederation == "CONMEBOL"`), não por posição na árvore. Isto não é perda de expressividade: já era assim, na prática, para o caso de competições que cruzam confederações (§3, nota) — agora fica assim pra todas, de forma consistente.

**Migração do conteúdo já autorado.** O Brasil (único país com subárvore hoje) teve `geo_conmebol` removido; `geo_bra` passou a filho direto de um nó `Continent` (`geo_continent_southamerica`), com `footballConfederation = "CONMEBOL"` como campo escalar. Os `geoCityId` das 11 cidades e dos 20 clubes **não mudaram** — só os ancestrais entre `Country` e `City` foram reorganizados (ver §11).

### 9.3 Sobre competições que cruzam confederações

A leitura externa que motivou esta correção também apontou um caso real (UAFA — União das Associações de Futebol Árabes, que atravessa CAF e AFC) como argumento a favor de não modelar confederação como nó de árvore rígido. Vale registrar: isso **já não era um problema** mesmo antes de D-53, porque `Competition.memberSet` (D-31, §3) já era resolvido por `memberPredicateId` — uma função sobre propriedades dos clubes/países, não uma travessia de subárvore. Uma "Copa Afro-Árabe" sempre pôde ser modelada como um predicado tipo "país fala árabe E está em CAF ou AFC", sem precisar de um nó geográfico artificial cruzando confederações. D-53 não resolve um problema novo aqui — só remove a tentação de tentar resolvê-lo *errado* (criando um `SubRegion` cruzando confederações, o que teria violado a árvore de contenção de pai único).

---

## 10. D-54 — `CityProfile`: classifica a cidade, não confunde com `DistrictArchetype`

### 10.1 O problema

`DistrictArchetype` (`Geração de Identidade de Clube v2` §4: `WorkingClass`/`Docklands`/`HistoricCenter`/`Affluent`/`University`/`Outskirts`/`Coastal`) descreve o **bairro/entorno de um estádio** — é isso que o campo sempre modelou. Sem uma classificação própria pra cidade inteira, a tentação natural é usar `DistrictArchetype` na cidade (`"Recife é Coastal"`), o que é categoricamente errado: uma cidade grande tem vários desses simultaneamente (o centro histórico, o bairro portuário, a zona nobre e a orla convivem na mesma cidade). O erro só aparece quando alguém tenta usar o campo pra algo cidade-inteira (agrupar cidades por tamanho, decidir onde uma competição regional funciona) e descobre que ele não serve.

### 10.2 A decisão

**D-54 — Toda `City` do Gazetteer carrega um `CityProfile`, eixo ortogonal a `DistrictArchetype`:**

```
CityProfile {
  settlementTier:       SettlementTier        // enum ordinal, 6 degraus
  administrativeRole:   AdministrativeRole[]  // NationalCapital · StateCapital · RegionalHub · None
  economicRoles:        EconomicRole[]        // Port · Industrial · Tech · Tourism · Agriculture ·
                                               // Finance · Mining · Services · University
  geographicTraits:     GeographicTrait[]     // Coastal · Riverine · Mountain · Highland · Desert ·
                                               // Island · Border  (traço do SÍTIO da cidade)
  gameplayImportance:   GameplayImportance    // Major · Minor · Background
}
```

**Desvio deliberado da proposta original de duas colunas (`settlementClass` + `populationBand` separados).** Fundidos num único `settlementTier` ordinal (`Megacity` > `Metropolis` > `LargeCity` > `MidCity` > `SmallCity` > `Town`, cada um com faixa populacional aproximada só como guia descritivo, não campo autorado à parte). Motivo: as duas variáveis sempre covariam na prática — nenhuma fórmula do projeto hoje consome população em número absoluto (a mesma razão pela qual D-39, no Registro de Calibração, já rejeitou capacidade de estádio amarrada a população/banda) — e manter as duas como campos independentes cria exatamente o risco que o resto do projeto evita em todo lugar: duas fontes de verdade pra uma variável que podem discordar (uma cidade `"Town"` com `populationBand = "P5"` seria inconsistência silenciosa até alguém notar).

`geographicTraits` é deliberadamente um vocabulário **fechado e menor** que o de `DistrictArchetype`, e cobre o **sítio físico** da cidade (é litorânea? é de altitude?), não o caráter social/econômico de uma parte dela — essa distinção é o que evita o erro que motivou esta decisão.

### 10.3 Aplicado no Brasil (caso de teste)

As 11 cidades já em uso no lote-piloto receberam `CityProfile` (Rio de Janeiro e São Paulo como `Megacity`; Belo Horizonte, Porto Alegre, Curitiba, Salvador e Belém como `Metropolis`; Santos e Bragança Paulista como `MidCity`; Mirassol e Chapecó como `SmallCity`/`MidCity` conforme porte real). Nenhuma cidade nova foi cadastrada nesta rodada — só a classificação das que já existiam, exatamente o "backbone antes de escalar" que motivou parar pra fazer isto agora.

---

## 11. D-55 — `Gazetteer` (catálogo) e `ActiveWorld` (materialização) são conceitos distintos

### 11.1 O problema

A geografia do jogo precisa responder duas perguntas diferentes: "que cidades existem no mundo, mesmo que o jogo nunca as simule?" e "quais dessas estão de fato materializadas — com bairros, elencos, mercado local — nesta carreira específica?" Tratar as duas como a mesma coisa força uma escolha ruim: ou o Gazetteer cadastra só o que já vai ser jogado (não escala — cada expansão do mundo vira trabalho de autoria do zero) ou o jogo tenta simular tudo que está cadastrado (o extremo que o LOD inteiro do projeto, D-32/D-33/D-34, já existe pra evitar).

### 11.2 A decisão

**D-55 — `Gazetteer` é o catálogo de autoria (que cidades existem, com que `CityProfile`) — conteúdo estável, igual ao resto do `GeoNode`. `ActiveWorld` é estado de save-game (quais cidades do Gazetteer estão materializadas nesta carreira: bairros gerados, instalações, elenco, mercado local), com um estado por cidade em `{ACTIVE, DORMANT}`.**

```
ActiveWorldEntry {
  cityId: string            // referencia GeoNode kind=City do Gazetteer
  status: ActivationStatus  // ACTIVE | DORMANT
  activatedAtSeason: string | null
}
```

Ativar uma cidade `DORMANT` (`ActivateCity()`) é o gatilho que gera bairros, instalações, clubes/amadores locais, NPCs e contexto econômico daquela cidade — sob demanda, não antecipado.

**Por que isto não é decisão nova de arquitetura, é reaproveitamento.** O padrão `ACTIVE`/`DORMANT` por entidade, com materialização sob demanda e o resto derivado/adiado, é a mesma disciplina de **D-34** (armazenamento híbrido: Tier 1 grava estado, Tier 2/3 derivam da seed) aplicada a um eixo diferente (geografia, não profundidade de simulação de partida). Não é mecanismo novo — é o mesmo mecanismo, um nível acima.

**Isto também resolve, sem mecanismo extra, a pergunta de produto "o jogador pode adicionar cidades a um país?"** — não adiciona cidade nenhuma ao mundo (isso seria fabricar geografia, na contramão de D-29/D-30). O jogador (ou uma expansão de mundo autorada) **ativa** uma cidade que já existe no Gazetteer e estava dormente. O mapa mundial pode "conhecer" milhares de cidades sem o jogo simular milhares de cidades ao mesmo tempo — exatamente o texto do GDD §5 (LOD) aplicado à geografia.

**O que esta decisão NÃO faz.** Não implementa `ActiveWorld` em código — é mecânica de runtime/save-state, e como qualquer coisa nessa camada, está atrás de **D-07** (mesma janela de prioridade que `CapProfile`, D-50 do Registro de Calibração, já recebeu). O que trava aqui é o esquema e o padrão, pra não ser descoberto/reinventado quando a implementação chegar.

---

## 12. D-56 e D-57 — o pipeline de terceirização para de deixar cada IA inventar geografia

### 12.1 O problema

O kit de terceirização de clubes (`tp_import.py`, `PROMPT_A_CLUBES.md`) pedia que o modelo externo devolvesse `geoNodes` **junto** com os clubes — ou seja, cada IA terceirizada inventava sua própria árvore geográfica (IDs, nomes, estrutura) para a liga que estava produzindo. Rodando isso com Gemini pra Inglaterra, outro Gemini pra Argentina e um terceiro pra Japão, nada garante que os três produzam o mesmo `countryId` pro mesmo país, ou a mesma convenção de nome de cidade — três fontes de verdade divergentes sobre o mesmo mundo, descobertas só quando alguém tentar juntar os lotes.

### 12.2 A decisão

**D-56 — O lote de clubes NÃO declara geografia. Ele só *seleciona* um `geoCityId` de um Gazetteer já publicado, recebido como entrada do prompt. Enviar `geoNodes` no payload reprova o lote inteiro.**

Mudança concreta em `tp_import.py`: `validate_clubs()` passa a receber `geo_nodes` como parâmetro explícito (vindo do Gazetteer publicado — hoje `tp_world_data.GEO_NODES`), não mais do próprio payload. `prompt_clubes()` lista as cidades disponíveis (`geoCityId` + nome) e instrui o modelo a **parar e sinalizar a lacuna**, não inventar, se a cidade que precisa não estiver na lista. A checagem de colisão de âncora na mesma cidade (condição 2 do seletor de `NamingRule`, §6.1 da *Geração de Identidade v2*) passa a comparar por `geoCityId`, não pelo texto livre `anchorCity` — elimina um bug latente (duas grafias da mesma cidade escondendo uma colisão real, ou duas cidades homônimas de estados diferentes gerando uma colisão falsa).

**D-57 — A produção de geografia (estados/cidades de um país) é um kit de terceirização PRÓPRIO, separado do kit de clubes: `tp_geo_import.py`.** Um país já precisa existir no Gazetteer (`tp_gazetteer.py` — continente e confederação já resolvidos, D-53) antes de este kit rodar; ele só preenche a subárvore `Region`/`City` (com `CityProfile`, D-54) abaixo do país. A saída (`GEO_NODES`, `CITY_PROFILES`, `COUNTRY_FOOTBALL_CONFEDERATION` de um país) é o que o kit de clubes (D-56) consome como `availableGeoNodes` depois.

```
        WORLD GAZETTEER (tp_gazetteer.py — continentes, países, confederação)
                   │
        ┌──────────┴──────────┐
        ↓                     ↓
  tp_geo_import.py       tp_geo_import.py
  (geografia: Inglaterra)  (geografia: Argentina)
        ↓                     ↓
  tp_import.py            tp_import.py
  (clubes: só SELECIONA    (clubes: só SELECIONA
   geoCityId já existente)  geoCityId já existente)
```

Isso é a mesma separação de responsabilidade que o Registro de Calibração já aplicou em outro lugar (schema/prompt/validador vêm de uma fonte única, campos derivados são proibidos no JSON de entrada) — agora estendida de "um clube não inventa capacidade de estádio" para "um lote de clubes não inventa geografia".

**Consequência que não estava prevista.** Isto inverte a ordem de trabalho que fazia sentido quando só existia uma liga: antes, geografia e clube nasciam juntos, no mesmo lote. Agora, **geografia precisa vir primeiro**, país por país, antes que o kit de clubes daquele país possa rodar. Para o Brasil isso já está resolvido (Gazetteer populado, §9.2/§10.3). Para qualquer país novo, a sequência correta é: `tp_geo_import.py --prompt <país>` → validar → emitir o módulo de geografia daquele país → só então `tp_import.py --geo <módulo> --prompt clubes`.

### 12.3 Backbone mundial já cadastrado nesta rodada

Population do nível `Continent` (6, fixo) e `Country` (218 linhas — 211 membros FIFA + 7 territórios afiliados a confederação sem assento FIFA próprio) com `footballConfederation` e continente físico verificados por pesquisa dirigida — ver `tp_gazetteer.py`, módulo `COUNTRIES`. **`SubRegion`, `Region` e `City` NÃO foram pré-cadastrados pro mundo inteiro** — ficam sob demanda, país por país, via `tp_geo_import.py` (D-57), a mesma disciplina de "não simular o planeta inteiro agora" que já valia pra bairros (`DistrictArchetype`) desde a decisão original. Cadastrar as ~200 subárvores nacionais inteiras antes de qualquer liga nova ser produzida seria trabalho especulativo — o kit existe exatamente pra que cada país entre sob demanda, na hora que uma liga dele for de fato produzida.

---

## 13. O que isto destrava (rodada 2)

Com D-53–D-57, a produção paralela com múltiplas IAs deixa de correr o risco de cada uma inventar seu próprio planeta: geografia é produzida uma vez por país (kit próprio, D-57), publicada, e o kit de clubes (D-56) só referencia. O `CityProfile` (D-54) dá à cidade uma classificação que faltava sem sobrecarregar `DistrictArchetype`. O padrão `Gazetteer`/`ActiveWorld` (D-55) resolve de antemão a pergunta de expansão de mundo/"adicionar cidade" sem exigir implementação agora.

**A prioridade de implementação não muda.** `ActiveWorld` (runtime) continua atrás de **D-07**, mesma janela que todo o resto desta camada. O que muda é que a produção de **conteúdo de autoria** (Brasil já feito; próximos países) pode prosseguir com um pipeline que não acumula dívida de consistência geográfica — era exatamente o custo que ficaria mais caro de corrigir depois de dezenas de ligas produzidas, e a motivação de fazer isto agora em vez de na segunda liga.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | Geografia do mundo espelha a estrutura real (país/sub-região/cidade), árvore de contenção pura — corrigida por D-53 (confederação sai da árvore) |
| D-31 | `Competition.memberSet` derivado por predicado em autoria, congelado por edição — sem recálculo em runtime |
| D-32 | Tier é automático via distância competitiva; jogador não escolhe manualmente. Ortogonal a D-28 |
| D-33 | Promoção dinâmica de Tier tem granularidade por partida (fixture) |
| D-34 | Armazenamento híbrido: Tier 1 grava estado; Tier 2/3 deriva de seed + deltas esparsos |
| D-35 | Seed de geração (estática) e seed de simulação (fluxo temporal) são conceitos distintos, mesma disciplina |
| D-36 | `seed = hash(masterSeed, "world_sim", entityId, season, round)`, padrão de `club_identity` |
| D-37 | Deltas do jogador sobrepõem a projeção da seed; a seed nunca é mutada |
| D-53 | Geografia física (`Continent`) e confederação de futebol (`Country.footballConfederation`) são árvores/eixos separados — corrige D-30 |
| D-54 | `CityProfile` classifica a cidade (settlementTier/administrativeRole/economicRoles/geographicTraits/gameplayImportance), ortogonal a `DistrictArchetype` (bairro) |
| D-55 | `Gazetteer` (catálogo de autoria) e `ActiveWorld` (materialização por save, ACTIVE/DORMANT) são conceitos distintos — reaproveita o padrão de D-34 |
| D-56 | Kit de clubes não declara `geoNodes` — só seleciona `geoCityId` de um Gazetteer publicado; colisão de âncora passa a checar por `geoCityId` |
| D-57 | Geografia de um país é kit de terceirização próprio (`tp_geo_import.py`), roda antes do kit de clubes daquele país |
