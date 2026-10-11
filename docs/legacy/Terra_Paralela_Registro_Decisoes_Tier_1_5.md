# Terra Paralela — Registro de Decisões: Structured Match Resolver (Tier 1.5)

> **Natureza deste documento.** Não é o companion doc *Resolução Estruturada de Partidas* — esse permanece bloqueado pelo retorno da pesquisa (`Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md`) e, de qualquer forma, por **D-08** (moratória de design). Este é um **registro de decisões**: fecha as pendências de *design* levantadas na triagem da sessão de arquitetura do Tier 1.5, para que, quando a pesquisa voltar, o companion doc possa ser escrito sem nenhuma decisão de produto travando o caminho.
>
> **Cross-ref:** *Match Engine* · *Tactics → Behavior* · *Orquestração de Simulação* · *Sistema de Arbitragem* · GDD §5 (LOD) · `Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md`.
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório (SplitMix64, streams isolados).
>
> **Escopo coberto:** escopo do resolvedor, fronteira com os demais tiers, contrato do recibo de partida, paridade de traits, disciplina de calibração e arbitragem. **Fora:** taxonomia de situações, representação do estado tático, qualquer número — tudo isso depende da pesquisa e permanece deliberadamente vazio aqui.

---

## 0. Higiene de artefato — o research prompt não é reemitido

A sessão gerou um arquivo `Research_Prompt_Taxonomia_Situacoes_Estado_Tatico.md` cujo nome e conteúdo coincidem com um documento já presente na base do projeto. **Decisão de higiene: o arquivo já existente na base é o canônico; nenhuma segunda cópia entra no ledger.** Se a versão da base divergir em algum ponto do que este registro assume, a base vence e este registro é corrigido — não o contrário.

Este documento **não** duplica o conteúdo do prompt. Ele o **emenda** (§7) e decide o que o prompt deliberadamente deixou fora.

---

## 1. Nomenclatura canônica

**D-30 — O sistema chama-se `StructuredMatchResolver`. "Tier 1.5" é designação de LOD, não nome de sistema.**

Motivo: "Tier 1.5" descreve *onde* na hierarquia de LOD o resolvedor vive; não descreve o que ele é. Código, testes e telemetria usam `StructuredMatchResolver`; o GDD §5 e a prosa de orquestração usam "Tier 1.5". Um nome por camada de discurso, sem sinônimos circulando na mesma camada.

---

## 2. Escopo da v1 — a pendência principal da triagem

A pergunta em aberto era: o resolvedor serve só o botão "simular partida", ou também um modo de *playback* assistível (assistir ao tabuleiro rodando) e, adiante, um modo interativo (intervir em momentos-chave, substituir em pausas)?

**D-31 — A v1 do `StructuredMatchResolver` é resolvedor puro. Entrada `(lineups, tactics, seed)`, saída `MatchResult` + `eventStream`. Sem playback, sem intervenção, sem UI de partida.**

Três motivos, em ordem de peso:

1. **O playback não é um modo do resolvedor — é um leitor do `eventStream`.** Se o resolvedor emite um `eventStream` com carimbo de minuto (e ele emite, por D-33), então "assistir" é renderizar esse stream em velocidade controlada. Isso é trabalho de camada de apresentação, e vive atrás de **D-07** (nenhum Godot antes do passo 4) junto com todo o resto do rendering. Construir o playback como modo do resolvedor criaria um segundo caminho de execução dentro do mesmo sistema — exatamente o que "one way" proíbe.
2. **O modo interativo muda o contrato, não a implementação.** Intervir em momentos-chave significa que `TeamTactics` deixa de ser constante durante a partida e passa a ser série temporal. Isso é uma mudança de assinatura que atinge também o Tier 1, e é decisão do *Match Engine*, não deste resolvedor. Antecipá-la aqui seria escolher a interface de um módulo que não existe — o erro que **D-19**/§9.4 do doc de identidade de clube já ensinou a não repetir.
3. **A v1 precisa provar a hipótese arquitetural, não vender o modo de jogo.** A hipótese é "situação como átomo produz simulação consistente e explicável". Isso se prova com um resolvedor headless e um harness. Playback é polimento sobre uma hipótese ainda não validada.

