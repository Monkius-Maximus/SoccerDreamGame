# Terra Paralela — Registro de Decisões: Sistema de Arbitragem (Referee)

> **Natureza deste documento.** Não é um companion doc novo — é um **registro de decisões**, escrito assim deliberadamente para respeitar **D-08** (moratória de design: nenhum companion doc novo até o steering passar no harness). Fecha as lacunas de arquitetura levantadas na sessão de design sobre regras de arbitragem, sem tocar em números/thresholds que dependem de pesquisa (ver §7).
>
> **Cross-ref:** *Tactics → Behavior* (steering/separation, `player.decide(tick)`) · GDD §3 (Event & Interruption System, tier "Abstract mini-games") · GDD §5 (LOD) · decisões travadas do projeto D-01 a D-09.
> Convenção: prosa PT-BR, identificadores em inglês. Determinismo obrigatório onde há desempate (sem PRNG onde ordem determinística basta).
>
> **Numeração:** decisões numeradas a partir de **D-30**, continuando a última usada em documentos vistos nesta sessão (D-29, *Registro de Decisões: Camada de Autoria de Mundo*). ⚠️ Confirmar contra o ledger que nenhum D-30+ já foi atribuído em outra sessão antes de gravar definitivamente.
>
> **Escopo coberto:** mecanismo de detecção e resolução de regras (Tier 1 apenas — GDD §5). **Fora:** números/thresholds finais (viram dependência de pesquisa, §7), Tiers 2/3 (resolução estatística, só consomem as *taxas-base* que a pesquisa do §7 vai produzir), UX do recibo pós-partida.

---

## 1. Reconciliação necessária — como o contato existe, se a física não colide jogadores

Antes de fechar as lacunas específicas, uma inconsistência estrutural precisa ser resolvida, porque todas as regras de falta e mão na bola dependem dela.

**O problema:** a sessão de design descreveu faltas como leitura de "dados físicos que o motor já tem" (ângulo de entrada, velocidade relativa, contato antes da bola). Mas **D-03** trava que colisão jogador-vs-jogador foi **removida da física**, tratada por separação de steering + sistema de arbitragem. Se não há colisão rígida, não existe um evento de física puro para o juiz ler.

**D-30 — Origem do evento de contato: sintetizado pela camada de steering, não pela física.**

Quando dois jogadores executam uma ação de disputa/desarme (`Intention` de tipo desarme, per *Tactics → Behavior* §3) e a distância entre eles viola o raio mínimo de separação que o steering já mantém, o **steering emite o evento**, não a física. Os dados cinemáticos necessários (ângulo de aproximação, velocidade relativa, quem iniciou a ação, se a bola já estava no ponto de contato) já são subprodutos do cálculo de separação — nada novo é computado, só reaproveitado e exposto.

Isso fecha o ciclo: `player.decide(tick)` → steering calcula força + detecta violação de separação em contexto de disputa → emite `PlayerContactDetected` no `eventStream` → `Referee` consome. Um caminho, sem sistema de colisão paralelo.

---

## 2. Reinícios — direto vs. indireto (fecha a lacuna de tiro livre)

**D-31 — `BallState` ganha discriminador de tipo em vez de estado novo.**

Em vez de inflar o enum com `FreeKickDirect` / `FreeKickIndirect` como valores irmãos (duplicaria a máquina de reinício), o valor único `FreeKickRestart` carrega um campo `FreeKickType { Direct, Indirect }`. Um estado, um parâmetro — não dois caminhos paralelos.

**Consequência mecânica do indireto:** ao entrar em `FreeKickRestart(Indirect)`, o motor arma uma flag transiente `pendingSecondTouch = true`, análoga em espírito à flag de impedimento (§4 da sessão anterior) — vive até ser resolvida, não é checagem contínua. Se a bola cruza a linha de gol adversária sem que **qualquer jogador além do cobrador** a tenha tocado, o gol é anulado e o reinício vira tiro de meta. Se entra direto no próprio gol, o reinício vira escanteio para o adversário. A flag cai no primeiro toque de qualquer outro jogador.

