# Terra Paralela — Plano de Migração de Repositório (2D → 3D)

> Companion doc operacional. Cross-ref: `docs/ARCHITECTURE.md` (regras de camadas — permanecem canônicas) · GDD §1 (será revisado, ver §6) · *Decisão de Direção de Arte* (D1 = Opção B, 3D em tempo real) · GDD §5 (LOD).
> Convenção: prosa PT-BR, identificadores em inglês.
>
> **Origem:** `github.com/Monkius-Maximus/SoccerDreamGame` (main, 10 commits).
> **Destino:** `github.com/Monkius-Maximus/testingclaude` (vazio — confirmado em auditoria de 2026-07-20).

---

## 0. Premissas travadas (NÃO reabrir)

1. **D1 = Opção B (3D em tempo real).** A migração existe para servir esta decisão.
2. **A regra de ouro sobrevive intacta:** toda a lógica de simulação vive fora do projeto Godot (`src/`), engine-agnostic, testada headless. O 3D não muda uma linha do core.
3. **Um caminho, sem fallback:** o destino recebe o conteúdo por cópia única e passa a ser o repositório canônico. O `SoccerDreamGame` vira arquivo morto (read-only), não um "backup vivo". Não haverá sincronização bidirecional.
4. **Sem reescrita oportunista durante a migração.** Divergências código × design (Elo vs. Poisson etc.) são registradas na §7 e atacadas *depois*, em prompts próprios. A migração é transporte, não reforma.

---

## 1. Inventário auditado da origem

| Caminho | Veredito | Motivo |
| :--- | :--- | :--- |
| `src/SoccerSim.Core/` | **Copiar intacto** | Zero dependência de Godot ou de projeção visual. Time, Events, LOD, MatchEngine, Resolvers, Modes, Economy, Domain, SplitMix64. |
| `src/SoccerSim.Infrastructure.Sqlite/` | **Copiar intacto** | Só depende de Core + `Microsoft.Data.Sqlite`. |
| `sql/` (0001–0005 + `9999_seed_dev.sql`) | **Copiar intacto** | 17 tabelas ANSI-portáveis, embedadas na assembly de infra. |
| `tests/SoccerSim.Core.Tests/` | **Copiar intacto** | 12 suítes xUnit headless. São o critério de aceitação da própria migração (§4). |
| `docs/ARCHITECTURE.md` | **Copiar intacto** | Nenhuma menção a projeção visual; as regras de camadas valem igual em 3D. |
| `SoccerDreamGame.sln` + `Directory.Build.props` + `.gitignore` | **Copiar com renomeio** | `.sln` renomeia para o nome do jogo; referências de projeto não mudam de caminho. |
| `game/` | **NÃO copiar — recriar** | Ver §3. Único território 2D do repositório. |

**O que existe dentro de `game/` e o destino de cada peça:**

| Arquivo | Destino |
| :--- | :--- |
| `autoload/GameBootstrap.cs` | **Porta quase intacto** — composition root puro (DI manual, migrations, wiring do core). Nada de 2D nele. |
| `autoload/TimeManagerNode.cs`, `EventBusNode.cs`, `SaveServiceNode.cs`, `GameModeManager.cs` | **Portam quase intactos** — wrappers finos; só `GameModeManager` referencia caminhos de cena (atualizar strings). |
| `scenes/main_menu/`, `scenes/calendar/` | **Recriar** — são Control/UI; o conteúdo lógico copia, o `.tscn` refaz. |
| `scenes/match/MatchScene.cs` (ticker) | **Portar a lógica, trocar a raiz** — o ticker (placar, relógio, feed, box score) é `CanvasLayer`/UI puro e continua útil como HUD por cima da partida 3D. A raiz `Node2D` vira `Node3D`. |
| `scenes/lifesim/LifeSimScene.cs` | **Descartar** — stub de 13 linhas. Nada a preservar. |
| `project.godot` | **Recriar** — ver §3.1. |
| `game.csproj` | **Recriar igual** — `Godot.NET.Sdk/4.6.3`, `net8.0`, mesmas duas ProjectReferences. Só `RootNamespace`/`assembly_name` mudam se o nome do jogo mudar. |

---

## 2. Ordem de commits no destino

Commits pequenos, cada um deixando o repo num estado verificável. Nenhum commit mistura transporte com adaptação.

**C1 — Fundação headless.**
`src/`, `sql/`, `tests/`, `docs/`, `Directory.Build.props`, `.gitignore`, `.sln` (sem o projeto `game` ainda).
*Verificação:* `dotnet test tests/SoccerSim.Core.Tests` verde, sem Godot instalado. Este commit prova que o coração chegou vivo.

**C2 — Casca Godot 3D.**
`game/` novo: `project.godot` 3D (§3.1), `game.csproj`, `icon.svg`, os 5 autoloads portados, `MainMenu` mínimo. `.sln` ganha o projeto `game` de volta.
*Verificação:* projeto abre no editor Godot 4.6 (.NET), builda, roda até o menu; autoloads criam `user://save.db` e aplicam migrations no primeiro launch.