**Consequência positiva registrada:** o playback fica *barato* depois, precisamente porque D-33 exige `eventStream` completo desde a v1. A decisão de escopo não é uma amputação; é uma ordenação.

**D-32 — O `StructuredMatchResolver` nunca serve Tiers 2/3. O duplo Poisson permanece intocado.**

Gerar `eventStream` por jogador para centenas de partidas de fundo que ninguém vai ler viola a disciplina de LOD do GDD §5 — Tier 2/3 não têm lógica por jogador, por definição. Um caminho por contexto: Tier 1 para partida jogada, Tier 1.5 para partida simulada do clube do jogador, Poisson para o mundo. **Sem fallback entre tiers em nenhuma direção** — nem o Poisson cobrindo o Tier 1.5 quando ele falhar, nem o Tier 1.5 "melhorando" o mundo de fundo.

---

## 3. Contrato do recibo de partida — a razão de o sistema existir

A motivação de produto do Tier 1.5, que a triagem corretamente marcou como contexto solto, entra aqui como premissa formal:

> **Premissa de produto.** O defeito que este sistema existe para corrigir não é a variância — futebol sem zebra não é futebol. É a **opacidade causal**: perder com o melhor elenco do mundo sem que o jogo ofereça nenhuma leitura do porquê, produzindo a sensação de que "os jogadores desaprenderam a jogar". A meta é **zebra com recibo**.

Disso decorrem quatro decisões travadas.

**D-33 — O `StructuredMatchResolver` emite `eventStream` completo com carimbo de minuto, no mesmo formato do Tier 1.** Gols, chutes, passes-chave, duelos, faltas, cartões, substituições. Não é opcional nem "modo verboso": é a saída primária, da qual o placar é derivado. Estado derivado não se armazena como verdade — o placar é `Read()` sobre o stream, não um campo escrito em paralelo.

**D-34 — O schema do recibo de partida (`MatchReceipt`) é idêntico para Tier 1 e Tier 1.5. Divergência entre os dois é sinal de calibração, nunca uma feature.**

Esta é a decisão mais consequente do documento. Se os dois resolvedores produzem recibos com campos diferentes, o jogador aprende a ler duas linguagens e o harness perde a única comparação que importa. Schema único significa que "simular" e "jogar" respondem à mesma pergunta com o mesmo vocabulário — e que qualquer diferença sistemática entre eles é um bug de calibração, detectável automaticamente.

**D-35 — Divulgação em três níveis: ordinal → relativo → contrafactual.**

| Nível | O que expõe | Exemplo de forma |
| :--- | :--- | :--- |
| Ordinal | Quem prevaleceu, sem escala inventada | "dominado no meio central" |
| Relativo | Desempenho contra o papel esperado, não contra uma nota absoluta | "seu volante perdeu a maioria dos duelos que disputou" |
| Contrafactual | O que teria mudado o quadro | "com a linha mais baixa, aquelas bolas em profundidade não se formariam" |

Corolários travados:

- **Sem notas 1–10 por jogador.** Uma nota agregada é exatamente a opacidade que o sistema combate: comprime a causa num escalar e devolve ao jogador um número sem referente. Veredito relativo ao papel, não escalar absoluto.
- **O contrafactual nunca é oráculo direto.** Ele é entregue por **staff in-fiction falível** (auxiliar técnico, analista), com a competência do staff modulando a qualidade do conselho. Um oráculo perfeito transformaria o jogo num solucionador de otimização; um auxiliar que às vezes lê errado é futebol.

**D-36 — Rolagens cruas de PRNG nunca são expostas ao jogador.** Nem em modo avançado, nem em tooltip, nem em log de partida. Expor a rolagem converte a leitura tática numa leitura de dado e mata exatamente a suspensão de descrença que o recibo existe para proteger. As rolagens são visíveis **apenas** ao harness de calibração e ao debug, atrás de flag de desenvolvimento — nunca em build de jogador.

