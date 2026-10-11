# Research Prompt — Taxonomia de Situações e Estado Tático Relacional

> **Sistema:** Structured Match Resolver (Tier 1.5) — arquitetura situação-primeiro.
> **Papel deste documento:** prompt de pesquisa dirigida. Os resultados alimentam o design doc companion *Resolução Estruturada de Partidas*. Nenhum design doc nem prompt de implementação será escrito antes desta pesquisa retornar.
> **Cross-ref:** GDD §5 (LOD) · *Match Engine* · *Tactics → Behavior* · *Orquestração de Simulação* · *Sistema de Arbitragem*.
> Convenção do projeto: prosa em PT-BR, identificadores em inglês.

---

## 0. Contexto travado — decisões já tomadas (NÃO reabrir)

Trate tudo nesta seção como premissa fixa. A pesquisa existe para **preencher** esta arquitetura com taxonomia, representação e dados empíricos — não para questioná-la, "melhorá-la" ou propor alternativas a ela. Se algum achado parecer contradizer uma premissa, registre o achado e a tensão, mas não redesenhe.

1. **O jogo.** *Terra Paralela*: simulador de vida e carreira futebolística (Godot 4 + C#), mundo paralelo com nações reais e clubes/jogadores inteiramente fictícios.
2. **Disciplina de LOD.** Tier 1 = partida jogada/assistida pelo jogador (engine completo: física + IA tick-a-tick, steering + utility AI). Tier 1.5 = partida do próprio clube **simulada** (o resolvedor estruturado objeto desta pesquisa). Tiers 2/3 = mundo de fundo (resolução estatística por duplo Poisson). Um caminho por contexto, **sem fallbacks entre tiers**.
3. **Átomo do resolvedor = situação tática.** A partida no Tier 1.5 é modelada como sequência de situações, não como sequência de duelos de atributo. O duelo individual (`drible vs desarme` etc.) existe, mas **subordinado à camada de execução** de uma situação já formada.
4. **Pipeline fixa.** `estado global da partida → estado tático → situação criada → decisão do jogador → execução da ação → novo estado`, com relógio de partida (posses/momentos finitos ao longo de 90 min; critério de parada explícito).
5. **Situações são relacionais.** Espaço, superioridade, compactação e afins são propriedades da **interação entre as duas configurações táticas**, nunca de um time isolado. O gerador de situações recebe `(TeamTactics A, TeamTactics B, estado da partida)` como entrada conjunta.
6. **Fronteira de abstração (não-negociável).** O passo `estado tático → situação` é uma **amostragem condicionada** — a situação é sorteada de uma distribuição parametrizada pelos dois estados táticos. **Nunca** simulação de movimento de agentes até a situação se formar. Custo O(situações por partida), jamais O(agentes × ticks).
7. **Tabuleiro como substrato.** A metáfora do tabuleiro é a representação espacial do estado tático (linhas, zonas ocupadas, superioridades), não a grade de resolução de duelos.
8. **Calibração em camadas — requisito de primeira classe.** O Tier 1 será instrumentado para rotular e emitir situações no `eventStream` (telemetria). O Tier 1.5 será calibrado em duas camadas — (a) frequência de situações geradas e (b) distribuição de desfechos por situação — além dos agregados já exigidos (gols, variância, taxa de zebra) e da coerência com o mundo Poisson.
9. **Contratos existentes consumidos, não redesenhados.** `TeamTactics` (formação, mentalidade, roles/duties, instruções de time), Personality Vector, atributos 1–99, forma/fadiga, `RefereeProfile` (o árbitro é função pura lendo variáveis de contexto que o resolvedor deve emitir).
10. **Determinismo.** PRNG SplitMix64 com streams isolados; o amostrador de situações é um stream próprio. `(lineups, tactics, seed) → resultado`, replayável.
11. **Vocabulário v1 enxuto.** O primeiro build terá 3–5 tipos de situação; a taxonomia cresce incrementalmente. Candidatos levantados em sessão de design (hipótese a validar, não decisão): *posse assentada*, *transição ofensiva*, *transição defensiva*, *bola em profundidade*, *bola parada*.

---

## 1. Objetivo da pesquisa

Responder três perguntas-mestre:

1. **Taxonomia:** que classificações de situações/fases de jogo a literatura técnica (treinadores) e a analítica de futebol já usam — e qual subconjunto mínimo cobre uma partida inteira sem lacunas na v1?
2. **Representação:** como representar o estado tático **relacional** de forma compacta o suficiente para parametrizar uma amostragem, mas expressiva o suficiente para gerar recibos legíveis pelo jogador ("fui engolido no meio central")?
3. **Ancoragem empírica:** que dados e modelos publicados existem para fundamentar as distribuições (frequência de situações, desfechos por situação) **antes** de qualquer número ser especificado no design doc? ("Research before thresholds.")

---

## 2. Eixos de investigação

### Eixo A — Taxonomia de situações (fases e momentos do jogo)

- Como a literatura de treinamento classifica o jogo? Investigar o framework dos **"quatro momentos"** (organização ofensiva, organização defensiva, transição ataque→defesa, transição defesa→ataque) e onde a **bola parada** entra como quinto momento. Origem, variantes, grau de consenso.
- Sub-fases da posse: construção (*build-up*), progressão, criação e finalização — quais divisões são padrão, quais são idiossincráticas de escolas específicas.
- Como os **provedores de dados** (StatsBomb, Opta, Wyscout) segmentam a partida em eventos e sequências: taxonomias de tipo de posse (jogo corrido, contra-ataque, bola parada, pressão alta recuperada etc.), definições de "posse" e de "sequência". Que granularidade eles usam e por quê.
- Trabalhos acadêmicos de **segmentação de cadeias de posse** e classificação automática de fase de jogo: que categorias emergem quando se classifica futebol de verdade.
- **Convergência:** cruzar as fontes e propor o vocabulário mínimo v1 (3–5 situações) que particiona a partida sem sobreposição nem lacuna. Avaliar os candidatos da premissa 11 contra o que a literatura sustenta.

### Eixo B — Representação relacional do estado tático

- **Esquemas de zoneamento do campo** em uso real: terços, corredores (5 faixas / *halfspaces*), a grade do *Juego de Posición*, grades de analítica (ex.: a grade do xT). Vantagens de cada granularidade; qual é suficiente para parametrizar amostragem sem virar mini-engine.
- **Métricas de estado coletivo** usadas em análise: altura do bloco, altura da linha defensiva, compactação (vertical/horizontal), largura efetiva, intensidade de pressão (ex.: PPDA). Como são definidas e medidas; quais são deriváveis dos parâmetros que `TeamTactics` já tem (mentalidade, largura, linha, pressão, ritmo, diretividade).
- **Propriedades relacionais:** superioridade numérica por zona, superioridade posicional, superioridade qualitativa — como a literatura tática define esses conceitos e se existe formalização utilizável (não só prosa de treinador).
- **Precedentes de influence maps** em jogos e na academia esportiva (controle de campo/pitch control): o que existe de publicado sobre representar domínio espacial de dois times como campo escalar — e qual versão degenerada (por zonas, sem tracking) serve ao Tier 1.5.
- **Pergunta-síntese:** qual é o **estado tático mínimo suficiente** — o menor vetor derivável de `(TeamTactics A, TeamTactics B, placar, tempo, fadiga)` que condiciona a amostragem de situações de forma críveL e explicável?

### Eixo C — Verificação de xT, EPV e modelos de Markov de partida

Estes frameworks foram flagados em sessão de design como leads **não verificados**. Verificar antes de especificar em cima deles:

- **Expected Threat (xT):** definição exata, autor e formulação original (lead a confirmar: Karun Singh, ~2018), a grade usada, como as matrizes de transição zona→zona são estimadas, e se existem **matrizes públicas** reutilizáveis para semear nossas distribuições-base.
- **Expected Possession Value (EPV):** formulação para futebol (leads a confirmar: Fernández, Bornn, Cervone), diferenças em relação ao xT, dependência de dados de tracking (que nós não teremos — nosso input é tático-paramétrico).
- **Modelos de Markov de posse/partida** na literatura acadêmica (lead a confirmar: Sarah Rudd): estados, transições, o que eles capturam e ignoram.
- **A pergunta decisiva do eixo:** esses modelos condicionam as transições ao **contexto tático** dos dois times, ou são médias liga-inteira que lavam exatamente a variável que nos interessa? Se são médias: servem como *prior* neutro a ser modulado pela tática, ou não servem de nada? Responder explicitamente.

### Eixo D — Dados empíricos de frequência e desfecho

Levantar, com fonte, os números que o harness de calibração e as distribuições-base vão precisar (nenhum número entra no design doc sem origem):

- Posses por partida (por time), distribuição de duração de posse, fração de posses que alcançam o terço final.
- Chutes por partida, conversão de chute em gol **por tipo de situação** (jogo corrido vs. contra-ataque vs. bola parada), fração de gols originados de bola parada, de contra-ataque, de cruzamento.
- Frequência de transições por partida e taxa de conversão de transição em chance clara.
- Contagem e taxa de sucesso de duelos por zona do campo (alimenta a camada de execução).
- Taxas de falta e cartão **por tipo de situação** (transição, pressão alta, bola parada) — interface com o `RefereeProfile`, que lê contexto de falta emitido pelo resolvedor.
- Fontes candidatas: dados abertos da StatsBomb, FBref, literatura acadêmica, relatórios técnicos (ex.: UEFA). Reforço da metodologia do projeto: usamos **apenas distribuições estatísticas**, nunca registros individuais — o mundo é fictício.

### Eixo E — Referências de jogos (resolução abstrata de partida)

- ***Football, Tactics & Glory:*** como funciona a resolução zonal por turnos; como tática e habilidades discretas modulam as checagens; o que a comunidade documentou da matemática interna. Referência mais próxima do nosso resolvedor.
- **Football Manager:** o que é documentado (oficial) vs. engenharia reversa da comunidade sobre o *instant result* e a abstração por *highlights* — como o FM resolve o que não é renderizado, e como apresenta o "porquê" ao jogador.
- ***New Star Manager*** (resolução por momentos-chave) e o board game ***Counter Attack*** (estrutura de situações e dados físicos): que estruturas de situação eles usam e o que vale copiar/evitar.
- **Transversal ao eixo:** como cada um entrega **explicabilidade** (recibo pós-partida, feedback setorial, narrativa) — e onde eles falham no problema do "seus jogadores desaprenderam a jogar" que motivou o Tier 1.5.

### Eixo F — Instrumentação do Tier 1 (rotulagem de situações emergentes)

- Como segmentar uma simulação contínua de agentes em situações rotuladas: definições operacionais de posse e sequência dos provedores de dados; critérios de corte (perda de posse, bola fora, falta, N segundos sem progressão).
- Literatura de **classificação automática de fase de jogo** a partir de dados de tracking/eventos (regras heurísticas vs. ML): quais features discriminam as fases — são essas as variáveis que nosso `eventStream` precisa carregar.
- Resultado esperado do eixo: um **contrato de telemetria** — a lista de variáveis que o Tier 1 deve emitir por tick/evento para que a rotulagem de situações seja computável offline pelo harness, sem tocar no loop quente do engine.

---

## 3. Metodologia e rigor

- Classificar **cada achado** em: **[Documentado]** (fonte primária, paper, documentação oficial), **[Prática de comunidade]** (consenso não-oficial, engenharia reversa, fóruns) ou **[Incerto]** (especulação, fonte fraca, extrapolação sua). Não misturar as categorias.
- Citar a fonte de todo número e de toda afirmação factual. Número sem fonte não entra.
- Distinguir sempre **futebol real** de **necessidade do jogo**: o objetivo é credibilidade e explicabilidade, não fidelidade absoluta.
- Não propor arquitetura alternativa, não escrever código, não sugerir schemas C#.
- Quando duas fontes divergirem, registrar as duas e o motivo provável da divergência.

---

## 4. Formato de saída exigido

1. **Sumário executivo** (máx. 1 página): os achados que mais afetam o design doc.
2. **Achados por eixo (A–F)**, cada um com a classificação de confiabilidade da §3.
3. **Tabela consolidada de dados numéricos** (Eixo D) com fonte por linha.
4. **Decisões prontas para o design doc** — seção obrigatória, com recomendação fundamentada para cada item:
   - Vocabulário v1 de situações (3–5 tipos, com justificativa de cobertura total da partida);
   - Representação do estado tático (esquema de zonas + vetor relacional mínimo suficiente);
   - Abordagem de parametrização do amostrador (e veredito sobre xT/EPV/Markov como base ou prior);
   - Lista concreta de **alvos de calibração em camadas** que o harness deve comparar (métrica, camada, tolerância sugerida se houver base);
   - **Contrato de telemetria do Tier 1** (variáveis a emitir e critérios de segmentação/rotulagem);
   - Lacunas que permanecem **decisão de design** (não de pesquisa) — listar explicitamente, sem resolvê-las.

---

## 5. Fora de escopo

- Código, schemas, estruturas de dados C#, UX do relatório pós-partida.
- Curvas e limiares numéricos finais — serão fixados no design doc, sobre os dados deste relatório.
- Tiers 2/3 (Poisson permanece intocado) e o interior do sistema de arbitragem (apenas a interface de contexto de falta entra, via Eixo D).
- Modos assistível/interativo do Tier 1.5 (playback, intervenção em momentos-chave) — evolução registrada, fora da v1.