**Classificação direto vs. indireto:** contato físico e mão na bola são diretos; obstrução, jogo perigoso, impedimento e infrações de goleiro são indiretos. Esta classificação é conhecimento estável (não muda ano a ano como o texto exato das Leis), mas o **texto oficial vigente** ainda é dependência de pesquisa (§7, item 1) antes de virar tabela de implementação — não travar a lista aqui sem citação seria violar a mesma disciplina que o projeto já aplica a números.

---

## 3. Regras de goleiro (fecha a lacuna enumerada e não especificada)

**D-32 — Recuo (back-pass) e recepção de lateral: mesmo mecanismo do `lastTouch`, um predicado a mais.**

Reaproveita a variável `lastTouch` que já resolve escanteio/tiro de meta. Ao goleiro **manusear** a bola dentro da própria área:

- `if lastTouch.player == teammate AND lastTouch.method == DeliberateFootPass` → `FreeKickRestart(Indirect)` para o adversário, no ponto do manuseio.
- `if lastTouch.restartType == ThrowIn AND lastTouch.receivingPlayer == ownGoalkeeper` → mesmo reinício.

Nenhum sistema novo: é um `if` sobre uma variável que já existe, avaliado só no momento em que o goleiro manuseia a bola.

**D-33 — Tempo de posse do goleiro: timer reaproveitando o padrão da vantagem.**

`GoalkeeperPossessionStarted` inicia um timer (mesmo padrão arquitetural do timer de vantagem — resolução diferida, §5 da sessão anterior); excedido o threshold, dispara `FreeKickRestart(Indirect)`. **Mecanismo travado, valor do threshold é dependência (§7, item 3)** — a aplicação real da regra dos 6 segundos diverge bastante do texto formal, então isso é tanto pesquisa quanto calibração de game feel.

---

## 4. Mão na bola (fecha a lacuna sinalizada como mais difícil)

**D-34 — Handball reaproveita o padrão de score de falta, não um sistema à parte.**

Mesma arquitetura de `FoulLegalityScore`/`FoulSeverityScore`, com inputs equivalentes já disponíveis via D-30 (steering) mais o segmento de braço do rig de animação:

- posição do braço relativa à silhueta natural do torso (estendido/afastado = score mais alto);
- direção do contato — braço se movendo em direção à bola vs. bola vindo em direção a um braço parado (silhueta natural);
- tempo de reação disponível para recolher o braço antes do contato;
- se o contato precede diretamente um gol ou uma chance clara (eleva a severidade mesmo com score de intenção marginal — este é um agravante conhecido do texto atual do IFAB, a confirmar em §7 item 1).

Score cruza o threshold modulado por `RefereeProfile.CARD_SEVERITY` → mesmo pipeline de saída que uma falta comum (`FreeKickRestart(Direct)` ou pênalti, via o mesmo gate geométrico point-in-polygon já travado). **Nenhum pipeline paralelo é criado** — handball é uma segunda *fonte* de contact score, consumida pelo mesmo resolvedor.

---

## 5. Disputa de pênaltis (fecha a lacuna "sistema separado")

**D-35 — É um `MatchPhase`, não um valor de `BallState`.**

Diferente dos demais reinícios (valores de `BallState` dentro de uma partida em andamento, com relógio corrido), a disputa de pênaltis não tem posse disputada nem relógio contínuo — é sequencial e finita por regra própria (alternância, melhor-de-5, morte súbita). Por isso é um **estado de partida** próprio, `MatchPhase.PenaltyShootout`, fora da máquina de `BallState`.

**D-36 — Cada cobrança reaproveita a resolução de chute/defesa já existente, sem sistema de resolução novo.**

Consistente com "um caminho, sem fallback": a cobrança de pênalti não introduz um segundo resolvedor de chute. É a mesma lógica de finalização vs. defesa do jogo aberto, chamada com a bola posicionada na marca e os demais jogadores fora do raio de interferência. `PenaltyShootoutSequence` só rastreia placar, ordem de cobrança e critério de parada (morte súbita).

