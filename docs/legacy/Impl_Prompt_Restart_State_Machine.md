# Implementation Prompt — Restart State Machine + Regras Classe 1 (Estágio 1)

> **Alvo:** Claude Code. **Sistema:** Match Engine, Tier 1. **Design doc fonte:** *Terra Paralela — Regras da Partida, Competições e Personalização* §1–§2.1, §7 (estágio 1).
> Convenção: prosa PT-BR, identificadores em inglês. Godot 4 + C#.

---

## 0. Contexto travado (NÃO reabrir)

1. `MatchSimulation` é **classe C# pura**, não Node. Simulação ⊥ apresentação ⊥ persistência.
2. Fixed timestep herdado do engine; determinismo total: `(lineups, tactics, seed) → resultado`, replayável. As regras Classe 1 são **geométricas puras — sem PRNG**.
3. Este estágio implementa **somente**: máquina de estados de reinícios, classificadores geométricos (bola fora, gol, último toque), relógio de partida, execução de reinício. **Sem faltas, sem impedimento, sem árbitro** — são estágios 2–4, research-gated.
4. Partida em visão lateral 2D (side-on) — trilha canônica travada em **D-01** (3D arquivado, `testingclaude` arquivado); checagens de linha são comparações de coordenada + raio da bola.

## 1. A máquina de estados

```
OpenPlay → StoppageEvent → RestartAssignment → DeadBall → RestartExecution → OpenPlay
```

- **`OpenPlay`:** loop tick-a-tick normal (física + IA dos jogadores).
- **`StoppageEvent`:** emitido pelos classificadores (§2). Tipo fechado (enum/discriminated union): `BallCrossedTouchline`, `BallCrossedGoalLine`, `GoalScored`, `HalfTimeReached`, `FullTimeReached`. Os tipos `FoulCommitted` e `OffsideOffence` **existem na taxonomia** (o enum os declara) mas nenhum produtor os emite neste estágio — se um chegar à máquina, lançar `NotSupportedException` explícita. Sem stub silencioso.
- **`RestartAssignment`:** função pura `(StoppageEvent, matchState) → Restart`. Tabela do design doc §1: tiro de meta, escanteio, arremesso lateral, kick-off pós-gol, kick-off de início de tempo. Evento não-classificável → **throw**. Nunca "play on".
- **`DeadBall`:** bola parada no local do reinício; jogadores reposicionam (âncoras de formação do Tactics→Behavior, fase "dead ball").
- **`RestartExecution`:** o jogador designado executa (a decisão de *como* executar é a utility AI normal, com o conjunto de ações restrito ao contexto do reinício); a máquina volta a `OpenPlay` no primeiro toque legal.

Cada `Restart` carrega: tipo, posição, time executor. Um caminho de resolução por tipo de evento — sem defaults, sem fallbacks.

## 2. Classificadores Classe 1 (funções puras do estado da física)

- **Bola fora:** a bola está fora quando cruza **inteiramente** a linha — checagem `posição ± raio` contra a coordenada da linha, avaliada por tick. Emite `BallCrossedTouchline` ou `BallCrossedGoalLine`.
- **Último toque:** lido do log de contatos da física (o engine já registra contatos bola↔jogador). O classificador **não infere** — se o log estiver vazio no momento do cruzamento, throw (precondição violada).
- **Gol:** caso especial de `BallCrossedGoalLine` com a bola inteira cruzando o segmento entre as traves → `GoalScored` (prioridade sobre tiro de meta/escanteio; a ordem de avaliação é fixa e documentada no código).
- **Relógio:** 90' corridos, dois tempos, o relógio **nunca para** durante `DeadBall`. Contador de paradas acumula duração de cada `DeadBall` em campo separado do `matchState` (consumidor futuro: acréscimos, estágio 4 — acumular agora, não usar ainda). `HalfTimeReached`/`FullTimeReached` são StoppageEvents do próprio relógio.

## 3. Estado e eventos

- `matchState` ganha: estado atual da máquina, placar, relógio (tempo, metade), acumulador de paradas, restart pendente.
- Todo `StoppageEvent`, `Restart` e `GoalScored` entra no `eventStream` tipado (consumidores: replay, telemetria do Tier 1 — contrato da pesquisa de taxonomia, HUD).
- **Nada de estado duplicado:** o placar é derivado da contagem de `GoalScored` no stream ou mantido como acumulador único — escolher **um** e documentar; nunca ambos.

## 4. Critérios de aceite

1. **Cobertura total:** teste de propriedade — qualquer sequência de partida termina cada interrupção num `Restart` classificado ou num throw explícito; nenhum estado sem saída.
2. **Determinismo de replay:** mesma seed + mesmos inputs → sequência idêntica de StoppageEvents/Restarts no `eventStream`, byte a byte.
3. **Geometria da bola inteira:** casos de borda testados — bola tocando a linha (dentro), bola com centro fora mas raio tocando (dentro), bola inteira fora (fora); idem para o gol entre as traves.
4. **Prioridade gol > fundo:** cruzamento entre as traves nunca classifica como escanteio/tiro de meta.
5. **Relógio:** transições de tempo disparam nos instantes exatos; acumulador de paradas confere com a soma das durações de `DeadBall`.
6. **Fail-fast:** `FoulCommitted`/`OffsideOffence` injetados manualmente → `NotSupportedException`; cruzamento sem último toque no log → throw.

## 5. Fora de escopo (não implementar, não stubar "por conveniência")

- Faltas, cartões, impedimento, vantagem, acréscimos aplicados (estágios 2–4, research-gated).
- `MatchRuleset` como objeto de dados — este estágio lê os valores padrão IFAB hard-coded como constantes nomeadas; a extração é o estágio 5, após duplicação real.
- Qualquer coisa de Tier 1.5/2/3, apresentação, HUD, persistência.