**C3 — Cenas de modo.**
`CalendarScene` (UI portada) + `MatchScene` com raiz `Node3D` + HUD ticker portado + `LifeSimScene` placeholder `Node3D`. `GameModeManager` com os caminhos de cena atualizados.
*Verificação:* ciclo completo Menu → Calendar → Match (ticker roda uma partida Tier 1) → Calendar, transições validadas pelo `ModeStateMachine` (transição ilegal ainda lança — comportamento preservado).

**C4 — README + selagem da origem.**
README novo descrevendo o projeto 3D. No `SoccerDreamGame`: último commit apontando para o repo novo + arquivar no GitHub (Settings → Archive). Um repositório canônico, *one way*.

---

## 3. Checklist do `game/` 3D novo

### 3.1 `project.godot`

- `config_version=5`, features `("4.6", "C#")`.
- **Renderer: decisão a tomar antes de C2** — o 2D usava `gl_compatibility` (correto para 16-bit). Para 3D em tempo real, os candidatos são `forward_plus` (desktop, mais recursos de luz) ou `mobile`. Depende do alvo de plataforma; registrado como decisão em aberto (§7.5). *Verificar a documentação do Godot 4.6 sobre os renderers antes de fixar.*
- Bloco `[autoload]` idêntico ao original, **mesma ordem** (`GameBootstrap` primeiro — é o composition root que os outros leem).
- `run/main_scene` → menu novo.

### 3.2 Estrutura

```
game/
  autoload/        # os 5 portados
  scenes/
    main_menu/
    calendar/
    match/         # Node3D + HUD CanvasLayer (ticker portado)
    lifesim/       # Node3D placeholder
  game.csproj      # Godot.NET.Sdk/4.6.3, net8.0, refs a Core + Infra.Sqlite
```

A razão de `src/` ser irmão de `game/` permanece: o `Godot.NET.Sdk` globa todo `.cs` sob o projeto Godot — core dentro de `game/` compilaria em dobro.

### 3.3 O que NÃO entra no C2/C3 (escopo negativo explícito)

Campo 3D, modelos de jogador, câmera de partida, física de bola, steering/utility AI, pipeline de personagem — tudo isso tem docs próprios e virá por prompts de implementação próprios, sobre a casca que a migração entrega. A migração termina com o **ticker rodando sobre um `Node3D` vazio**, e isso é sucesso, não pendência.

---

## 4. Critérios de aceitação da migração

1. `dotnet test` verde no destino, sem Godot (C1).
2. `dotnet build <solution>.sln` verde (C2).
3. Editor Godot 4.6 (.NET) abre `game/`, roda, cria `user://save.db`, aplica as 6 migrations (C2).
4. Ciclo de modos completo sem crash; transição ilegal continua lançando (C3).
5. `SoccerDreamGame` arquivado; README do destino aponta a origem histórica (C4).
6. **Diff disciplinado:** `git diff` entre a origem e o destino em `src/`, `sql/`, `tests/`, `docs/` deve ser vazio (fora renomeios de sln/namespace, se houver). Qualquer edição no core durante a migração é bug do processo.

---

## 5. Instrução para o prompt de Claude Code

Quando isto virar prompt de implementação, incluir as cláusulas padrão do projeto: princípios de engenharia não-negociáveis (fail-fast, um caminho, sem fallback, mudanças cirúrgicas), a ordem C1→C4 como sequência obrigatória com verificação por commit, e a cláusula **"verifique a documentação do Godot 4.6 antes de configurar o renderer e o SDK; pergunte antes de codar se algo divergir deste plano"**.

---

## 6. Revisão documental decorrente (fora do repo, no GDD)

- **GDD §1** (perspectivas side-on/isométrica, resoluções de sprite 64×64/16×16/512×512) fica obsoleto sob a Opção B. Reescrever como "Core Architecture & Rendering": cena de partida 3D com câmera dinâmica, vida-sim 3D, e a política de HUD herdada (ticker como CanvasLayer).
- Os demais parágrafos do GDD (tempo, eventos, estado de personagem, LOD) **não mudam** — e o código migrado os implementa.

---

## 7. Divergências registradas (pós-migração, cada uma em prompt próprio)

1. **Resolvers Tier 2/3:** código usa Elo→gols (`EloResolverBase`); design atual pede duplo Poisson. Ponto de troca: implementações de `ILeagueResolver`. A interface sobrevive.
2. **Tier 1.5 (resolvedor estruturado por situações):** inexistente no código; bloqueado pelo retorno da pesquisa de taxonomia (design doc *Resolução Estruturada de Partidas*).
3. **Tactics → Behavior:** contrato `player.decide(tick)` sem código; primeiro sistema genuinamente 3D a implementar sobre a casca.
4. **Form/Mood:** schema segue o modelo -5/+5 com reset sazonal do GDD original; revisar contra os docs de ciclo de vida antes de estender.
5. **Renderer 3D** (`forward_plus` vs. `mobile`): decisão de plataforma-alvo, a fixar antes de C2.
6. **Nome do jogo/solução no destino** (`testingclaude` é nome de repo, não de produto): decidir se `.sln`, `RootNamespace` e `assembly_name` adotam "Terra Paralela" já na migração (recomendado — renomear depois é mais caro).