Isso também é coerente com o GDD §3: cobranças de pênalti em **eventos de carreira/vida** (não a partida em si) já são descritas ali como "Abstract mini-games" de alta relevância — a disputa **dentro de uma partida Tier 1**, por ser parte do próprio match engine físico, usa a resolução física real em vez do resolvedor abstrato do GDD §3. Os dois não competem: são LODs diferentes para contextos diferentes.

**Em aberto, não travado aqui:** se personalidade/composure devem modular a cobrança sob pressão de disputa (peso adicional de ansiedade no `Intention` scoring da *Tactics → Behavior*). É extensão de um sistema existente (Personality Vector), não parte deste registro — listado em §8.

---

## 6. Taxonomia de eventos (`eventStream`) — trava o contrato pendente

**D-37 — Vocabulário v1 enxuto, mesmo princípio do SMR.** Seguindo o mesmo padrão já aplicado à taxonomia de situações do Tier 1.5 (*vocabulário v1 enxuto, cresce por duplicação real, não por antecipação*), a lista abaixo é o mínimo necessário para o juiz de Tier 1 funcionar — não um catálogo exaustivo antecipado.

| Evento | Payload essencial | Consumidor | Origem |
| :--- | :--- | :--- | :--- |
| `BallOutOfBounds` | posição, `lastTouch` | Referee → lateral/escanteio/tiro de meta | Física da bola |
| `BallCrossedGoalLine` | posição, altura (Z) | Referee → gol (sujeito a flags pendentes) | Física da bola |
| `PassIssued` | passador, momento | Referee → snapshot de impedimento | `Tactics → Behavior` |
| `PlayerContactDetected` | ângulo, velocidade relativa, precedência de toque na bola, iniciador | Referee → `FoulLegalityScore`/`Severity` | Steering (D-30) |
| `HandballContactDetected` | posição do braço, velocidade relativa braço/bola, silhueta | Referee → `HandballIntentScore` | Steering + rig de animação (D-34) |
| `GoalkeeperPossessionStarted` / `Ended` | goleiro, timestamp | Referee → timer 6s (D-33) | Física da bola |
| `RestartTaken` | tipo, jogador | Referee → limpa flags pendentes (offside, segundo toque) | Referee (auto-consumo) |
| `CelebrationTriggered` | jogador, tipo de animação | Referee → lookup evento→cartão | Sistema de animação |
| `SimulationAttemptDetected` | jogador, `FoulLegalityScore` do contato, animação de queda | Referee → cavar falta (§ sessão anterior) | Steering + animação |
| `PeriodClockExpired` | período | Referee → fim de tempo/acréscimos | `TimeManagerNode` |

Cada evento carrega `timestamp` (obrigatório, usado em D-38) além do payload próprio. Novos eventos entram **no fim** desta lista quando duplicação real exigir — mesma disciplina de "campos amostrados novos entram no fim da ordem do stream" já aplicada em *Geração de Identidade de Clube v2* §8.

---

## 7. Ordem de resolução de gatilhos simultâneos

**D-38 — Prioridade por causalidade temporal, não por ordem de código.**

1. **Bola morta trava tudo.** Se `BallState` já não é `InPlay`, novos gatilhos geométricos (fora, gol) são ignorados até o próximo `RestartTaken`.
2. **Flags pendentes anteriores invalidam eventos posteriores no mesmo tick.** Um `pendingOffside` ou `pendingSecondTouch` avaliado antes de `BallCrossedGoalLine` no mesmo tick anula o gol antes de ele ser creditado — a flag existe precisamente para isso.
3. **Timestamp dentro do tick decide precedência entre eventos concorrentes.** Uma falta com `timestamp` anterior ao momento em que a bola cruzou a linha de fundo precede a determinação de escanteio/tiro de meta — o jogo já havia parado antes do evento "seguinte" existir.
4. **Empate real (mesmíssimo timestamp, caso raro de contato multi-agente simultâneo):** desempate por `FoulSeverityScore` (mais grave vence), e se ainda empatado, por ordem ordinal determinística de `playerId` — **sem PRNG**, porque não há razão para introduzir aleatoriedade numa resolução que já é determinística por construção.

---

## 8. Dependências de dados (não são decisões em aberto)

Nenhum destes itens é resolvido por mais design — são pesquisa ou calibração, na disciplina "research before thresholds" já aplicada ao resto do projeto.