> **Princípio registrado.** "Script feel" em jogos de futebol nasce de opacidade causal, não de aleatoriedade. Transparência se entrega expondo **o estado do jogo**, não os operadores probabilísticos que geram os desfechos. D-35 e D-36 são as duas faces dessa regra.

---

## 4. Traits e paridade entre resolvedores

A sessão levantou traits discretos visíveis (no idioma de cartas de FUT: *habilidades* como drible ou finta elevados) como vetor de expressividade do resolvedor. O projeto já tem traits (`Maestro`, `Bombardeiro`) com dívida de calibração conhecida.

**D-37 — Paridade obrigatória de trait: um trait afeta os dois resolvedores ou nenhum. Não existe trait exclusivo do Tier 1.5.**

Motivo: se um trait modula o tabuleiro mas não os pesos de utility do Tier 1, o **mesmo jogador se comporta de forma diferente** conforme a partida foi jogada ou simulada. Isso reintroduz, pela porta dos fundos, exatamente a incoerência que o Tier 1.5 existe para eliminar — e o jogador percebe: simula quando o trait ajuda, joga quando atrapalha.

**D-38 — O catálogo de traits é propriedade de *Character Creation*, não do resolvedor.** Nenhum trait novo é criado nesta camada. Um trait entra no catálogo com **duas declarações obrigatórias**: como modula os pesos da `UtilityAI` (Tier 1) e como modula a amostragem/execução de situação (Tier 1.5). Trait sem as duas declarações não entra — é erro de definição, não item a resolver depois.

**D-39 — Atributos 1–99 permanecem a fonte contínua; traits são modificadores discretos sobre ela, nunca substitutos.** A camada de execução de situação lê `EffectiveAttributes.Read()` como único ponto de acesso, igual ao resto do projeto. Um trait não escreve atributo, não cria campo paralelo e não vira caminho alternativo de leitura.

---

## 5. Disciplina de calibração — correção de uma tensão real

A premissa 8 do research prompt diz que o Tier 1.5 será calibrado contra a frequência de situações rotuladas emitidas pelo Tier 1. O princípio canônico do projeto diz que **coerência interna entre tiers não é evidência de correção** — cada tier deve ser ancorado independentemente a dado externo. As duas coisas, como escritas, colidem: calibrar A contra B é precisamente tomar coerência interna como alvo.

**D-40 — Resolução da tensão: o dado empírico externo é o alvo de calibração dos dois tiers. A telemetria do Tier 1 é *cross-check*, não alvo.**

Concretamente:

| Camada | Alvo de calibração | Papel da telemetria do Tier 1 |
| :--- | :--- | :--- |
| Frequência de situações | Dado empírico publicado (Eixo D da pesquisa) | Cross-check: se Tier 1 e Tier 1.5 divergem **ambos** do dado real, o defeito é compartilhado; se divergem entre si, um dos dois está errado — e o dado externo diz qual |
| Desfecho por situação | Dado empírico publicado (Eixo D) | Idem |
| Agregados (gols, variância, taxa de zebra) | Dado empírico publicado | Idem |
| Coerência com o mundo Poisson | Distribuição de placares das ligas de fundo | Não se aplica |

**D-41 — A regra de arbitragem é declarada agora, antes de qualquer divergência ser observada.** Quando Tier 1 e Tier 1.5 discordarem, **nenhum dos dois é automaticamente a verdade**: o desempate é contra o dado externo. Se o dado externo não cobrir a métrica em disputa, a divergência é **registrada como débito de calibração**, não resolvida por preferência. Declarar a regra depois de ver o resultado é como se escolhe o vencedor que já se queria.

**D-42 — Distinção conformance × calibração, aplicada ao resolvedor.** *Conformance*: determinístico, CI-friendly, seeds congeladas, igualdade contra golden file — roda a cada commit. *Calibração*: estatística, offline, milhares de execuções, ancorada em dado externo — roda sob demanda. Nunca no mesmo harness, nunca no mesmo critério de falha. Um teste que "às vezes passa" não é teste.

**D-43 — O amostrador de situações é um stream próprio no `StreamName`.** Isolado dos streams de física, de geração e de arbitragem. Garantia mantida: `(lineups, tactics, seed) → resultado` replayável bit a bit. Consequência da disciplina de stream já estabelecida: acrescentar um consumo de PRNG novo no amostrador desloca a sequência — logo, consumos novos entram **no fim** da ordem, e mundos/partidas já gravados não invalidam.