| # | Item | Origem exigida | Entregável |
| :--- | :--- | :--- | :--- |
| 1 | Texto vigente das Leis 11/12/14/4 do IFAB (classificação direto/indireto completa, definição atual de handball incl. "silhueta"/consequência imediata) | Pesquisa documentada — ⚠️ **verificar duplicidade com trabalho de arbitragem já existente no projeto antes de disparar** | Tabela de infrações: predicado, variáveis, tipo de reinício, consequência disciplinar |
| 2 | Faixas de `RefereeProfile` (`Leniency`, `AdvantageBias`, `CardSeverity`, `HomeBias`, `Consistency`) e taxas-base de cartão/pênalti/falta por liga | Pesquisa empírica — ⚠️ mesma verificação de duplicidade | Tabela de faixas por parâmetro + taxas-base por tier (alimenta também Tiers 2/3) |
| 3 | Threshold do tempo de posse do goleiro (regra dos 6s — texto formal vs. aplicação real) | Item 1 + nota de divergência prática | Um valor calibrado |
| 4 | Curva de resposta do `HandballIntentScore` | Item 1 (texto da diretriz atual) + calibração de game feel (mesma disciplina de D-04) | Curva, fixada no primeiro teste visual |
| 5 | Curvas de resposta de `FoulLegalityScore`/`FoulSeverityScore` | Item 2 + calibração de game feel | Curvas, fixadas contra o harness de steering |

Os itens 1 e 2 carregam a mesma suspeita de supersessão já registrada na triagem anterior desta conversa — **não redisparar pesquisa sem checar o ledger primeiro.**

---

## 9. O que permanece deliberadamente em aberto

1. **Modulação de personalidade/pressão em cobranças de pênalti** (D-35/D-36) — extensão do Personality Vector existente, não decisão deste registro.
2. **Todos os itens numéricos do §7** — bloqueados por pesquisa/calibração, não por design.
3. **Sequenciamento de implementação.** Este módulo depende de steering com separação funcionando (D-30) — ou seja, entra depois de *Tactics → Behavior* ter passado do primeiro caso de uso (marcação/desarme, item 2 do caminho incremental daquele doc). Não redefine **D-07**; só registra a dependência.

---

## 10. O que isto destrava

Com este registro, a arquitetura do juiz fecha em tudo que era decisão de design: reinícios diretos/indiretos, regras de goleiro, mão na bola, disputa de pênaltis, taxonomia de eventos e ordem de resolução — todos com mecanismo travado e caminho único, sem sistemas paralelos. As únicas pendências reais são cinco itens numéricos (§7), cada um com origem nomeada, e uma dependência de sequenciamento já implícita em D-07.

Nenhum companion doc novo foi criado (respeita D-08). Quando o steering passar no harness e a janela deste módulo abrir, a implementação parte direto deste registro — sem decisão de design pendente no caminho.

---

### Sumário das decisões travadas neste registro

| Id | Decisão |
| :--- | :--- |
| D-30 | Evento de contato sintetizado pelo steering (separação), não pela física — reconcilia com D-03 |
| D-31 | `FreeKickRestart` com campo `FreeKickType {Direct, Indirect}`; flag `pendingSecondTouch` transiente |
| D-32 | Recuo/lateral direto ao goleiro: predicado sobre `lastTouch`, mesmo mecanismo de escanteio/tiro de meta |
| D-33 | Tempo de posse do goleiro: timer no padrão da vantagem; valor é dependência (§7.3) |
| D-34 | Handball reaproveita `FoulLegalityScore`/`Severity`; segunda fonte de score, mesmo pipeline de saída |
| D-35 | Disputa de pênaltis é `MatchPhase.PenaltyShootout`, não valor de `BallState` |
| D-36 | Cobranças reaproveitam a resolução de chute/defesa do jogo aberto — sem resolvedor novo |
| D-37 | Taxonomia de eventos v1 travada (10 eventos, `eventStream`); vocabulário enxuto, cresce por duplicação |
| D-38 | Ordem de resolução: bola-morta-trava-tudo → flags pendentes → timestamp → severidade → ordinal determinístico |