---

## 6. Critérios de aceitação do vocabulário v1 — o que se trava sem a pesquisa

A taxonomia de situações **não é decidida aqui**: "research before thresholds" vale para vocabulário tanto quanto para número, e o Eixo A existe exatamente para isso. O que se pode travar hoje, sem antecipar o achado, são os **critérios que o vocabulário proposto terá de satisfazer** para ser aceito.

**D-44 — O vocabulário v1 de situações é aceito se, e somente se, satisfizer os cinco critérios:**

1. **Cardinalidade:** 3–5 tipos. Menos não cobre a partida; mais é taxonomia rica antes de duplicação real (violação de abstração diferida).
2. **Partição:** cobre a partida inteira **sem lacuna e sem sobreposição**. Todo momento de jogo pertence a exatamente um tipo. Se sobra momento sem tipo, o vocabulário está incompleto; se um momento cabe em dois, os tipos não são disjuntos.
3. **Nomeabilidade em linguagem de treinador:** cada tipo precisa ter nome que um técnico usaria. Um tipo que só existe como categoria interna do amostrador não pode aparecer no recibo — e um tipo que não pode aparecer no recibo não serve a D-34.
4. **Tupla legível:** cada situação carrega `(onde, quem venceu, contra o quê)` — os três campos que o recibo precisa para produzir a frase ordinal de D-35. Tipo que não consegue preencher a tupla é tipo mal definido.
5. **Derivabilidade relacional:** a frequência do tipo precisa ser derivável de `(TeamTactics A, TeamTactics B, estado da partida)`. Tipo cuja frequência não responde à tática dos dois times não pertence ao vocabulário — seria evento ambiental, não situação.

Os candidatos da premissa 11 do prompt (*posse assentada*, *transição ofensiva*, *transição defensiva*, *bola em profundidade*, *bola parada*) continuam **hipótese a validar**, e são avaliados contra estes cinco critérios quando a pesquisa retornar.

---

## 7. Emendas ao research prompt — Eixos A, E e F

O prompt foi escrito antes de D-34/D-35 existirem. Três eixos precisam de acréscimo para que a pesquisa volte com o que o recibo exige. **Nenhuma emenda remove ou reescreve texto do prompt; são acréscimos.**

**Eixo A — acrescentar:**
> Para cada tipo de situação candidato, registrar **como um treinador o nomeia em prosa** e quais das três informações `(onde, quem venceu, contra o quê)` a literatura associa a ele. Um tipo que a literatura sustenta mas que não é nomeável em linguagem de treinador deve ser sinalizado como tal — o vocabulário v1 precisa ser dizível ao jogador (critério D-44.3).

**Eixo E — acrescentar:**
> Além de *como* cada jogo resolve a partida, documentar **o que cada um mostra ao jogador depois** e em que granularidade: relatório setorial, mapa de calor, narrativa, notas por jogador. Especificamente: algum deles expõe rolagens ou probabilidades cruas, e o que a comunidade relata sobre o efeito disso na percepção de justiça do resultado? Registrar também os casos de **falha de explicabilidade** — onde o jogador reclama de resultado incompreensível e o que o jogo deixou de mostrar.

**Eixo F — acrescentar:**
> O contrato de telemetria deve ser suficiente para preencher, por situação rotulada, a tupla `(onde, quem venceu, contra o quê)`. O critério de suficiência não é "permite classificar a fase de jogo", e sim **"permite gerar a mesma frase de recibo que o Tier 1.5 geraria"** — o schema do recibo dirige o contrato de telemetria, não o contrário.

---

## 8. Posição na sequência de implementação

**D-45 — O `StructuredMatchResolver` permanece atrás de D-07 e D-08. Nada aqui é implementável agora.**

Bloqueios ativos, em ordem:
1. **D-07** (sequência obrigatória `SplitMix64 → física headless → steering mínimo → primeiro sprite`) — a prioridade vigente segue sendo a migração `System.Random` → SplitMix64, com `Prompt_Implementacao_SplitMix64.md` pronto para handoff.
2. **D-08** (moratória de design) — o companion doc *Resolução Estruturada de Partidas* não é escrito enquanto o steering não passar no harness.
3. **Retorno da pesquisa** — o Eixo A precisa retornar antes de qualquer taxonomia entrar em doc.
4. **Instrumentação do Tier 1** — que por sua vez exige o Tier 1 existir.

Este registro existe justamente para que, quando os quatro destravarem, **nenhuma decisão de design esteja pendente** no caminho do companion doc.

---

## 9. O que permanece deliberadamente em aberto

Registrado para não virar decisão por omissão:

1. **Taxonomia de situações v1** — critérios travados (D-44), vocabulário aberto. Bloqueado pelo Eixo A.
2. **Representação do estado tático** (esquema de zonas + vetor relacional mínimo) — bloqueado pelo Eixo B.
3. **Veredito sobre xT/EPV/Markov** como base, prior ou nada — bloqueado pelo Eixo C.
4. **Todos os números** (frequências, taxas de conversão, tolerâncias do harness) — bloqueados pelo Eixo D.
5. **Contrato de telemetria concreto** — bloqueado pelo Eixo F, agora emendado (§7).
6. **Campos exatos do `MatchReceipt`** — o schema é único (D-34) e a divulgação é em três níveis (D-35), mas a lista de campos depende da tupla de situação, logo do Eixo A.
7. **Competência do staff in-fiction** que entrega o contrafactual (D-35) — qual atributo do staff modula a qualidade do conselho, e como o erro se manifesta. Pertence ao módulo de staff, que não existe.

---

## 10. O que isto destrava

Das cinco pendências que a triagem marcou como 🟡, **quatro estão fechadas** (escopo v1, fronteira com Tier 2, paridade de traits, nível de transparência do recibo) e a quinta (vocabulário) tem agora critério de aceitação explícito em vez de estar simplesmente aberta.

Além disso, uma contradição que estava latente entre o research prompt e os princípios canônicos — calibrar contra o Tier 1 versus ancoragem externa independente — está **resolvida em favor do princípio** (D-40/D-41), com a regra de arbitragem declarada antes de qualquer divergência ser observada, que é a única hora em que declará-la significa alguma coisa.

**A prioridade de implementação não muda:** segue `System.Random` → SplitMix64, depois física headless.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | Sistema chama-se `StructuredMatchResolver`; "Tier 1.5" é designação de LOD |
| D-31 | v1 é resolvedor puro; playback é leitor de `eventStream`, atrás de D-07 |
| D-32 | Resolvedor nunca serve Tiers 2/3; Poisson intocado; sem fallback entre tiers |
| D-33 | `eventStream` completo com carimbo de minuto é saída primária; placar é derivado |
| D-34 | Schema de `MatchReceipt` idêntico em Tier 1 e Tier 1.5; divergência é sinal de calibração |
| D-35 | Divulgação em três níveis (ordinal → relativo → contrafactual); sem notas 1–10; contrafactual via staff falível |
| D-36 | Rolagens cruas de PRNG nunca expostas ao jogador; só harness e debug |
| D-37 | Paridade de trait: afeta os dois resolvedores ou nenhum |
| D-38 | Catálogo de traits pertence a *Character Creation*; trait entra com duas declarações obrigatórias |
| D-39 | Atributos 1–99 são a fonte contínua; traits são modificadores, não substitutos |
| D-40 | Dado empírico externo é o alvo dos dois tiers; telemetria do Tier 1 é cross-check |
| D-41 | Regra de arbitragem declarada antes de qualquer divergência observada |
| D-42 | Conformance (determinístico, CI) e calibração (estatística, offline) nunca no mesmo harness |
| D-43 | Amostrador de situações tem stream próprio no `StreamName` |
| D-44 | Cinco critérios de aceitação do vocabulário v1 (cardinalidade, partição, nomeabilidade, tupla, derivabilidade) |
| D-45 | `StructuredMatchResolver` permanece atrás de D-07, D-08 e do retorno da pesquisa |
